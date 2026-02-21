using ServiceMesh.Core.Models;

namespace ServiceMesh.Core.Interfaces;

/// <summary>
/// 服务注册接口
/// </summary>
public interface IServiceRegistry
{
    /// <summary>
    /// 注册服务实例
    /// </summary>
    Task<ServiceRegistryResponse> RegisterAsync(ServiceRegistryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销服务实例
    /// </summary>
    Task<bool> DeregisterAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送心跳
    /// </summary>
    Task<bool> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// 发现服务实例
    /// </summary>
    Task<ServiceDiscoveryResponse> DiscoverAsync(ServiceDiscoveryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅服务变更
    /// </summary>
    Task SubscribeAsync(string serviceName, Func<ServiceDiscoveryResponse, Task> callback, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅
    /// </summary>
    Task UnsubscribeAsync(string serviceName);

    /// <summary>
    /// 获取单个健康实例（自动负载均衡）
    /// </summary>
    Task<ServiceInstance?> GetHealthyInstanceAsync(string serviceName, string? version = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 负载均衡器接口
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 从实例列表中选择一个实例
    /// </summary>
    ServiceInstance? Select(List<ServiceInstance> instances);
}

/// <summary>
/// 健康检查接口
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// 检查服务实例健康状态
    /// </summary>
    Task<bool> CheckHealthAsync(ServiceInstance instance, CancellationToken cancellationToken = default);
}
