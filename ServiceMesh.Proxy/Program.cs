using Yarp.ReverseProxy;
using ServiceMesh.Core.Tracing;
using ServiceMesh.Discovery;
using ServiceMesh.Proxy.Configuration;
using ServiceMesh.Proxy.Middleware;
using ServiceMesh.Proxy.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 添加动态代理配置提供程序
builder.Services.AddSingleton<DynamicProxyConfigProvider>();
builder.Services.AddSingleton<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>(sp => sp.GetRequiredService<DynamicProxyConfigProvider>());

// 添加YARP反向代理服务
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 添加 HttpClient（配置连接池和大文件传输优化）
builder.Services.AddHttpClient("ProxyClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // 连接池配置
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 100,
        EnableMultipleHttp2Connections = true,
        
        // TCP Keep-Alive
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
        
        // 大文件传输优化
        MaxResponseHeadersLength = 1024 * 1024, // 1MB响应头限制
        ResponseDrainTimeout = TimeSpan.FromSeconds(30), // 响应排空超时
    })
    .ConfigureHttpClient(client =>
    {
        // 大文件传输超时设置
        client.Timeout = TimeSpan.FromMinutes(30); // 大文件上传/下载需要更长的超时
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan); // 不自动清理 Handler

// 添加专门用于大文件传输的HTTP客户端
builder.Services.AddHttpClient("FileTransferClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // 大文件传输专用配置
        MaxConnectionsPerServer = 20, // 较少的连接数，但每个连接更稳定
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        
        // 启用TCP Keep-Alive
        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
        
        // 禁用代理自动检测以提高性能
        UseProxy = false,
    })
    .ConfigureHttpClient(client =>
    {
        // 大文件传输需要很长的超时时间
        client.Timeout = TimeSpan.FromHours(2);
    });

// 添加服务发现
builder.Services.AddServiceDiscovery(options =>
{
    options.RegistryUrl = builder.Configuration.GetValue<string>("RegistryUrl") ?? "http://localhost:5000";
    options.CacheRefreshInterval = TimeSpan.FromSeconds(30);
});

// 添加链路追踪
builder.Services.AddSingleton<ITraceCollector, InMemoryTraceCollector>();

// 添加动态代理服务
builder.Services.AddSingleton<DynamicProxyService>();

// 添加YARP配置更新服务
builder.Services.AddSingleton<IYarpConfigUpdater, YarpConfigUpdater>();

// 添加YARP同步后台服务
builder.Services.AddHostedService<YarpSyncBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 使用链路追踪中间件
app.UseTracing("ServiceMesh.Proxy");

// 使用服务代理中间件
app.UseServiceProxy();

app.MapControllers();

app.Run();
