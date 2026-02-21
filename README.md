# ServiceMesh - .NET 服务管理系统

基于 .NET 8 实现的服务管理系统，提供服务发现、自动注册和服务转发功能。

## 项目结构

```
ServiceMesh/
├── ServiceMesh.Core/           # 核心类库
│   ├── Models/                 # 数据模型
│   ├── Interfaces/             # 接口定义
│   ├── LoadBalancers/          # 负载均衡算法
│   └── HealthCheck/            # 健康检查
├── ServiceMesh.Registry/       # 服务注册中心
├── ServiceMesh.Discovery/      # 服务发现客户端
├── ServiceMesh.Proxy/          # 请求转发代理
├── ServiceMesh.Agent/          # 服务自动注册组件
└── ServiceMesh.Demo/           # 演示服务
```

## 功能特性

### 1. 服务发现
- 基于 HTTP API 的服务发现
- 本地缓存 + 定时刷新机制
- 服务变更订阅通知

### 2. 自动注册
- 服务启动自动注册
- 自动获取本地IP地址
- 定时心跳维护
- 服务停止自动注销

### 3. 服务转发
- 动态路由转发
- 负载均衡（轮询、加权轮询、随机）
- 断路器保护
- 超时控制

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

### 服务注册配置

```csharp
builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = "MyService";
    options.Port = 5001;
    options.RegistryUrl = "http://localhost:5000";
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.Weight = 100;
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

## 负载均衡策略

- **RoundRobin**: 轮询
- **WeightedRoundRobin**: 加权轮询
- **Random**: 随机
- **LeastConnections**: 最小连接数

## 健康检查

- HTTP 健康检查
- TCP 端口检查
- 心跳超时清理
- 主动健康探测
