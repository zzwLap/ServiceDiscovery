using System.Net;
using Yarp.ReverseProxy;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Proxy.Services;

/// <summary>
/// 动态代理服务
/// </summary>
public class DynamicProxyService
{
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DynamicProxyService> _logger;
    private readonly DefaultProxyInvoker _proxyInvoker;
    
    // 断路器策略
    private readonly AsyncCircuitBreakerPolicy<HttpResponseMessage> _circuitBreakerPolicy;
    private readonly AsyncTimeoutPolicy _timeoutPolicy;

    public DynamicProxyService(
        IServiceDiscovery serviceDiscovery,
        IHttpClientFactory httpClientFactory,
        ILogger<DynamicProxyService> logger,
        DefaultProxyInvoker? proxyInvoker = null)
    {
        _serviceDiscovery = serviceDiscovery;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _proxyInvoker = proxyInvoker ?? new DefaultProxyInvoker(httpClientFactory);

        // 配置断路器：连续5次失败后断开30秒
        _circuitBreakerPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (result, duration) =>
                {
                    _logger.LogWarning("断路器打开，持续时间: {Duration}", duration);
                },
                onReset: () =>
                {
                    _logger.LogInformation("断路器关闭");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("断路器半开，尝试恢复");
                });

        // 配置超时：10秒
        _timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// 使用YARP执行代理请求
    /// </summary>
    public async Task<HttpResponseMessage> ProxyRequestWithYarpAsync(
        string clusterId,
        string destinationId,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 使用YARP的代理调用器执行请求
            var proxyResponse = await _proxyInvoker.InvokeAsync(
                clusterId,
                destinationId,
                request,
                cancellationToken);
            
            return proxyResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YARP代理请求失败: {ClusterId}/{DestinationId}", clusterId, destinationId);
            throw;
        }
    }

    /// <summary>
    /// 转发请求到目标服务 - 优化大文件传输
    /// </summary>
    public async Task<HttpResponseMessage> ProxyRequestAsync(
        string serviceName,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        // 1. 服务发现
        var instance = await _serviceDiscovery.GetHealthyInstanceAsync(serviceName, cancellationToken: cancellationToken);
        
        if (instance == null)
        {
            _logger.LogError("未找到可用的服务实例: {ServiceName}", serviceName);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent($"Service '{serviceName}' is not available")
            };
        }

        // 2. 构建目标URL
        var targetUrl = BuildTargetUrl(instance, request);
        _logger.LogInformation("转发请求到: {TargetUrl}", targetUrl);

        // 3. 创建转发请求（保留原始请求的内容流）
        var forwardRequest = new HttpRequestMessage(request.Method, targetUrl);
        
        // 如果存在请求内容，使用流式复制
        if (request.Content != null)
        {
            // 检查是否为流式内容
            if (request.Content is StreamContent originalStreamContent)
            {
                // 重新创建StreamContent以支持重新读取
                var stream = await originalStreamContent.ReadAsStreamAsync();
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }
                
                // 使用64KB缓冲区创建新的StreamContent
                var newStreamContent = new StreamContent(stream, 64 * 1024);
                
                // 复制内容头部
                foreach (var header in request.Content.Headers)
                {
                    newStreamContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                
                forwardRequest.Content = newStreamContent;
            }
            else
            {
                // 对于非流式内容，直接复制
                forwardRequest.Content = request.Content;
            }
        }

        // 复制请求头
        foreach (var header in request.Headers)
        {
            forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 4. 执行请求（带断路器和超时）- 使用ResponseHeadersRead实现流式响应
        // 根据内容大小选择合适的客户端
        var isLargeFile = request.Content?.Headers.ContentLength > 10 * 1024 * 1024;
        var clientName = isLargeFile ? "FileTransferClient" : "ProxyClient";
        var client = _httpClientFactory.CreateClient(clientName);
        
        try
        {
            var response = await _circuitBreakerPolicy
                .WrapAsync(_timeoutPolicy)
                .ExecuteAsync(async ct =>
                {
                    // 使用ResponseHeadersRead模式，立即返回响应头，不等待整个内容加载
                    var result = await client.SendAsync(
                        forwardRequest, 
                        HttpCompletionOption.ResponseHeadersRead, 
                        ct);
                    return result;
                }, cancellationToken);

            return response;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogError("断路器已打开，请求被拒绝: {ServiceName}", serviceName);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Circuit breaker is open")
            };
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogError("请求超时: {ServiceName}", serviceName);
            return new HttpResponseMessage(HttpStatusCode.RequestTimeout)
            {
                Content = new StringContent("Request timeout")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转发请求失败: {ServiceName}", serviceName);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent($"Proxy error: {ex.Message}")
            };
        }
    }

    /// <summary>
    /// 构建目标URL
    /// </summary>
    private string BuildTargetUrl(ServiceInstance instance, HttpRequestMessage request)
    {
        var path = request.RequestUri?.PathAndQuery ?? "/";
        
        // 统一处理路径：移除 /svc/{serviceName} 前缀
        // 使用 /svc 作为代理前缀，避免与服务端的 /api 前缀冲突
        // 支持格式: /svc/serviceName/xxx 或 /svc/ServiceName/xxx
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        if (segments.Length >= 2 && 
            segments[0].Equals("svc", StringComparison.OrdinalIgnoreCase))
        {
            // 跳过 /svc/{serviceName}，保留后面的路径
            var remainingSegments = segments.Skip(2);
            path = "/" + string.Join("/", remainingSegments);
        }
        
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            path = "/";
        }

        return $"http://{instance.GetAddress()}{path}";
    }
}
