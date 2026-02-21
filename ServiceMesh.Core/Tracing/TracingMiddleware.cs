using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ServiceMesh.Core.Tracing;

/// <summary>
/// 链路追踪中间件 - 自动提取/注入 Trace 信息
/// </summary>
public class TracingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITraceCollector _traceCollector;
    private readonly ILogger<TracingMiddleware> _logger;
    private readonly string _serviceName;

    public TracingMiddleware(
        RequestDelegate next,
        ITraceCollector traceCollector,
        ILogger<TracingMiddleware> logger,
        string serviceName)
    {
        _next = next;
        _traceCollector = traceCollector;
        _logger = logger;
        _serviceName = serviceName;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. 从请求头提取或创建 TraceContext
        var headers = context.Request.Headers
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());
        
        var traceContext = TracePropagation.ExtractFromHeaders(headers);
        
        // 2. 设置当前上下文
        TraceContextHolder.Current = traceContext;
        
        // 3. 创建 Span
        var span = new TraceSpan
        {
            TraceId = traceContext.TraceId,
            SpanId = traceContext.SpanId,
            ParentSpanId = traceContext.ParentSpanId,
            ServiceName = _serviceName,
            OperationName = $"{context.Request.Method} {context.Request.Path}",
            StartTime = DateTime.UtcNow,
            Tags = new Dictionary<string, string>
            {
                ["http.method"] = context.Request.Method,
                ["http.url"] = context.Request.Path,
                ["http.host"] = context.Request.Host.ToString(),
                ["http.user_agent"] = context.Request.Headers.UserAgent.ToString()
            }
        };

        // 4. 将 Trace 信息注入响应头（便于客户端追踪）
        context.Response.Headers[TracePropagation.TraceParentHeader] = TracePropagation.EncodeTraceParent(traceContext);

        try
        {
            _logger.LogDebug(
                "开始处理请求 [{TraceId}] {Method} {Path}",
                traceContext.TraceId[..8],
                context.Request.Method,
                context.Request.Path);

            // 5. 执行后续中间件
            await _next(context);

            // 6. 记录成功
            span.Status = SpanStatus.Ok;
            span.Tags["http.status_code"] = context.Response.StatusCode.ToString();
        }
        catch (Exception ex)
        {
            // 7. 记录错误
            span.Status = SpanStatus.Error;
            span.ErrorMessage = ex.Message;
            span.Tags["error"] = "true";
            span.Tags["error.message"] = ex.Message;
            
            _logger.LogError(ex,
                "请求处理异常 [{TraceId}] {Method} {Path}: {Error}",
                traceContext.TraceId[..8],
                context.Request.Method,
                context.Request.Path,
                ex.Message);
            
            throw;
        }
        finally
        {
            // 8. 完成 Span
            span.EndTime = DateTime.UtcNow;
            
            _logger.LogInformation(
                "请求完成 [{TraceId}] {Method} {Path} - {StatusCode} - {DurationMs}ms",
                span.TraceId[..8],
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                span.Duration.TotalMilliseconds);

            // 9. 异步收集 Span
            _ = _traceCollector.CollectAsync(span);
            
            // 10. 清理上下文
            TraceContextHolder.Current = null;
        }
    }
}

/// <summary>
/// 扩展方法
/// </summary>
public static class TracingMiddlewareExtensions
{
    public static IApplicationBuilder UseTracing(
        this IApplicationBuilder app,
        string serviceName)
    {
        return app.UseMiddleware<TracingMiddleware>(serviceName);
    }
}
