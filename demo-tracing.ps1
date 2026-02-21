# 链路追踪功能演示脚本

Write-Host "=== ServiceMesh 链路追踪演示 ===" -ForegroundColor Green
Write-Host ""

# 1. 直接访问 OrderService
Write-Host "1. 直接访问 OrderService (localhost:5001)" -ForegroundColor Yellow
$headers = @{
    "traceparent" = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5001/api/demo/info" -Headers $headers -TimeoutSec 5
    Write-Host "响应状态: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "响应头中的 traceparent: $($response.Headers['traceparent'])" -ForegroundColor Cyan
    Write-Host "响应内容: $($response.Content)" -ForegroundColor Gray
} catch {
    Write-Host "请求失败: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# 2. 通过代理访问（会自动传播 TraceId）
Write-Host "2. 通过 Proxy 访问 OrderService (localhost:8080)" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8080/gateway/OrderService/demo/info" -TimeoutSec 5
    Write-Host "响应状态: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "响应头中的 traceparent: $($response.Headers['traceparent'])" -ForegroundColor Cyan
} catch {
    Write-Host "请求失败: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# 3. 查询链路数据
Write-Host "3. 查询链路追踪数据" -ForegroundColor Yellow
Start-Sleep -Seconds 1

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/api/trace/traces" -TimeoutSec 5
    $traces = $response.Content | ConvertFrom-Json
    
    Write-Host "总链路数: $($traces.total)" -ForegroundColor Green
    Write-Host ""
    
    if ($traces.traces.Count -gt 0) {
        Write-Host "最近链路:" -ForegroundColor Cyan
        foreach ($trace in $traces.traces | Select-Object -First 5) {
            Write-Host "  TraceId: $($trace.traceId.Substring(0,8))... | 服务: $($trace.services -join ', ') | 耗时: $($trace.duration.TotalMilliseconds)ms | 错误: $($trace.hasError)" -ForegroundColor Gray
        }
        
        # 查询第一个链路的详细数据
        $firstTraceId = $traces.traces[0].traceId
        Write-Host ""
        Write-Host "4. 查询第一个链路的详细数据 (TraceId: $firstTraceId)" -ForegroundColor Yellow
        
        $detailResponse = Invoke-WebRequest -Uri "http://localhost:5000/api/trace/trace/$firstTraceId" -TimeoutSec 5
        $detail = $detailResponse.Content | ConvertFrom-Json
        
        Write-Host "Span数量: $($detail.spanCount)" -ForegroundColor Green
        Write-Host "总耗时: $($detail.duration)" -ForegroundColor Green
        
        # 显示链路树
        Write-Host "链路结构:" -ForegroundColor Cyan
        foreach ($node in $detail.tree) {
            Show-TraceNode $node 0
        }
    }
} catch {
    Write-Host "查询链路数据失败: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# 4. 查询服务拓扑
Write-Host "5. 查询服务调用拓扑" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/api/trace/topology" -TimeoutSec 5
    $topology = $response.Content | ConvertFrom-Json
    
    Write-Host "服务节点:" -ForegroundColor Cyan
    foreach ($node in $topology.nodes) {
        Write-Host "  - $($node.name)" -ForegroundColor Gray
    }
    
    if ($topology.edges.Count -gt 0) {
        Write-Host "调用关系:" -ForegroundColor Cyan
        foreach ($edge in $topology.edges) {
            Write-Host "  $($edge.source) -> $($edge.target) (调用次数: $($edge.count))" -ForegroundColor Gray
        }
    } else {
        Write-Host "暂无调用关系数据" -ForegroundColor Yellow
    }
} catch {
    Write-Host "查询拓扑失败: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# 5. 查询服务性能统计
Write-Host "6. 查询 OrderService 性能统计" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/api/trace/stats/OrderService" -TimeoutSec 5
    $stats = $response.Content | ConvertFrom-Json
    
    Write-Host "服务名称: $($stats.serviceName)" -ForegroundColor Green
    Write-Host "总请求数: $($stats.totalRequests)" -ForegroundColor Green
    Write-Host "错误数: $($stats.errorCount)" -ForegroundColor $(if($stats.errorCount -gt 0){"Red"}else{"Green"})
    Write-Host "错误率: $([math]::Round($stats.errorRate * 100, 2))%" -ForegroundColor $(if($stats.errorRate -gt 0.01){"Red"}else{"Green"})
    Write-Host "平均延迟: $([math]::Round($stats.avgDuration, 2))ms" -ForegroundColor Green
    Write-Host "P95延迟: $([math]::Round($stats.p95, 2))ms" -ForegroundColor Green
    Write-Host "P99延迟: $([math]::Round($stats.p99, 2))ms" -ForegroundColor Green
} catch {
    Write-Host "查询性能统计失败: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== 演示完成 ===" -ForegroundColor Green

# 辅助函数：递归显示链路节点
function Show-TraceNode($node, $indent) {
    $prefix = "  " * $indent
    $statusColor = if ($node.status -eq "Error") { "Red" } else { "Green" }
    Write-Host "$prefix[$($node.serviceName)] $($node.operationName) - $($node.duration)ms" -ForegroundColor $statusColor
    
    foreach ($child in $node.children) {
        Show-TraceNode $child ($indent + 1)
    }
}
