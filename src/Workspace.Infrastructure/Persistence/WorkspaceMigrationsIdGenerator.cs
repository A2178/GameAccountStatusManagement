using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Workspace.Infrastructure.Persistence;

// 已套用的 M0/M1 migration ID 不能改名，否則既有資料庫會再次執行建表操作。
// 讀取時相容舊的 12 位前綴；新 migration 仍使用 EF 標準的 14 位時間前綴。
public sealed partial class WorkspaceMigrationsIdGenerator : IMigrationsIdGenerator
{
    private readonly object timestampLock = new();
    private DateTime lastTimestamp;

    public bool IsValidId(string value) => MigrationIdPattern().IsMatch(value);

    public string GetName(string id) => id[(id.IndexOf('_') + 1)..];

    public string GenerateId(string name)
    {
        lock (timestampLock)
        {
            var now = DateTime.UtcNow;
            var timestamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
            if (timestamp <= lastTimestamp)
                timestamp = lastTimestamp.AddSeconds(1);
            lastTimestamp = timestamp;
            return timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + name;
        }
    }

    [GeneratedRegex(@"^(?:[0-9]{12}|[0-9]{14})_.+$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationIdPattern();
}
