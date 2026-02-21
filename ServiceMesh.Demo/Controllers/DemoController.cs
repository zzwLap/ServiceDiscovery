using Microsoft.AspNetCore.Mvc;
using ServiceMesh.Core.Interfaces;

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
    private readonly string _serviceName;
    private readonly string _instanceId;

    public DemoController(ILogger<DemoController> logger, IServiceDiscovery serviceDiscovery, IConfiguration config)
    {
        _logger = logger;
        _serviceDiscovery = serviceDiscovery;
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
}
