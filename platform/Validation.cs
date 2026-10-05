using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Chipmunk.Platform;

public static class Validation
{
    public static string Text(JsonObject body, string name, int maximum = 512, bool required = true)
    {
        if (!required && body[name] is null) return "";
        if (body[name] is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length > maximum || required && string.IsNullOrWhiteSpace(text))
            throw new ApiError(400, "参数无效：" + name, "Invalid field: " + name);
        return text;
    }
    public static DateTimeOffset Time(JsonObject body, string name)
    {
        var value = Text(body, name);
        if (!Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ApiError(400, "时间必须包含明确时区", "Time must be ISO 8601 with an explicit timezone");
        return time.ToUniversalTime();
    }
    public static bool Boolean(JsonObject body, string name, bool fallback = false)
    {
        if (body[name] is null) return fallback;
        if (body[name] is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
        throw new ApiError(400, "参数必须为布尔值：" + name, "Boolean required: " + name);
    }
    public static void Only(JsonObject body, params string[] names)
    {
        if (body.Any(x => !names.Contains(x.Key, StringComparer.Ordinal)))
            throw new ApiError(400, "请求包含不支持的字段", "Request contains unsupported fields");
    }
}
