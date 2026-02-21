using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Registry.Services;

/// <summary>
/// Redis 服务存储 - 支持持久化和高可用
/// </summary>
public class RedisServiceStore : IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisServiceStore> _logger;
    private readonly string _instanceId;
    
    // 本地缓存作为Redis的L1缓存
    private readonly ConcurrentDictionary<string, ServiceInstance> _localCache = new();
    private readonly ConcurrentDictionary<string, List<string>> _serviceIndex = new();
    
    // Redis Key 前缀
    private const string KEY_PREFIX = "servicemesh:instance:";
    private const string SERVICE_INDEX_PREFIX = "servicemesh:service:";
    private const string VERSION_KEY = "servicemesh:version";

    public RedisServiceStore(string connectionString, ILogger<RedisServiceStore> logger)
    {
        _logger = logger;
        _instanceId = Guid.NewGuid().ToString("N")[..8];
        
        var options = ConfigurationOptions.Parse(connectionString);
        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;
        options.AbortOnConnectFail = false;
        
        _redis = ConnectionMultiplexer.Connect(options);
        _db = _redis.GetDatabase();
        
        // 订阅变更通知
        var sub = _redis.GetSubscriber();
        sub.Subscribe($"{KEY_PREFIX}changes", OnRedisMessage);
        
        _logger.LogInformation("Redis存储已初始化，实例ID: {InstanceId}", _instanceId);
    }

    /// <summary>
    /// 添加或更新服务实例
    /// </summary>
    public async Task AddOrUpdateAsync(ServiceInstance instance)
    {
        var key = $"{KEY_PREFIX}{instance.Id}";
        var json = JsonSerializer.Serialize(instance);
        
        // 使用事务保证原子性
        var tran = _db.CreateTransaction();
        tran.StringSetAsync(key, json, TimeSpan.FromMinutes(5)); // 5分钟过期，需心跳续期
        tran.SetAddAsync($"{SERVICE_INDEX_PREFIX}{instance.ServiceName}", instance.Id);
        tran.StringIncrementAsync(VERSION_KEY);
        
        await tran.ExecuteAsync();
        
        // 更新本地缓存
        _localCache[instance.Id] = instance;
        _serviceIndex.AddOrUpdate(instance.ServiceName, 
            _ => new List<string> { instance.Id },
            (_, list) => { list.Add(instance.Id); return list; });
        
        // 发布变更通知
        await _redis.GetSubscriber().PublishAsync($"{KEY_PREFIX}changes", 
            $"update:{instance.ServiceName}:{instance.Id}");
        
        _logger.LogDebug("实例已保存到Redis: {InstanceId}", instance.Id);
    }

    /// <summary>
    /// 获取服务实例
    /// </summary>
    public async Task<ServiceInstance?> GetInstanceAsync(string instanceId)
    {
        // L1缓存优先
        if (_localCache.TryGetValue(instanceId, out var cached))
            return cached;
        
        // 从Redis获取
        var key = $"{KEY_PREFIX}{instanceId}";
        var json = await _db.StringGetAsync(key);
        
        if (json.IsNullOrEmpty)
            return null;
        
        var instance = JsonSerializer.Deserialize<ServiceInstance>(json!);
        if (instance != null)
            _localCache[instanceId] = instance;
        
        return instance;
    }

    /// <summary>
    /// 获取服务的所有实例
    /// </summary>
    public async Task<List<ServiceInstance>> GetServiceInstancesAsync(string serviceName)
    {
        var instanceIds = await _db.SetMembersAsync($"{SERVICE_INDEX_PREFIX}{serviceName}");
        var instances = new List<ServiceInstance>();
        
        foreach (var id in instanceIds)
        {
            var instance = await GetInstanceAsync(id!);
            if (instance != null)
                instances.Add(instance);
        }
        
        return instances;
    }

    /// <summary>
    /// 获取所有健康实例
    /// </summary>
    public async Task<List<ServiceInstance>> GetHealthyInstancesAsync(string serviceName)
    {
        var instances = await GetServiceInstancesAsync(serviceName);
        return instances.Where(i => i.Status == ServiceStatus.Healthy).ToList();
    }

    /// <summary>
    /// 移除服务实例
    /// </summary>
    public async Task<bool> RemoveInstanceAsync(string instanceId)
    {
        var instance = await GetInstanceAsync(instanceId);
        if (instance == null)
            return false;
        
        var key = $"{KEY_PREFIX}{instanceId}";
        
        var tran = _db.CreateTransaction();
        tran.KeyDeleteAsync(key);
        tran.SetRemoveAsync($"{SERVICE_INDEX_PREFIX}{instance.ServiceName}", instanceId);
        tran.StringIncrementAsync(VERSION_KEY);
        
        var result = await tran.ExecuteAsync();
        
        // 更新本地缓存
        _localCache.TryRemove(instanceId, out _);
        if (_serviceIndex.TryGetValue(instance.ServiceName, out var list))
            list.Remove(instanceId);
        
        // 发布变更通知
        await _redis.GetSubscriber().PublishAsync($"{KEY_PREFIX}changes", 
            $"remove:{instance.ServiceName}:{instanceId}");
        
        return result;
    }

    /// <summary>
    /// 更新实例心跳 - 同时续期Redis过期时间
    /// </summary>
    public async Task<bool> UpdateHeartbeatAsync(string instanceId)
    {
        var key = $"{KEY_PREFIX}{instanceId}";
        var json = await _db.StringGetAsync(key);
        
        if (json.IsNullOrEmpty)
            return false;
        
        var instance = JsonSerializer.Deserialize<ServiceInstance>(json!);
        if (instance == null)
            return false;
        
        instance.LastHeartbeat = DateTime.UtcNow;
        instance.Status = ServiceStatus.Healthy;
        
        // 更新并续期
        var newJson = JsonSerializer.Serialize(instance);
        await _db.StringSetAsync(key, newJson, TimeSpan.FromMinutes(5));
        
        // 更新本地缓存
        _localCache[instanceId] = instance;
        
        return true;
    }

    /// <summary>
    /// 更新实例状态
    /// </summary>
    public async Task<bool> UpdateStatusAsync(string instanceId, ServiceStatus status)
    {
        var instance = await GetInstanceAsync(instanceId);
        if (instance == null)
            return false;
        
        instance.Status = status;
        await AddOrUpdateAsync(instance);
        
        return true;
    }

    /// <summary>
    /// 获取所有服务名称
    /// </summary>
    public async Task<List<string>> GetAllServiceNamesAsync()
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: $"{SERVICE_INDEX_PREFIX}*").ToArray();
        
        return keys.Select(k => k.ToString().Replace(SERVICE_INDEX_PREFIX, "")).ToList();
    }

    /// <summary>
    /// 获取全局版本号（用于增量更新）
    /// </summary>
    public async Task<long> GetVersionAsync()
    {
        var version = await _db.StringGetAsync(VERSION_KEY);
        return version.IsNullOrEmpty ? 0 : (long)version;
    }

    /// <summary>
    /// 获取指定版本之后变更的实例
    /// </summary>
    public async Task<List<ServiceInstance>> GetChangesSinceAsync(long version)
    {
        // 简化实现：返回所有实例，实际可用Redis Stream或Sorted Set优化
        var allInstances = new List<ServiceInstance>();
        var serviceNames = await GetAllServiceNamesAsync();
        
        foreach (var serviceName in serviceNames)
        {
            var instances = await GetServiceInstancesAsync(serviceName);
            allInstances.AddRange(instances);
        }
        
        return allInstances;
    }

    /// <summary>
    /// 处理Redis消息
    /// </summary>
    private void OnRedisMessage(RedisChannel channel, RedisValue message)
    {
        _logger.LogDebug("收到Redis消息: {Message}", message);
        // 可触发事件通知本地订阅者
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
