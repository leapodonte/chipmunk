using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public static class Auth
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Secret(IConfiguration config, string name) => config[name] is { Length: >= 32 } value ? value : throw new InvalidOperationException(name + " must be at least 32 characters");
    public static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    public static string Required(JsonObject body, string name)
    {
        if (body[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && text.Length <= 512) return text;
        throw new ApiError(400, "缺少或无效参数：" + name, "Missing or invalid parameter: " + name);
    }
    public static void DevKey(HttpContext context, IConfiguration config)
    {
        if (config["DEMO_AUTH"] != "true" || !Equal(context.Request.Headers["X-Dev-Key"].ToString(), Secret(config, "DEV_API_KEY")))
            throw new ApiError(401, "开发登录需要有效开发密钥", "Demo login requires a valid development key");
    }
    public static async Task<User> Current(Store store, HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) throw new ApiError(401, "请先登录", "Bearer token required");
        var rows = await store.Rows("SELECT u.id,u.tenant_id,u.clinic_id,u.nickname,u.phone FROM platform_session s JOIN platform_user u ON u.id=s.user_id WHERE token_hash=@hash AND expires_at>now()",
            ("hash", Hash(header[7..])));
        if (rows.Count != 1) throw new ApiError(401, "登录已过期", "Token is invalid or expired");
        var r = rows[0];
        return new User(r[0], r[1], r[2], r[3], r[4]);
    }
    public static async Task<JsonObject> Login(Store store, HttpContext context, IConfiguration config, JsonObject body, bool phone)
    {
        string identity;
        var mode = "wechat";
        if (phone)
        {
            DevKey(context, config);
            if (Required(body, "smsCode") != "123456") throw new ApiError(400, "演示验证码为123456", "Demo SMS code is 123456");
            var number = Required(body, "phone");
            if (!System.Text.RegularExpressions.Regex.IsMatch(number, "^1[3-9][0-9]{9}$")) throw new ApiError(400, "手机号格式错误", "Invalid phone number");
            identity = "demo-phone:" + number; mode = "demo";
        }
        else
        {
            var code = Required(body, "code");
            if (code.StartsWith("demo:", StringComparison.Ordinal))
            {
                DevKey(context, config); identity = code; mode = "demo";
            }
            else
            {
                var appId = config["WECHAT_APP_ID"]; var secret = config["WECHAT_APP_SECRET"];
                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secret)) throw new ApiError(503, "微信登录尚未配置，请使用开发登录", "WeChat credentials are not configured; use demo login");
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var url = "https://api.weixin.qq.com/sns/jscode2session?appid=" + Uri.EscapeDataString(appId) + "&secret=" + Uri.EscapeDataString(secret) + "&js_code=" + Uri.EscapeDataString(code) + "&grant_type=authorization_code";
                using var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) throw new ApiError(502, "微信服务暂时不可用", "WeChat upstream is unavailable");
                var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
                if (result["errcode"]?.GetValue<int>() is int error && error != 0 || result["openid"] is null) throw new ApiError(401, "微信登录凭证无效", "Invalid WeChat login code");
                identity = "wechat:" + result["openid"]!.GetValue<string>();
            }
        }
        await store.Lock("login:" + identity);
        var tenant = "tenant_demo"; var clinic = "clinic_demo";
        var rows = await store.Rows("SELECT id FROM platform_user WHERE tenant_id=@tenant AND identity_key=@identity", ("tenant", tenant), ("identity", identity));
        var isNew = rows.Count == 0;
        var id = isNew ? "u_" + Guid.NewGuid().ToString("N") : rows[0][0];
        if (isNew)
        {
            await store.Execute("INSERT INTO platform_user(id,tenant_id,clinic_id,identity_key,nickname,phone) VALUES(@id,@tenant,@clinic,@identity,@name,@phone)",
                ("id", id), ("tenant", tenant), ("clinic", clinic), ("identity", identity), ("name", "花栗鼠同学"), ("phone", phone ? Required(body, "phone") : ""));
            var user = new User(id, tenant, clinic, "花栗鼠同学", "");
            await store.Add(user, "message", new JsonObject { ["title"] = "欢迎体验花栗鼠", ["content"] = "这是开发演示环境，请使用测试数据。", ["time"] = "09:00", ["read"] = false, ["type"] = "system" });
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await store.Execute("INSERT INTO platform_session(token_hash,user_id,expires_at) VALUES(@hash,@id,now()+interval '24 hours')", ("hash", Hash(token)), ("id", id));
        return new JsonObject { ["token"] = token, ["userId"] = id, ["isNewUser"] = isNew, ["expiresIn"] = 86400, ["authMode"] = mode };
    }
}
