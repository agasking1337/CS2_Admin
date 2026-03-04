using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class BanManager
{
    private readonly ISwiftlyCore _core;
    private readonly Dictionary<string, Ban> _banCache = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private readonly AsyncLocal<AdminContext> _currentAdmin = new();

    public BanManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SetAdminContext(string? adminName, ulong? adminSteamId)
    {
        _currentAdmin.Value = new AdminContext
        {
            Name = adminName ?? PluginLocalizer.Get(_core)["console_name"],
            SteamId = adminSteamId ?? 0
        };
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Ban database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Ban database initialization warning: {Message}", ex.Message);
        }
    }

    public Task<bool> AddBanAsync(
        ulong steamId,
        string targetName,
        int durationMinutes,
        string reason,
        bool isGlobal)
    {
        return AddBanInternalAsync(steamId, targetName, null, BanTargetType.SteamId, durationMinutes, reason, isGlobal);
    }

    public Task<bool> AddIpBanAsync(
        string ipAddress,
        string targetName,
        int durationMinutes,
        string reason,
        bool isGlobal,
        ulong steamId = 0)
    {
        return AddBanInternalAsync(steamId, targetName, NormalizeIpAddress(ipAddress), BanTargetType.Ip, durationMinutes, reason, isGlobal);
    }

    private async Task<bool> AddBanInternalAsync(
        ulong steamId,
        string targetName,
        string? ipAddress,
        BanTargetType targetType,
        int durationMinutes,
        string reason,
        bool isGlobal)
    {
        try
        {
            var normalizedIp = NormalizeIpAddress(ipAddress);
            var admin = _currentAdmin.Value ?? new AdminContext();
            var createdAt = DateTime.UtcNow;
            DateTime? expiresAt = durationMinutes > 0 ? createdAt.AddMinutes(durationMinutes) : null;
            // Use numeric values for compatibility with both legacy numeric schemas and current string schemas.
            var targetTypeValue = targetType == BanTargetType.Ip ? "1" : "0";

            var ban = new Ban
            {
                SteamId = steamId,
                TargetName = targetName,
                TargetType = targetType,
                IpAddress = normalizedIp,
                AdminName = admin.Name,
                AdminSteamId = admin.SteamId,
                Reason = reason,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt,
                IsGlobal = isGlobal,
                ServerId = ServerIdentity.GetServerId(_core),
                ServerIp = ServerIdentity.GetIp(_core),
                ServerPort = ServerIdentity.GetPort(_core),
                Status = BanStatus.Active
            };

            using var connection = _core.Database.GetConnection("admins");
            connection.Execute(
                """
                INSERT INTO `admin_bans`
                (`steamid`, `target_name`, `target_type`, `ip_address`, `admin_name`, `admin_steamid`, `reason`, `is_global`, `server_id`, `server_ip`, `server_port`, `created_at`, `expires_at`, `status`)
                VALUES
                (@SteamId, @TargetName, @TargetType, @IpAddress, @AdminName, @AdminSteamId, @Reason, @IsGlobal, @ServerId, @ServerIp, @ServerPort, @CreatedAt, @ExpiresAt, @Status)
                """,
                new
                {
                    ban.SteamId,
                    ban.TargetName,
                    TargetType = targetTypeValue,
                    ban.IpAddress,
                    ban.AdminName,
                    ban.AdminSteamId,
                    ban.Reason,
                    ban.IsGlobal,
                    ban.ServerId,
                    ban.ServerIp,
                    ban.ServerPort,
                    ban.CreatedAt,
                    ban.ExpiresAt,
                    Status = "1"
                });

            CacheBan(ban);
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error adding ban: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> UnbanAsync(ulong steamId, string unbanReason)
    {
        return await UnbanSteamByIdWithCountAsync(steamId, unbanReason) > 0;
    }

    public async Task<int> UnbanSteamByIdWithCountAsync(ulong steamId, string unbanReason)
    {
        try
        {
            var admin = _currentAdmin.Value ?? new AdminContext();
            using var connection = _core.Database.GetConnection("admins");
            var affected = connection.Execute(
                $"""
                UPDATE `admin_bans`
                SET `status` = @Status,
                    `unban_admin_name` = @UnbanAdminName,
                    `unban_admin_steamid` = @UnbanAdminSteamId,
                    `unban_reason` = @UnbanReason,
                    `unban_date` = @UnbanDate
                WHERE `steamid` = @SteamId
                  AND {PunishmentQueryCompat.ActiveStatusWhere}
                """,
                new
                {
                    SteamId = steamId,
                    Status = "2",
                    UnbanAdminName = admin.Name,
                    UnbanAdminSteamId = admin.SteamId,
                    UnbanReason = unbanReason,
                    UnbanDate = DateTime.UtcNow
                });

            RemoveFromCache(new Ban { TargetType = BanTargetType.SteamId, SteamId = steamId });
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] unban steam affected={Count} steamid={SteamId}", affected, steamId);
            return affected;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error unbanning player: {Message}", ex.Message);
            return 0;
        }
    }

    public async Task<bool> UnbanIpAsync(string ipAddress, string unbanReason)
    {
        return await UnbanIpWithCountAsync(ipAddress, unbanReason) > 0;
    }

    public async Task<int> UnbanIpWithCountAsync(string ipAddress, string unbanReason)
    {
        try
        {
            var admin = _currentAdmin.Value ?? new AdminContext();
            using var connection = _core.Database.GetConnection("admins");
            var normalizedIp = NormalizeIpAddress(ipAddress);
            if (string.IsNullOrWhiteSpace(normalizedIp))
            {
                return 0;
            }
            var affected = connection.Execute(
                $"""
                UPDATE `admin_bans`
                SET `status` = @Status,
                    `unban_admin_name` = @UnbanAdminName,
                    `unban_admin_steamid` = @UnbanAdminSteamId,
                    `unban_reason` = @UnbanReason,
                    `unban_date` = @UnbanDate
                WHERE `ip_address` = @IpAddress
                  AND {PunishmentQueryCompat.ActiveStatusWhere}
                """,
                new
                {
                    IpAddress = normalizedIp,
                    Status = "2",
                    UnbanAdminName = admin.Name,
                    UnbanAdminSteamId = admin.SteamId,
                    UnbanReason = unbanReason,
                    UnbanDate = DateTime.UtcNow
                });

            RemoveFromCache(new Ban { TargetType = BanTargetType.Ip, IpAddress = normalizedIp });
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] unban ip affected={Count} ip={Ip}", affected, normalizedIp);
            return affected;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error unbanning ip: {Message}", ex.Message);
            return 0;
        }
    }

    public async Task<Ban?> GetActiveBanAsync(ulong steamId, string? ipAddress, bool multiServerEnabled)
    {
        try
        {
            var normalizedIp = NormalizeIpAddress(ipAddress);
            if (TryGetCachedBan(steamId, ipAddress, out var cached))
            {
                return cached;
            }

            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;
            var serverId = ServerIdentity.GetServerId(_core);
            var serverIp = ServerIdentity.GetIp(_core);
            var serverPort = ServerIdentity.GetPort(_core);
            var rows = connection.Query<BanRow>(
                $"""
                SELECT
                    `id` AS `Id`,
                    `steamid` AS `SteamId`,
                    `target_name` AS `TargetName`,
                    `target_type` AS `TargetType`,
                    `ip_address` AS `IpAddress`,
                    `admin_name` AS `AdminName`,
                    `admin_steamid` AS `AdminSteamId`,
                    `reason` AS `Reason`,
                    `is_global` AS `IsGlobal`,
                    `server_id` AS `ServerId`,
                    `server_ip` AS `ServerIp`,
                    `server_port` AS `ServerPort`,
                    `created_at` AS `CreatedAt`,
                    `expires_at` AS `ExpiresAt`,
                    `status` AS `Status`,
                    `unban_admin_name` AS `UnbanAdminName`,
                    `unban_admin_steamid` AS `UnbanAdminSteamId`,
                    `unban_reason` AS `UnbanReason`,
                    `unban_date` AS `UnbanDate`
                FROM `admin_bans`
                WHERE
                    (
                        (`steamid` = @SteamId AND {PunishmentQueryCompat.ActiveSteamTargetWhere})
                        OR
                        (@HasIp = 1 AND `ip_address` = @IpAddress AND {PunishmentQueryCompat.ActiveIpTargetWhere})
                    )
                    AND {PunishmentQueryCompat.ActiveStatusWhere}
                    AND (`expires_at` IS NULL OR `expires_at` > @Now)
                ORDER BY `created_at` DESC
                LIMIT 64
                """,
                new
                {
                    SteamId = steamId,
                    HasIp = string.IsNullOrWhiteSpace(normalizedIp) ? 0 : 1,
                    IpAddress = normalizedIp,
                    Now = now
                }).ToList();

            foreach (var row in rows)
            {
                if (!IsActiveStatus(row.Status))
                {
                    continue;
                }

                var mapped = ToBan(row);
                var matchesTarget = (mapped.TargetType == BanTargetType.SteamId && mapped.SteamId == steamId)
                                    || (mapped.TargetType == BanTargetType.Ip && !string.IsNullOrWhiteSpace(normalizedIp) && NormalizeIpAddress(mapped.IpAddress) == normalizedIp);
                if (!matchesTarget)
                {
                    continue;
                }

                if (!mapped.IsGlobal && multiServerEnabled && !IsSameServer(mapped, serverId, serverIp, serverPort))
                {
                    continue;
                }

                CacheBan(mapped);
                return mapped;
            }

            return null;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking ban: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<Ban?> GetActiveBanForEnforcementAsync(ulong steamId, string? ipAddress)
    {
        try
        {
            var normalizedIp = NormalizeIpAddress(ipAddress);
            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;

            var row = connection.QueryFirstOrDefault<BanRow>(
                $"""
                SELECT
                    `id` AS `Id`,
                    `steamid` AS `SteamId`,
                    `target_name` AS `TargetName`,
                    `target_type` AS `TargetType`,
                    `ip_address` AS `IpAddress`,
                    `admin_name` AS `AdminName`,
                    `admin_steamid` AS `AdminSteamId`,
                    `reason` AS `Reason`,
                    `is_global` AS `IsGlobal`,
                    `server_id` AS `ServerId`,
                    `server_ip` AS `ServerIp`,
                    `server_port` AS `ServerPort`,
                    `created_at` AS `CreatedAt`,
                    `expires_at` AS `ExpiresAt`,
                    `status` AS `Status`,
                    `unban_admin_name` AS `UnbanAdminName`,
                    `unban_admin_steamid` AS `UnbanAdminSteamId`,
                    `unban_reason` AS `UnbanReason`,
                    `unban_date` AS `UnbanDate`
                FROM `admin_bans`
                WHERE
                    (
                        (@HasSteam = 1 AND `steamid` = @SteamId)
                        OR
                        (@HasIp = 1 AND `ip_address` = @IpAddress)
                    )
                    AND {PunishmentQueryCompat.ActiveStatusWhere}
                    AND (`expires_at` IS NULL OR `expires_at` > @Now)
                ORDER BY `created_at` DESC
                LIMIT 1
                """,
                new
                {
                    HasSteam = steamId > 0 ? 1 : 0,
                    SteamId = steamId,
                    HasIp = string.IsNullOrWhiteSpace(normalizedIp) ? 0 : 1,
                    IpAddress = normalizedIp,
                    Now = now
                });

            if (row == null || !IsActiveStatus(row.Status))
            {
                return null;
            }

            var ban = ToBan(row);
            return ban.IsActive ? ban : null;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking enforcement ban: {Message}", ex.Message);
            return null;
        }
    }

    public Ban? GetActiveBanFromCache(ulong steamId, string? ipAddress)
    {
        if (TryGetCachedBan(steamId, ipAddress, out var ban))
        {
            return ban;
        }
        return null;
    }

    private bool TryGetCachedBan(ulong steamId, string? ipAddress, out Ban? ban)
    {
        ban = null;
        var normalizedIp = NormalizeIpAddress(ipAddress);
        if (DateTime.UtcNow - _lastCacheUpdate >= _cacheLifetime)
        {
            return false;
        }

        var steamKey = GetSteamKey(steamId);
        if (_banCache.TryGetValue(steamKey, out var steamBan) && steamBan.IsActive)
        {
            ban = steamBan;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(normalizedIp))
        {
            var ipKey = GetIpKey(normalizedIp);
            if (_banCache.TryGetValue(ipKey, out var ipBan) && ipBan.IsActive)
            {
                ban = ipBan;
                return true;
            }
        }

        return false;
    }

    private void CacheBan(Ban ban)
    {
        if (ban.TargetType == BanTargetType.SteamId && ban.SteamId != 0)
        {
            _banCache[GetSteamKey(ban.SteamId)] = ban;
        }
        else if (ban.TargetType == BanTargetType.Ip && !string.IsNullOrWhiteSpace(ban.IpAddress))
        {
            _banCache[GetIpKey(ban.IpAddress)] = ban;
        }

        _lastCacheUpdate = DateTime.UtcNow;
    }

    private void RemoveFromCache(Ban ban)
    {
        if (ban.TargetType == BanTargetType.SteamId && ban.SteamId != 0)
        {
            _banCache.Remove(GetSteamKey(ban.SteamId));
        }
        else if (ban.TargetType == BanTargetType.Ip && !string.IsNullOrWhiteSpace(ban.IpAddress))
        {
            _banCache.Remove(GetIpKey(ban.IpAddress));
        }
    }

    private static string GetSteamKey(ulong steamId) => $"steam:{steamId}";
    private static string GetIpKey(string ip) => $"ip:{NormalizeIpAddress(ip)}";

    private static string? NormalizeIpAddress(string? ipAddress)
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

    private static bool IsSameServer(Ban ban, string currentServerId, string currentServerIp, int currentServerPort)
    {
        if (!string.IsNullOrWhiteSpace(ban.ServerId) &&
            ban.ServerId.Equals(currentServerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var samePort = ban.ServerPort > 0 && currentServerPort > 0 && ban.ServerPort == currentServerPort;
        if (!samePort)
        {
            if (ban.ServerPort == 0 || currentServerPort == 0)
            {
                // If one side does not have a valid port, fall back to IP comparison only.
                return IsSameIpScope(ban.ServerIp, currentServerIp);
            }

            return false;
        }

        return IsSameIpScope(ban.ServerIp, currentServerIp);
    }

    private static bool IsSameIpScope(string? leftIp, string? rightIp)
    {
        if (IsWildcardIp(leftIp) || IsWildcardIp(rightIp))
        {
            return true;
        }

        return string.Equals((leftIp ?? string.Empty).Trim(), (rightIp ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWildcardIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return true;
        }

        var normalized = ip.Trim();
        return normalized == "0.0.0.0"
               || normalized == "::"
               || normalized == "[::]";
    }

    public async Task<int> GetTotalBansAsync(ulong steamId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            return connection.Select<Ban>(b => b.SteamId == steamId).Count();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting total bans: {Message}", ex.Message);
            return 0;
        }
    }

    public async Task CleanupExpiredBansAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var cleaned = connection.Execute(
                $"""
                UPDATE `admin_bans`
                SET `status` = '3'
                WHERE
                    {PunishmentQueryCompat.ActiveStatusWhere}
                    AND `expires_at` IS NOT NULL
                    AND `expires_at` <= @Now
                """,
                new { Now = DateTime.UtcNow });

            if (cleaned > 0)
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Marked {Count} bans as expired", cleaned);
                _banCache.Clear();
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error cleaning expired bans: {Message}", ex.Message);
        }
    }

    public void ClearCache()
    {
        _banCache.Clear();
        _lastCacheUpdate = DateTime.MinValue;
    }

    private static bool IsActiveStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return true;
        }

        var status = rawStatus.Trim();
        return status == "0"
               || status == "1"
               || status.Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    private static BanTargetType ParseTargetType(string? rawTargetType)
    {
        if (string.IsNullOrWhiteSpace(rawTargetType))
        {
            return BanTargetType.SteamId;
        }

        var targetType = rawTargetType.Trim();
        if (targetType == "1" || targetType.Equals("ip", StringComparison.OrdinalIgnoreCase))
        {
            return BanTargetType.Ip;
        }

        return BanTargetType.SteamId;
    }

    private static BanStatus ParseStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return BanStatus.Active;
        }

        var status = rawStatus.Trim();
        if (status == "2" || status.Equals("unbanned", StringComparison.OrdinalIgnoreCase))
        {
            return BanStatus.Unbanned;
        }

        if (status == "3" || status.Equals("expired", StringComparison.OrdinalIgnoreCase))
        {
            return BanStatus.Expired;
        }

        return BanStatus.Active;
    }

    private static Ban ToBan(BanRow row)
    {
        return new Ban
        {
            Id = row.Id,
            SteamId = row.SteamId,
            TargetName = row.TargetName ?? string.Empty,
            TargetType = ParseTargetType(row.TargetType),
            IpAddress = row.IpAddress,
            AdminName = row.AdminName ?? string.Empty,
            AdminSteamId = row.AdminSteamId,
            Reason = row.Reason ?? string.Empty,
            IsGlobal = row.IsGlobal,
            ServerId = row.ServerId ?? string.Empty,
            ServerIp = row.ServerIp ?? string.Empty,
            ServerPort = row.ServerPort,
            CreatedAt = row.CreatedAt,
            ExpiresAt = row.ExpiresAt,
            Status = ParseStatus(row.Status),
            UnbanAdminName = row.UnbanAdminName,
            UnbanAdminSteamId = row.UnbanAdminSteamId,
            UnbanReason = row.UnbanReason,
            UnbanDate = row.UnbanDate
        };
    }

    private sealed class BanRow
    {
        public int Id { get; set; }
        public ulong SteamId { get; set; }
        public string? TargetName { get; set; }
        public string? TargetType { get; set; }
        public string? IpAddress { get; set; }
        public string? AdminName { get; set; }
        public ulong AdminSteamId { get; set; }
        public string? Reason { get; set; }
        public bool IsGlobal { get; set; }
        public string? ServerId { get; set; }
        public string? ServerIp { get; set; }
        public int ServerPort { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Status { get; set; }
        public string? UnbanAdminName { get; set; }
        public ulong? UnbanAdminSteamId { get; set; }
        public string? UnbanReason { get; set; }
        public DateTime? UnbanDate { get; set; }
    }
}


