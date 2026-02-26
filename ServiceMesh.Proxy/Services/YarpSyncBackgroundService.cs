using Microsoft.Extensions.Hosting;
using ServiceMesh.Core.Interfaces;

namespace ServiceMesh.Proxy.Services
{
    /// <summary>
    /// YARP同步后台服务，用于定期同步服务发现与YARP配置
    /// </summary>
    public class YarpSyncBackgroundService : BackgroundService
    {
        private readonly ILogger<YarpSyncBackgroundService> _logger;
        private readonly IYarpConfigUpdater _yarpConfigUpdater;
        private readonly IServiceDiscovery _serviceDiscovery;

        public YarpSyncBackgroundService(
            ILogger<YarpSyncBackgroundService> logger,
            IYarpConfigUpdater yarpConfigUpdater,
            IServiceDiscovery serviceDiscovery)
        {
            _logger = logger;
            _yarpConfigUpdater = yarpConfigUpdater;
            _serviceDiscovery = serviceDiscovery;
        }

        protected override async Task<Task> ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("YARP同步后台服务启动");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await SyncServicesAsync(stoppingToken);
                        
                        // 每30秒同步一次
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常停止
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "同步服务时发生错误");
                        
                        // 发生错误时等待更长时间再重试
                        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                    }
                }
            }
            finally
            {
                _logger.LogInformation("YARP同步后台服务停止");
            }

            return Task.CompletedTask;
        }

        private async Task SyncServicesAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("开始同步服务...");

            // 获取当前活跃的服务
            var activeServices = _yarpConfigUpdater.GetActiveServices();

            // 为每个活跃服务检查其健康状态
            foreach (var serviceName in activeServices)
            {
                var instance = await _serviceDiscovery.GetHealthyInstanceAsync(serviceName, cancellationToken: cancellationToken);
                
                if (instance == null)
                {
                    // 如果服务不再健康，从YARP配置中移除
                    _yarpConfigUpdater.UnregisterService(serviceName);
                    _logger.LogInformation("服务 {ServiceName} 不再健康，已从YARP配置中移除", serviceName);
                }
            }

            _logger.LogDebug("服务同步完成");
        }
    }
}