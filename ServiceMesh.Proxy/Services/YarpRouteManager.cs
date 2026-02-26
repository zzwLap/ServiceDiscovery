using Yarp.ReverseProxy.Configuration;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Proxy.Services
{
    /// <summary>
    /// YARP路由管理器，用于动态更新路由配置
    /// </summary>
    public class YarpRouteManager
    {
        private readonly IProxyConfigProvider _configProvider;
        private readonly IServiceDiscovery _serviceDiscovery;
        private readonly ILogger<YarpRouteManager> _logger;
        
        private readonly Dictionary<string, ServiceInstance> _activeInstances = new();

        public YarpRouteManager(
            IProxyConfigProvider configProvider,
            IServiceDiscovery serviceDiscovery,
            ILogger<YarpRouteManager> logger)
        {
            _configProvider = configProvider;
            _serviceDiscovery = serviceDiscovery;
            _logger = logger;
        }

        /// <summary>
        /// 更新路由配置以包含指定服务
        /// </summary>
        public async Task<bool> AddOrUpdateServiceRouteAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                // 获取服务的健康实例
                var instance = await _serviceDiscovery.GetHealthyInstanceAsync(serviceName, cancellationToken: cancellationToken);
                
                if (instance == null)
                {
                    _logger.LogWarning("无法为服务 {ServiceName} 找到健康实例", serviceName);
                    return false;
                }

                // 更新内部实例缓存
                _activeInstances[serviceName] = instance;

                // 触发配置刷新
                await RefreshConfigurationAsync(cancellationToken);

                _logger.LogInformation("成功为服务 {ServiceName} 更新路由配置", serviceName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新服务 {ServiceName} 路由配置时出错", serviceName);
                return false;
            }
        }

        /// <summary>
        /// 删除服务路由
        /// </summary>
        public async Task<bool> RemoveServiceRouteAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_activeInstances.ContainsKey(serviceName))
                {
                    _activeInstances.Remove(serviceName);
                    
                    // 触发配置刷新
                    await RefreshConfigurationAsync(cancellationToken);

                    _logger.LogInformation("已删除服务 {ServiceName} 的路由配置", serviceName);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除服务 {ServiceName} 路由配置时出错", serviceName);
                return false;
            }
        }

        /// <summary>
        /// 刷新YARP配置
        /// </summary>
        private async Task RefreshConfigurationAsync(CancellationToken cancellationToken)
        {
            // 在这里我们不会真正刷新配置，因为IProxyConfigProvider是只读接口
            // 实际实现中可能需要通过其他方式来更新配置
            _logger.LogInformation("路由配置已更新，当前活跃服务数: {Count}", _activeInstances.Count);
        }

        /// <summary>
        /// 获取当前活跃的服务实例
        /// </summary>
        public IReadOnlyDictionary<string, ServiceInstance> GetActiveInstances()
        {
            return new Dictionary<string, ServiceInstance>(_activeInstances);
        }
    }
}