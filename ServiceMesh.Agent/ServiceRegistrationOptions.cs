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
    /// 注册重试次数（0表示无限重试直到成功）
    /// </summary>
    public int RegisterRetryCount { get; set; } = 3;

    /// <summary>
    /// 注册重试间隔
    /// </summary>
    public TimeSpan RegisterRetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 注册失败后的处理策略
    /// </summary>
    public RegistrationFailurePolicy FailurePolicy { get; set; } = RegistrationFailurePolicy.ContinueAndRetry;

    /// <summary>
    /// 是否启用默认健康检查中间件
    /// 启用后会自动响应 /health 路径的健康检查请求
    /// </summary>
    public bool EnableDefaultHealthCheck { get; set; } = false;

    /// <summary>
    /// 健康检查路径，默认为 /health
    /// </summary>
    public string HealthCheckPath { get; set; } = "/health";
}

/// <summary>
/// 服务注册失败处理策略
/// </summary>
public enum RegistrationFailurePolicy
{
    /// <summary>
    /// 继续运行并在后台持续重试注册
    /// </summary>
    ContinueAndRetry,

    /// <summary>
    /// 继续运行但不再尝试注册（静默模式）
    /// </summary>
    ContinueWithoutRegistration,

    /// <summary>
    /// 终止服务（快速失败）
    /// </summary>
    FailFast
}
