using Microsoft.AspNetCore.Mvc;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;
using System.Text.Json;

namespace ServiceMesh.Demo.Controllers;

/// <summary>
/// 演示服务控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DemoController : ControllerBase
{
    private readonly ILogger<DemoController> _logger;
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly string _serviceName;
    private readonly string _instanceId;

    public DemoController(
        ILogger<DemoController> logger,
        IServiceDiscovery serviceDiscovery,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _logger = logger;
        _serviceDiscovery = serviceDiscovery;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _serviceName = config.GetValue<string>("ServiceName") ?? "DemoService";
        _instanceId = Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// 获取服务信息
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            serviceName = _serviceName,
            instanceId = _instanceId,
            machineName = Environment.MachineName,
            timestamp = DateTime.UtcNow,
            framework = ".NET 8.0"
        });
    }

    /// <summary>
    /// 计算（模拟业务操作）
    /// </summary>
    [HttpGet("calculate")]
    public IActionResult Calculate([FromQuery] int a, [FromQuery] int b)
    {
        _logger.LogInformation("执行计算: {A} + {B}", a, b);
        
        return Ok(new
        {
            a = a,
            b = b,
            result = a + b,
            instanceId = _instanceId,
            serviceName = _serviceName
        });
    }

    /// <summary>
    /// 发现其他服务
    /// </summary>
    [HttpGet("discover/{serviceName}")]
    public async Task<IActionResult> DiscoverService(string serviceName)
    {
        var response = await _serviceDiscovery.DiscoverAsync(new()
        {
            ServiceName = serviceName,
            HealthyOnly = true
        });

        return Ok(new
        {
            requestedService = serviceName,
            foundInstances = response.Instances.Count,
            instances = response.Instances.Select(i => new
            {
                i.Id,
                i.Host,
                i.Port,
                i.Status,
                i.Version
            })
        });
    }

    /// <summary>
    /// 模拟耗时操作
    /// </summary>
    [HttpGet("slow")]
    public async Task<IActionResult> SlowOperation([FromQuery] int delayMs = 1000)
    {
        _logger.LogInformation("开始耗时操作，延迟: {DelayMs}ms", delayMs);
        
        await Task.Delay(delayMs);
        
        return Ok(new
        {
            message = "操作完成",
            delayMs = delayMs,
            instanceId = _instanceId,
            completedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 模拟错误
    /// </summary>
    [HttpGet("error")]
    public IActionResult SimulateError()
    {
        _logger.LogError("模拟错误发生");

        return StatusCode(500, new
        {
            error = "模拟服务器错误",
            instanceId = _instanceId,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 获取所有注册的服务名称列表
    /// </summary>
    [HttpGet("services")]
    public async Task<IActionResult> GetAllServices()
    {
        var registryUrl = _config.GetValue<string>("ServiceRegistry:Url") ?? "http://localhost:5000";
        var client = _httpClientFactory.CreateClient();

        try
        {
            var response = await client.GetAsync($"{registryUrl}/api/registry/services");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var services = JsonSerializer.Deserialize<List<string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return Ok(new
            {
                totalServices = services?.Count ?? 0,
                services = services ?? new List<string>(),
                instanceId = _instanceId,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取所有服务列表失败");
            return StatusCode(500, new
            {
                error = "获取服务列表失败",
                message = ex.Message,
                instanceId = _instanceId
            });
        }
    }

    /// <summary>
    /// 获取所有服务实例详情
    /// </summary>
    [HttpGet("instances")]
    public async Task<IActionResult> GetAllInstances()
    {
        var registryUrl = _config.GetValue<string>("ServiceRegistry:Url") ?? "http://localhost:5000";
        var client = _httpClientFactory.CreateClient();

        try
        {
            var response = await client.GetAsync($"{registryUrl}/api/registry/instances");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var instances = JsonSerializer.Deserialize<List<ServiceInstance>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // 按服务名称分组统计
            var serviceGroups = instances?
                .GroupBy(i => i.ServiceName)
                .Select(g => new
                {
                    serviceName = g.Key,
                    instanceCount = g.Count(),
                    healthyCount = g.Count(i => i.Status == ServiceStatus.Healthy),
                    unhealthyCount = g.Count(i => i.Status == ServiceStatus.Unhealthy)
                })
                .Cast<object>()
                .ToList();

            return Ok(new
            {
                totalInstances = instances?.Count ?? 0,
                serviceGroups = serviceGroups ?? new List<object>(),
                instances = instances?.Select(i => new
                {
                    i.Id,
                    i.ServiceName,
                    i.Host,
                    i.Port,
                    i.Version,
                    i.Status,
                    i.Weight,
                    i.LastHeartbeat
                }),
                instanceId = _instanceId,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取所有服务实例失败");
            return StatusCode(500, new
            {
                error = "获取服务实例失败",
                message = ex.Message,
                instanceId = _instanceId
            });
        }
    }
}
