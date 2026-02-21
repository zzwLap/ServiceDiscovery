using System.Diagnostics;

namespace ServiceMesh.Core.Tracing;

/// <summary>
/// 链路追踪上下文
/// </summary>
public class TraceContext
{
    /// <summary>
    /// 全局唯一链路ID
    /// </summary>
    public string TraceId { get; set; } = GenerateTraceId();

    /// <summary>
    /// 当前跨度ID
    /// </summary>
    public string SpanId { get; set; } = GenerateSpanId();

    /// <summary>
    /// 父跨度ID（根节点为空）
    /// </summary>
    public string? ParentSpanId { get; set; }

    /// <summary>
    /// 是否采样
    /// </summary>
    public bool IsSampled { get; set; } = true;

    /// <summary>
    /// 自定义标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    ///  baggage（跨服务传递的上下文）
    /// </summary>
    public Dictionary<string, string> Baggage { get; set; } = new();

    /// <summary>
    /// 创建子上下文（用于服务间调用）
    /// </summary>
    public TraceContext CreateChildContext()
    {
        return new TraceContext
        {
            TraceId = TraceId,
            SpanId = GenerateSpanId(),
            ParentSpanId = SpanId,
            IsSampled = IsSampled,
            Baggage = new Dictionary<string, string>(Baggage)
        };
    }

    private static string GenerateTraceId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string GenerateSpanId()
    {
        return Guid.NewGuid().ToString("N")[..16];
    }
}

/// <summary>
/// 链路跨度（Span）
/// </summary>
public class TraceSpan
{
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string? ParentSpanId { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public SpanStatus Status { get; set; } = SpanStatus.Ok;
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<SpanLog> Logs { get; set; } = new();
}

/// <summary>
/// 跨度日志
/// </summary>
public class SpanLog
{
    public DateTime Timestamp { get; set; }
    public string Event { get; set; } = string.Empty;
    public Dictionary<string, string> Fields { get; set; } = new();
}

public enum SpanStatus
{
    Ok,
    Error
}

/// <summary>
/// 异步本地存储（AsyncLocal）用于跨异步方法传递上下文
/// </summary>
public static class TraceContextHolder
{
    private static readonly AsyncLocal<TraceContext?> _currentContext = new();

    public static TraceContext? Current
    {
        get => _currentContext.Value;
        set => _currentContext.Value = value;
    }

    /// <summary>
    /// 获取或创建上下文
    /// </summary>
    public static TraceContext GetOrCreate()
    {
        return Current ??= new TraceContext();
    }
}
