namespace ServiceMesh.Core.Tracing;

/// <summary>
/// 带链路追踪的 HTTP 消息处理器
/// </summary>
public class TracingHttpHandler : DelegatingHandler
{
    private readonly ITraceCollector _traceCollector;
    private readonly string _serviceName;

    public TracingHttpHandler(
        ITraceCollector traceCollector,
        string serviceName)
        : base(new HttpClientHandler())
    {
        _traceCollector = traceCollector;
        _serviceName = serviceName;
    }

    public TracingHttpHandler(
        ITraceCollector traceCollector,
        string serviceName,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _traceCollector = traceCollector;
        _serviceName = serviceName;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 1. 获取当前上下文
        var parentContext = TraceContextHolder.Current;
        var traceContext = parentContext?.CreateChildContext() ?? new TraceContext();

        // 2. 将 Trace 信息注入请求头
        var headers = new Dictionary<string, string>();
        TracePropagation.InjectIntoHeaders(traceContext, headers);
        
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 3. 创建 Span
        var span = new TraceSpan
        {
            TraceId = traceContext.TraceId,
            SpanId = traceContext.SpanId,
            ParentSpanId = parentContext?.SpanId,
            ServiceName = _serviceName,
            OperationName = $"HTTP {request.Method} {request.RequestUri?.Host}",
            StartTime = DateTime.UtcNow,
            Tags = new Dictionary<string, string>
            {
                ["http.method"] = request.Method.ToString(),
                ["http.url"] = request.RequestUri?.ToString() ?? "",
                ["http.target"] = request.RequestUri?.PathAndQuery ?? "",
                ["peer.service"] = request.RequestUri?.Host ?? ""
            }
        };

        // 4. 设置当前上下文（用于嵌套调用）
        var originalContext = TraceContextHolder.Current;
        TraceContextHolder.Current = traceContext;

        try
        {
            // 5. 发送请求
            var response = await base.SendAsync(request, cancellationToken);

            // 6. 记录响应
            span.Status = response.IsSuccessStatusCode ? SpanStatus.Ok : SpanStatus.Error;
            span.Tags["http.status_code"] = ((int)response.StatusCode).ToString();

            return response;
        }
        catch (Exception ex)
        {
            span.Status = SpanStatus.Error;
            span.ErrorMessage = ex.Message;
            span.Tags["error"] = "true";
            span.Tags["error.type"] = ex.GetType().Name;
            throw;
        }
        finally
        {
            // 7. 完成 Span
            span.EndTime = DateTime.UtcNow;
            _ = _traceCollector.CollectAsync(span);

            // 8. 恢复上下文
            TraceContextHolder.Current = originalContext;
        }
    }
}

/// <summary>
/// 带链路追踪的 HTTP 客户端工厂
/// </summary>
public static class TracingHttpClient
{
    /// <summary>
    /// 创建带追踪的 HttpClient
    /// </summary>
    public static HttpClient Create(
        ITraceCollector traceCollector,
        string serviceName)
    {
        var handler = new TracingHttpHandler(traceCollector, serviceName);
        return new HttpClient(handler);
    }

    /// <summary>
    /// 创建带追踪的 HttpClient（带连接池配置）
    /// </summary>
    public static HttpClient CreateWithPool(
        ITraceCollector traceCollector,
        string serviceName)
    {
        var socketHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 100
        };

        var tracingHandler = new TracingHttpHandler(traceCollector, serviceName, socketHandler);
        return new HttpClient(tracingHandler);
    }
}
