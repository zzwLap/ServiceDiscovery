using Microsoft.Extensions.Logging;

namespace ServiceMesh.Core.Tracing;

/// <summary>
/// 链路数据收集器接口
/// </summary>
public interface ITraceCollector
{
    /// <summary>
    /// 收集完成的Span
    /// </summary>
    Task CollectAsync(TraceSpan span, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量收集
    /// </summary>
    Task CollectBatchAsync(IEnumerable<TraceSpan> spans, CancellationToken cancellationToken = default);
}

/// <summary>
/// 内存链路收集器（用于开发和测试）
/// </summary>
public class InMemoryTraceCollector : ITraceCollector
{
    private readonly List<TraceSpan> _spans = new();
    private readonly object _lock = new();

    public Task CollectAsync(TraceSpan span, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _spans.Add(span);
        }
        return Task.CompletedTask;
    }

    public Task CollectBatchAsync(IEnumerable<TraceSpan> spans, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _spans.AddRange(spans);
        }
        return Task.CompletedTask;
    }

    public List<TraceSpan> GetSpans()
    {
        lock (_lock)
        {
            return _spans.ToList();
        }
    }

    public List<TraceSpan> GetSpansByTraceId(string traceId)
    {
        lock (_lock)
        {
            return _spans.Where(s => s.TraceId == traceId).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _spans.Clear();
        }
    }
}

/// <summary>
/// 控制台链路收集器（用于调试）
/// </summary>
public class ConsoleTraceCollector : ITraceCollector
{
    private readonly ILogger<ConsoleTraceCollector> _logger;

    public ConsoleTraceCollector(ILogger<ConsoleTraceCollector> logger)
    {
        _logger = logger;
    }

    public Task CollectAsync(TraceSpan span, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Trace] {TraceId} | {SpanId} | {Operation} | {Service} | {DurationMs}ms | {Status}",
            span.TraceId[..8],
            span.SpanId[..8],
            span.OperationName,
            span.ServiceName,
            span.Duration.TotalMilliseconds,
            span.Status);

        return Task.CompletedTask;
    }

    public Task CollectBatchAsync(IEnumerable<TraceSpan> spans, CancellationToken cancellationToken = default)
    {
        foreach (var span in spans)
        {
            CollectAsync(span, cancellationToken);
        }
        return Task.CompletedTask;
    }
}
