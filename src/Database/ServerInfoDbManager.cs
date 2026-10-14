using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class ServerInfoDbManager
{
    private readonly ISwiftlyCore _core;

    public ServerInfoDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            await UpsertCurrentServerAsync();
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Server info database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task UpsertCurrentServerAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var serverId = ServerIdentity.GetServerId(_core);
            var existing = connection.FirstOrDefault<ServerInfo>(x => x.ServerId == serverId);
            if (existing != null)
            {
                existing.LastSeenAt = DateTime.UtcNow;
                existing.ServerIp = ServerIdentity.GetIp(_core);
                existing.ServerPort = ServerIdentity.GetPort(_core);
                connection.Update(existing);
            }
            else
            {
                connection.Insert(new ServerInfo
                {
                    ServerId = serverId,
                    ServerIp = ServerIdentity.GetIp(_core),
                    ServerPort = ServerIdentity.GetPort(_core),
                    LastSeenAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error upserting server info: {Message}", ex.Message);
        }
    }
}


