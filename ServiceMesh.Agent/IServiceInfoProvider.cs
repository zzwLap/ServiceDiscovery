namespace ServiceMesh.Agent;

/// <summary>
/// 服务信息提供者接口 - 用于动态获取服务注册所需的信息
/// </summary>
public interface IServiceInfoProvider
{
    /// <summary>
    /// 获取服务名称
    /// </summary>
    /// <returns>服务名称，如果无法获取则返回null</returns>
    string? GetServiceName();

    /// <summary>
    /// 获取服务端口号
    /// </summary>
    /// <returns>端口号，如果无法获取则返回0</returns>
    int GetPort();

    /// <summary>
    /// 获取服务主机地址
    /// </summary>
    /// <returns>主机地址，如果无法获取则返回null</returns>
    string? GetHost();
}
