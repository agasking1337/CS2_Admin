using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class DiscordLinkDbManager
{
    private readonly ISwiftlyCore _core;

    public DiscordLinkDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord link initialization warning: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task<DiscordLink?> GetBySteamIdAsync(ulong steamId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var row = connection.FirstOrDefault<DiscordLink>(x => x.SteamId == steamId);
            return Task.FromResult(row);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error reading discord link by steam: {Message}", ex.Message);
            return Task.FromResult<DiscordLink?>(null);
        }
    }

    public Task<DiscordLink?> GetByDiscordIdAsync(ulong discordId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var row = connection.FirstOrDefault<DiscordLink>(x => x.DiscordId == discordId);
            return Task.FromResult(row);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error reading discord link by discord id: {Message}", ex.Message);
            return Task.FromResult<DiscordLink?>(null);
        }
    }

    public Task<bool> LinkAsync(ulong steamId, ulong discordId, string discordName, string serverId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = connection.FirstOrDefault<DiscordLink>(x => x.SteamId == steamId);
            if (existing != null)
            {
                existing.DiscordId = discordId;
                existing.DiscordName = discordName;
                existing.ServerId = serverId;
                existing.LinkedAt = DateTime.UtcNow;
                connection.Update(existing);
                return Task.FromResult(true);
            }

            connection.Insert(new DiscordLink
            {
                SteamId = steamId,
                DiscordId = discordId,
                DiscordName = discordName,
                ServerId = serverId,
                LinkedAt = DateTime.UtcNow
            });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error writing discord link: {Message}", ex.Message);
            return Task.FromResult(false);
        }
    }

    public Task<bool> UnlinkBySteamIdAsync(ulong steamId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = connection.FirstOrDefault<DiscordLink>(x => x.SteamId == steamId);
            return Task.FromResult(existing != null && connection.Delete(existing));
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error deleting discord link: {Message}", ex.Message);
            return Task.FromResult(false);
        }
    }

    public Task<bool> UnlinkByDiscordIdAsync(ulong discordId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = connection.FirstOrDefault<DiscordLink>(x => x.DiscordId == discordId);
            return Task.FromResult(existing != null && connection.Delete(existing));
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error deleting discord link: {Message}", ex.Message);
            return Task.FromResult(false);
        }
    }
}
