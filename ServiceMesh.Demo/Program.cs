using ServiceMesh.Agent;
using ServiceMesh.Core.Tracing;
using ServiceMesh.Discovery;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 从配置读取服务信息
var serviceName = builder.Configuration.GetValue<string>("ServiceName") ?? "DemoService";
var servicePort = builder.Configuration.GetValue<int>("Port");
if (servicePort == 0)
{
    servicePort = 5001; // 默认端口
}

// 添加服务自动注册
builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = serviceName;
    options.Port = servicePort;
    options.RegistryUrl = builder.Configuration.GetValue<string>("RegistryUrl") ?? "http://localhost:5000";
    options.Version = "1.0.0";
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.Metadata = new Dictionary<string, string>
    {
        ["environment"] = "development",
        ["team"] = "platform"
    };
});

// 添加服务发现客户端
builder.Services.AddServiceDiscovery(options =>
{
    options.RegistryUrl = builder.Configuration.GetValue<string>("RegistryUrl") ?? "http://localhost:5000";
    options.LoadBalancer = LoadBalancerType.RoundRobin;
});

// 添加链路追踪
builder.Services.AddSingleton<ITraceCollector, InMemoryTraceCollector>();

// 配置服务监听端口
builder.WebHost.UseUrls($"http://0.0.0.0:{servicePort}");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 使用链路追踪中间件
app.UseTracing(serviceName);

app.MapControllers();

app.Run();
