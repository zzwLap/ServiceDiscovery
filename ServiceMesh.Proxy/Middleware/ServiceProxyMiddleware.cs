using ServiceMesh.Proxy.Services;

namespace ServiceMesh.Proxy.Middleware;

/// <summary>
/// 服务代理中间件
/// </summary>
public class ServiceProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ServiceProxyMiddleware> _logger;
    private readonly DynamicProxyService _proxyService;

    public ServiceProxyMiddleware(
        RequestDelegate next,
        ILogger<ServiceProxyMiddleware> logger,
        DynamicProxyService proxyService)
    {
        _next = next;
        _logger = logger;
        _proxyService = proxyService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        
        // 从路径中提取服务名 /api/{serviceName}/xxx
        var path = request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            await _next(context);
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] != "api")
        {
            await _next(context);
            return;
        }

        var serviceName = segments[1];
        
        _logger.LogDebug("代理请求: {ServiceName} - {Path}", serviceName, path);

        // 创建转发请求
        var forwardRequest = new HttpRequestMessage
        {
            Method = new HttpMethod(request.Method),
            RequestUri = new Uri($"http://localhost{path}")
        };

        // 复制请求体
        if (request.ContentLength > 0)
        {
            forwardRequest.Content = new StreamContent(request.Body);
            if (request.ContentType != null)
            {
                forwardRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
            }
        }

        // 复制请求头
        foreach (var header in request.Headers)
        {
            forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // 转发请求
        var response = await _proxyService.ProxyRequestAsync(serviceName, forwardRequest, context.RequestAborted);

        // 设置响应状态码
        context.Response.StatusCode = (int)response.StatusCode;

        // 复制响应头
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        // 写入响应体
        await response.Content.CopyToAsync(context.Response.Body);
    }
}

/// <summary>
/// 扩展方法
/// </summary>
public static class ServiceProxyMiddlewareExtensions
{
    public static IApplicationBuilder UseServiceProxy(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ServiceProxyMiddleware>();
    }
}
