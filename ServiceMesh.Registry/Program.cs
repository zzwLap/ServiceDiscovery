using ServiceMesh.Core.Tracing;
using ServiceMesh.Registry.BackgroundServices;
using ServiceMesh.Registry.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 注册内存存储
builder.Services.AddSingleton<InMemoryServiceStore>();

// 注册注册中心服务
builder.Services.AddSingleton<RegistryService>();

// 注册 HttpClient
builder.Services.AddSingleton<HttpClient>(sp => new HttpClient
{
    Timeout = TimeSpan.FromSeconds(10)
});

// 注册后台服务
builder.Services.AddHostedService<HealthCheckBackgroundService>();

// 注册链路追踪收集器
builder.Services.AddSingleton<InMemoryTraceCollector>();
builder.Services.AddSingleton<ITraceCollector>(sp => sp.GetRequiredService<InMemoryTraceCollector>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 使用链路追踪中间件
app.UseTracing("ServiceMesh.Registry");

app.MapControllers();

app.Run();
