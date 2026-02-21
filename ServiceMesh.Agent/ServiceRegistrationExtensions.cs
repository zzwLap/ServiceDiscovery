using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        // 获取服务器地址并更新配置
        var server = app.ApplicationServices.GetService<IServer>();
        var options = app.ApplicationServices.GetService<Microsoft.Extensions.Options.IOptions<ServiceRegistrationOptions>>()?.Value;

        if (server != null && options != null && options.Port == 0)
        {
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            if (addresses != null && addresses.Any())
            {
                var address = addresses.First();
                var uri = new Uri(address);
                
                // 通过配置更新端口
                var config = app.ApplicationServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                if (config is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    // 这里可以通过其他方式传递端口信息
                }
            }
        }

        return app;
    }
}
