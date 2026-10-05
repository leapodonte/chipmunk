using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Chipmunk.Platform;

// 所有平台查询限定租户、门诊和资源所有者，Odoo 不访问平台数据库。
public sealed class Store(NpgsqlConnection connection)
{
    public async Task<int> Execute(string sql, params (string, object?)[] values)
    {
        await using var command = Command(sql, values);
        return await command.ExecuteNonQueryAsync();
    }
    public async Task<List<string[]>> Rows(string sql, params (string, object?)[] values)
    {
        await using var command = Command(sql, values);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()!;
            rows.Add(row);
        }
        return rows;
    }
    private NpgsqlCommand Command(string sql, (string, object?)[] values)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in values)
        {
            if (value is null) command.Parameters.Add(new NpgsqlParameter(key, NpgsqlDbType.Text) { Value = DBNull.Value });
            else command.Parameters.AddWithValue(key, value);
        }
        return command;
    }
    public Task Lock(string key) => Execute("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", ("key", key));
    public async Task<JsonObject> Add(User user, string kind, JsonObject body, bool shared = false, string? id = null)
    {
        id ??= kind + "_" + Guid.NewGuid().ToString("N");
        body["id"] = id;
        await Execute("INSERT INTO platform_resource(id,tenant_id,clinic_id,owner_id,kind,body) VALUES(@id,@tenant,@clinic,@owner,@kind,CAST(@body AS jsonb))",
            ("id", id), ("tenant", user.Tenant), ("clinic", user.Clinic), ("owner", shared ? null : user.Id), ("kind", kind), ("body", body.ToJsonString()));
        return body;
    }
    public async Task<List<JsonObject>> List(User user, string kind, bool shared = false)
    {
        var rows = await Rows("SELECT body::text FROM platform_resource WHERE tenant_id=@tenant AND clinic_id=@clinic AND kind=@kind AND " +
            (shared ? "owner_id IS NULL" : "owner_id=@owner") + " ORDER BY created_at DESC,id LIMIT 1000",
            ("tenant", user.Tenant), ("clinic", user.Clinic), ("owner", user.Id), ("kind", kind));
        return rows.Select(row => JsonNode.Parse(row[0])!.AsObject()).ToList();
    }
    public async Task<JsonObject> Get(User user, string kind, string id, bool shared = false)
    {
        var rows = await Rows("SELECT body::text FROM platform_resource WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND kind=@kind AND " +
            (shared ? "owner_id IS NULL" : "owner_id=@owner"),
            ("id", id), ("tenant", user.Tenant), ("clinic", user.Clinic), ("kind", kind), ("owner", user.Id));
        if (rows.Count == 0) throw new ApiError(404, "资源不存在", "Resource not found");
        return JsonNode.Parse(rows[0][0])!.AsObject();
    }
    public Task Update(User user, JsonObject body) => Execute(
        "UPDATE platform_resource SET body=CAST(@body AS jsonb) WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND owner_id=@owner",
        ("body", body.ToJsonString()), ("id", body["id"]!.GetValue<string>()), ("tenant", user.Tenant), ("clinic", user.Clinic), ("owner", user.Id));
    public Task Audit(User user, HttpContext context, string action, string resource) => Execute(
        "INSERT INTO platform_audit(tenant_id,user_id,action,resource_id,ip,request_id) VALUES(@tenant,@user,@action,@resource,@ip,@request)",
        ("tenant", user.Tenant), ("user", user.Id), ("action", action), ("resource", resource),
        ("ip", context.Connection.RemoteIpAddress?.ToString() ?? ""), ("request", context.TraceIdentifier));
    public async Task Rate(string scope, int maximum, int seconds = 3600)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / seconds;
        var rows = await Rows("INSERT INTO platform_rate_limit(scope,bucket,count) VALUES(@scope,@bucket,1) ON CONFLICT(scope,bucket) DO UPDATE SET count=platform_rate_limit.count+1 RETURNING count",
            ("scope", scope), ("bucket", bucket));
        if (int.Parse(rows[0][0]) > maximum) throw new ApiError(429, "请求过于频繁", "Rate limit exceeded");
    }
    public static JsonArray Array(IEnumerable<JsonObject> values) => new(values.Select(x => (JsonNode)x.DeepClone()).ToArray());
}

public sealed record User(string Id, string Tenant, string Clinic, string Nickname, string Phone);
public sealed class ApiError(int status, string chinese, string english) : Exception(chinese)
{
    public int Status { get; } = status;
    public string English { get; } = english;
}
