using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using System.Globalization;
using System.Text.Json;

using CS2_Admin.Services;
namespace CS2_Admin.Commands;

public class ListPlayersCommand : CommandBase
{
    public ListPlayersCommand(
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

    public override async void Execute(ICommandContext context)
    {
        try
        {
            if (!HasPerm(context, Permissions.ListPlayers))
            {
                Reply(context, "no_permission");
                return;
            }

            var args = NormalizeArgs(context.Args, CommandsConfig.ListPlayers);
            var isJson = args.Length >= 1 && string.Equals(args[0], "-json", StringComparison.OrdinalIgnoreCase);
            var includeBots = args.Any(arg =>
                string.Equals(arg, "-all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-bots", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--bots", StringComparison.OrdinalIgnoreCase));

            var players = Core.PlayerManager
                .GetAllPlayers()
                .Where(p => p.IsValid && (includeBots || !p.IsFakeClient))
                .OrderBy(p => p.Controller.PlayerName)
                .ToList();

            if (!isJson)
            {
                var lines = new List<string>(players.Count);
                foreach (var player in players)
                    lines.Add(FormatPlayerConsoleLine(player));

                var output = string.Join('\n', lines);

                if (context.IsSentByPlayer && context.Sender != null)
                {
                    if (!string.IsNullOrEmpty(output))
                        context.Sender.SendConsole(output);

                    if (context.Sender.IsValid && !context.Sender.IsFakeClient && !string.IsNullOrEmpty(output))
                        context.Sender.SendChat($" \x02{L("prefix")}\x01 {L("players_list_console")}");
                }
                else
                {
                    if (lines.Count == 0)
                        context.Reply("NO_PLAYERS");
                    else
                    {
                        foreach (var line in lines)
                            context.Reply(line);
                    }
                }
            }
            else
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

                var entries = players.Select(p =>
                {
                    var teamNum = p.Controller.TeamNum;
                    var teamName = PlayerUtils.GetTeamName(teamNum, PluginLocalizer.Get(Core));
                    var score = p.Controller.Score;
                    var ping = (int)p.Controller.Ping;
                    var isAlive = p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0;
                    var ip = NormalizePlayerIp(p.IPAddress);
                    var tag = Tags.Enabled
                        ? PlayerUtils.GetScoreTag(p, Tags.PlayerTag)
                        : "-";

                    return new PlayerListEntry(
                        p.PlayerID,
                        SanitizePlayerNameForConsole(p.Controller.PlayerName ?? L("player_fallback_name", p.PlayerID)),
                        p.IsFakeClient ? "BOT" : p.SteamID.ToString(CultureInfo.InvariantCulture),
                        teamNum,
                        teamName,
                        score,
                        ping,
                        isAlive,
                        p.IsFakeClient ? "-" : ip,
                        tag
                    );
                }).ToList();

                var json = JsonSerializer.Serialize(entries, jsonOptions);

                if (context.IsSentByPlayer && context.Sender != null)
                {
                    context.Sender.SendConsole(json);
                    if (context.Sender.IsValid && !context.Sender.IsFakeClient)
                        context.Sender.SendChat($" \x02{L("prefix")}\x01 {L("players_list_json_console")}");
                }
                else
                {
                    context.Reply(json);
                }
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "ListPlayers command failed");
        }
    }

    private string FormatPlayerConsoleLine(IPlayer player)
    {
        var userId = player.PlayerID;
        var steamId = player.IsFakeClient ? "BOT" : player.SteamID.ToString(CultureInfo.InvariantCulture);
        var playerName = SanitizePlayerNameForConsole(player.Controller.PlayerName ?? L("player_fallback_name", userId));
        var ipAddress = player.IsFakeClient ? "-" : NormalizePlayerIp(player.IPAddress);
        return $"#{userId} | {steamId} | {playerName} | {ipAddress}";
    }

    private static string NormalizePlayerIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return "-";

        var normalized = ipAddress.Trim();
        var colonIndex = normalized.IndexOf(':');
        if (colonIndex > 0)
            normalized = normalized[..colonIndex];

        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }

    private string SanitizePlayerNameForConsole(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return L("unknown");

        var sanitized = playerName
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('|', '/')
            .Trim();

        while (sanitized.Contains("  ", StringComparison.Ordinal))
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(sanitized) ? L("unknown") : sanitized;
    }

    private record PlayerListEntry(
        int Id,
        string Name,
        string SteamId,
        int Team,
        string TeamName,
        int Score,
        int Ping,
        bool IsAlive,
        string Ip,
        string Tag
    );
}

