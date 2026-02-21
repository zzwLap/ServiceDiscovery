using System.Text.Json.Serialization;

namespace ServiceMesh.Core.Models;

/// <summary>
/// 服务注册请求
/// </summary>
public class ServiceRegistryRequest
{
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("healthCheckUrl")]
    public string? HealthCheckUrl { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 100;
}

/// <summary>
/// 服务注册响应
/// </summary>
public class ServiceRegistryResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// 心跳请求
/// </summary>
public class HeartbeatRequest
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;
}

/// <summary>
/// 服务发现请求
/// </summary>
public class ServiceDiscoveryRequest
{
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("healthyOnly")]
    public bool HealthyOnly { get; set; } = true;
}

/// <summary>
/// 服务发现响应
/// </summary>
public class ServiceDiscoveryResponse
{
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("instances")]
    public List<ServiceInstance> Instances { get; set; } = new();
}
