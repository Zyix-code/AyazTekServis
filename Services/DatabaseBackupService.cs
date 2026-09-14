using Microsoft.Data.Sqlite;

namespace AyazTekServis.Services;

public record DatabaseBackupResult(string FileName, string FullPath, byte[] Bytes);

public class DatabaseBackupService(IConfiguration configuration)
{
    private readonly IConfiguration _configuration = configuration;

    public async Task<DatabaseBackupResult> CreateAsync(CancellationToken cancellationToken = default)
    {
        var cs = _configuration.GetConnectionString("DefaultConnection") ?? "Data Source=AyazTekServis.db";
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"AyazTekServis-backup-{stamp}.db";
        var backupDir = Path.Combine(Directory.GetCurrentDirectory(), "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, fileName);

        var sourceBuilder = new SqliteConnectionStringBuilder(cs) { Pooling = false };
        await using (var source = new SqliteConnection(sourceBuilder.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using (var checkpoint = source.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                await checkpoint.ExecuteNonQueryAsync(cancellationToken);
            }

            var targetBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };
            await using (var destination = new SqliteConnection(targetBuilder.ConnectionString))
            {
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }
        }

        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        return new DatabaseBackupResult(fileName, backupPath, bytes);
    }
}
