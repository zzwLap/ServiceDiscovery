using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Forwarder;
using ServiceMesh.Proxy.Configuration;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpForwarder _httpForwarder;
    private readonly DynamicProxyConfigProvider _dynamicConfigProvider;

    public ServiceProxyMiddleware(
        RequestDelegate next,
        ILogger<ServiceProxyMiddleware> logger,
        DynamicProxyService proxyService,
        IServiceProvider serviceProvider,
        IHttpForwarder httpForwarder,
        DynamicProxyConfigProvider dynamicConfigProvider)
    {
        _next = next;
        _logger = logger;
        _proxyService = proxyService;
        _serviceProvider = serviceProvider;
        _httpForwarder = httpForwarder;
        _dynamicConfigProvider = dynamicConfigProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        
        // 从路径中提取服务名 /svc/{serviceName}/xxx
        // 使用 /svc 作为代理前缀，避免与服务端的 /api 前缀冲突
        var path = request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            await _next(context);
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("svc", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var serviceName = segments[1];
        
        _logger.LogDebug("代理请求: {ServiceName} - {Path}", serviceName, path);

        // 检查动态配置中是否有该服务的路由
        if (_dynamicConfigProvider.HasRoute(serviceName))
        {
            // 使用YARP处理请求
            await ProcessWithYarp(context, request, serviceName);
            return;
        }
        
        // 检查是否有对应的YARP路由配置
        var configProvider = _serviceProvider.GetService<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>();
        if (configProvider != null)
        {
            var config = configProvider.GetConfig();
            var route = config.Routes.FirstOrDefault(r => 
                r.RouteId.Equals(serviceName, StringComparison.OrdinalIgnoreCase) ||
                r.RouteId.Equals($"route-{serviceName}", StringComparison.OrdinalIgnoreCase));
            
            if (route != null)
            {
                // 使用YARP处理请求
                await ProcessWithYarp(context, request, serviceName);
                return;
            }
        }
        
        // 使用传统的代理服务处理请求
        await ProcessWithTraditionalProxy(context, request, path, serviceName);
    }

    /// <summary>
    /// 使用YARP处理请求 - 优化大文件传输
    /// </summary>
    private async Task ProcessWithYarp(HttpContext context, HttpRequest request, string serviceName)
    {
        try
        {
            // 获取服务的健康实例
            var serviceDiscovery = _serviceProvider.GetRequiredService<ServiceMesh.Core.Interfaces.IServiceDiscovery>();
            var instance = await serviceDiscovery.GetHealthyInstanceAsync(serviceName, cancellationToken: context.RequestAborted);
            
            if (instance == null)
            {
                _logger.LogError("未找到可用的服务实例: {ServiceName}", serviceName);
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync($"Service '{serviceName}' is not available");
                return;
            }

            // 构建目标地址
            var destinationAddress = $"http://{instance.GetAddress()}";
            
            // 根据请求大小选择合适的HTTP客户端
            var isLargeFile = request.ContentLength > 10 * 1024 * 1024; // 大于10MB视为大文件
            var clientName = isLargeFile ? "FileTransferClient" : "ProxyClient";
            var httpClient = _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            
            if (isLargeFile)
            {
                _logger.LogInformation("检测到大型文件传输，使用专用客户端: {ServiceName}, 大小: {Size} bytes", 
                    serviceName, request.ContentLength);
            }
            
            // 转发请求 - 针对大文件优化配置
            var error = await _httpForwarder.SendAsync(
                context,
                destinationAddress,
                httpClient,
                new ForwarderRequestConfig
                {
                    ActivityTimeout = isLargeFile ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(30),
                    Version = new Version(1, 1),
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                    // 禁用响应缓冲，实现流式传输
                    AllowResponseBuffering = false
                },
                HttpTransformer.Default,
                cancellationToken: context.RequestAborted);

            if (error != ForwarderError.None)
            {
                var errorFeature = context.Features.Get<IForwarderErrorFeature>();
                var exception = errorFeature?.Exception;
                _logger.LogError(exception, "YARP转发请求失败: {Error}", error);
                
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 502;
                    await context.Response.WriteAsync($"YARP代理错误: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YARP代理请求失败");
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"YARP代理错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 使用传统代理服务处理请求 - 优化大文件传输
    /// </summary>
    private async Task ProcessWithTraditionalProxy(HttpContext context, HttpRequest request, string path, string serviceName)
    {
        // 保持原始路径，让 DynamicProxyService 统一处理路径解析
        // 原始路径: /svc/serviceName/xxx/yyy
        // DynamicProxyService.BuildTargetUrl 会移除 /svc/{serviceName} 前缀
        
        // 创建转发请求
        var forwardRequest = new HttpRequestMessage
        {
            Method = new HttpMethod(request.Method),
            RequestUri = new Uri($"http://localhost{path}")
        };

        // 复制请求体 - 使用优化的流处理
        if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            // 使用较大的缓冲区（64KB）提高大文件传输性能
            var streamContent = new StreamContent(request.Body, 64 * 1024);
            
            if (request.ContentType != null)
            {
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
            }
            
            // 保留原始内容长度信息（如果存在）
            if (request.ContentLength.HasValue)
            {
                streamContent.Headers.ContentLength = request.ContentLength;
            }
            
            forwardRequest.Content = streamContent;
        }

        // 复制请求头（排除已自动处理的头部）
        foreach (var header in request.Headers)
        {
            // 跳过内容相关的头部，由StreamContent自动处理
            if (!header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        // 转发请求 - 使用ResponseHeadersRead模式实现流式传输
        using var response = await _proxyService.ProxyRequestAsync(
            serviceName, 
            forwardRequest, 
            context.RequestAborted);

        // 设置响应状态码
        context.Response.StatusCode = (int)response.StatusCode;

        // 复制响应头
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            // 避免重复设置Content-Length（如果已设置）
            if (!context.Response.Headers.ContainsKey(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        // 使用优化的流式复制（64KB缓冲区）
        var buffer = new byte[64 * 1024]; // 64KB缓冲区
        await response.Content.CopyToAsync(
            context.Response.Body, 
            context.RequestAborted);
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
