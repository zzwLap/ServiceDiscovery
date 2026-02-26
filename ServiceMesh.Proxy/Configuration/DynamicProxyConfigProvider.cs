using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace ServiceMesh.Proxy.Configuration;

/// <summary>
/// 动态代理配置提供程序，支持运行时更新YARP路由配置
/// </summary>
public class DynamicProxyConfigProvider : IProxyConfigProvider
{
        private volatile DynamicProxyConfig _config;
        private readonly object _lock = new();

        public DynamicProxyConfigProvider()
        {
            _config = new DynamicProxyConfig(ImmutableList<RouteConfig>.Empty, ImmutableList<ClusterConfig>.Empty);
        }

        /// <summary>
        /// 获取当前配置
        /// </summary>
        public IProxyConfig GetConfig() => _config;

        /// <summary>
        /// 添加或更新路由配置
        /// </summary>
        public void AddOrUpdateRoute(string serviceName, string address)
        {
            lock (_lock)
            {
                var routeId = $"route-{serviceName}";
                var clusterId = $"cluster-{serviceName}";
                var destinationId = $"destination-{serviceName}";

                // 创建新的路由配置
                var newRoute = new RouteConfig
                {
                    RouteId = routeId,
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        Path = $"/api/{serviceName}/{{**catch-all}}"
                    },
                    Transforms = new List<Dictionary<string, string>>
                    {
                        new() { { "PathPattern", "{**catch-all}" } }
                    }
                };

                // 创建新的集群配置
                var newCluster = new ClusterConfig
                {
                    ClusterId = clusterId,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        [destinationId] = new DestinationConfig
                        {
                            Address = address
                        }
                    },
                    HealthCheck = new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = true,
                            Interval = TimeSpan.FromSeconds(30),
                            Timeout = TimeSpan.FromSeconds(10),
                            Path = "/health",
                            Policy = "ConsecutiveFailures"
                        }
                    }
                };

                // 更新配置
                var currentRoutes = _config.Routes.ToList();
                var currentClusters = _config.Clusters.ToList();

                // 移除已存在的路由和集群
                currentRoutes.RemoveAll(r => r.RouteId == routeId);
                currentClusters.RemoveAll(c => c.ClusterId == clusterId);

                // 添加新的路由和集群
                currentRoutes.Add(newRoute);
                currentClusters.Add(newCluster);

                var oldConfig = _config;
                _config = new DynamicProxyConfig(
                    currentRoutes.ToImmutableList(),
                    currentClusters.ToImmutableList());
                
                oldConfig.SignalChange();
            }
        }

        /// <summary>
        /// 移除路由配置
        /// </summary>
        public void RemoveRoute(string serviceName)
        {
            lock (_lock)
            {
                var routeId = $"route-{serviceName}";
                var clusterId = $"cluster-{serviceName}";

                var currentRoutes = _config.Routes.ToList();
                var currentClusters = _config.Clusters.ToList();

                currentRoutes.RemoveAll(r => r.RouteId == routeId);
                currentClusters.RemoveAll(c => c.ClusterId == clusterId);

                var oldConfig = _config;
                _config = new DynamicProxyConfig(
                    currentRoutes.ToImmutableList(),
                    currentClusters.ToImmutableList());
                
                oldConfig.SignalChange();
            }
        }

        /// <summary>
        /// 检查是否存在指定服务的配置
        /// </summary>
        public bool HasRoute(string serviceName)
        {
            var routeId = $"route-{serviceName}";
            return _config.Routes.Any(r => r.RouteId == routeId);
        }
    }

/// <summary>
/// 动态代理配置实现
/// </summary>
public class DynamicProxyConfig : IProxyConfig
{
    private readonly CancellationTokenSource _cts = new();

        public DynamicProxyConfig(
            ImmutableList<RouteConfig> routes,
            ImmutableList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        public void SignalChange()
        {
            _cts.Cancel();
        }
    }
}
