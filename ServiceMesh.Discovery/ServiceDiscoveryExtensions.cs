using Microsoft.Extensions.DependencyInjection;
using ServiceMesh.Core.Interfaces;

namespace ServiceMesh.Discovery;

/// <summary>
/// 服务发现扩展方法
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// 添加服务发现客户端
    /// </summary>
    public static IServiceCollection AddServiceDiscovery(
        this IServiceCollection services,
        Action<ServiceDiscoveryOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ServiceDiscoveryOptions>(_ => { });
        }

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IServiceDiscovery, ServiceDiscoveryClient>();

        return services;
    }

    /// <summary>
    /// 添加服务发现客户端（带配置）
    /// </summary>
    public static IServiceCollection AddServiceDiscovery(
        this IServiceCollection services,
        string registryUrl,
        LoadBalancerType loadBalancer = LoadBalancerType.RoundRobin)
    {
        return services.AddServiceDiscovery(options =>
        {
            options.RegistryUrl = registryUrl;
            options.LoadBalancer = loadBalancer;
        });
    }
}
