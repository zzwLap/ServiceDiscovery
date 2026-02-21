using System.Collections.Concurrent;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Registry.Services;

/// <summary>
/// 内存服务存储
/// </summary>
public class InMemoryServiceStore
{
    // 服务实例存储: ServiceName -> (InstanceId -> ServiceInstance)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ServiceInstance>> _services = new();
    
    // 所有实例索引: InstanceId -> ServiceInstance
    private readonly ConcurrentDictionary<string, ServiceInstance> _instances = new();

    /// <summary>
    /// 添加或更新服务实例
    /// </summary>
    public void AddOrUpdate(ServiceInstance instance)
    {
        var serviceDict = _services.GetOrAdd(instance.ServiceName, _ => new ConcurrentDictionary<string, ServiceInstance>());
        serviceDict[instance.Id] = instance;
        _instances[instance.Id] = instance;
    }

    /// <summary>
    /// 获取服务实例
    /// </summary>
    public ServiceInstance? GetInstance(string instanceId)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return instance;
    }

    /// <summary>
    /// 获取服务的所有实例
    /// </summary>
    public List<ServiceInstance> GetServiceInstances(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var serviceDict))
        {
            return serviceDict.Values.ToList();
        }
        return new List<ServiceInstance>();
    }

    /// <summary>
    /// 获取所有健康实例
    /// </summary>
    public List<ServiceInstance> GetHealthyInstances(string serviceName)
    {
        return GetServiceInstances(serviceName)
            .Where(i => i.Status == ServiceStatus.Healthy)
            .ToList();
    }

    /// <summary>
    /// 移除服务实例
    /// </summary>
    public bool RemoveInstance(string instanceId)
    {
        if (_instances.TryRemove(instanceId, out var instance))
        {
            if (_services.TryGetValue(instance.ServiceName, out var serviceDict))
            {
                serviceDict.TryRemove(instanceId, out _);
                
                // 如果服务下没有实例了，移除服务
                if (serviceDict.IsEmpty)
                {
                    _services.TryRemove(instance.ServiceName, out _);
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// 更新实例心跳
    /// </summary>
    public bool UpdateHeartbeat(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            instance.LastHeartbeat = DateTime.UtcNow;
            instance.Status = ServiceStatus.Healthy;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 更新实例状态
    /// </summary>
    public bool UpdateStatus(string instanceId, ServiceStatus status)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            instance.Status = status;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取所有服务名称
    /// </summary>
    public List<string> GetAllServiceNames()
    {
        return _services.Keys.ToList();
    }

    /// <summary>
    /// 获取所有实例
    /// </summary>
    public List<ServiceInstance> GetAllInstances()
    {
        return _instances.Values.ToList();
    }

    /// <summary>
    /// 获取过期实例（用于清理）
    /// </summary>
    public List<ServiceInstance> GetExpiredInstances(TimeSpan timeout)
    {
        var now = DateTime.UtcNow;
        return _instances.Values
            .Where(i => now - i.LastHeartbeat > timeout)
            .ToList();
    }
}
