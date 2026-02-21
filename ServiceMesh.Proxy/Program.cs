using ServiceMesh.Core.Tracing;
using ServiceMesh.Discovery;
using ServiceMesh.Proxy.Middleware;
using ServiceMesh.Proxy.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 添加 HttpClient（配置连接池）
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
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan); // 不自动清理 Handler

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
