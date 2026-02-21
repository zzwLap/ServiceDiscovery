using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.LoadBalancers;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Discovery;

/// <summary>
/// 服务发现客户端配置
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// 注册中心地址
    /// </summary>
    public string RegistryUrl { get; set; } = "http://localhost:5000";

    /// <summary>
    /// 缓存刷新间隔
    /// </summary>
    public TimeSpan CacheRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public LoadBalancerType LoadBalancer { get; set; } = LoadBalancerType.RoundRobin;
}

public enum LoadBalancerType
{
    RoundRobin,
    WeightedRoundRobin,
    Random,
    LeastConnections
}

/// <summary>
/// 服务发现客户端
/// </summary>
public class ServiceDiscoveryClient : IServiceDiscovery, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceDiscoveryClient> _logger;
    private readonly ServiceDiscoveryOptions _options;
    private readonly ILoadBalancer _loadBalancer;
    
    // 本地缓存: ServiceName -> List<ServiceInstance>
    private readonly ConcurrentDictionary<string, List<ServiceInstance>> _cache = new();
    
    // 订阅回调
    private readonly ConcurrentDictionary<string, List<Func<ServiceDiscoveryResponse, Task>>> _subscribers = new();
    
    private readonly Timer _refreshTimer;
    private readonly CancellationTokenSource _cts = new();

    public ServiceDiscoveryClient(
        HttpClient httpClient,
        IOptions<ServiceDiscoveryOptions> options,
        ILogger<ServiceDiscoveryClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _loadBalancer = CreateLoadBalancer(_options.LoadBalancer);
        
        // 启动定时刷新缓存
        _refreshTimer = new Timer(
            async _ => await RefreshCacheAsync(),
            null,
            _options.CacheRefreshInterval,
            _options.CacheRefreshInterval);
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

    #region IServiceDiscovery 实现

    public async Task<ServiceDiscoveryResponse> DiscoverAsync(ServiceDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        // 先尝试从缓存获取
        if (_cache.TryGetValue(request.ServiceName, out var cachedInstances))
        {
            var filteredInstances = request.HealthyOnly
                ? cachedInstances.Where(i => i.Status == ServiceStatus.Healthy).ToList()
                : cachedInstances;

            if (!string.IsNullOrEmpty(request.Version))
            {
                filteredInstances = filteredInstances.Where(i => i.Version == request.Version).ToList();
            }

            return new ServiceDiscoveryResponse
            {
                ServiceName = request.ServiceName,
                Instances = filteredInstances
            };
        }

        // 缓存未命中，从注册中心获取
        return await FetchFromRegistryAsync(request, cancellationToken);
    }

    public Task SubscribeAsync(string serviceName, Func<ServiceDiscoveryResponse, Task> callback, CancellationToken cancellationToken = default)
    {
        var list = _subscribers.GetOrAdd(serviceName, _ => new List<Func<ServiceDiscoveryResponse, Task>>());
        lock (list)
        {
            list.Add(callback);
        }
        
        _logger.LogInformation("已订阅服务: {ServiceName}", serviceName);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string serviceName)
    {
        _subscribers.TryRemove(serviceName, out _);
        return Task.CompletedTask;
    }

    public async Task<ServiceInstance?> GetHealthyInstanceAsync(string serviceName, string? version = null, CancellationToken cancellationToken = default)
    {
        var response = await DiscoverAsync(new ServiceDiscoveryRequest
        {
            ServiceName = serviceName,
            Version = version,
            HealthyOnly = true
        }, cancellationToken);

        return _loadBalancer.Select(response.Instances);
    }

    #endregion

    /// <summary>
    /// 从注册中心获取服务
    /// </summary>
    private async Task<ServiceDiscoveryResponse> FetchFromRegistryAsync(ServiceDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_options.RegistryUrl}/api/registry/discover/{request.ServiceName}";
            if (!string.IsNullOrEmpty(request.Version))
            {
                url += $"?version={request.Version}&healthyOnly={request.HealthyOnly}";
            }
            else
            {
                url += $"?healthyOnly={request.HealthyOnly}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<ServiceDiscoveryResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // 更新缓存
            if (result != null)
            {
                _cache[request.ServiceName] = result.Instances;
            }

            return result ?? new ServiceDiscoveryResponse { ServiceName = request.ServiceName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从注册中心获取服务失败: {ServiceName}", request.ServiceName);
            return new ServiceDiscoveryResponse { ServiceName = request.ServiceName };
        }
    }

    /// <summary>
    /// 刷新缓存
    /// </summary>
    private async Task RefreshCacheAsync()
    {
        foreach (var serviceName in _cache.Keys.ToList())
        {
            try
            {
                var response = await FetchFromRegistryAsync(new ServiceDiscoveryRequest
                {
                    ServiceName = serviceName,
                    HealthyOnly = false
                }, _cts.Token);

                // 检查是否有变化，如果有则通知订阅者
                if (_cache.TryGetValue(serviceName, out var oldInstances))
                {
                    var hasChanged = HasInstancesChanged(oldInstances, response.Instances);
                    if (hasChanged)
                    {
                        await NotifySubscribersAsync(serviceName, response);
                    }
                }

                _cache[serviceName] = response.Instances;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新缓存失败: {ServiceName}", serviceName);
            }
        }
    }

    /// <summary>
    /// 检查实例列表是否有变化
    /// </summary>
    private bool HasInstancesChanged(List<ServiceInstance> oldList, List<ServiceInstance> newList)
    {
        var oldIds = new HashSet<string>(oldList.Select(i => i.Id));
        var newIds = new HashSet<string>(newList.Select(i => i.Id));
        return !oldIds.SetEquals(newIds);
    }

    /// <summary>
    /// 通知订阅者
    /// </summary>
    private async Task NotifySubscribersAsync(string serviceName, ServiceDiscoveryResponse response)
    {
        if (_subscribers.TryGetValue(serviceName, out var callbacks))
        {
            foreach (var callback in callbacks)
            {
                try
                {
                    await callback(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "通知订阅者失败: {ServiceName}", serviceName);
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _refreshTimer?.Dispose();
        _cts?.Dispose();
    }
}
