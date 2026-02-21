namespace ServiceMesh.Agent;

/// <summary>
/// 服务注册配置选项
/// </summary>
public class ServiceRegistrationOptions
{
    /// <summary>
    /// 注册中心地址
    /// </summary>
    public string RegistryUrl { get; set; } = "http://localhost:5000";

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 服务主机地址（为空则自动获取）
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 服务端口号
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 服务版本
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 健康检查地址
    /// </summary>
    public string? HealthCheckUrl { get; set; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// 服务权重
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否自动注册
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>
    /// 注册重试次数
    /// </summary>
    public int RegisterRetryCount { get; set; } = 3;

    /// <summary>
    /// 注册重试间隔
    /// </summary>
    public TimeSpan RegisterRetryInterval { get; set; } = TimeSpan.FromSeconds(5);
}
