using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public static class ApiBody
{
    public static async Task<JsonObject> Read(HttpContext context)
    {
        if (context.Request.Method is "GET" or "DELETE") return new();
        if (context.Request.ContentLength > 65536) throw TooLarge();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffer.Length + read > 65536) throw TooLarge();
            await buffer.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted);
        }
        if (buffer.Length == 0) return new();
        buffer.Position = 0;
        try
        {
            using var document = await System.Text.Json.JsonDocument.ParseAsync(buffer, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new InvalidOperationException();
            ValidateKeys(document.RootElement);
            return JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        { throw new ApiError(400, "JSON必须为有效对象", "JSON must be a valid object"); }
    }
    // 拒绝重复字段，避免权限、签名和幂等逻辑对同一请求产生不同解释。
    private static void ValidateKeys(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidOperationException();
                ValidateKeys(property.Value);
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateKeys(item);
    }
    private static ApiError TooLarge() => new(413, "JSON请求过大", "JSON request exceeds 64 KiB");
}
