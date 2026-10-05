using Npgsql;

namespace Chipmunk.Platform;

// 独立小连接池避免业务事务占满连接池后，等待安全计数的连接造成饥饿。
public sealed class RateLimitSource : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; }
    public RateLimitSource(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = 5, ApplicationName = "smilelab-rate-limiter" };
        Source = NpgsqlDataSource.Create(builder.ConnectionString);
    }
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
