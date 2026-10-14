using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class WhoCommand : CommandBase
{
    public WhoCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
    }

    public override void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.Who))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.Who);
            var target = string.Join(' ', args).Trim();
            var matchedPlayers = string.IsNullOrWhiteSpace(target)
                ? Core.PlayerManager.GetAllPlayers().Where(player => player.IsValid && !player.IsFakeClient).ToList()
                : PlayerUtils.FindPlayersByTarget(Core, target, caller: context.Sender)
                    .Where(player => !player.IsFakeClient)
                    .ToList();

            if (matchedPlayers.Count == 0)
            {
                Reply(context, "player_not_found");
                return;
            }

            var lines = matchedPlayers
                .OrderBy(player => player.PlayerID)
                .Select(player => $"#{player.PlayerID} | {player.SteamID} | {SanitizePlayerName(player.Controller.PlayerName)} | {NormalizePlayerIp(player.IPAddress)}")
                .ToList();

            if (context.IsSentByPlayer && context.Sender != null)
            {
                context.Sender.SendConsole(string.Join('\n', lines));
                context.Sender.SendChat($" \x02{L("prefix")}\x01 {L("players_list_console")}");
                return;
            }

            foreach (var line in lines)
                context.Reply(line);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Who command failed");
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
