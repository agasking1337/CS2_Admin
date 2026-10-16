using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class SayCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly DiscordBotService _discord;

    public SayCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        DiscordBotService discord)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
        _discord = discord;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Say);

            if (!HasPerm(context, Permissions.Say))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "say_usage");
                return;
            }

            var messageText = string.Join(" ", args);
            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
            foreach (var p in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
            {
                var visibleAdmin = ResolveVisibleAdminName(p, adminName);
                p.SendChat($" \x04[Admin]\x01 \x10{visibleAdmin}\x01: {messageText}");
            }

            _ = AdminLogManager.AddLogAsync("say", adminName, context.Sender?.SteamID ?? 0, null, null, $"message={messageText}");
            Core.Logger.LogInformationIfEnabled("SAY from {Admin}: {Message}", adminName, messageText);
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "Say command failed");
        }
    }
}
