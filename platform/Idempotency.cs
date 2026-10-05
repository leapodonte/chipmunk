using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public static class Idempotency
{
    // JSON 属性排序；语义相同的请求不因属性顺序不同而冲突。
    public static string Canonical(JsonNode? node) => Normalize(node)?.ToJsonString() ?? "null";
    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, Normalize(x.Value)))),
        JsonArray array => new JsonArray(array.Select(Normalize).ToArray()),
        _ => node?.DeepClone()
    };

    public static async Task<JsonObject> Run(Store store, User user, HttpContext context, string scope, JsonObject body, Func<Task<JsonObject>> action)
    {
        scope = scope + ":" + user.Tenant + ":" + user.Clinic;
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new ApiError(400, "此操作需要1–128字符的Idempotency-Key", "A 1–128 character Idempotency-Key is required");
        await store.Lock("idempotency:" + user.Id + ":" + scope + ":" + key);
        var hash = Auth.Hash(Canonical(body));
        var rows = await store.Rows("SELECT body_hash,result::text FROM platform_idempotency WHERE user_id=@user AND scope=@scope AND key=@key", ("user", user.Id), ("scope", scope), ("key", key));
        if (rows.Count != 0)
        {
            if (rows[0][0] != hash) throw new ApiError(409, "幂等键已用于不同请求", "Idempotency key was used for a different request");
            return JsonNode.Parse(rows[0][1])!.AsObject();
        }
        var result = await action();
        await store.Execute("INSERT INTO platform_idempotency(user_id,scope,key,body_hash,result) VALUES(@user,@scope,@key,@hash,CAST(@result AS jsonb))", ("user", user.Id), ("scope", scope), ("key", key), ("hash", hash), ("result", result.ToJsonString()));
        return result;
    }
}
