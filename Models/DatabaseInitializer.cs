using System.Data.Common;
using AyazTekServis.Services;
using Microsoft.EntityFrameworkCore;

namespace AyazTekServis.Models;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(ApplicationDbContext db, PasswordService passwordService)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");
            await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL;");
            await ExecuteAsync(connection, "PRAGMA busy_timeout = 5000;");
            await ExecuteAsync(connection, "PRAGMA temp_store = MEMORY;");

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "Users" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
                    "Username" TEXT NOT NULL DEFAULT '',
                    "Email" TEXT NOT NULL DEFAULT '',
                    "Password" TEXT NOT NULL DEFAULT '',
                    "SecurityQuestion" TEXT NOT NULL DEFAULT '',
                    "SecurityAnswer" TEXT NOT NULL DEFAULT '',
                    "IsActive" INTEGER NOT NULL DEFAULT 0,
                    "IsAdmin" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "LastLoginAt" TEXT NULL,
                    "ThemePreference" TEXT NOT NULL DEFAULT 'system'
                );
                """);

            await AddMissingColumnsAsync(connection, "Users", new()
            {
                ["Username"] = "TEXT NOT NULL DEFAULT ''",
                ["Email"] = "TEXT NOT NULL DEFAULT ''",
                ["Password"] = "TEXT NOT NULL DEFAULT ''",
                ["SecurityQuestion"] = "TEXT NOT NULL DEFAULT ''",
                ["SecurityAnswer"] = "TEXT NOT NULL DEFAULT ''",
                ["IsActive"] = "INTEGER NOT NULL DEFAULT 0",
                ["IsAdmin"] = "INTEGER NOT NULL DEFAULT 0",
                ["CreatedAt"] = "TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'",
                ["LastLoginAt"] = "TEXT NULL",
                ["ThemePreference"] = "TEXT NOT NULL DEFAULT 'system'"
            });

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "ServiceRecords" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ServiceRecords" PRIMARY KEY AUTOINCREMENT,
                    "Sender" TEXT NOT NULL DEFAULT '',
                    "CustomerEmail" TEXT NOT NULL DEFAULT '',
                    "CustomerPhone" TEXT NOT NULL DEFAULT '',
                    "Brand" TEXT NOT NULL DEFAULT '',
                    "Model" TEXT NOT NULL DEFAULT '',
                    "ProductType" TEXT NOT NULL DEFAULT '',
                    "SerialNumber" TEXT NOT NULL DEFAULT '',
                    "ServiceArrivalDate" TEXT NOT NULL DEFAULT '2000-01-01 00:00:00',
                    "ServiceSentDate" TEXT NULL,
                    "SentToService" TEXT NOT NULL DEFAULT '',
                    "TrackingNumber" TEXT NOT NULL DEFAULT '',
                    "FaultReason" TEXT NOT NULL DEFAULT '',
                    "Accessories" TEXT NOT NULL DEFAULT '',
                    "Notes" TEXT NOT NULL DEFAULT '',
                    "ControlNotes" TEXT NOT NULL DEFAULT '',
                    "Status" TEXT NOT NULL DEFAULT 'Kayıt Açıldı',
                    "ServiceResult" TEXT NOT NULL DEFAULT '',
                    "IsRepaired" INTEGER NOT NULL DEFAULT 0,
                    "IsUnderWarranty" INTEGER NOT NULL DEFAULT 1,
                    "HasCharge" INTEGER NOT NULL DEFAULT 0,
                    "ChargeAmount" TEXT NULL,
                    "ChargeCurrency" TEXT NOT NULL DEFAULT 'TRY',
                    "ChargeVatMode" TEXT NOT NULL DEFAULT 'KDV Hariç',
                    "DeliveryMethod" TEXT NOT NULL DEFAULT 'Müşteriye Teslim',
                    "DeliveryDate" TEXT NULL,
                    "CreatedByUserId" INTEGER NULL,
                    "CreatedByUsername" TEXT NOT NULL DEFAULT '',
                    "UpdatedByUserId" INTEGER NULL,
                    "UpdatedByUsername" TEXT NOT NULL DEFAULT '',
                    "CreatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "UpdatedAt" TEXT NULL,
                    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                    "DeletedAt" TEXT NULL,
                    "DeletedByUserId" INTEGER NULL,
                    "DeletedByUsername" TEXT NOT NULL DEFAULT ''
                );
                """);

            await AddMissingColumnsAsync(connection, "ServiceRecords", new()
            {
                ["Sender"] = "TEXT NOT NULL DEFAULT ''",
                ["CustomerEmail"] = "TEXT NOT NULL DEFAULT ''",
                ["CustomerPhone"] = "TEXT NOT NULL DEFAULT ''",
                ["Brand"] = "TEXT NOT NULL DEFAULT ''",
                ["Model"] = "TEXT NOT NULL DEFAULT ''",
                ["ProductType"] = "TEXT NOT NULL DEFAULT ''",
                ["SerialNumber"] = "TEXT NOT NULL DEFAULT ''",
                ["ServiceArrivalDate"] = "TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'",
                ["ServiceSentDate"] = "TEXT NULL",
                ["SentToService"] = "TEXT NOT NULL DEFAULT ''",
                ["TrackingNumber"] = "TEXT NOT NULL DEFAULT ''",
                ["FaultReason"] = "TEXT NOT NULL DEFAULT ''",
                ["Accessories"] = "TEXT NOT NULL DEFAULT ''",
                ["Notes"] = "TEXT NOT NULL DEFAULT ''",
                ["ControlNotes"] = "TEXT NOT NULL DEFAULT ''",
                ["Status"] = "TEXT NOT NULL DEFAULT 'Kayıt Açıldı'",
                ["ServiceResult"] = "TEXT NOT NULL DEFAULT ''",
                ["IsRepaired"] = "INTEGER NOT NULL DEFAULT 0",
                ["IsUnderWarranty"] = "INTEGER NOT NULL DEFAULT 1",
                ["HasCharge"] = "INTEGER NOT NULL DEFAULT 0",
                ["ChargeAmount"] = "TEXT NULL",
                ["ChargeCurrency"] = "TEXT NOT NULL DEFAULT 'TRY'",
                ["ChargeVatMode"] = "TEXT NOT NULL DEFAULT 'KDV Hariç'",
                ["DeliveryMethod"] = "TEXT NOT NULL DEFAULT 'Müşteriye Teslim'",
                ["DeliveryDate"] = "TEXT NULL",
                ["CreatedByUserId"] = "INTEGER NULL",
                ["CreatedByUsername"] = "TEXT NOT NULL DEFAULT ''",
                ["UpdatedByUserId"] = "INTEGER NULL",
                ["UpdatedByUsername"] = "TEXT NOT NULL DEFAULT ''",
                ["CreatedAt"] = "TEXT NOT NULL DEFAULT '2000-01-01 00:00:00'",
                ["UpdatedAt"] = "TEXT NULL",
                ["IsDeleted"] = "INTEGER NOT NULL DEFAULT 0",
                ["DeletedAt"] = "TEXT NULL",
                ["DeletedByUserId"] = "INTEGER NULL",
                ["DeletedByUsername"] = "TEXT NOT NULL DEFAULT ''"
            });
            var oldRecordColumns = await GetColumnsAsync(connection, "ServiceRecords");
            if (oldRecordColumns.Contains("CustomerName") || oldRecordColumns.Contains("SenderTitle"))
            {
                var source = oldRecordColumns.Contains("SenderTitle") && oldRecordColumns.Contains("CustomerName")
                    ? "COALESCE(NULLIF(\"SenderTitle\", ''), \"CustomerName\", '')"
                    : oldRecordColumns.Contains("SenderTitle") ? "COALESCE(\"SenderTitle\", '')" : "COALESCE(\"CustomerName\", '')";
                await ExecuteAsync(connection, $"UPDATE \"ServiceRecords\" SET \"Sender\" = {source} WHERE COALESCE(\"Sender\", '') = ''; ");
            }
            if (oldRecordColumns.Contains("DeviceModel"))
                await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"Model\" = \"DeviceModel\" WHERE COALESCE(\"Model\", '') = '' AND COALESCE(\"DeviceModel\", '') <> ''; ");
            if (oldRecordColumns.Contains("IssueDescription"))
                await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"FaultReason\" = \"IssueDescription\" WHERE COALESCE(\"FaultReason\", '') = '' AND COALESCE(\"IssueDescription\", '') <> ''; ");

            await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"CreatedByUsername\" = 'Sistem (eski kayıt)' WHERE COALESCE(\"CreatedByUsername\", '') = '';");
            await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"DeliveryMethod\" = 'Müşteriye Teslim' WHERE \"DeliveryMethod\" = 'Şirketten Teslim';");
            await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"Status\" = 'Serviste' WHERE \"Status\" = 'Servise Gönderildi';");
            await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"ChargeVatMode\" = 'KDV Hariç' WHERE COALESCE(\"ChargeVatMode\", '') NOT IN ('KDV Dahil', 'KDV Hariç');");
            await ExecuteAsync(connection, "UPDATE \"ServiceRecords\" SET \"ServiceSentDate\" = \"ServiceArrivalDate\" WHERE \"ServiceSentDate\" IS NULL AND \"Status\" NOT IN ('Kayıt Açıldı', 'Beklemede') AND \"ServiceArrivalDate\" IS NOT NULL;");

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "ServiceParts" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ServiceParts" PRIMARY KEY AUTOINCREMENT,
                    "ServiceRecordId" INTEGER NOT NULL,
                    "PartType" TEXT NOT NULL DEFAULT 'Takılan',
                    "PartName" TEXT NOT NULL DEFAULT '',
                    "SerialNumber" TEXT NOT NULL DEFAULT '',
                    "Quantity" INTEGER NOT NULL DEFAULT 1,
                    "UnitPrice" TEXT NULL,
                    "Currency" TEXT NOT NULL DEFAULT 'TRY',
                    "Notes" TEXT NOT NULL DEFAULT '',
                    CONSTRAINT "FK_ServiceParts_ServiceRecords_ServiceRecordId"
                        FOREIGN KEY ("ServiceRecordId") REFERENCES "ServiceRecords" ("Id") ON DELETE CASCADE
                );
                """);
            await AddMissingColumnsAsync(connection, "ServiceParts", new()
            {
                ["PartName"] = "TEXT NOT NULL DEFAULT ''",
                ["UnitPrice"] = "TEXT NULL",
                ["Currency"] = "TEXT NOT NULL DEFAULT 'TRY'",
                ["Notes"] = "TEXT NOT NULL DEFAULT ''"
            });
            await ExecuteAsync(connection, "UPDATE \"ServiceParts\" SET \"PartName\" = 'Parça' WHERE COALESCE(\"PartName\", '') = ''; ");

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "AuditLogs" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "UserId" INTEGER NULL,
                    "Username" TEXT NOT NULL DEFAULT '',
                    "Action" TEXT NOT NULL DEFAULT '',
                    "EntityType" TEXT NOT NULL DEFAULT '',
                    "EntityId" TEXT NOT NULL DEFAULT '',
                    "Description" TEXT NOT NULL DEFAULT '',
                    "OldValues" TEXT NOT NULL DEFAULT '',
                    "NewValues" TEXT NOT NULL DEFAULT '',
                    "IpAddress" TEXT NOT NULL DEFAULT '',
                    "CreatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """);

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "UserId" INTEGER NOT NULL,
                    "TokenHash" TEXT NOT NULL,
                    "ExpiresAt" TEXT NOT NULL,
                    "UsedAt" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """);

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "SystemSettings" (
                    "Key" TEXT NOT NULL PRIMARY KEY,
                    "Value" TEXT NOT NULL DEFAULT '',
                    "UpdatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "UpdatedBy" TEXT NOT NULL DEFAULT ''
                );
                """);

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "UserSessions" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "UserId" INTEGER NOT NULL,
                    "Username" TEXT NOT NULL DEFAULT '',
                    "SessionId" TEXT NOT NULL DEFAULT '',
                    "IpAddress" TEXT NOT NULL DEFAULT '',
                    "UserAgent" TEXT NOT NULL DEFAULT '',
                    "StartedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "LastSeenAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "ExpiresAt" TEXT NOT NULL,
                    "RevokedAt" TEXT NULL
                );
                """);
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "BackupHistories" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "CreatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "UserId" INTEGER NULL,
                    "Username" TEXT NOT NULL DEFAULT '',
                    "FileName" TEXT NOT NULL DEFAULT '',
                    "SizeBytes" INTEGER NOT NULL DEFAULT 0,
                    "IsSuccessful" INTEGER NOT NULL DEFAULT 0,
                    "ErrorMessage" TEXT NOT NULL DEFAULT ''
                );
                """);
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "ErrorLogs" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "CreatedAt" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    "UserId" INTEGER NULL,
                    "Username" TEXT NOT NULL DEFAULT '',
                    "Method" TEXT NOT NULL DEFAULT '',
                    "Path" TEXT NOT NULL DEFAULT '',
                    "IpAddress" TEXT NOT NULL DEFAULT '',
                    "ExceptionType" TEXT NOT NULL DEFAULT '',
                    "Message" TEXT NOT NULL DEFAULT '',
                    "StackTrace" TEXT NOT NULL DEFAULT ''
                );
                """);

            await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username_NoCase ON Users(Username COLLATE NOCASE);");
            await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_UserSessions_SessionId ON UserSessions(SessionId);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_UserSessions_UserId_RevokedAt_ExpiresAt ON UserSessions(UserId, RevokedAt, ExpiresAt);");
            await ExecuteAsync(connection, """
                WITH ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY LastSeenAt DESC, Id DESC) AS rn
                    FROM UserSessions
                    WHERE RevokedAt IS NULL
                )
                UPDATE UserSessions SET RevokedAt = CURRENT_TIMESTAMP
                WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);
                """);
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_BackupHistories_CreatedAt ON BackupHistories(CreatedAt DESC);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ErrorLogs_CreatedAt ON ErrorLogs(CreatedAt DESC);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ErrorLogs_UserId_CreatedAt ON ErrorLogs(UserId, CreatedAt DESC);");

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "ServiceSequences" (
                    "Period" TEXT NOT NULL PRIMARY KEY,
                    "LastNumber" INTEGER NOT NULL DEFAULT 0
                );
                """);

            await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username);");
            await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ServiceRecords_Status ON ServiceRecords(Status);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ServiceRecords_SerialNumber ON ServiceRecords(SerialNumber);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ServiceRecords_IsDeleted_CreatedAt ON ServiceRecords(IsDeleted, CreatedAt DESC);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ServiceRecords_IsDeleted_Status ON ServiceRecords(IsDeleted, Status);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ServiceRecords_IsDeleted_ServiceArrivalDate ON ServiceRecords(IsDeleted, ServiceArrivalDate);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_ServiceRecords_IsDeleted_ServiceSentDate ON ServiceRecords(IsDeleted, ServiceSentDate);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Users_CreatedAt ON Users(CreatedAt DESC);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedAt ON AuditLogs(CreatedAt);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId_CreatedAt ON AuditLogs(UserId, CreatedAt DESC);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_AuditLogs_EntityType_EntityId ON AuditLogs(EntityType, EntityId);");
            await ExecuteAsync(connection, """
                UPDATE AuditLogs
                SET UserId = CASE WHEN EntityId GLOB '[0-9]*' THEN CAST(EntityId AS INTEGER) ELSE UserId END,
                    Username = CASE
                        WHEN instr(Description, ' sisteme giriş yaptı.') > 1
                        THEN substr(Description, 1, instr(Description, ' sisteme giriş yaptı.') - 1)
                        ELSE Username
                    END
                WHERE Action = 'Giriş' AND Username = 'Sistem';
                """);
            await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_PasswordResetTokens_TokenHash ON PasswordResetTokens(TokenHash);");
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PasswordResetTokens_UserId_ExpiresAt ON PasswordResetTokens(UserId, ExpiresAt);");
            await BackfillServiceNumbersAsync(connection);
            await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS IX_ServiceRecords_TrackingNumber ON ServiceRecords(TrackingNumber);");
        }
        finally
        {
            await connection.CloseAsync();
        }
        var thresholdDate = new DateTime(2001, 1, 1);
        var users = await db.Users
            .Where(user => !EF.Functions.Like(user.Password, "PBKDF2$%")
                || (!string.IsNullOrWhiteSpace(user.SecurityAnswer) && !EF.Functions.Like(user.SecurityAnswer, "PBKDF2$%"))
                || user.CreatedAt < thresholdDate)
            .ToListAsync();

        var changed = false;
        foreach (var user in users)
        {
            if (!passwordService.IsHashed(user.Password))
            {
                user.Password = passwordService.Hash(user.Password);
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(user.SecurityAnswer) && !passwordService.IsHashed(user.SecurityAnswer))
            {
                user.SecurityAnswer = passwordService.Hash("answer|" + user.SecurityAnswer.Trim().ToLowerInvariant());
                changed = true;
            }
            if (user.CreatedAt < thresholdDate)
            {
                user.CreatedAt = DateTime.Now;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task BackfillServiceNumbersAsync(DbConnection connection)
    {
        var rows = new List<(long Id, DateTime Date, string Tracking)>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, ServiceArrivalDate, TrackingNumber FROM ServiceRecords ORDER BY ServiceArrivalDate, Id;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawDate = reader["ServiceArrivalDate"]?.ToString();
                if (!DateTime.TryParse(rawDate, out var date)) date = DateTime.Today;
                rows.Add((Convert.ToInt64(reader["Id"]), date, reader["TrackingNumber"]?.ToString() ?? string.Empty));
            }
        }

        var counters = new Dictionary<string, int>();
        foreach (var row in rows)
        {
            var period = row.Date.ToString("yyyy/MM");
            counters.TryGetValue(period, out var n);
            n++;
            counters[period] = n;
            var expectedPrefix = period + "/";
            if (!row.Tracking.StartsWith(expectedPrefix, StringComparison.Ordinal) || !int.TryParse(row.Tracking[expectedPrefix.Length..], out _))
            {
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE ServiceRecords SET TrackingNumber=$number WHERE Id=$id;";
                update.Parameters.Add(CreateParameter(update, "$number", $"{period}/{n:00}"));
                update.Parameters.Add(CreateParameter(update, "$id", row.Id));
                await update.ExecuteNonQueryAsync();
            }
        }

        foreach (var item in counters)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO ServiceSequences(Period,LastNumber) VALUES($p,$n) ON CONFLICT(Period) DO UPDATE SET LastNumber = MAX(LastNumber, excluded.LastNumber);";
            cmd.Parameters.Add(CreateParameter(cmd, "$p", item.Key));
            cmd.Parameters.Add(CreateParameter(cmd, "$n", item.Value));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static DbParameter CreateParameter(DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(DbConnection connection, string tableName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) existing.Add(reader["name"]?.ToString() ?? string.Empty);
        return existing;
    }

    private static async Task AddMissingColumnsAsync(DbConnection connection, string tableName, Dictionary<string, string> columns)
    {
        var existing = await GetColumnsAsync(connection, tableName);
        foreach (var column in columns)
        {
            if (existing.Contains(column.Key)) continue;
            await ExecuteAsync(connection, $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{column.Key}\" {column.Value};");
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
