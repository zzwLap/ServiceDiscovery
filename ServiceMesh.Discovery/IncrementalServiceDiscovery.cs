using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.LoadBalancers;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Discovery;

/// <summary>
/// 增量服务发现客户端 - 支持WebSocket实时推送
/// </summary>
public class IncrementalServiceDiscovery : IServiceDiscovery, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IncrementalServiceDiscovery> _logger;
    private readonly ServiceDiscoveryOptions _options;
    private readonly ILoadBalancer _loadBalancer;
    
    // 本地缓存
    private readonly ConcurrentDictionary<string, ServiceInstance> _instanceCache = new();
    private readonly ConcurrentDictionary<string, List<string>> _serviceIndex = new();
    
    // 订阅回调
    private readonly ConcurrentDictionary<string, List<Func<ServiceDiscoveryResponse, Task>>> _subscribers = new();
    
    // WebSocket
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _webSocketCts;
    private Task? _webSocketReceiveTask;
    
    // 版本控制
    private long _localVersion = 0;
    private readonly Timer _incrementalSyncTimer;
    
    // 变更队列（用于批量处理）
    private readonly ConcurrentQueue<ServiceChangeEvent> _changeQueue = new();
    private readonly Timer _batchProcessTimer;

    public IncrementalServiceDiscovery(
        HttpClient httpClient,
        IOptions<ServiceDiscoveryOptions> options,
        ILogger<IncrementalServiceDiscovery> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _loadBalancer = CreateLoadBalancer(_options.LoadBalancer);
        
        // 启动增量同步定时器（5秒一次）
        _incrementalSyncTimer = new Timer(
            async _ => await SyncIncrementalAsync(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
        
        // 启动批量处理定时器（100ms处理一次变更队列）
        _batchProcessTimer = new Timer(
            _ => ProcessChangeBatch(),
            null,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));
        
        // 启动WebSocket连接
        _ = ConnectWebSocketAsync();
    }

    private ILoadBalancer CreateLoadBalancer(LoadBalancerType type)
    {
        return type switch
        {
            LoadBalancerType.WeightedRoundRobin => new WeightedRoundRobinBalancer(),
            LoadBalancerType.Random => new RandomBalancer(),
            LoadBalancerType.LeastConnections => new LeastConnectionsBalancer(),
            _ => new RoundRobinBalancer()
        };
    }

    #region WebSocket 实时推送

    private async Task ConnectWebSocketAsync()
    {
        while (true)
        {
            try
            {
                _webSocketCts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();
                
                var wsUrl = _options.RegistryUrl.Replace("http://", "ws://").Replace("https://", "wss://");
                await _webSocket.ConnectAsync(
                    new Uri($"{wsUrl}/ws/registry"), 
                    _webSocketCts.Token);
                
                _logger.LogInformation("WebSocket已连接");
                
                // 启动接收任务
                _webSocketReceiveTask = ReceiveWebSocketMessagesAsync(_webSocketCts.Token);
                
                // 等待连接断开
                await _webSocketReceiveTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket连接失败，5秒后重试");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private async Task ReceiveWebSocketMessagesAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        
        while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(buffer, ct);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    break;
                }
                
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var changeEvent = JsonSerializer.Deserialize<ServiceChangeEvent>(message);
                
                if (changeEvent != null)
                {
                    _changeQueue.Enqueue(changeEvent);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket接收消息失败");
            }
        }
    }

    #endregion

    #region 增量同步

    private async Task SyncIncrementalAsync()
    {
        try
        {
            var url = $"{_options.RegistryUrl}/api/registry/changes?sinceVersion={_localVersion}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
                return;
            
            var json = await response.Content.ReadAsStringAsync();
            var changes = JsonSerializer.Deserialize<IncrementalChanges>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (changes == null || changes.Version <= _localVersion)
                return;
            
            // 应用变更
            foreach (var instance in changes.AddedOrUpdated)
            {
                ApplyChange(instance, ChangeType.AddOrUpdate);
            }
            
            foreach (var instanceId in changes.Removed)
            {
                RemoveInstance(instanceId);
            }
            
            _localVersion = changes.Version;
            _logger.LogDebug("增量同步完成，版本: {Version}", _localVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "增量同步失败");
        }
    }

    private void ProcessChangeBatch()
    {
        var batch = new List<ServiceChangeEvent>();
        while (_changeQueue.TryDequeue(out var change) && batch.Count < 100)
        {
            batch.Add(change);
        }
        
        if (batch.Count == 0)
            return;
        
        // 按服务分组处理
        var groupedChanges = batch.GroupBy(c => c.ServiceName);
        
        foreach (var group in groupedChanges)
        {
            var serviceName = group.Key;
            var latestChange = group.OrderByDescending(c => c.Timestamp).First();
            
            if (latestChange.ChangeType == ChangeType.Remove)
            {
                RemoveInstance(latestChange.InstanceId);
            }
            else
            {
                ApplyChange(latestChange.Instance, ChangeType.AddOrUpdate);
            }
            
            // 触发订阅通知
            _ = NotifySubscribersAsync(serviceName);
        }
        
        _logger.LogDebug("批量处理完成，处理 {Count} 条变更", batch.Count);
    }

    private void ApplyChange(ServiceInstance instance, ChangeType changeType)
    {
        var oldInstance = _instanceCache.TryGetValue(instance.Id, out var old) ? old : null;
        
        // 更新实例缓存
        _instanceCache[instance.Id] = instance;
        
        // 更新服务索引
        if (oldInstance != null && oldInstance.ServiceName != instance.ServiceName)
        {
            // 服务名变更，从旧服务移除
            if (_serviceIndex.TryGetValue(oldInstance.ServiceName, out var oldList))
                oldList.Remove(instance.Id);
        }
        
        _serviceIndex.AddOrUpdate(instance.ServiceName,
            _ => new List<string> { instance.Id },
            (_, list) =>
            {
                if (!list.Contains(instance.Id))
                    list.Add(instance.Id);
                return list;
            });
    }

    private void RemoveInstance(string instanceId)
    {
        if (!_instanceCache.TryRemove(instanceId, out var instance))
            return;
        
        if (_serviceIndex.TryGetValue(instance.ServiceName, out var list))
        {
            list.Remove(instanceId);
            if (list.Count == 0)
                _serviceIndex.TryRemove(instance.ServiceName, out _);
        }
    }

    #endregion

    #region IServiceDiscovery 实现

    public Task<ServiceDiscoveryResponse> DiscoverAsync(ServiceDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!_serviceIndex.TryGetValue(request.ServiceName, out var instanceIds))
        {
            return Task.FromResult(new ServiceDiscoveryResponse 
            { 
                ServiceName = request.ServiceName,
                Instances = new List<ServiceInstance>()
            });
        }
        
        var instances = instanceIds
            .Select(id => _instanceCache.TryGetValue(id, out var inst) ? inst : null)
            .Where(i => i != null)
            .Cast<ServiceInstance>()
            .Where(i => !request.HealthyOnly || i.Status == ServiceStatus.Healthy)
            .Where(i => string.IsNullOrEmpty(request.Version) || i.Version == request.Version)
            .ToList();
        
        return Task.FromResult(new ServiceDiscoveryResponse
        {
            ServiceName = request.ServiceName,
            Instances = instances
        });
    }

    public Task<ServiceInstance?> GetHealthyInstanceAsync(string serviceName, string? version = null, CancellationToken cancellationToken = default)
    {
        var response = DiscoverAsync(new ServiceDiscoveryRequest
        {
            ServiceName = serviceName,
            Version = version,
            HealthyOnly = true
        }, cancellationToken).Result;
        
        return Task.FromResult(_loadBalancer.Select(response.Instances));
    }

    public Task SubscribeAsync(string serviceName, Func<ServiceDiscoveryResponse, Task> callback, CancellationToken cancellationToken = default)
    {
        var list = _subscribers.GetOrAdd(serviceName, _ => new List<Func<ServiceDiscoveryResponse, Task>>());
        lock (list)
        {
            list.Add(callback);
        }
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string serviceName)
    {
        _subscribers.TryRemove(serviceName, out _);
        return Task.CompletedTask;
    }

    private async Task NotifySubscribersAsync(string serviceName)
    {
        if (!_subscribers.TryGetValue(serviceName, out var callbacks))
            return;
        
        var response = await DiscoverAsync(new ServiceDiscoveryRequest
        {
            ServiceName = serviceName,
            HealthyOnly = true
        });
        
        foreach (var callback in callbacks)
        {
            try
            {
                await callback(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通知订阅者失败");
            }
        }
    }

    #endregion

    public void Dispose()
    {
        _webSocketCts?.Cancel();
        _webSocket?.Dispose();
        _incrementalSyncTimer?.Dispose();
        _batchProcessTimer?.Dispose();
    }
}

/// <summary>
/// 服务变更事件
/// </summary>
public class ServiceChangeEvent
{
    public string InstanceId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public ChangeType ChangeType { get; set; }
    public ServiceInstance? Instance { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum ChangeType
{
    AddOrUpdate,
    Remove
}

/// <summary>
/// 增量变更数据
/// </summary>
public class IncrementalChanges
{
    public long Version { get; set; }
    public List<ServiceInstance> AddedOrUpdated { get; set; } = new();
    public List<string> Removed { get; set; } = new();
}
