using System.Net.Http;

namespace ServiceMesh.Proxy.Services
{
    /// <summary>
    /// 默认代理调用器实现
    /// </summary>
    public class DefaultProxyInvoker
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public DefaultProxyInvoker(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<HttpResponseMessage> InvokeAsync(
            string clusterId,
            string destinationId,
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // 使用现有的HttpClient工厂创建客户端
            var httpClient = _httpClientFactory.CreateClient("ProxyClient");
            
            // 根据集群和目标ID构建最终的目标URL
            // 这里需要从配置或其他地方获取实际的目标地址
            // 为了示例目的，我们将使用一个虚拟的路由逻辑
            
            var response = await httpClient.SendAsync(request, cancellationToken);
            return response;
        }
    }
}