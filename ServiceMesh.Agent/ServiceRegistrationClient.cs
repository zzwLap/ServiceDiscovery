using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceMesh.Core.Models;

namespace ServiceMesh.Agent;

/// <summary>
/// 服务注册客户端 - 用于服务自动注册和心跳维护
/// </summary>
public class ServiceRegistrationClient : IHostedService, IDisposable
{
    private readonly ServiceRegistrationOptions _options;
    private readonly ILogger<ServiceRegistrationClient> _logger;
    private readonly HttpClient _httpClient;
    private Timer? _heartbeatTimer;
    private string? _instanceId;
    private bool _isRegistered = false;

    public ServiceRegistrationClient(
        IOptions<ServiceRegistrationOptions> options,
        ILogger<ServiceRegistrationClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoRegister)
        {
            _logger.LogInformation("自动注册已禁用");
            return;
        }

        if (string.IsNullOrEmpty(_options.ServiceName))
        {
            throw new InvalidOperationException("服务名称不能为空");
        }

        if (_options.Port <= 0)
        {
            throw new InvalidOperationException("服务端口号必须大于0");
        }

        // 自动获取主机地址
        var host = _options.Host ?? GetLocalIpAddress();
        if (string.IsNullOrEmpty(host))
        {
            throw new InvalidOperationException("无法获取本地IP地址，请手动配置");
        }

        // 尝试注册服务（带重试）
        for (int i = 0; i < _options.RegisterRetryCount; i++)
        {
            try
            {
                _instanceId = await RegisterAsync(host, cancellationToken);
                if (!string.IsNullOrEmpty(_instanceId))
                {
                    _isRegistered = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "服务注册失败，第{Retry}次重试", i + 1);
                if (i < _options.RegisterRetryCount - 1)
                {
                    await Task.Delay(_options.RegisterRetryInterval, cancellationToken);
                }
            }
        }

        if (!_isRegistered)
        {
            throw new InvalidOperationException($"服务注册失败，已重试{_options.RegisterRetryCount}次");
        }

        // 启动心跳定时器
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            _options.HeartbeatInterval,
            _options.HeartbeatInterval);

        _logger.LogInformation("服务注册客户端已启动，实例ID: {InstanceId}", _instanceId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Change(Timeout.Infinite, 0);

        if (_isRegistered && !string.IsNullOrEmpty(_instanceId))
        {
            try
            {
                await DeregisterAsync(cancellationToken);
                _logger.LogInformation("服务已成功注销");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "服务注销失败");
            }
        }

        _logger.LogInformation("服务注册客户端已停止");
    }

    /// <summary>
    /// 注册服务
    /// </summary>
    private async Task<string?> RegisterAsync(string host, CancellationToken cancellationToken)
    {
        var request = new ServiceRegistryRequest
        {
            ServiceName = _options.ServiceName,
            Host = host,
            Port = _options.Port,
            Version = _options.Version,
            Metadata = _options.Metadata,
            HealthCheckUrl = _options.HealthCheckUrl ?? $"http://{host}:{_options.Port}/health",
            Weight = _options.Weight
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{_options.RegistryUrl}/api/registry/register";
        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<ServiceRegistryResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Success == true)
            {
                _logger.LogInformation("服务注册成功: {ServiceName} - {InstanceId}", 
                    _options.ServiceName, result.InstanceId);
                return result.InstanceId;
            }
        }

        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"注册失败: {response.StatusCode}, {errorContent}");
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    private async Task DeregisterAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_instanceId))
            return;

        var url = $"{_options.RegistryUrl}/api/registry/deregister/{_instanceId}";
        var response = await _httpClient.PostAsync(url, null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        if (string.IsNullOrEmpty(_instanceId))
            return;

        try
        {
            var request = new HeartbeatRequest
            {
                InstanceId = _instanceId,
                ServiceName = _options.ServiceName
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_options.RegistryUrl}/api/registry/heartbeat";
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("心跳发送失败: {StatusCode}", response.StatusCode);
            }
            else
            {
                _logger.LogDebug("心跳发送成功: {InstanceId}", _instanceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "心跳发送异常");
        }
    }

    /// <summary>
    /// 获取本地IP地址
    /// </summary>
    private string? GetLocalIpAddress()
    {
        try
        {
            // 优先获取非回环的IPv4地址
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }

            // 备选：通过网络接口获取
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && 
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var props = ni.GetIPProperties();
                    var address = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && 
                                             !IPAddress.IsLoopback(a.Address));
                    if (address != null)
                    {
                        return address.Address.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取本地IP地址失败");
        }

        return null;
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _httpClient?.Dispose();
    }
}
