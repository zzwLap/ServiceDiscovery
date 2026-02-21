using System.Text.Json.Serialization;

namespace ServiceMesh.Core.Models;

/// <summary>
/// 服务实例信息
/// </summary>
public class ServiceInstance
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 服务名称
    /// </summary>
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口号
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; set; }

    /// <summary>
    /// 服务版本
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 服务标签/元数据
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// 健康检查地址
    /// </summary>
    [JsonPropertyName("healthCheckUrl")]
    public string? HealthCheckUrl { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    [JsonPropertyName("registeredAt")]
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    [JsonPropertyName("lastHeartbeat")]
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 服务状态
    /// </summary>
    [JsonPropertyName("status")]
    public ServiceStatus Status { get; set; } = ServiceStatus.Healthy;

    /// <summary>
    /// 权重（用于负载均衡）
    /// </summary>
    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 100;

    public string GetAddress() => $"{Host}:{Port}";
}

/// <summary>
/// 服务状态
/// </summary>
public enum ServiceStatus
{
    Unknown = 0,
    Healthy = 1,
    Unhealthy = 2,
    Offline = 3
}
