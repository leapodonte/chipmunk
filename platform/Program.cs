using System.Text.Json.Nodes;
using Chipmunk.Platform;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 11 * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 11 * 1024 * 1024);
builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // 只信任独立内部 Docker 网络；服务不发布任何主机端口。
    o.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("172.18.0.0"), 16));
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins((builder.Configuration["CORS_ORIGINS"] ?? "https://app.smilelab.ai,http://localhost:5173,http://localhost:8080").Split(',')).WithHeaders("Authorization", "Content-Type", "X-Dev-Key", "Idempotency-Key").WithMethods("GET", "POST", "PUT", "OPTIONS")));
var config = builder.Configuration;
Auth.Secret(config, "DEV_API_KEY"); Auth.Secret(config, "MEDIA_SIGNING_KEY");
var connectionString = config["PLATFORM_DATABASE"] ?? throw new InvalidOperationException("PLATFORM_DATABASE required");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddHostedService<OdooSync>();
var app = builder.Build();
app.UseForwardedHeaders(); app.UseCors();
app.Use(async (ctx, next) => {
    ctx.Response.Headers["X-Request-Id"] = ctx.TraceIdentifier;
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (ApiError e) { ctx.Response.StatusCode = e.Status; await ctx.Response.WriteAsJsonAsync(new { code = e.Status, message = ctx.Request.Headers.AcceptLanguage.ToString().StartsWith("en") ? e.English : e.Message, data = (object?)null }); }
    catch (Exception e) {
        app.Logger.LogError(e, "请求失败 {RequestId}", ctx.TraceIdentifier);
        if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { code = 500, message = "服务器错误 / Server error", data = (object?)null }); }
    }
});
app.MapGet("/health", async (NpgsqlDataSource source) => { await using var c = await source.OpenConnectionAsync(); await new Store(c).Rows("SELECT 1"); return Results.Json(new { status = "ok", service = "chipmunk-platform", environment = "dev-demo", aiMode = "mock", storage = "disk" }); });
app.MapGet("/api/openapi.json", () => Results.File(Path.Combine(AppContext.BaseDirectory, "openapi.json"), "application/json"));
app.MapGet("/api/developer-guide", () => Results.File(Path.Combine(AppContext.BaseDirectory, "DeveloperGuide.md"), "text/markdown; charset=utf-8", "Smilelab-API.md"));
app.MapGet("/api/docs", () => Results.Content("""
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Smilelab API 开发文档</title>
<style>body{max-width:800px;margin:64px auto;padding:0 24px;font:18px/1.7 system-ui;color:#173047}a{color:#096985}code{background:#f1f5f8;padding:3px 8px}li{margin:12px 0}</style>
<h1>Smilelab 小程序 API 开发文档</h1><p>开发与演示环境 · REST v1 · 私有磁盘媒体 · 独立 Odoo 商业集成</p>
<p>Base URL：<code>https://app.smilelab.ai/api/v1</code></p><ul><li><a href="/api/developer-guide">下载中文开发文档</a></li><li><a href="/api/openapi.json">OpenAPI 3 契约，可导入 Postman 或 Apifox</a></li><li><a href="https://odoo.smilelab.ai">Odoo 管理入口</a></li></ul>
<p>开发登录需要单独交付的 X-Dev-Key。AI 模拟只返回原图占位，短信不发送；请使用测试数据。</p></html>
""", "text/html; charset=utf-8"));
app.MapMethods("/storage/objects/{id}", ["POST", "PUT", "GET"], async (string id, HttpContext ctx, NpgsqlDataSource source) => {
    await using var c = await source.OpenConnectionAsync(); await using var tx = await c.BeginTransactionAsync();
    var result = await Media.Handle(new Store(c), config, ctx, id);
    await tx.CommitAsync(); return result;
});
app.MapMethods("/api/v1/{**endpoint}", ["GET", "POST"], async (string endpoint, HttpContext ctx, NpgsqlDataSource source) => {
    await using var c = await source.OpenConnectionAsync(); await using var tx = await c.BeginTransactionAsync();
    var store = new Store(c);
    JsonObject body = new();
    if (ctx.Request.Method == "POST")
    {
        if (ctx.Request.ContentLength > 65536) throw new ApiError(413, "JSON请求过大", "JSON request exceeds 64 KiB");
        try { body = (await JsonNode.ParseAsync(ctx.Request.Body))?.AsObject() ?? new(); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { throw new ApiError(400, "JSON格式错误", "Invalid JSON object"); }
    }
    var api = new MiniApi(store, config, ctx);
    var data = await api.Handle(endpoint.Trim('/'), body);
    await tx.CommitAsync();
    return Results.Json(new { code = 0, message = "ok", data });
});
await using (var c = await app.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync())
{
    var store = new Store(c);
    await store.Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql")));
    await Seed.Initialize(store);
}
await app.RunAsync();
