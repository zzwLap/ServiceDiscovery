using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Core.HealthCheck;

/// <summary>
/// HTTP 健康检查器
/// </summary>
public class HttpHealthChecker : IHealthChecker
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    public HttpHealthChecker(HttpClient httpClient, TimeSpan? timeout = null)
    {
        _httpClient = httpClient;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<bool> CheckHealthAsync(ServiceInstance instance, CancellationToken cancellationToken = default)
    {
        try
        {
            var healthUrl = instance.HealthCheckUrl;
            if (string.IsNullOrEmpty(healthUrl))
            {
                // 默认健康检查端点
                healthUrl = $"http://{instance.GetAddress()}/health";
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var response = await _httpClient.GetAsync(healthUrl, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// TCP 健康检查器
/// </summary>
public class TcpHealthChecker : IHealthChecker
{
    private readonly TimeSpan _timeout;

    public TcpHealthChecker(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<bool> CheckHealthAsync(ServiceInstance instance, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(instance.Host, instance.Port);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
