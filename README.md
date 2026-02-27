# ServiceMesh - .NET 服务管理系统

基于 .NET 8 实现的服务网格系统，提供服务注册发现、自动注册、健康检查、负载均衡和动态代理功能。

## 项目结构

```
ServiceMesh/
├── ServiceMesh.Core/           # 核心类库
│   ├── Models/                 # 数据模型（ServiceInstance, ServiceRegistryRequest等）
│   ├── Interfaces/             # 接口定义（IServiceDiscovery, IServiceRegistry等）
│   ├── LoadBalancers/          # 负载均衡算法（轮询、加权轮询、随机）
│   ├── HealthCheck/            # 健康检查
│   └── Tracing/                # 分布式追踪
├── ServiceMesh.Registry/       # 服务注册中心
│   ├── Controllers/            # API控制器
│   ├── Services/               # 业务服务
│   └── BackgroundServices/     # 后台任务（健康检查）
├── ServiceMesh.Discovery/      # 服务发现客户端
│   ├── IncrementalServiceDiscovery.cs  # 增量同步实现
│   └── ServiceDiscoveryClient.cs       # 服务发现客户端
├── ServiceMesh.Proxy/          # 请求转发代理（基于YARP）
│   ├── Configuration/          # 动态配置
│   ├── Middleware/             # 代理中间件
│   └── Services/               # 代理服务
├── ServiceMesh.Agent/          # 服务自动注册组件
│   ├── ServiceRegistrationClient.cs    # 注册客户端
│   ├── DefaultServiceInfoProvider.cs   # 默认服务信息提供者
│   ├── IServiceInfoProvider.cs         # 服务信息提供者接口
│   ├── DefaultHealthCheckMiddleware.cs # 默认健康检查中间件
│   └── ServiceRegistrationExtensions.cs # 扩展方法
└── ServiceMesh.Demo/           # 演示服务
    └── Controllers/            # Demo控制器（含服务发现示例）
```

## 功能特性

### 1. 服务注册与发现
- **自动服务注册**：服务启动时自动注册到注册中心
- **服务信息提供者接口(IServiceInfoProvider)**：可自定义服务名称、端口、Host获取逻辑
- **自动获取本地IP**：支持通配符地址(0.0.0.0)自动替换为实际IP
- **增量同步**：客户端增量获取服务变更，减少网络开销
- **WebSocket实时推送**：服务变更实时通知客户端

### 2. 健康检查
- **默认健康检查中间件**：为简单应用提供开箱即用的健康检查端点
- **HTTP健康检查**：注册中心主动探测服务健康状态
- **心跳维护**：客户端定时发送心跳维持注册状态
- **故障转移**：自动剔除不健康实例

### 3. 负载均衡与代理
- **多种负载均衡策略**：轮询、加权轮询、随机、最小连接数
- **动态路由**：基于YARP的动态代理配置
- **服务网关**：统一入口，自动服务发现转发

### 4. 弹性设计
- **注册失败策略**：FailFast（快速失败）、ContinueAndRetry（持续重试）、ContinueWithoutRegistration（静默运行）
- **自动重试**：注册失败自动重试，支持无限重试模式
- **后台重试**：服务启动后后台持续尝试注册

## 快速开始

### 1. 启动注册中心

```bash
cd ServiceMesh.Registry
dotnet run --urls "http://localhost:5000"
```

### 2. 启动演示服务

启动多个实例：

```bash
# 实例 1
cd ServiceMesh.Demo
dotnet run --ServiceName "OrderService" --Port 5001

# 实例 2
cd ServiceMesh.Demo
dotnet run --ServiceName "OrderService" --Port 5002

# 实例 3
cd ServiceMesh.Demo
dotnet run --ServiceName "PaymentService" --Port 5003
```

### 3. 启动代理网关

```bash
cd ServiceMesh.Proxy
dotnet run --urls "http://localhost:8080"
```

## API 使用示例

### 服务注册

```http
POST http://localhost:5000/api/registry/register
Content-Type: application/json

{
  "serviceName": "OrderService",
  "host": "192.168.1.100",
  "port": 5001,
  "version": "1.0.0",
  "metadata": {
    "environment": "production"
  }
}
```

### 服务发现

```http
GET http://localhost:5000/api/registry/discover/OrderService
```

### 通过代理访问服务

```http
# 方式1：通过网关控制器
GET http://localhost:8080/gateway/OrderService/demo/info

# 方式2：通过中间件
GET http://localhost:8080/api/OrderService/demo/info
```

## API 参考

### 注册中心 API

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/registry/register` | 注册服务 |
| POST | `/api/registry/deregister/{instanceId}` | 注销服务 |
| POST | `/api/registry/heartbeat` | 发送心跳 |
| GET | `/api/registry/discover/{serviceName}` | 发现服务 |
| GET | `/api/registry/instance/{serviceName}` | 获取单个健康实例 |
| GET | `/api/registry/services` | 获取所有服务名称 |
| GET | `/api/registry/instances` | 获取所有服务实例 |

### Demo 服务 API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/demo/info` | 获取服务信息 |
| GET | `/api/demo/services` | 获取所有注册服务 |
| GET | `/api/demo/instances` | 获取所有服务实例详情 |
| GET | `/api/demo/discover/{serviceName}` | 发现指定服务 |
| GET | `/api/demo/calculate?a=1&b=2` | 计算示例 |
| GET | `/api/demo/slow?delayMs=1000` | 模拟耗时操作 |
| GET | `/api/demo/error` | 模拟错误 |

