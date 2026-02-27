using System.Text.Json.Serialization;

namespace ServiceMesh.Proxy.Configuration;

/// <summary>
/// YARP反向代理配置模型
/// </summary>
public class YarpReverseProxyConfig
{
    public RouteModel[] Routes { get; set; } = Array.Empty<RouteModel>();
    public ClusterModel[] Clusters { get; set; } = Array.Empty<ClusterModel>();
}

public class RouteModel
{
    public string RouteId { get; set; } = string.Empty;
    public string Match { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
}

public class ClusterModel
{
    public string ClusterId { get; set; } = string.Empty;
    public DestinationModel[] Destinations { get; set; } = Array.Empty<DestinationModel>();
}

public class DestinationModel
{
    public string DestinationId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}