using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ServiceMesh.Agent;

/// <summary>
/// 默认健康检查中间件 - 为简单应用提供基础的健康检查响应
/// 配置从 ServiceRegistrationOptions 统一获取
/// </summary>
public class DefaultHealthCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ServiceRegistrationOptions _options;

    public DefaultHealthCheckMiddleware(
        RequestDelegate next,
        IOptions<ServiceRegistrationOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var healthCheckPath = _options.HealthCheckUrl ?? $"/health";

        // 只处理健康检查路径
        if (!context.Request.Path.Equals(healthCheckPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 构建健康检查响应
        var response = new
        {
            status = HealthStatus.Healthy.ToString(),
            service = _options.ServiceName,
            timestamp = DateTime.UtcNow,
            checks = new Dictionary<string, object>
            {
                ["self"] = new
                {
                    status = "Healthy",
                    description = "服务运行正常"
                }
            }
        };

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(context.Response.Body, response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