## 架构说明

```
┌─────────────────────────────────────────────────────────────┐
│                         客户端请求                            │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│                    ServiceMesh.Proxy                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │  服务发现     │  │  负载均衡     │  │  断路器       │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│                  ServiceMesh.Registry                        │
│                    (服务注册中心)                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │  服务存储     │  │  健康检查     │  │  心跳管理     │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
└─────────────────────┬───────────────────────────────────────┘
                      │
        ┌─────────────┼─────────────┐
        │             │             │
        ▼             ▼             ▼
┌──────────┐  ┌──────────┐  ┌──────────┐
│ Service  │  │ Service  │  │ Service  │
│   A      │  │   B      │  │   C      │
│ (Agent)  │  │ (Agent)  │  │ (Agent)  │
└──────────┘  └──────────┘  └──────────┘
```

## 配置说明

### 服务注册配置（基础）

```csharp
builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = "MyService";                          // 服务名称（可选，默认使用程序集名）
    options.Port = 5001;                                        // 端口号（可选，自动获取）
    options.Host = "192.168.1.100";                            // 主机地址（可选，自动获取本地IP）
    options.RegistryUrl = "http://localhost:5000";             // 注册中心地址
    options.Version = "1.0.0";                                 // 服务版本
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);      // 心跳间隔
    options.Weight = 100;                                       // 服务权重
    options.Metadata["env"] = "production";                    // 元数据
});

app.UseServiceRegistration();  // 启用服务注册（自动获取端口）
```

### 服务注册配置（自动获取全部信息）

```csharp
// 最简单配置：自动获取服务名、端口、IP
builder.Services.AddServiceRegistration(options =>
{
    options.RegistryUrl = "http://localhost:5000";
    options.EnableDefaultHealthCheck = true;  // 启用默认健康检查
});

app.UseServiceRegistration();
```

### 自定义服务信息提供者

```csharp
// 实现接口
public class MyServiceInfoProvider : IServiceInfoProvider
{
    public string? GetServiceName() => Environment.GetEnvironmentVariable("SERVICE_NAME");
    public int GetPort() => int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
    public string? GetHost() => null; // 使用默认获取逻辑
}

// 注册
builder.Services.AddServiceRegistration<MyServiceInfoProvider>(options =>
{
    options.RegistryUrl = "http://localhost:5000";
});
```

### 服务发现配置

```csharp
builder.Services.AddServiceDiscovery(options =>
{
    options.RegistryUrl = "http://localhost:5000";
    options.LoadBalancer = LoadBalancerType.RoundRobin;
    options.CacheRefreshInterval = TimeSpan.FromSeconds(30);
});
```

### 注册失败策略

```csharp
builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = "MyService";
    options.RegistryUrl = "http://localhost:5000";
    
    // 注册失败策略
    options.FailurePolicy = RegistrationFailurePolicy.ContinueAndRetry;  // 默认：持续重试
    // options.FailurePolicy = RegistrationFailurePolicy.FailFast;       // 快速失败
    // options.FailurePolicy = RegistrationFailurePolicy.ContinueWithoutRegistration; // 静默运行
    
    options.RegisterRetryCount = 3;                              // 重试次数（0=无限重试）
    options.RegisterRetryInterval = TimeSpan.FromSeconds(5);     // 重试间隔
});
```

## 负载均衡策略

| 策略 | 说明 |
|------|------|
| **RoundRobin** | 轮询（默认） |
| **WeightedRoundRobin** | 加权轮询（根据服务权重） |
| **Random** | 随机 |
| **LeastConnections** | 最小连接数 |

## 健康检查

### 默认健康检查中间件

为简单应用提供开箱即用的健康检查：

```csharp
builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = "MyService";
    options.EnableDefaultHealthCheck = true;        // 启用默认健康检查
    options.HealthCheckUrl = "/health";             // 健康检查路径（可选）
});

app.UseServiceRegistration();  // 自动启用健康检查中间件
```

访问 `GET /health` 返回：
```json
{
  "status": "Healthy",
  "service": "MyService",
  "timestamp": "2026-02-27T10:30:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "description": "服务运行正常"
    }
  }
}
```

### 自定义健康检查

如需自定义健康检查逻辑，可不启用默认中间件，自行实现：

```csharp
// 不启用默认健康检查
options.EnableDefaultHealthCheck = false;

// 自行实现健康检查端点
app.MapHealthChecks("/health", new HealthCheckOptions
{
    // 自定义配置
});
```

### 健康检查机制

| 类型 | 说明 |
|------|------|
| **心跳检查** | 客户端定时向注册中心发送心跳 |
| **主动探测** | 注册中心主动HTTP探测服务健康状态 |
| **超时清理** | 心跳超时后自动剔除不健康实例 |
| **故障转移** | 代理层自动跳过不健康实例 |
