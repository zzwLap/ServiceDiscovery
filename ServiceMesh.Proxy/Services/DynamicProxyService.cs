using System.Net;
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
    
    // 断路器策略
    private readonly AsyncCircuitBreakerPolicy<HttpResponseMessage> _circuitBreakerPolicy;
    private readonly AsyncTimeoutPolicy _timeoutPolicy;

    public DynamicProxyService(
        IServiceDiscovery serviceDiscovery,
        IHttpClientFactory httpClientFactory,
        ILogger<DynamicProxyService> logger)
    {
        _serviceDiscovery = serviceDiscovery;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

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
    /// 转发请求到目标服务
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

        // 3. 创建转发请求
        var forwardRequest = new HttpRequestMessage(request.Method, targetUrl)
        {
            Content = request.Content
        };

        // 复制请求头
        foreach (var header in request.Headers)
        {
            forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 4. 执行请求（带断路器和超时）
        // 使用命名客户端获取连接池中的连接
        var client = _httpClientFactory.CreateClient("ProxyClient");
        
        try
        {
            var response = await _circuitBreakerPolicy
                .WrapAsync(_timeoutPolicy)
                .ExecuteAsync(async ct =>
                {
                    var result = await client.SendAsync(forwardRequest, ct);
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
        
        // 移除服务名前缀（如 /api/userservice/xxx -> /xxx）
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 1)
        {
            path = "/" + string.Join("/", segments.Skip(1));
        }

        return $"http://{instance.GetAddress()}{path}";
    }
}
