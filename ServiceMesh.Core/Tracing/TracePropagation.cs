namespace ServiceMesh.Core.Tracing;

/// <summary>
/// 链路传播协议（基于 W3C Trace Context 标准）
/// </summary>
public static class TracePropagation
{
    // HTTP Header 名称
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";
    public const string BaggageHeader = "baggage";

    /// <summary>
    /// 将 TraceContext 编码为 HTTP Header 值
    /// </summary>
    /// <remarks>
    /// W3C 格式: version-traceId-parentId-flags
    /// 示例: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
    /// </remarks>
    public static string EncodeTraceParent(TraceContext context)
    {
        var version = "00";
        var flags = context.IsSampled ? "01" : "00";
        return $"{version}-{context.TraceId}-{context.SpanId}-{flags}";
    }

    /// <summary>
    /// 从 HTTP Header 解码 TraceContext
    /// </summary>
    public static bool TryDecodeTraceParent(string? traceParent, out TraceContext? context)
    {
        context = null;
        
        if (string.IsNullOrEmpty(traceParent))
            return false;

        var parts = traceParent.Split('-');
        if (parts.Length != 4)
            return false;

        // 验证版本
        if (parts[0] != "00")
            return false;

        // 验证 TraceId 长度（32位十六进制）
        if (parts[1].Length != 32)
            return false;

        // 验证 SpanId 长度（16位十六进制）
        if (parts[2].Length != 16)
            return false;

        context = new TraceContext
        {
            TraceId = parts[1],
            SpanId = parts[2],
            IsSampled = parts[3] == "01"
        };

        return true;
    }

    /// <summary>
    /// 编码 Baggage
    /// </summary>
    public static string EncodeBaggage(Dictionary<string, string> baggage)
    {
        // 格式: key1=value1,key2=value2
        return string.Join(",", baggage.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
    }

    /// <summary>
    /// 解码 Baggage
    /// </summary>
    public static Dictionary<string, string> DecodeBaggage(string? baggageHeader)
    {
        var result = new Dictionary<string, string>();
        
        if (string.IsNullOrEmpty(baggageHeader))
            return result;

        var items = baggageHeader.Split(',');
        foreach (var item in items)
        {
            var parts = item.Split('=', 2);
            if (parts.Length == 2)
            {
                result[parts[0].Trim()] = Uri.UnescapeDataString(parts[1].Trim());
            }
        }

        return result;
    }

    /// <summary>
    /// 从 HTTP 请求头中提取 TraceContext
    /// </summary>
    public static TraceContext ExtractFromHeaders(IDictionary<string, string> headers)
    {
        // 尝试提取现有上下文
        if (headers.TryGetValue(TraceParentHeader, out var traceParent))
        {
            if (TryDecodeTraceParent(traceParent, out var existingContext) && existingContext != null)
            {
                // 提取 baggage
                if (headers.TryGetValue(BaggageHeader, out var baggage))
                {
                    existingContext.Baggage = DecodeBaggage(baggage);
                }

                // 创建子上下文（保持 TraceId，生成新的 SpanId）
                return existingContext.CreateChildContext();
            }
        }

        // 没有现有上下文，创建新的
        return new TraceContext();
    }

    /// <summary>
    /// 将 TraceContext 注入到 HTTP 请求头
    /// </summary>
    public static void InjectIntoHeaders(TraceContext context, IDictionary<string, string> headers)
    {
        headers[TraceParentHeader] = EncodeTraceParent(context);
        
        if (context.Baggage.Count > 0)
        {
            headers[BaggageHeader] = EncodeBaggage(context.Baggage);
        }
    }
}
