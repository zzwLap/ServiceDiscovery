using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Agent;

/// <summary>
/// 自适应心跳服务 - 根据负载动态调整心跳频率
/// </summary>
public class AdaptiveHeartbeatService : IHostedService, IDisposable
{
    private readonly ServiceRegistrationOptions _options;
    private readonly ILogger<AdaptiveHeartbeatService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _instanceId;
    private readonly string _serviceName;
    
    // 自适应配置
    private readonly AdaptiveHeartbeatConfig _config;
    
    // 运行时状态
    private Timer? _heartbeatTimer;
    private readonly Stopwatch _runtimeWatch = new();
    private readonly Queue<RequestMetric> _recentRequests = new();
    private readonly object _metricsLock = new();
    
    // 当前心跳间隔（动态调整）
    private TimeSpan _currentInterval;
    private ServiceLoadLevel _currentLoadLevel = ServiceLoadLevel.Normal;
    
    // 连续失败计数
    private int _consecutiveFailures = 0;
    private const int MAX_CONSECUTIVE_FAILURES = 3;

    public AdaptiveHeartbeatService(
        string instanceId,
        string serviceName,
        IOptions<ServiceRegistrationOptions> options,
        ILogger<AdaptiveHeartbeatService> logger,
        AdaptiveHeartbeatConfig? config = null)
    {
        _instanceId = instanceId;
        _serviceName = serviceName;
        _options = options.Value;
        _logger = logger;
        _config = config ?? new AdaptiveHeartbeatConfig();
        _currentInterval = _options.HeartbeatInterval;
        
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5) // 心跳超时更短
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runtimeWatch.Start();
        
        // 立即发送第一次心跳
        _ = SendHeartbeatAsync();
        
        // 启动自适应定时器
        ScheduleNextHeartbeat();
        
        // 启动负载监控
        _ = MonitorLoadAsync(cancellationToken);
        
        _logger.LogInformation(
            "自适应心跳服务已启动，初始间隔: {Interval}s",
            _currentInterval.TotalSeconds);
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Change(Timeout.Infinite, 0);
        _runtimeWatch.Stop();
        
        // 发送最后一次心跳（快速）
        await SendHeartbeatAsync(true);
        
