using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServiceMesh.Agent;

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
    /// 使用服务注册（自动获取端口）
    /// </summary>
    public static IApplicationBuilder UseServiceRegistration(this IApplicationBuilder app)
    {
        var server = app.ApplicationServices.GetService<IServer>();
        var optionsMonitor = app.ApplicationServices.GetService<IOptionsMonitor<ServiceRegistrationOptions>>();

        if (server == null || optionsMonitor == null)
        {
            return app;
        }

        var options = optionsMonitor.CurrentValue;
        
        // 如果端口已配置，不需要自动获取
        if (options.Port != 0)
        {
            return app;
        }

        // 注册一个启动完成后的回调来获取服务器地址
        var lifetime = app.ApplicationServices.GetService<IHostApplicationLifetime>();
        if (lifetime != null)
        {
            lifetime.ApplicationStarted.Register(() =>
            {
                try
                {
                    var addressesFeature = server.Features.Get<IServerAddressesFeature>();
                    var addresses = addressesFeature?.Addresses;
                    
                    if (addresses != null && addresses.Any())
                    {
                        var address = addresses.First();
                        var uri = new Uri(address);
                        
                        // 更新配置值
                        options.Port = uri.Port;
                        
                        if (string.IsNullOrEmpty(options.Host))
                        {
                            options.Host = uri.Host;
                        }
                        
                        // 记录日志
                        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?
                            .CreateLogger("ServiceRegistration");
                        logger?.LogInformation("自动获取服务地址成功: {Host}:{Port}", options.Host, options.Port);
                    }
                    else
                    {
                        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?
                            .CreateLogger("ServiceRegistration");
                        logger?.LogWarning("无法获取服务器地址，请手动配置端口");
                    }
                }
                catch (Exception ex)
                {
                    var logger = app.ApplicationServices.GetService<ILoggerFactory>()?
                        .CreateLogger("ServiceRegistration");
                    logger?.LogError(ex, "自动获取服务器地址时发生错误");
                }
            });
        }

        return app;
    }
}
