using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public class WarnManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _warnCache = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromSeconds(30);
    private readonly AsyncLocal<AdminContext> _currentAdmin = new();

    public WarnManager(ISwiftlyCore core)
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
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("Warn database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Warn database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<bool> AddWarnAsync(ulong steamId, int durationMinutes, string reason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                DateTime? expiresAt = durationMinutes > 0 ? DateTime.UtcNow.AddMinutes(durationMinutes) : null;

                var warn = new Warn
                {
                    SteamId = steamId,
                    AdminName = admin.Name,
                    AdminSteamId = admin.SteamId,
                    Reason = reason,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    Status = WarnStatus.Active
                };

                using var connection = _core.Database.GetConnection("mysql_detailed");
                var id = connection.Insert(warn);
                warn.Id = Convert.ToInt64(id);
                _warnCache[steamId] = new CacheEntry(warn, DateTime.UtcNow);
                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("Error adding warn: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<bool> UnwarnAsync(ulong steamId, string unwarnReason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                using var connection = _core.Database.GetConnection("mysql_detailed");

                var warns = connection.Select<Warn>(w => w.SteamId == steamId && w.Status == WarnStatus.Active);
                var warn = warns
                    .Where(w => w.ExpiresAt == null || w.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(w => w.CreatedAt)
                    .FirstOrDefault();

                if (warn == null)
                {
                    return false;
                }

                warn.Status = WarnStatus.Removed;
                warn.UnwarnAdminName = admin.Name;
                warn.UnwarnAdminSteamId = admin.SteamId;
                warn.UnwarnReason = unwarnReason;
                warn.UnwarnDate = DateTime.UtcNow;

                connection.Update(warn);
                _warnCache.TryRemove(steamId, out _);
                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("Error removing warn: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<Warn?> GetActiveWarnAsync(ulong steamId)
    {
        try
        {
            if (_warnCache.TryGetValue(steamId, out var cachedWarnEntry))
            {
                if (DateTime.UtcNow - cachedWarnEntry.CachedAt >= _cacheLifetime)
                {
                    _warnCache.TryRemove(steamId, out _);
                }
                else
                {
                    var cachedWarn = cachedWarnEntry.Warn;
                    if (cachedWarn.IsExpired || cachedWarn.Status != WarnStatus.Active)
                    {
                        _warnCache.TryRemove(steamId, out _);
                        return null;
                    }

                    return cachedWarn;
                }
            }

            using var connection = _core.Database.GetConnection("mysql_detailed");
            var warns = connection.Select<Warn>(w => w.SteamId == steamId && w.Status == WarnStatus.Active);
            var warn = warns
                .Where(w => w.ExpiresAt == null || w.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(w => w.CreatedAt)
                .FirstOrDefault();

            if (warn != null)
            {
                _warnCache[steamId] = new CacheEntry(warn, DateTime.UtcNow);
            }
            else
            {
                _warnCache.TryRemove(steamId, out _);
            }

            return warn;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error checking warn: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<int> GetTotalWarnsAsync(ulong steamId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                var warns = connection.Select<Warn>(w => w.SteamId == steamId);
                return warns.Count();
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("Error getting total warns: {Message}", ex.Message);
                return 0;
            }
        });
    }

    public async Task<int> GetActiveWarnCountAsync(ulong steamId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                var warns = connection.Select<Warn>(w => w.SteamId == steamId && w.Status == WarnStatus.Active);
                return warns.Count(w => w.ExpiresAt == null || w.ExpiresAt > DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("Error getting active warn count: {Message}", ex.Message);
                return 0;
            }
        });
    }

    public async Task<List<Warn>> GetWarnHistoryAsync(ulong steamId, WarnHistoryFilter filter, int limit = 30)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                var now = DateTime.UtcNow;
                var warns = connection.Select<Warn>(w => w.SteamId == steamId).ToList();

                var filtered = filter switch
                {
                    WarnHistoryFilter.Active => warns.Where(w => w.Status == WarnStatus.Active && (w.ExpiresAt == null || w.ExpiresAt > now)),
                    WarnHistoryFilter.Expired => warns.Where(w => w.Status == WarnStatus.Expired || (w.Status == WarnStatus.Active && w.ExpiresAt != null && w.ExpiresAt <= now)),
                    WarnHistoryFilter.Removed => warns.Where(w => w.Status == WarnStatus.Removed),
                    _ => warns
                };

                return filtered
                    .OrderByDescending(w => w.CreatedAt)
                    .Take(Math.Max(1, limit))
                    .ToList();
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("Error getting warn history: {Message}", ex.Message);
                return new List<Warn>();
            }
        });
    }

    public void InvalidateCache(ulong steamId)
    {
        if (_warnCache.TryRemove(steamId, out _))
        {
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Trace][Warn] cache-invalidate steamid={SteamId}", steamId);
        }
    }

    public async Task UpdateExpiredWarnsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("mysql_detailed");
                var activeWarns = connection.Select<Warn>(w => w.Status == WarnStatus.Active);
                var expiredWarns = activeWarns.Where(w => w.ExpiresAt != null && w.ExpiresAt <= DateTime.UtcNow);

                int cleaned = 0;
                foreach (var warn in expiredWarns)
                {
                    warn.Status = WarnStatus.Expired;
                    connection.Update(warn);
                    cleaned++;
                }

                if (cleaned > 0)
                {
                    _core.Logger.LogInformationIfEnabled("Marked {Count} warns as expired", cleaned);
                    _warnCache.Clear();
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("Error cleaning expired warns: {Message}", ex.Message);
            }
        });
    }

    private readonly record struct CacheEntry(Warn Warn, DateTime CachedAt);
}

public enum WarnHistoryFilter
{
    All,
    Active,
    Expired,
    Removed
}




