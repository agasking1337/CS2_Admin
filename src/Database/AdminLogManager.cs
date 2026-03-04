using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class AdminLogManager
{
    private readonly ISwiftlyCore _core;
    private DiscordWebhook? _discordWebhook;

    public AdminLogManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SetDiscordWebhook(DiscordWebhook discordWebhook)
    {
        _discordWebhook = discordWebhook;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Admin log database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Admin log database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task AddLogAsync(string action, string adminName, ulong adminSteamId, ulong? targetSteamId, string? targetIp, string details, string? targetName = null)
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            connection.Insert(new AdminLog
            {
                Action = action,
                AdminName = adminName,
                AdminSteamId = adminSteamId,
                TargetSteamId = targetSteamId,
                TargetIp = targetIp,
                Details = details,
                ServerId = ServerIdentity.GetServerId(_core),
                ServerIp = ServerIdentity.GetIp(_core),
                ServerPort = ServerIdentity.GetPort(_core),
                CreatedAt = DateTime.UtcNow
            });

            if (ShouldSendAdminActionWebhook(action, adminSteamId))
            {
                await _discordWebhook!.SendAdminActionNotificationAsync(
                    action,
                    adminName,
                    adminSteamId,
                    targetSteamId,
                    details,
                    ServerIdentity.GetServerId(_core),
                    targetName);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error writing admin log: {Message}", ex.Message);
        }
    }

    private bool ShouldSendAdminActionWebhook(string action, ulong adminSteamId)
    {
        if (_discordWebhook == null)
            return false;

        return !action.Equals("calladmin", StringComparison.OrdinalIgnoreCase)
               && !action.Equals("report", StringComparison.OrdinalIgnoreCase);
    }
}
