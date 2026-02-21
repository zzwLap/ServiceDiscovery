using Microsoft.AspNetCore.Mvc;
using ServiceMesh.Core.Interfaces;
using ServiceMesh.Core.Models;
using ServiceMesh.Registry.Services;

namespace ServiceMesh.Registry.Controllers;

/// <summary>
/// 服务注册中心 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RegistryController : ControllerBase
{
    private readonly RegistryService _registryService;
    private readonly ILogger<RegistryController> _logger;

    public RegistryController(RegistryService registryService, ILogger<RegistryController> logger)
    {
        _registryService = registryService;
        _logger = logger;
    }

    /// <summary>
    /// 注册服务
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<ServiceRegistryResponse>> Register([FromBody] ServiceRegistryRequest request)
    {
        if (string.IsNullOrEmpty(request.ServiceName) || string.IsNullOrEmpty(request.Host) || request.Port <= 0)
        {
            return BadRequest(new ServiceRegistryResponse 
            { 
                Success = false, 
                Message = "服务名称、主机地址和端口号不能为空" 
            });
        }

        var response = await _registryService.RegisterAsync(request);
        return response.Success ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    [HttpPost("deregister/{instanceId}")]
    public async Task<ActionResult> Deregister(string instanceId)
    {
        var result = await _registryService.DeregisterAsync(instanceId);
        return result ? Ok(new { success = true, message = "注销成功" }) 
                      : NotFound(new { success = false, message = "实例不存在" });
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    [HttpPost("heartbeat")]
    public async Task<ActionResult> Heartbeat([FromBody] HeartbeatRequest request)
    {
        var result = await _registryService.HeartbeatAsync(request);
        return result ? Ok(new { success = true }) 
                      : NotFound(new { success = false, message = "实例不存在" });
    }

    /// <summary>
    /// 发现服务
    /// </summary>
    [HttpGet("discover/{serviceName}")]
    public async Task<ActionResult<ServiceDiscoveryResponse>> Discover(
        string serviceName, 
        [FromQuery] string? version = null,
        [FromQuery] bool healthyOnly = true)
    {
        var request = new ServiceDiscoveryRequest
        {
            ServiceName = serviceName,
            Version = version,
            HealthyOnly = healthyOnly
        };

        var response = await _registryService.DiscoverAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// 获取单个健康实例
    /// </summary>
    [HttpGet("instance/{serviceName}")]
    public async Task<ActionResult<ServiceInstance>> GetInstance(
        string serviceName,
        [FromQuery] string? version = null)
    {
        var instance = await _registryService.GetHealthyInstanceAsync(serviceName, version);
        
        if (instance == null)
        {
            return NotFound(new { message = "没有可用的服务实例" });
        }

        return Ok(instance);
    }

    /// <summary>
    /// 获取所有服务
    /// </summary>
    [HttpGet("services")]
    public ActionResult<List<string>> GetAllServices()
    {
        var store = HttpContext.RequestServices.GetRequiredService<InMemoryServiceStore>();
        var services = store.GetAllServiceNames();
        return Ok(services);
    }

    /// <summary>
    /// 获取所有实例
    /// </summary>
    [HttpGet("instances")]
    public ActionResult<List<ServiceInstance>> GetAllInstances()
    {
        var store = HttpContext.RequestServices.GetRequiredService<InMemoryServiceStore>();
        var instances = store.GetAllInstances();
        return Ok(instances);
    }
}