        _logger.LogInformation("自适应心跳服务已停止");
    }

    /// <summary>
    /// 记录请求指标（由服务调用）
    /// </summary>
    public void RecordRequest(TimeSpan duration, bool success, string? endpoint = null)
    {
        lock (_metricsLock)
        {
            _recentRequests.Enqueue(new RequestMetric
            {
                Timestamp = DateTime.UtcNow,
                Duration = duration,
                Success = success,
                Endpoint = endpoint
            });
            
            // 只保留最近1分钟的请求
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (_recentRequests.Count > 0 && _recentRequests.Peek().Timestamp < cutoff)
            {
                _recentRequests.Dequeue();
            }
        }
    }

    /// <summary>
    /// 负载监控循环
    /// </summary>
    private async Task MonitorLoadAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                
                var loadLevel = CalculateLoadLevel();
                if (loadLevel != _currentLoadLevel)
                {
                    _currentLoadLevel = loadLevel;
                    AdjustHeartbeatInterval(loadLevel);
                    
                    _logger.LogInformation(
                        "负载级别变更: {LoadLevel} -> 心跳间隔: {Interval}s",
                        loadLevel,
                        _currentInterval.TotalSeconds);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "负载监控异常");
            }
        }
    }

    /// <summary>
    /// 计算当前负载级别
    /// </summary>
    private ServiceLoadLevel CalculateLoadLevel()
    {
        lock (_metricsLock)
        {
            var recentCount = _recentRequests.Count;
            var errorRate = CalculateErrorRate();
            var avgLatency = CalculateAverageLatency();
            
            // 高负载判断：请求多或延迟高
            if (recentCount > _config.HighLoadRequestThreshold || 
                avgLatency > _config.HighLoadLatencyThreshold ||
                errorRate > _config.HighErrorRateThreshold)
            {
                return ServiceLoadLevel.High;
            }
            
            // 中负载判断
            if (recentCount > _config.MediumLoadRequestThreshold ||
                avgLatency > _config.MediumLoadLatencyThreshold ||
                errorRate > _config.MediumErrorRateThreshold)
            {
                return ServiceLoadLevel.Medium;
            }
            
            // 低负载：长时间无请求
            if (recentCount == 0 && _runtimeWatch.Elapsed > TimeSpan.FromMinutes(5))
            {
                return ServiceLoadLevel.Low;
            }
            
            return ServiceLoadLevel.Normal;
        }
    }

    private double CalculateErrorRate()
    {
        if (_recentRequests.Count == 0) return 0;
        return (double)_recentRequests.Count(r => !r.Success) / _recentRequests.Count;
    }

    private TimeSpan CalculateAverageLatency()
    {
        if (_recentRequests.Count == 0) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(_recentRequests.Average(r => r.Duration.TotalMilliseconds));
    }

    /// <summary>
    /// 根据负载调整心跳间隔
    /// </summary>
    private void AdjustHeartbeatInterval(ServiceLoadLevel loadLevel)
    {
        _currentInterval = loadLevel switch
        {
            ServiceLoadLevel.High => _config.HighLoadInterval,      // 高负载：更频繁（10秒）
            ServiceLoadLevel.Medium => _config.MediumLoadInterval,  // 中负载：正常（20秒）
            ServiceLoadLevel.Normal => _options.HeartbeatInterval,  // 正常：默认（30秒）
            ServiceLoadLevel.Low => _config.LowLoadInterval,        // 低负载：降低频率（60秒）
            _ => _options.HeartbeatInterval
        };
        
        // 重新调度下次心跳
        ScheduleNextHeartbeat();
    }

    /// <summary>
    /// 调度下次心跳
    /// </summary>
    private void ScheduleNextHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            _currentInterval,
            Timeout.InfiniteTimeSpan); // 一次性定时器，发送后重新调度
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    private async Task SendHeartbeatAsync(bool isShutdown = false)
    {
        try
        {
            var request = new HeartbeatRequest
            {
                InstanceId = _instanceId,
                ServiceName = _serviceName
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_options.RegistryUrl}/api/registry/heartbeat";
            
            // 关闭时缩短超时
            using var cts = new CancellationTokenSource(isShutdown ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5));
            var response = await _httpClient.PostAsync(url, content, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _consecutiveFailures = 0;
                _logger.LogDebug("心跳发送成功: {InstanceId}, 间隔: {Interval}s", 
                    _instanceId, _currentInterval.TotalSeconds);
            }
            else
            {
                _consecutiveFailures++;
                _logger.LogWarning("心跳发送失败: {StatusCode}, 连续失败: {Failures}", 
                    response.StatusCode, _consecutiveFailures);
                
                // 连续失败时缩短间隔快速重试
                if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    _currentInterval = TimeSpan.FromSeconds(5);
                }
            }
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogError(ex, "心跳发送异常, 连续失败: {Failures}", _consecutiveFailures);
        }
        finally
        {
            // 重新调度下次心跳（如果不是关闭）
            if (!isShutdown)
            {
                ScheduleNextHeartbeat();
            }
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _httpClient?.Dispose();
    }
}

/// <summary>
/// 自适应心跳配置
/// </summary>
public class AdaptiveHeartbeatConfig
{
    // 负载阈值
    public int HighLoadRequestThreshold { get; set; } = 100;      // 1分钟内超过100请求为高负载
    public int MediumLoadRequestThreshold { get; set; } = 50;     // 1分钟内超过50请求为中负载
    
    public TimeSpan HighLoadLatencyThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan MediumLoadLatencyThreshold { get; set; } = TimeSpan.FromMilliseconds(200);
    
    public double HighErrorRateThreshold { get; set; } = 0.1;     // 错误率超过10%
    public double MediumErrorRateThreshold { get; set; } = 0.05;  // 错误率超过5%
    
    // 心跳间隔配置
    public TimeSpan HighLoadInterval { get; set; } = TimeSpan.FromSeconds(10);   // 高负载：10秒
    public TimeSpan MediumLoadInterval { get; set; } = TimeSpan.FromSeconds(20); // 中负载：20秒
    public TimeSpan LowLoadInterval { get; set; } = TimeSpan.FromSeconds(60);    // 低负载：60秒
}

/// <summary>
/// 负载级别
/// </summary>
public enum ServiceLoadLevel
{
    Low,      // 低负载：长时间无请求
    Normal,   // 正常负载
    Medium,   // 中负载：请求量增加
    High      // 高负载：请求量大或延迟高
}

/// <summary>
/// 请求指标
/// </summary>
public class RequestMetric
{
    public DateTime Timestamp { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? Endpoint { get; set; }
}
