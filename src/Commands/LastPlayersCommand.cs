using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class LastPlayersCommand : CommandBase
{
    private readonly PlayerSessionManager _playerSessionManager;

    public LastPlayersCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        PlayerSessionManager playerSessionManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _playerSessionManager = playerSessionManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.LastPlayers))
            {
                Reply(context, "no_permission");
                return;
            }

            var recent = await _playerSessionManager.GetRecentDisconnectedPlayersAsync(5);
            if (recent.Count == 0)
            {
                Reply(context, "lastban_no_recent_players");
                return;
            }

            var lines = recent.Take(5)
                .Select(player => $"{SanitizePlayerName(player.Name)} | {player.SteamId} | {NormalizePlayerIp(player.IpAddress)} | {player.LastSeenAt:yyyy-MM-dd HH:mm:ss}")
                .ToList();

            if (context.IsSentByPlayer && context.Sender != null)
            {
                await OnMainThreadAsync(() =>
                {
                    if (!context.Sender.IsValid)
                        return;

                    context.Sender.SendConsole(string.Join('\n', lines));
                    context.Sender.SendChat($" \x02{L("prefix")}\x01 {L("last_players_console")}");
                });
                return;
            }

            foreach (var line in lines)
                context.Reply(line);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Last players command failed");
        }
    }

    private string SanitizePlayerName(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return L("unknown");

        return playerName.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/').Trim();
    }

    private static string NormalizePlayerIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return "-";

        var normalized = ipAddress.Trim();
        var colonIndex = normalized.IndexOf(':');
        return colonIndex > 0 ? normalized[..colonIndex] : normalized;
    }
}
