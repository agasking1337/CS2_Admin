using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class AdminLogManager
{
    private static readonly HashSet<string> AuditActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ban",
        "ipban",
        "addban",
        "unban",
        "kick",
        "mute",
        "unmute",
        "gag",
        "ungag",
        "warn",
        "unwarn",
        "addadmin",
        "editadmin",
        "removeadmin",
        "addgroup",
        "editgroup",
        "removegroup",
        "adminreload"
    };

    private readonly ISwiftlyCore _core;
    private DiscordBotService? _discordBot;
    private DateTime _lastSlapDiscordLog = DateTime.MinValue;

    public AdminLogManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SetDiscordBotService(DiscordBotService discordBot)
    {
        _discordBot = discordBot;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("Admin log database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Admin log database initialization warning: {Message}", ex.Message);
        }
    }

            public async Task AddLogAsync(
        string action,
        string adminName,
        ulong adminSteamId,
        ulong? targetSteamId,
        string? targetIp,
        string details,
        string? targetName = null,
        int? targetUserId = null,
        string? reason = null)
    {
        var serverId = ServerIdentity.GetServerId(_core);

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var now = DateTime.UtcNow;

            connection.Insert(new AdminLog
            {
                Action = SafeName.ForReason(action, 64),
                AdminName = SafeName.ForPlayer(adminName, adminSteamId),
                AdminSteamId = adminSteamId,
                TargetSteamId = targetSteamId,
                TargetIp = targetIp,
                Details = SafeName.ForReason(details, 4096),
                ServerId = serverId,
                ServerIp = ServerIdentity.GetIp(_core),
                ServerPort = ServerIdentity.GetPort(_core),
                CreatedAt = now
            });

            if (ShouldWriteAuditAction(action))
            {
                connection.Insert(new AdminActionLogRecord
                {
                    Action = NormalizeAction(action),
                    TargetSteamId = targetSteamId is > 0 ? targetSteamId : null,
                    TargetName = SafeName.ForPlayer(targetName, targetSteamId ?? 0),
                    TargetUserId = targetUserId,
                    AdminName = SafeName.ForPlayer(adminName, adminSteamId),
                    AdminSteamId = adminSteamId == 0 ? null : adminSteamId,
                    Reason = SafeName.ForReason(reason ?? ExtractReason(details), 2048),
                    ServerId = string.IsNullOrWhiteSpace(serverId) ? string.Empty : serverId,
                    CreatedAt = now
                });
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error writing admin log to database: {Message}", ex.Message);
        }

        try
        {
            if (ShouldSendAdminActionWebhook(action, adminSteamId))
            {
                await _discordBot!.SendAdminActionNotificationAsync(
                    action,
                    adminName,
                    adminSteamId,
                    targetSteamId,
                    details,
                    serverId,
                    targetName);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error sending discord admin log: {Message}", ex.Message);
        }
    }
    private bool ShouldSendAdminActionWebhook(string action, ulong adminSteamId)
    {
        if (_discordBot == null)
            return false;

        if (action.Equals("calladmin", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("report", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (action.Equals("slap", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSlapDiscordLog).TotalSeconds < 5)
            {
                return false;
            }
            _lastSlapDiscordLog = now;
        }

        return true;
    }

    private static bool ShouldWriteAuditAction(string action)
    {
        return AuditActions.Contains(NormalizeAction(action));
    }

    private static string NormalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return string.Empty;
        }

        var normalized = action.Trim().ToLowerInvariant();
        if (normalized == "ban_both")
        {
            return "ban";
        }

        return normalized.StartsWith("lastban_", StringComparison.Ordinal)
            ? normalized["lastban_".Length..]
            : normalized;
    }

    private static string? ExtractReason(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        const string marker = "reason=";
        var index = details.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + marker.Length;
        var end = details.IndexOf(';', start);
        if (end < 0)
        {
            end = details.Length;
        }

        var parsedReason = details[start..end].Trim();
        return string.IsNullOrWhiteSpace(parsedReason) ? null : parsedReason;
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    public async Task<List<AdminLog>> GetTargetHistoryAsync(ulong targetSteamId, int limit = 5)
    {
        try
        {
            if (_core.Database == null)
            {
                return new List<AdminLog>();
            }

            using var connection = _core.Database.GetConnection("mysql_detailed");
            if (connection == null)
            {
                return new List<AdminLog>();
            }

            var query = "SELECT * FROM `admin_log` WHERE `target_steamid` = @TargetSteamId ORDER BY `created_at` DESC LIMIT @Limit";
            var result = await connection.QueryAsync<AdminLog>(query, new { TargetSteamId = targetSteamId, Limit = limit });
            return result.ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting target history: {Message}", ex.Message);
            return new List<AdminLog>();
        }
    }
}
