using System.Security.Cryptography;
using Npgsql;

namespace Chipmunk.Platform;

// 独立平台迁移，不调用旧 Admin.Api 的 --init。
public static class Migrations
{
    public static async Task Apply(NpgsqlDataSource source)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var store = new Store(connection);
        await store.Lock("chipmunk:database:migrations");
        await store.Execute("CREATE TABLE IF NOT EXISTS platform_migration(version text PRIMARY KEY,sha256 text NOT NULL,applied_at timestamptz NOT NULL DEFAULT now())");
        var files = new[] { Path.Combine(AppContext.BaseDirectory, "schema.sql") }
            .Concat(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "migrations"), "*.sql").Order(StringComparer.Ordinal));
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(file);
            var sql = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
            var version = Path.GetFileName(file);
            var rows = await store.Rows("SELECT sha256 FROM platform_migration WHERE version=@version", ("version", version));
            if (rows.Count != 0)
            {
                if (rows[0][0] != hash) throw new InvalidOperationException("Applied migration changed: " + version);
                continue;
            }
            await store.Execute(sql);
            await store.Execute("INSERT INTO platform_migration(version,sha256) VALUES(@version,@hash)", ("version", version), ("hash", hash));
        }
        await Seed.Initialize(store);
        await transaction.CommitAsync();
    }
}
