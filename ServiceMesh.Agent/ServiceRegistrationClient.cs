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
    private readonly IServiceInfoProvider _serviceInfoProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private Timer? _heartbeatTimer;
    private string? _instanceId;
    private bool _isRegistered = false;

    public ServiceRegistrationClient(
        IOptions<ServiceRegistrationOptions> options,
        ILogger<ServiceRegistrationClient> logger,
        IServiceInfoProvider serviceInfoProvider,
        IHostApplicationLifetime lifetime)
    {
        _options = options.Value;
        _logger = logger;
        _serviceInfoProvider = serviceInfoProvider;
        _lifetime = lifetime;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoRegister)
        {
            _logger.LogInformation("自动注册已禁用");
            return Task.CompletedTask;
        }

        // 延迟到应用程序启动完成后再执行注册
        _lifetime.ApplicationStarted.Register(() =>
        {
            _ = RegisterServiceAsync(_lifetime.ApplicationStopping);
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行服务注册
    /// </summary>
    private async Task RegisterServiceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 如果服务名称为空，尝试从提供者获取
            if (string.IsNullOrEmpty(_options.ServiceName))
            {
                var serviceNameFromProvider = _serviceInfoProvider.GetServiceName();
                if (!string.IsNullOrEmpty(serviceNameFromProvider))
                {
                    _options.ServiceName = serviceNameFromProvider;
                    _logger.LogInformation("从服务信息提供者获取服务名称: {ServiceName}", _options.ServiceName);
                }
            }

            // 如果端口未设置，尝试从提供者获取
            if (_options.Port <= 0)
            {
                var portFromProvider = _serviceInfoProvider.GetPort();
                if (portFromProvider > 0)
                {
                    _options.Port = portFromProvider;
                    _logger.LogInformation("从服务信息提供者获取端口号: {Port}", _options.Port);
                }
            }

            if (string.IsNullOrEmpty(_options.ServiceName))
            {
                _logger.LogError("服务名称不能为空，请配置服务名称或实现 IServiceInfoProvider 接口");
                return;
            }

            if (_options.Port <= 0)
            {
                _logger.LogError("服务端口号必须大于0，请配置端口号或实现 IServiceInfoProvider 接口");
                return;
            }

            // 自动获取主机地址
            var host = _options.Host ?? _serviceInfoProvider.GetHost() ?? GetLocalIpAddress();
            if (string.IsNullOrEmpty(host))
            {
                _logger.LogError("无法获取本地IP地址，请手动配置");
                return;
            }

            // 尝试注册服务（带重试）
            var registrationSuccess = await TryRegisterAsync(host, cancellationToken);

            if (!registrationSuccess)
            {
                // 根据配置的策略处理注册失败
                await HandleRegistrationFailureAsync(host, cancellationToken);
            }
            else
            {
                // 启动心跳定时器
                StartHeartbeatTimer();
                _logger.LogInformation("服务注册客户端已启动，实例ID: {InstanceId}", _instanceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务注册过程中发生异常");
        }
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
    /// 尝试注册服务（带重试）
    /// </summary>
    private async Task<bool> TryRegisterAsync(string host, CancellationToken cancellationToken)
    {
        // 如果设置为无限重试（0），则使用int.MaxValue作为上限
        var maxRetries = _options.RegisterRetryCount == 0 ? int.MaxValue : _options.RegisterRetryCount;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                _instanceId = await RegisterAsync(host, cancellationToken);
                if (!string.IsNullOrEmpty(_instanceId))
                {
                    _isRegistered = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                var retryCount = i + 1;
                if (_options.RegisterRetryCount == 0)
                {
                    _logger.LogWarning(ex, "服务注册失败，正在进行第{Retry}次重试（无限重试模式）", retryCount);
                }
                else
                {
                    _logger.LogError(ex, "服务注册失败，第{Retry}次重试", retryCount);
                }

                // 检查是否还有重试机会
                if (retryCount < maxRetries)
                {
                    await Task.Delay(_options.RegisterRetryInterval, cancellationToken);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 处理注册失败
    /// </summary>
    private async Task HandleRegistrationFailureAsync(string host, CancellationToken cancellationToken)
    {
        var policy = _options.FailurePolicy;

        // 兼容旧版FailFastOnRegistrationError配置
        if (_options.FailFastOnRegistrationError)
        {
            policy = RegistrationFailurePolicy.FailFast;
        }

        switch (policy)
        {
            case RegistrationFailurePolicy.FailFast:
                _logger.LogError("服务注册失败，已重试{RetryCount}次，根据配置终止服务",
                    _options.RegisterRetryCount == 0 ? "无限" : _options.RegisterRetryCount.ToString());
                throw new InvalidOperationException($"服务注册失败，已重试{_options.RegisterRetryCount}次");

            case RegistrationFailurePolicy.ContinueWithoutRegistration:
                _logger.LogWarning("服务注册失败，根据配置将继续运行但不再尝试注册");
                _isRegistered = false;
                break;

            case RegistrationFailurePolicy.ContinueAndRetry:
            default:
                _logger.LogWarning("服务注册失败，服务将继续运行并在后台持续重试注册");
                _ = Task.Run(async () => await BackgroundRegistrationRetryAsync(host, cancellationToken), cancellationToken);
                break;
        }
    }

    /// <summary>
    /// 后台持续重试注册
    /// </summary>
    private async Task BackgroundRegistrationRetryAsync(string host, CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动后台注册重试任务");

        while (!cancellationToken.IsCancellationRequested && !_isRegistered)
        {
            try
            {
                await Task.Delay(_options.RegisterRetryInterval, cancellationToken);

                _instanceId = await RegisterAsync(host, cancellationToken);
                if (!string.IsNullOrEmpty(_instanceId))
                {
                    _isRegistered = true;
                    StartHeartbeatTimer();
                    _logger.LogInformation("后台重试注册成功，实例ID: {InstanceId}", _instanceId);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台注册重试失败，将在{RetryInterval}秒后再次尝试",
                    _options.RegisterRetryInterval.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// 启动心跳定时器
    /// </summary>
    private void StartHeartbeatTimer()
    {
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            _options.HeartbeatInterval,
            _options.HeartbeatInterval);
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
