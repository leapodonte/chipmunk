using Npgsql;

namespace Chipmunk.Platform;

// 清理不依赖 Odoo 是否启用；保留业务幂等记录和所有临床/媒体原件。
public sealed class PlatformMaintenance(NpgsqlDataSource source, ILogger<PlatformMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await source.OpenConnectionAsync(stoppingToken);
                await using var transaction = await connection.BeginTransactionAsync(stoppingToken);
                var store = new Store(connection);
                var acquired = await store.Rows("SELECT pg_try_advisory_xact_lock(hashtextextended('chipmunk:maintenance',0))");
                if (acquired[0][0] == "True")
                {
                    // 每轮最多删各5000条，避免长期占用写锁；下轮继续处理积压。
                    await store.Execute("DELETE FROM platform_rate_limit WHERE ctid IN (SELECT ctid FROM platform_rate_limit WHERE expires_at<now() ORDER BY expires_at LIMIT 5000); DELETE FROM platform_session WHERE token_hash IN (SELECT token_hash FROM platform_session WHERE expires_at<now()-interval '7 days' ORDER BY expires_at LIMIT 5000)");
                }
                await transaction.CommitAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "平台过期会话及频控清理失败");
            }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
