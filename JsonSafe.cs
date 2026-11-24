using System.Text.Json.Nodes;

namespace NcmNative;

/// <summary>
/// JSON 取值的安全封装。
/// 网易接口同一字段在不同响应里可能被序列化成 int 或 long，
/// JsonNode.GetValue&lt;T&gt;() 遇到类型不完全匹配直接抛异常，所以统一走 TryGetValue。
/// </summary>
public static class Jx
{
    public static long AsLong(this JsonNode n, long def = 0)
    {
        if (n is not JsonValue v) return def;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (v.TryGetValue<string>(out var s) && long.TryParse(s, out var p)) return p;
        return def;
    }

    public static int AsInt(this JsonNode n, int def = 0)
    {
        var l = n.AsLong(def);
        if (l > int.MaxValue) return int.MaxValue;
        if (l < int.MinValue) return int.MinValue;
        return (int)l;
    }

    public static string AsStr(this JsonNode n, string def = "")
    {
        if (n is not JsonValue v) return def;
        if (v.TryGetValue<string>(out var s)) return s ?? def;
        return def;
    }

    public static JsonNode Get(this JsonNode n, string key) => n?[key];
}
