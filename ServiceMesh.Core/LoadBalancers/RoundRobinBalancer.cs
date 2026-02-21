using System.Collections.Concurrent;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Core.LoadBalancers;

/// <summary>
/// 轮询负载均衡器
/// </summary>
public class RoundRobinBalancer : ILoadBalancer
{
    private readonly ConcurrentDictionary<string, int> _counters = new();

    public ServiceInstance? Select(List<ServiceInstance> instances)
    {
        if (instances == null || instances.Count == 0)
            return null;

        var serviceName = instances[0].ServiceName;
        var counter = _counters.AddOrUpdate(serviceName, 0, (_, value) => value + 1);
        
        var index = Math.Abs(counter) % instances.Count;
        return instances[index];
    }
}

/// <summary>
/// 加权轮询负载均衡器
/// </summary>
public class WeightedRoundRobinBalancer : ILoadBalancer
{
    private readonly ConcurrentDictionary<string, int> _counters = new();

    public ServiceInstance? Select(List<ServiceInstance> instances)
    {
        if (instances == null || instances.Count == 0)
            return null;

        // 按权重展开
        var weightedList = new List<ServiceInstance>();
        foreach (var instance in instances)
        {
            for (int i = 0; i < instance.Weight; i++)
            {
                weightedList.Add(instance);
            }
        }

        if (weightedList.Count == 0)
            return null;

        var serviceName = instances[0].ServiceName;
        var counter = _counters.AddOrUpdate(serviceName, 0, (_, value) => value + 1);
        
        var index = Math.Abs(counter) % weightedList.Count;
        return weightedList[index];
    }
}

/// <summary>
/// 随机负载均衡器
/// </summary>
public class RandomBalancer : ILoadBalancer
{
    private readonly Random _random = new();

    public ServiceInstance? Select(List<ServiceInstance> instances)
    {
        if (instances == null || instances.Count == 0)
            return null;

        var index = _random.Next(instances.Count);
        return instances[index];
    }
}

/// <summary>
/// 最小连接数负载均衡器（模拟）
/// </summary>
public class LeastConnectionsBalancer : ILoadBalancer
{
    public ServiceInstance? Select(List<ServiceInstance> instances)
    {
        if (instances == null || instances.Count == 0)
            return null;

        // 简化实现：返回第一个，实际应该跟踪连接数
        return instances.OrderBy(i => i.Metadata.GetValueOrDefault("connections", "0"))
                       .FirstOrDefault();
    }
}
