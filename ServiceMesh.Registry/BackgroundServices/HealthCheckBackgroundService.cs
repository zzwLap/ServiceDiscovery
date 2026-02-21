using ServiceMesh.Core.HealthCheck;
using ServiceMesh.Core.Models;
using ServiceMesh.Registry.Services;

namespace ServiceMesh.Registry.BackgroundServices;

/// <summary>
/// 健康检查后台服务
/// </summary>
public class HealthCheckBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthCheckBackgroundService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _heartbeatTimeout;

    public HealthCheckBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<HealthCheckBackgroundService> logger,
        TimeSpan? checkInterval = null,
        TimeSpan? heartbeatTimeout = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromMinutes(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("健康检查后台服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var registryService = scope.ServiceProvider.GetRequiredService<RegistryService>();
                var store = scope.ServiceProvider.GetRequiredService<InMemoryServiceStore>();
                var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();

                // 1. 清理过期实例（心跳超时）
                await registryService.CleanupExpiredInstancesAsync(_heartbeatTimeout);

                // 2. 主动健康检查
                await PerformHealthChecksAsync(store, httpClient, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康检查过程中发生错误");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("健康检查后台服务已停止");
    }

    private async Task PerformHealthChecksAsync(InMemoryServiceStore store, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var instances = store.GetAllInstances();
        var healthChecker = new HttpHealthChecker(httpClient, TimeSpan.FromSeconds(5));

        foreach (var instance in instances)
        {
            try
            {
                var isHealthy = await healthChecker.CheckHealthAsync(instance, cancellationToken);
                
                if (isHealthy && instance.Status != ServiceStatus.Healthy)
                {
                    // 恢复健康
                    store.UpdateStatus(instance.Id, ServiceStatus.Healthy);
                    _logger.LogInformation("服务恢复健康: {ServiceName} - {InstanceId}", 
                        instance.ServiceName, instance.Id);
                }
                else if (!isHealthy && instance.Status == ServiceStatus.Healthy)
                {
                    // 标记为不健康
                    store.UpdateStatus(instance.Id, ServiceStatus.Unhealthy);
                    _logger.LogWarning("服务不健康: {ServiceName} - {InstanceId}", 
                        instance.ServiceName, instance.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康检查失败: {ServiceName} - {InstanceId}", 
                    instance.ServiceName, instance.Id);
            }
        }
    }
}
