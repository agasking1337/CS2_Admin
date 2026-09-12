using System.Collections.Concurrent;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Dapper;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Services;

public sealed class AltAccountDetectionService
{
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(6);
    private const int MaxLinkedAccounts = 8;
    private const int MaxSanctionsPerAccount = 8;

    private readonly ISwiftlyCore _core;
    private readonly PlayerIpDbManager _playerIpDbManager;
    private readonly ConcurrentDictionary<ulong, DateTime> _recentAlerts = new();

    public AltAccountDetectionService(ISwiftlyCore core, PlayerIpDbManager playerIpDbManager)
    {
        _core = core;
        _playerIpDbManager = playerIpDbManager;
    }

    public async Task<AltAccountReport?> CheckAsync(ulong steamId, string? ipAddress)
    {
        var normalizedIp = NormalizeIp(ipAddress);
        if (steamId == 0 || string.IsNullOrWhiteSpace(normalizedIp))
        {
            return null;
        }

        try
        {
            var linkedAccounts = await _playerIpDbManager.FindAccountsByIpAsync(normalizedIp, steamId, MaxLinkedAccounts);
            var ipBans = await GetIpTargetedBansAsync(normalizedIp);

            var matches = new List<AltAccountMatch>();
            foreach (var account in linkedAccounts)
            {
                var sanctions = await GetSanctionHistoryAsync(account.SteamId);
                if (sanctions.Count > 0)
                {
                    matches.Add(new AltAccountMatch(account, sanctions));
                }
            }

            if (matches.Count == 0 && ipBans.Count == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (_recentAlerts.TryGetValue(steamId, out var lastAlert) && now - lastAlert < AlertCooldown)
            {
                return null;
            }

            if (_recentAlerts.Count > 512)
            {
                foreach (var entry in _recentAlerts)
                {
                    if (now - entry.Value > AlertCooldown)
                    {
                        _recentAlerts.TryRemove(entry.Key, out _);
                    }
                }
            }

            _recentAlerts[steamId] = now;
            return new AltAccountReport(normalizedIp, matches, ipBans);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Alt account check failed for {SteamId}: {Message}", steamId, ex.Message);
            return null;
        }
    }

    private async Task<List<AltSanction>> GetSanctionHistoryAsync(ulong steamId)
    {
        using var connection = _core.Database.GetConnection("mysql_detailed");
        var now = DateTime.UtcNow;
        var rows = await connection.QueryAsync<SanctionRow>(
            """
            SELECT 'ban' AS `Type`, `reason` AS `Reason`, `admin_name` AS `AdminName`, `created_at` AS `CreatedAt`, `expires_at` AS `ExpiresAt`, `status` AS `Status`
            FROM `admin_bans` WHERE `steamid` = @SteamId
            UNION ALL
            SELECT 'mute' AS `Type`, `reason` AS `Reason`, `admin_name` AS `AdminName`, `created_at` AS `CreatedAt`, `expires_at` AS `ExpiresAt`, `status` AS `Status`
            FROM `admin_mutes` WHERE `steamid` = @SteamId
            UNION ALL
            SELECT 'gag' AS `Type`, `reason` AS `Reason`, `admin_name` AS `AdminName`, `created_at` AS `CreatedAt`, `expires_at` AS `ExpiresAt`, `status` AS `Status`
            FROM `admin_gags` WHERE `steamid` = @SteamId
            UNION ALL
            SELECT 'warn' AS `Type`, `reason` AS `Reason`, `admin_name` AS `AdminName`, `created_at` AS `CreatedAt`, `expires_at` AS `ExpiresAt`, `status` AS `Status`
            FROM `admin_warns` WHERE `steamid` = @SteamId
            ORDER BY `CreatedAt` DESC
            """,
            new { SteamId = Convert.ToInt64(steamId) });

        return rows
            .Take(MaxSanctionsPerAccount)
            .Select(r => new AltSanction(
                r.Type ?? "sanction",
                r.Reason ?? string.Empty,
                r.AdminName ?? string.Empty,
                r.CreatedAt,
                r.ExpiresAt,
                IsActiveStatus(r.Status) && (!r.ExpiresAt.HasValue || r.ExpiresAt.Value > now)))
            .ToList();
    }

    private async Task<List<IpBanInfo>> GetIpTargetedBansAsync(string ipAddress)
    {
        using var connection = _core.Database.GetConnection("mysql_detailed");
        var now = DateTime.UtcNow;
        var rows = await connection.QueryAsync<IpBanRow>(
            $"""
            SELECT `target_name` AS `TargetName`, `steamid` AS `SteamId`, `reason` AS `Reason`, `admin_name` AS `AdminName`, `created_at` AS `CreatedAt`, `expires_at` AS `ExpiresAt`, `status` AS `Status`
            FROM `admin_bans`
            WHERE `ip_address` = @IpAddress AND {PunishmentQueryCompat.ActiveIpTargetWhere}
            ORDER BY `created_at` DESC
            LIMIT 5
            """,
            new { IpAddress = ipAddress });

        return rows
            .Select(r => new IpBanInfo(
                r.TargetName ?? string.Empty,
                r.SteamId,
                r.Reason ?? string.Empty,
                r.AdminName ?? string.Empty,
                r.CreatedAt,
                r.ExpiresAt,
                IsActiveStatus(r.Status) && (!r.ExpiresAt.HasValue || r.ExpiresAt.Value > now)))
            .ToList();
    }

    private static bool IsActiveStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s is "" or "0" or "1" or "active";
    }

    private static string? NormalizeIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var normalized = ipAddress.Trim();
        var colonIndex = normalized.IndexOf(':');
        if (colonIndex > 0)
        {
            normalized = normalized[..colonIndex];
        }

        return normalized;
    }

    private sealed class SanctionRow
    {
        public string? Type { get; set; }
        public string? Reason { get; set; }
        public string? AdminName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Status { get; set; }
    }

    private sealed class IpBanRow
    {
        public string? TargetName { get; set; }
        public ulong SteamId { get; set; }
        public string? Reason { get; set; }
        public string? AdminName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Status { get; set; }
    }
}

public sealed record AltSanction(string Type, string Reason, string AdminName, DateTime CreatedAt, DateTime? ExpiresAt, bool IsActive);

public sealed record IpBanInfo(string TargetName, ulong SteamId, string Reason, string AdminName, DateTime CreatedAt, DateTime? ExpiresAt, bool IsActive);

public sealed record AltAccountMatch(PlayerIpAccountLink Account, IReadOnlyList<AltSanction> Sanctions);

public enum AltAlertSeverity
{
    Low,
    Medium,
    High
}

public sealed record AltAccountReport(string IpAddress, IReadOnlyList<AltAccountMatch> Matches, IReadOnlyList<IpBanInfo> IpBans)
{
    public bool IsEmpty => Matches.Count == 0 && IpBans.Count == 0;

    public AltAlertSeverity Severity
    {
        get
        {
            if (IpBans.Any(b => b.IsActive) || Matches.Any(m => m.Sanctions.Any(s => s.IsActive && s.Type == "ban")))
            {
                return AltAlertSeverity.High;
            }

            if (Matches.Any(m => m.Sanctions.Any(s => s.IsActive || s.Type == "ban")) || IpBans.Count > 0)
            {
                return AltAlertSeverity.Medium;
            }

            return AltAlertSeverity.Low;
        }
    }
}
