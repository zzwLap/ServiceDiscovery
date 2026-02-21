using Microsoft.AspNetCore.Mvc;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Proxy.Services;

namespace ServiceMesh.Proxy.Controllers;

/// <summary>
/// 网关控制器 - 提供统一的API入口
/// </summary>
[ApiController]
[Route("gateway/{serviceName}")]
public class GatewayController : ControllerBase
{
    private readonly DynamicProxyService _proxyService;
    private readonly ILogger<GatewayController> _logger;

    public GatewayController(DynamicProxyService proxyService, ILogger<GatewayController> logger)
    {
        _proxyService = proxyService;
        _logger = logger;
    }

    [HttpGet("{**path}")]
    public async Task<IActionResult> Get(string serviceName, string? path)
    {
        return await ProxyRequest(serviceName, path, HttpMethod.Get);
    }

    [HttpPost("{**path}")]
    public async Task<IActionResult> Post(string serviceName, string? path)
    {
        return await ProxyRequest(serviceName, path, HttpMethod.Post);
    }

    [HttpPut("{**path}")]
    public async Task<IActionResult> Put(string serviceName, string? path)
    {
        return await ProxyRequest(serviceName, path, HttpMethod.Put);
    }

    [HttpDelete("{**path}")]
    public async Task<IActionResult> Delete(string serviceName, string? path)
    {
        return await ProxyRequest(serviceName, path, HttpMethod.Delete);
    }

    private async Task<IActionResult> ProxyRequest(string serviceName, string? path, HttpMethod method)
    {
        var targetPath = $"/api/{path ?? ""}";
        var requestUri = new Uri($"http://localhost{targetPath}");

        var request = new HttpRequestMessage(method, requestUri);

        // 复制请求体
        if (Request.ContentLength > 0)
        {
            request.Content = new StreamContent(Request.Body);
            if (Request.ContentType != null)
            {
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(Request.ContentType);
            }
        }

        // 复制请求头
        foreach (var header in Request.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        var response = await _proxyService.ProxyRequestAsync(serviceName, request, HttpContext.RequestAborted);

        var content = await response.Content.ReadAsByteArrayAsync();
        
        return new ContentResult
        {
            Content = System.Text.Encoding.UTF8.GetString(content),
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            StatusCode = (int)response.StatusCode
        };
    }
}
