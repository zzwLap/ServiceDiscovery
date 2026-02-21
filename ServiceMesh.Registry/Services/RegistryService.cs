using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Registry.Services;

/// <summary>
/// 注册中心服务实现
/// </summary>
public class RegistryService : IServiceRegistry, IServiceDiscovery
{
    private readonly InMemoryServiceStore _store;
    private readonly ILogger<RegistryService> _logger;
    private readonly Dictionary<string, List<Func<ServiceDiscoveryResponse, Task>>> _subscribers = new();

    public RegistryService(InMemoryServiceStore store, ILogger<RegistryService> logger)
    {
        _store = store;
        _logger = logger;
    }

    #region IServiceRegistry 实现

    public Task<ServiceRegistryResponse> RegisterAsync(ServiceRegistryRequest request, CancellationToken cancellationToken = default)
    {
        var instance = new ServiceInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            ServiceName = request.ServiceName,
            Host = request.Host,
            Port = request.Port,
            Version = request.Version,
            Metadata = request.Metadata ?? new Dictionary<string, string>(),
            HealthCheckUrl = request.HealthCheckUrl,
            Weight = request.Weight,
            RegisteredAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow,
            Status = ServiceStatus.Healthy
        };

        _store.AddOrUpdate(instance);
        
        _logger.LogInformation("服务已注册: {ServiceName} - {InstanceId} at {Host}:{Port}", 
            instance.ServiceName, instance.Id, instance.Host, instance.Port);

        // 通知订阅者
        _ = NotifySubscribersAsync(instance.ServiceName);

        return Task.FromResult(new ServiceRegistryResponse
        {
            Success = true,
            InstanceId = instance.Id,
            Message = "注册成功"
        });
    }

    public Task<bool> DeregisterAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var instance = _store.GetInstance(instanceId);
        if (instance == null)
        {
            return Task.FromResult(false);
        }

        var result = _store.RemoveInstance(instanceId);
        if (result)
        {
            _logger.LogInformation("服务已注销: {ServiceName} - {InstanceId}", 
                instance.ServiceName, instanceId);
            
            // 通知订阅者
            _ = NotifySubscribersAsync(instance.ServiceName);
        }

        return Task.FromResult(result);
    }

    public Task<bool> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        var result = _store.UpdateHeartbeat(request.InstanceId);
        
        if (result)
        {
            _logger.LogDebug("收到心跳: {ServiceName} - {InstanceId}", 
                request.ServiceName, request.InstanceId);
        }
        else
        {
            _logger.LogWarning("心跳失败，实例不存在: {InstanceId}", request.InstanceId);
        }

        return Task.FromResult(result);
    }

    #endregion

    #region IServiceDiscovery 实现

    public Task<ServiceDiscoveryResponse> DiscoverAsync(ServiceDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        var instances = request.HealthyOnly 
            ? _store.GetHealthyInstances(request.ServiceName)
            : _store.GetServiceInstances(request.ServiceName);

        // 按版本过滤
        if (!string.IsNullOrEmpty(request.Version))
        {
            instances = instances.Where(i => i.Version == request.Version).ToList();
        }

        return Task.FromResult(new ServiceDiscoveryResponse
        {
            ServiceName = request.ServiceName,
            Instances = instances
        });
    }

    public Task<ServiceInstance?> GetHealthyInstanceAsync(string serviceName, string? version = null, CancellationToken cancellationToken = default)
    {
        var instances = _store.GetHealthyInstances(serviceName);
        
        if (!string.IsNullOrEmpty(version))
        {
            instances = instances.Where(i => i.Version == version).ToList();
        }

        if (instances.Count == 0)
        {
            return Task.FromResult<ServiceInstance?>(null);
        }

        // 简单轮询选择
        var instance = instances[Random.Shared.Next(instances.Count)];
        return Task.FromResult<ServiceInstance?>(instance);
    }

    public Task SubscribeAsync(string serviceName, Func<ServiceDiscoveryResponse, Task> callback, CancellationToken cancellationToken = default)
    {
        lock (_subscribers)
        {
            if (!_subscribers.ContainsKey(serviceName))
            {
                _subscribers[serviceName] = new List<Func<ServiceDiscoveryResponse, Task>>();
            }
            _subscribers[serviceName].Add(callback);
        }
        
        _logger.LogInformation("服务订阅: {ServiceName}", serviceName);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string serviceName)
    {
        lock (_subscribers)
        {
            _subscribers.Remove(serviceName);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 通知订阅者服务变更
    /// </summary>
    private async Task NotifySubscribersAsync(string serviceName)
    {
        List<Func<ServiceDiscoveryResponse, Task>>? callbacks;
        lock (_subscribers)
        {
            if (!_subscribers.TryGetValue(serviceName, out callbacks))
            {
                return;
            }
            callbacks = callbacks.ToList(); // 复制一份避免并发修改
        }

        var response = new ServiceDiscoveryResponse
        {
            ServiceName = serviceName,
            Instances = _store.GetHealthyInstances(serviceName)
        };

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

    #endregion

    /// <summary>
    /// 清理过期实例
    /// </summary>
    public async Task CleanupExpiredInstancesAsync(TimeSpan timeout)
    {
        var expiredInstances = _store.GetExpiredInstances(timeout);
        
        foreach (var instance in expiredInstances)
        {
            _logger.LogWarning("清理过期实例: {ServiceName} - {InstanceId}, 最后心跳: {LastHeartbeat}",
                instance.ServiceName, instance.Id, instance.LastHeartbeat);
            
            await DeregisterAsync(instance.Id);
        }
    }
}
