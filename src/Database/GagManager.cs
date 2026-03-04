using Dommel;
using CS2_Admin.Models;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class GagManager
{
    private readonly ISwiftlyCore _core;
    private readonly Dictionary<ulong, Gag> _gagCache = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private readonly AsyncLocal<AdminContext> _currentAdmin = new();

    public GagManager(ISwiftlyCore core)
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

            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Gag database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Gag database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<bool> AddGagAsync(ulong steamId, int durationMinutes, string reason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                DateTime? expiresAt = durationMinutes > 0 ? DateTime.UtcNow.AddMinutes(durationMinutes) : null;

                var gag = new Gag
                {
                    SteamId = steamId,
                    AdminName = admin.Name,
                    AdminSteamId = admin.SteamId,
                    Reason = reason,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    Status = GagStatus.Active
                };

                using var connection = _core.Database.GetConnection("admins");
                var id = connection.Insert(gag);
                gag.Id = Convert.ToInt32(id);
                _gagCache[steamId] = gag;

                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error adding gag: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<bool> UngagAsync(ulong steamId, string ungagReason)
    {
        return await Task.Run(() =>
        {
            try
            {
                var admin = _currentAdmin.Value ?? new AdminContext();
                using var connection = _core.Database.GetConnection("admins");
                
                var gag = connection.FirstOrDefault<Gag>(g => g.SteamId == steamId && g.Status == GagStatus.Active);
                if (gag == null) return false;

                gag.Status = GagStatus.Ungagged;
                gag.UngagAdminName = admin.Name;
                gag.UngagAdminSteamId = admin.SteamId;
                gag.UngagReason = ungagReason;
                gag.UngagDate = DateTime.UtcNow;

                connection.Update(gag);
                _gagCache.Remove(steamId);

                return true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error ungagging player: {Message}", ex.Message);
                return false;
            }
        });
    }

    public async Task<Gag?> GetActiveGagAsync(ulong steamId)
    {
        try
        {
            if (_gagCache.TryGetValue(steamId, out Gag? cachedGag) &&
                DateTime.UtcNow - _lastCacheUpdate < _cacheLifetime)
            {
                if (cachedGag.IsExpired || cachedGag.Status != GagStatus.Active)
                {
                    _gagCache.Remove(steamId);
                    return null;
                }
                return cachedGag;
            }

            using var connection = _core.Database.GetConnection("admins");
            var gag = connection.FirstOrDefault<Gag>(g => 
                g.SteamId == steamId && 
                g.Status == GagStatus.Active &&
                (g.ExpiresAt == null || g.ExpiresAt > DateTime.UtcNow));

            if (gag != null)
            {
                _gagCache[steamId] = gag;
                _lastCacheUpdate = DateTime.UtcNow;
            }
            else
            {
                _gagCache.Remove(steamId);
            }

            return gag;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error checking gag: {Message}", ex.Message);
            return null;
        }
    }

    public Gag? GetActiveGagFromCache(ulong steamId)
    {
        if (_gagCache.TryGetValue(steamId, out Gag? cachedGag) && cachedGag.IsActive)
        {
            return cachedGag;
        }
        return null;
    }

    public async Task<int> GetTotalGagsAsync(ulong steamId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("admins");
                var gags = connection.Select<Gag>(g => g.SteamId == steamId);
                return gags.Count();
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting total gags: {Message}", ex.Message);
                return 0;
            }
        });
    }

    public async Task CleanupExpiredGagsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var connection = _core.Database.GetConnection("admins");
                var expiredGags = connection.Select<Gag>(g => 
                    g.Status == GagStatus.Active && 
                    g.ExpiresAt != null && 
                    g.ExpiresAt <= DateTime.UtcNow);

                int cleaned = 0;
                foreach (var gag in expiredGags)
                {
                    gag.Status = GagStatus.Expired;
                    connection.Update(gag);
                    cleaned++;
                }

                if (cleaned > 0)
                {
                    _core.Logger.LogInformationIfEnabled("[CS2_Admin] Marked {Count} gags as expired", cleaned);
                    _gagCache.Clear();
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error cleaning expired gags: {Message}", ex.Message);
            }
        });
    }

    public void ClearCache()
    {
        _gagCache.Clear();
    }
}


