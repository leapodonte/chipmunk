using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public static class Media
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly SemaphoreSlim Uploads = new(2, 2);
    // 在读取正文、取得数据库连接或分配解码内存之前限制并行上传。
    public static IDisposable? AcquireUpload(HttpContext context)
    {
        if (context.Request.Method == "GET") return null;
        if (!Uploads.Wait(0))
        {
            context.Response.Headers.RetryAfter = "2";
            throw new ApiError(429, "上传繁忙，请稍后重试", "Upload capacity is busy; retry shortly");
        }
        return new UploadLease();
    }
    private sealed class UploadLease : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) Uploads.Release(); }
    }
    private static string Sign(IConfiguration config, string action, string id, long expires) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Auth.Secret(config, "MEDIA_SIGNING_KEY")), Encoding.UTF8.GetBytes($"{action}:{id}:{expires}"))).ToLowerInvariant();
    private static string Base(IConfiguration config) => (config["PUBLIC_URL"] ?? "https://app.smilelab.ai").TrimEnd('/');
    public static string Download(IConfiguration config, JsonObject media)
    {
        var id = media["id"]!.GetValue<string>(); var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 900;
        return $"{Base(config)}/storage/objects/{id}?expires={expiry}&signature={Sign(config, "read", id, expiry)}";
    }
    public static void Ready(JsonObject media)
    {
        if (media["uploaded"]?.GetValue<bool>() != true) throw new ApiError(409, "文件尚未上传", "Object is not uploaded");
    }
    public static async Task<JsonObject> ByKey(Store store, User user, string key)
    {
        var rows = await store.Rows("SELECT body::text FROM platform_resource WHERE tenant_id=@tenant AND clinic_id=@clinic AND owner_id=@user AND kind='media' AND body->>'objectKey'=@key", ("tenant", user.Tenant), ("clinic", user.Clinic), ("user", user.Id), ("key", key));
        if (rows.Count != 1) throw new ApiError(404, "文件不存在", "Object not found");
        var media = JsonNode.Parse(rows[0][0])!.AsObject();
        Ready(media); return media;
    }
    public static async Task<JsonObject> Grant(Store store, IConfiguration config, User user, JsonObject body)
    {
        var scene = Auth.Required(body, "scene"); var ext = Validation.Text(body, "ext", 8, false).ToLowerInvariant(); if (ext == "") ext = "jpg";
        if (scene is not ("checkin_photo" or "ai_photo" or "avatar") || ext is not ("jpg" or "jpeg" or "png" or "webp")) throw new ApiError(400, "图片用途或扩展名无效", "Unsupported scene or extension");
        await store.Rate("upload:" + user.Id, 100, 86400);
        var root = config["MEDIA_ROOT"] ?? "/data/objects";
        Directory.CreateDirectory(root);
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace < 10L * 1024 * 1024 * 1024) throw new ApiError(507, "磁盘空间不足", "Disk reserve is below 10 GiB");
        var quota = await store.Rows("SELECT COALESCE(sum((body->>'size')::bigint),0) FROM platform_resource WHERE kind='media' AND owner_id=@user", ("user", user.Id));
        if (long.Parse(quota[0][0]) >= 100L * 1024 * 1024) throw new ApiError(413, "演示用户文件容量已达上限", "Demo user storage quota is 100 MiB");
        var id = "media_" + Guid.NewGuid().ToString("N"); var key = $"chipmunk-private/{user.Tenant}/{user.Id}/{scene}/{id}.{ext}";
        var expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        await store.Add(user, "media", new JsonObject { ["objectKey"] = key, ["scene"] = scene, ["ext"] = ext, ["uploaded"] = false, ["size"] = 0, ["expires"] = expires,
            ["uploaderId"] = user.Id, ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["storageClass"] = "private", ["original"] = true }, id: id);
        var signature = Sign(config, "upload", id, expires);
        var policy = Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject { ["expiration"] = DateTimeOffset.FromUnixTimeSeconds(expires).ToString("O"), ["conditions"] = new JsonArray(new JsonObject { ["key"] = key }, new JsonArray("content-length-range", 1, MaximumBytes)) }.ToJsonString()));
        return new JsonObject { ["uploadUrl"] = $"{Base(config)}/storage/objects/{id}?expires={expires}&signature={signature}", ["objectKey"] = key,
            ["accessKeyId"] = "disk-demo", ["policy"] = policy, ["signature"] = signature, ["expiresIn"] = 300, ["maxSize"] = MaximumBytes,
            ["method"] = "POST", ["storageProvider"] = "disk-mock", ["formData"] = new JsonObject { ["key"] = key, ["OSSAccessKeyId"] = "disk-demo", ["policy"] = policy, ["signature"] = signature, ["success_action_status"] = "200" } };
    }
    public static async Task<IResult> Handle(Store store, IConfiguration config, HttpContext context, string id)
    {
        var action = context.Request.Method == "GET" ? "read" : "upload";
        if (!long.TryParse(context.Request.Query["expires"], out var expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || !Auth.Equal(Sign(config, action, id, expiry), context.Request.Query["signature"].ToString())) throw new ApiError(403, "文件签名无效或已过期", "Invalid or expired object signature");
        await store.Lock("media:" + id);
        var rows = await store.Rows("SELECT r.body::text,u.id,r.tenant_id,r.clinic_id,u.nickname,u.phone FROM platform_resource r JOIN platform_user u ON r.owner_id=u.id AND r.tenant_id=u.tenant_id WHERE r.id=@id AND r.kind='media'", ("id", id));
        if (rows.Count == 0) throw new ApiError(404, "文件不存在", "Object not found");
        var r = rows[0]; var media = JsonNode.Parse(r[0])!.AsObject(); var user = new User(r[1], r[2], r[3], r[4], r[5]);
        var root = Path.GetFullPath(config["MEDIA_ROOT"] ?? "/data/objects");
        // 文件路径完全由数据库中的随机资源 ID 决定，不接受客户端文件名或路径。
        var filePath = Path.Combine(root, id + "." + media["ext"]!.GetValue<string>());
        if (action == "read") { Ready(media); if (!File.Exists(filePath)) throw new ApiError(404, "文件缺失", "Object bytes missing"); return Results.File(filePath, media["contentType"]!.GetValue<string>(), enableRangeProcessing: true); }
        if (media["uploaded"]!.GetValue<bool>()) throw new ApiError(409, "原始文件不可覆盖", "Object is immutable and already uploaded");
        if (expiry != media["expires"]!.GetValue<long>()) throw new ApiError(403, "凭证不匹配", "Upload grant does not match object");
        Stream stream;
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(); var file = form.Files.GetFile("file") ?? throw new ApiError(400, "缺少file字段", "Multipart field file required");
            if (file.Length > MaximumBytes) throw new ApiError(413, "文件超过10MiB", "Object exceeds 10 MiB");
            if (form["key"].ToString() != media["objectKey"]!.GetValue<string>()) throw new ApiError(400, "对象键不匹配", "Multipart key does not match grant");
            stream = file.OpenReadStream();
        }
        else if (context.Request.Method == "PUT") stream = context.Request.Body;
        else throw new ApiError(400, "上传需要multipart或PUT", "Use multipart POST or raw PUT");
        using var buffer = new MemoryStream(); var chunk = new byte[65536]; int read;
        while ((read = await stream.ReadAsync(chunk, context.RequestAborted)) > 0) { if (buffer.Length + read > MaximumBytes) throw new ApiError(413, "文件超过10MiB", "Object exceeds 10 MiB"); await buffer.WriteAsync(chunk.AsMemory(0, read)); }
        var bytes = buffer.ToArray(); var ext = media["ext"]!.GetValue<string>();
        await store.Lock("quota:" + user.Id);
        var usage = await store.Rows("SELECT COALESCE(sum((body->>'size')::bigint),0) FROM platform_resource WHERE kind='media' AND owner_id=@user", ("user", user.Id));
        if (long.Parse(usage[0][0]) + bytes.Length > 100L * 1024 * 1024) throw new ApiError(413, "演示用户文件容量已达上限", "Demo user storage quota is 100 MiB");
        var valid = ext is "jpg" or "jpeg" ? bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[^2] == 255 && bytes[^1] == 217
            : ext == "png" ? bytes.Length >= 24 && bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            : bytes.Length >= 16 && Encoding.ASCII.GetString(bytes,0,4) == "RIFF" && Encoding.ASCII.GetString(bytes,8,4) == "WEBP";
        if (!valid) throw new ApiError(400, "图片格式与扩展名不匹配", "Image signature does not match extension");
        var dimensions = ImageValidation.Decode(bytes);
        var mime = ext == "png" ? "image/png" : ext == "webp" ? "image/webp" : "image/jpeg";
        Directory.CreateDirectory(root); var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, context.RequestAborted);
            if (File.Exists(filePath))
            {
                var existing = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(filePath, context.RequestAborted))).ToLowerInvariant();
                if (existing != sha) throw new ApiError(409, "已存在不同的原始文件，不能覆盖", "Different original bytes already exist; overwrite is forbidden");
            }
            else { File.Move(temporary, filePath, false); context.Items["media.created.file"] = filePath; }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        media["uploaded"] = true; media["size"] = bytes.Length; media["sha256"] = sha; media["contentType"] = mime;
        media["width"] = dimensions.Width; media["height"] = dimensions.Height;
        media["uploadedAt"] = DateTimeOffset.UtcNow.ToString("O");
        await store.Update(user, media); await store.Audit(user, context, "media.uploaded", id);
        return Results.Json(new { code = 0, message = "ok", data = new { objectKey = media["objectKey"]!.GetValue<string>(), mediaId = id, size = bytes.Length, sha256 = media["sha256"]!.GetValue<string>(), url = Download(config, media) } });
    }
}
