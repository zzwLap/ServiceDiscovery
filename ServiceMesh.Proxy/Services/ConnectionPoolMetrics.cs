using System.Collections.Concurrent;
using System.Diagnostics;

namespace ServiceMesh.Proxy.Services;

/// <summary>
/// 连接池指标监控
/// </summary>
public class ConnectionPoolMetrics
{
    private readonly ILogger<ConnectionPoolMetrics> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    
    // 指标计数器
    private long _totalRequests = 0;
    private long _activeConnections = 0;
    private long _pooledConnections = 0;
    private readonly ConcurrentDictionary<string, ServiceMetrics> _serviceMetrics = new();

    public ConnectionPoolMetrics(ILogger<ConnectionPoolMetrics> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 记录请求开始
    /// </summary>
    public void RecordRequestStart(string serviceName)
    {
        Interlocked.Increment(ref _totalRequests);
        var metrics = _serviceMetrics.GetOrAdd(serviceName, _ => new ServiceMetrics());
        metrics.IncrementActiveRequests();
    }

    /// <summary>
    /// 记录请求完成
    /// </summary>
    public void RecordRequestComplete(string serviceName, TimeSpan duration, bool success)
    {
        var metrics = _serviceMetrics.GetOrAdd(serviceName, _ => new ServiceMetrics());
        metrics.DecrementActiveRequests();
        metrics.RecordLatency(duration);
        
        if (!success)
        {
            metrics.IncrementErrors();
        }
    }

    /// <summary>
    /// 获取连接池状态报告
    /// </summary>
    public ConnectionPoolReport GetReport()
    {
        return new ConnectionPoolReport
        {
            TotalRequests = Interlocked.Read(ref _totalRequests),
            ServiceReports = _serviceMetrics.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GetReport())
        };
    }

    /// <summary>
    /// 启动定期报告
    /// </summary>
    public void StartReporting(TimeSpan interval)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(interval);
                PrintReport();
            }
        });
    }

    private void PrintReport()
    {
        var report = GetReport();
        _logger.LogInformation(
            "连接池状态 - 总请求: {Total}, 服务数: {Services}",
            report.TotalRequests,
            report.ServiceReports.Count);
        
        foreach (var service in report.ServiceReports)
        {
            _logger.LogDebug(
                "  {Service}: 活跃={Active}, 平均延迟={Latency:F2}ms, 错误率={ErrorRate:P}",
                service.Key,
                service.Value.ActiveRequests,
                service.Value.AverageLatencyMs,
                service.Value.ErrorRate);
        }
    }
}

/// <summary>
/// 单个服务的指标
/// </summary>
public class ServiceMetrics
{
    private long _activeRequests = 0;
    private long _totalRequests = 0;
    private long _errorCount = 0;
    private readonly List<double> _latencies = new();
    private readonly object _lock = new();

    public void IncrementActiveRequests() => Interlocked.Increment(ref _activeRequests);
    public void DecrementActiveRequests() => Interlocked.Decrement(ref _activeRequests);
    
    public void RecordLatency(TimeSpan latency)
    {
        Interlocked.Increment(ref _totalRequests);
        lock (_lock)
        {
            _latencies.Add(latency.TotalMilliseconds);
            // 只保留最近1000条记录
            if (_latencies.Count > 1000)
                _latencies.RemoveAt(0);
        }
    }

    public void IncrementErrors() => Interlocked.Increment(ref _errorCount);

    public ServiceMetricsReport GetReport()
    {
        lock (_lock)
        {
            return new ServiceMetricsReport
            {
                ActiveRequests = Interlocked.Read(ref _activeRequests),
                TotalRequests = Interlocked.Read(ref _totalRequests),
                ErrorCount = Interlocked.Read(ref _errorCount),
                AverageLatencyMs = _latencies.Count > 0 ? _latencies.Average() : 0,
                P99LatencyMs = _latencies.Count > 0 ? GetPercentile(_latencies, 0.99) : 0
            };
        }
    }

    private static double GetPercentile(List<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}

public class ConnectionPoolReport
{
    public long TotalRequests { get; set; }
    public Dictionary<string, ServiceMetricsReport> ServiceReports { get; set; } = new();
}

public class ServiceMetricsReport
{
    public long ActiveRequests { get; set; }
    public long TotalRequests { get; set; }
    public long ErrorCount { get; set; }
    public double AverageLatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double ErrorRate => TotalRequests > 0 ? (double)ErrorCount / TotalRequests : 0;
}
