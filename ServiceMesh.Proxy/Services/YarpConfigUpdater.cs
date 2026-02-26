using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;
using ServiceMesh.Proxy.Configuration;

namespace ServiceMesh.Proxy.Services
{
    /// <summary>
    /// YARP配置更新服务，用于动态更新路由配置
    /// </summary>
    public class YarpConfigUpdater : IYarpConfigUpdater
    {
        private readonly DynamicProxyConfigProvider _dynamicConfigProvider;
        private readonly IServiceDiscovery _serviceDiscovery;
        private readonly ILogger<YarpConfigUpdater> _logger;
        private readonly Dictionary<string, ServiceInstance> _activeServices = new();

        public YarpConfigUpdater(
            DynamicProxyConfigProvider dynamicConfigProvider,
            IServiceDiscovery serviceDiscovery,
            ILogger<YarpConfigUpdater> logger)
        {
            _dynamicConfigProvider = dynamicConfigProvider;
            _serviceDiscovery = serviceDiscovery;
            _logger = logger;
        }

        /// <summary>
        /// 注册服务到YARP配置
        /// </summary>
        public async Task<bool> RegisterServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                var instance = await _serviceDiscovery.GetHealthyInstanceAsync(serviceName, cancellationToken: cancellationToken);
                
                if (instance == null)
                {
                    _logger.LogWarning("无法为服务 {ServiceName} 找到健康实例", serviceName);
                    return false;
                }

                // 添加或更新服务实例
                _activeServices[serviceName] = instance;

                // 更新YARP动态配置
                var destinationAddress = $"http://{instance.GetAddress()}";
                _dynamicConfigProvider.AddOrUpdateRoute(serviceName, destinationAddress);

                _logger.LogInformation("已注册服务 {ServiceName} 到YARP配置，目标地址: {Address}", serviceName, destinationAddress);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册服务 {ServiceName} 到YARP配置时出错", serviceName);
                return false;
            }
        }

        /// <summary>
        /// 从YARP配置中移除服务
        /// </summary>
        public bool UnregisterService(string serviceName)
        {
            if (_activeServices.ContainsKey(serviceName))
            {
                _activeServices.Remove(serviceName);
                
                // 从YARP动态配置中移除
                _dynamicConfigProvider.RemoveRoute(serviceName);
                
                _logger.LogInformation("已从YARP配置中移除服务 {ServiceName}", serviceName);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取当前活动的服务列表
        /// </summary>
        public IReadOnlyList<string> GetActiveServices()
        {
            return _activeServices.Keys.ToList().AsReadOnly();
        }
    }

    public interface IYarpConfigUpdater
    {
        Task<bool> RegisterServiceAsync(string serviceName, CancellationToken cancellationToken = default);
        bool UnregisterService(string serviceName);
        IReadOnlyList<string> GetActiveServices();
    }
}