using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServiceMesh.Agent;

/// <summary>
/// 默认服务信息提供者实现
/// </summary>
public class DefaultServiceInfoProvider : IServiceInfoProvider
{
    private readonly ILogger<DefaultServiceInfoProvider> _logger;
    private readonly IServiceProvider _serviceProvider;
    private Uri? _cachedServerUri;

    public DefaultServiceInfoProvider(
        IServiceProvider serviceProvider,
        ILogger<DefaultServiceInfoProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// 获取服务名称 - 默认使用入口程序集名称
    /// </summary>
    public string? GetServiceName()
    {
        try
        {
            // 优先使用入口程序集名称
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly?.GetName().Name is { Length: > 0 } assemblyName)
            {
                _logger.LogDebug("从入口程序集获取服务名称: {ServiceName}", assemblyName);
                return assemblyName;
            }

            // 备选：使用进程名称
            var processName = Process.GetCurrentProcess().ProcessName;
            if (!string.IsNullOrEmpty(processName) && processName != "dotnet")
            {
                _logger.LogDebug("从进程名称获取服务名称: {ServiceName}", processName);
                return processName;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取服务名称时发生异常");
        }

        return null;
    }

    /// <summary>
    /// 获取服务端口号 - 从服务器地址功能中获取
    /// </summary>
    public int GetPort()
    {
        var uri = GetServerUri();
        if (uri != null)
        {
            _logger.LogDebug("从服务器地址获取端口号: {Port}", uri.Port);
            return uri.Port;
        }

        _logger.LogWarning("无法获取服务器端口号");
        return 0;
    }

    /// <summary>
    /// 获取服务主机地址 - 从服务器地址功能中获取，如果是通配符地址则返回实际本地IP
    /// </summary>
    public string? GetHost()
    {
        var uri = GetServerUri();
        if (uri == null)
        {
            return null;
        }

        // 如果是通配符地址，获取实际本地IP
        if (IsWildcardHost(uri.Host))
        {
            var localIp = GetLocalIpAddress();
            if (!string.IsNullOrEmpty(localIp))
            {
                _logger.LogDebug("服务器绑定地址为通配符({WildcardHost})，使用本地IP: {LocalIp}", uri.Host, localIp);
                return localIp;
            }
        }

        _logger.LogDebug("从服务器地址获取主机: {Host}", uri.Host);
        return uri.Host;
    }

    /// <summary>
    /// 判断是否为通配符主机地址
    /// </summary>
    private static bool IsWildcardHost(string host)
    {
        return host == "0.0.0.0" ||
               host == "*" ||
               host == "+" ||
               host == "[::]" ||
               host.Equals("::", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取本地IP地址
    /// </summary>
    private static string? GetLocalIpAddress()
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
        catch
        {
            // 忽略异常
        }

        return null;
    }

    /// <summary>
    /// 获取服务器URI（带缓存）
    /// </summary>
    private Uri? GetServerUri()
    {
        if (_cachedServerUri != null)
        {
            return _cachedServerUri;
        }

        try
        {
            var server = _serviceProvider.GetService<IServer>();
            if (server == null)
            {
                _logger.LogWarning("无法获取IServer服务");
                return null;
            }

            var address = server.Features.Get<IServerAddressesFeature>()?.Addresses?.FirstOrDefault();
            if (address == null)
            {
                _logger.LogWarning("服务器地址列表为空");
                return null;
            }

            _cachedServerUri = new Uri(address);
            return _cachedServerUri;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取服务器地址时发生异常");
            return null;
        }
    }
}
