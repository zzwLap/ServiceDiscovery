using Microsoft.AspNetCore.Mvc;
using ServiceMesh.Core.Tracing;

namespace ServiceMesh.Registry.Controllers;

/// <summary>
/// 链路追踪查询 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TraceController : ControllerBase
{
    private readonly InMemoryTraceCollector _traceCollector;
    private readonly ILogger<TraceController> _logger;

    public TraceController(InMemoryTraceCollector traceCollector, ILogger<TraceController> logger)
    {
        _traceCollector = traceCollector;
        _logger = logger;
    }

    /// <summary>
    /// 查询指定 TraceId 的所有 Span
    /// </summary>
    [HttpGet("trace/{traceId}")]
    public IActionResult GetTrace(string traceId)
    {
        var spans = _traceCollector.GetSpansByTraceId(traceId);
        
        if (spans.Count == 0)
            return NotFound(new { message = "未找到该链路数据" });

        // 构建链路树
        var traceTree = BuildTraceTree(spans);
        
        return Ok(new
        {
            traceId = traceId,
            spanCount = spans.Count,
            duration = CalculateTotalDuration(spans),
            tree = traceTree
        });
    }

    /// <summary>
    /// 查询所有链路（支持分页）
    /// </summary>
    [HttpGet("traces")]
    public IActionResult GetTraces([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var allSpans = _traceCollector.GetSpans();
        
        // 按 TraceId 分组
        var traces = allSpans
            .GroupBy(s => s.TraceId)
            .Select(g => new
            {
                traceId = g.Key,
                spanCount = g.Count(),
                startTime = g.Min(s => s.StartTime),
                endTime = g.Max(s => s.EndTime),
                duration = g.Max(s => s.EndTime) - g.Min(s => s.StartTime),
                services = g.Select(s => s.ServiceName).Distinct().ToList(),
                hasError = g.Any(s => s.Status == SpanStatus.Error)
            })
            .OrderByDescending(t => t.startTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            total = allSpans.Select(s => s.TraceId).Distinct().Count(),
            page = page,
            pageSize = pageSize,
            traces = traces
        });
    }

    /// <summary>
    /// 查询服务调用拓扑
    /// </summary>
    [HttpGet("topology")]
    public IActionResult GetServiceTopology()
    {
        var spans = _traceCollector.GetSpans();
        
        // 构建服务调用关系
        var edges = new List<object>();
        var nodes = spans.Select(s => s.ServiceName).Distinct().ToList();
        
        foreach (var span in spans.Where(s => !string.IsNullOrEmpty(s.ParentSpanId)))
        {
            var parentSpan = spans.FirstOrDefault(s => s.SpanId == span.ParentSpanId);
            if (parentSpan != null && parentSpan.ServiceName != span.ServiceName)
            {
                edges.Add(new
                {
                    source = parentSpan.ServiceName,
                    target = span.ServiceName,
                    count = 1
                });
            }
        }

        // 合并相同边
        var aggregatedEdges = edges
            .GroupBy(e => new { Source = e.GetType().GetProperty("source")?.GetValue(e), Target = e.GetType().GetProperty("target")?.GetValue(e) })
            .Select(g => new
            {
                source = g.Key.Source,
                target = g.Key.Target,
                count = g.Count()
            })
            .ToList();

        return Ok(new
        {
            nodes = nodes.Select(n => new { id = n, name = n }),
            edges = aggregatedEdges
        });
    }

    /// <summary>
    /// 查询服务性能统计
    /// </summary>
    [HttpGet("stats/{serviceName}")]
    public IActionResult GetServiceStats(string serviceName)
    {
        var spans = _traceCollector.GetSpans()
            .Where(s => s.ServiceName == serviceName)
            .ToList();

        if (spans.Count == 0)
            return NotFound(new { message = "未找到该服务数据" });

        var durations = spans.Select(s => s.Duration.TotalMilliseconds).ToList();
        
        return Ok(new
        {
            serviceName = serviceName,
            totalRequests = spans.Count,
            errorCount = spans.Count(s => s.Status == SpanStatus.Error),
            errorRate = (double)spans.Count(s => s.Status == SpanStatus.Error) / spans.Count,
            avgDuration = durations.Average(),
            p50 = GetPercentile(durations, 0.5),
            p95 = GetPercentile(durations, 0.95),
            p99 = GetPercentile(durations, 0.99),
            maxDuration = durations.Max(),
            minDuration = durations.Min()
        });
    }

    /// <summary>
    /// 清空链路数据
    /// </summary>
    [HttpPost("clear")]
    public IActionResult ClearTraces()
    {
        _traceCollector.Clear();
        return Ok(new { message = "链路数据已清空" });
    }

    private object BuildTraceTree(List<TraceSpan> spans)
    {
        var rootSpans = spans.Where(s => string.IsNullOrEmpty(s.ParentSpanId)).ToList();
        
        return rootSpans.Select(root => BuildSpanNode(root, spans)).ToList();
    }

    private object BuildSpanNode(TraceSpan span, List<TraceSpan> allSpans)
    {
        var children = allSpans.Where(s => s.ParentSpanId == span.SpanId).ToList();
        
        return new
        {
            spanId = span.SpanId,
            parentSpanId = span.ParentSpanId,
            serviceName = span.ServiceName,
            operationName = span.OperationName,
            startTime = span.StartTime,
            endTime = span.EndTime,
            duration = span.Duration.TotalMilliseconds,
            status = span.Status.ToString(),
            errorMessage = span.ErrorMessage,
            tags = span.Tags,
            children = children.Select(c => BuildSpanNode(c, allSpans)).ToList()
        };
    }

    private TimeSpan CalculateTotalDuration(List<TraceSpan> spans)
    {
        if (spans.Count == 0) return TimeSpan.Zero;
        
        var start = spans.Min(s => s.StartTime);
        var end = spans.Max(s => s.EndTime);
        return end - start;
    }

    private double GetPercentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}
