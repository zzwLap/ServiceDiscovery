using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServiceMesh.Agent;

/// <summary>
/// 健康检查中间件扩展
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// 使用默认健康检查中间件
    /// 配置从 ServiceRegistrationOptions 统一获取
    /// </summary>
    public static IApplicationBuilder UseDefaultHealthCheck(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DefaultHealthCheckMiddleware>();
    }
}

/// <summary>
/// 自动健康检查启动过滤器
/// 当 EnableDefaultHealthCheck 为 true 时自动注册健康检查中间件
/// </summary>
internal class AutoHealthCheckStartupFilter : IStartupFilter
{
    private readonly IOptionsMonitor<ServiceRegistrationOptions> _optionsMonitor;

    public AutoHealthCheckStartupFilter(IOptionsMonitor<ServiceRegistrationOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            // 如果启用默认健康检查，在管道最开始注册中间件
            // 这样可以确保健康检查端点优先响应，不受其他中间件影响
            if (_optionsMonitor.CurrentValue.EnableDefaultHealthCheck)
            {
                builder.UseDefaultHealthCheck();
            }
            
            next(builder);
        };
    }
}

/// <summary>
/// 服务注册扩展方法
/// </summary>
public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// 添加服务自动注册
    /// </summary>
    public static IServiceCollection AddServiceRegistration(
        this IServiceCollection services,
        Action<ServiceRegistrationOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ServiceRegistrationOptions>(_ => { });
        }

        // 注册默认的服务信息提供者（如果用户未注册自定义实现）
        services.AddSingleton<IServiceInfoProvider, DefaultServiceInfoProvider>();
        services.AddHostedService<ServiceRegistrationClient>();
        
        // 注册自动健康检查启动过滤器
        // 当 EnableDefaultHealthCheck 为 true 时自动配置健康检查中间件
        services.AddSingleton<IStartupFilter, AutoHealthCheckStartupFilter>();

        return services;
    }

    /// <summary>
    /// 添加服务自动注册（带自定义服务信息提供者）
    /// </summary>
    /// <typeparam name="TProvider">自定义服务信息提供者类型</typeparam>
    public static IServiceCollection AddServiceRegistration<TProvider>(
        this IServiceCollection services,
        Action<ServiceRegistrationOptions>? configureOptions = null)
        where TProvider : class, IServiceInfoProvider
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ServiceRegistrationOptions>(_ => { });
        }

        // 注册自定义服务信息提供者
        services.AddSingleton<IServiceInfoProvider, TProvider>();
        services.AddHostedService<ServiceRegistrationClient>();

        return services;
    }

    /// <summary>
    /// 添加服务自动注册（简化版）
    /// </summary>
    public static IServiceCollection AddServiceRegistration(
        this IServiceCollection services,
        string serviceName,
        string registryUrl = "http://localhost:5000")
    {
        return services.AddServiceRegistration(options =>
        {
            options.ServiceName = serviceName;
            options.RegistryUrl = registryUrl;
        });
    }

    /// <summary>
    /// 添加服务自动注册并配置元数据
    /// </summary>
    public static IServiceCollection AddServiceRegistration(
        this IServiceCollection services,
        Action<ServiceRegistrationOptions> configureOptions,
        Dictionary<string, string> metadata)
    {
        return services.AddServiceRegistration(options =>
        {
            configureOptions(options);
            foreach (var item in metadata)
            {
                options.Metadata[item.Key] = item.Value;
            }
        });
    }

}
