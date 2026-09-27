using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Menu.Handlers;
using CS2_Admin.Utils;
using CS2_Admin.Services;
using CS2_Admin.Models;

namespace CS2_Admin.Menu;

public class AdminMenuManager
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly WarnManager _warnManager;
    private readonly AdminPlaytimeDbManager _adminPlaytimeDbManager;
    private readonly TagDbManager _tagDbManager;
    private readonly Dictionary<string, IAdminMenuHandler> _handlers;

    public AdminMenuManager(
        ISwiftlyCore core,
        PluginConfig config,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        GroupDbManager groupDbManager,
        AdminLogManager adminLogManager,
        AdminPlaytimeDbManager adminPlaytimeDbManager,
        TagDbManager tagDbManager)
    {
        _core = core;
        _config = config;
        _warnManager = warnManager;
        _adminPlaytimeDbManager = adminPlaytimeDbManager;
        _tagDbManager = tagDbManager;
        _handlers = new Dictionary<string, IAdminMenuHandler>();

        // Register handlers
        RegisterHandler("server_management", new ServerManagementHandler(_core, _config));
        RegisterHandler("player_management", new PlayerManagementHandler(_core, _config, _warnManager));
        RegisterHandler("admin_management", new AdminManagementHandler(_core, _config, adminDbManager, groupDbManager, adminLogManager));
        RegisterHandler("fun_commands", new FunCommandsMenuHandler(_core, _config));
    }

    private void RegisterHandler(string key, IAdminMenuHandler handler)
    {
        _handlers[key] = handler;
    }

    public void OpenAdminMenu(IPlayer player)
    {
        _core.MenusAPI.OpenMenuForPlayer(player, BuildAdminMenu(player));
    }

    private IMenuAPI BuildAdminMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();

        var title = T("menu_admin_title");
        builder.Design
            .SetMenuTitle(title)
            .Design.SetMenuFooterColor("#00FEED")
            .Design.SetVisualGuideLineColor("#00FEED")
            .Design.SetNavigationMarkerColor("#00FEED");

        AddHandlerOption(player, builder, "server_management", "menu_server_management");
        AddHandlerOption(player, builder, "player_management", "menu_player_management");
        AddHandlerOption(player, builder, "fun_commands", "menu_fun_commands");
        if (HasPermission(player, _config.Permissions.AdminTime))
        {
            var playtimeButton = new ButtonMenuOption(T("menu_admin_playtime")) { CloseAfterClick = false };
            playtimeButton.Click += (_, args) =>
            {
                OpenAdminPlaytimeMenu(args.Player);
                return ValueTask.CompletedTask;
            };
            builder.AddOption(playtimeButton);
        }
        if (_core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot))
        {
            AddHandlerOption(player, builder, "admin_management", "menu_admin_management");
        }

        return builder.Build();
    }

    private void AddHandlerOption(IPlayer player, IMenuBuilderAPI builder, string key, string translationKey)
    {
        if (!_handlers.TryGetValue(key, out var handler))
            return;

        var text = T(translationKey);
        builder.AddOption(new SubmenuMenuOption(text, () => handler.CreateMenu(player)));
    }

    private bool HasPermission(IPlayer player, string permission)
    {
        return _core.Permission.PlayerHasPermission(player.SteamID, permission)
               || _core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot);
    }

    private void OpenAdminPlaytimeMenu(IPlayer player)
    {
        _ = Task.Run(async () =>
        {
            var rows = await _adminPlaytimeDbManager.GetTopAdminsAsync(_config.AdminPlaytime.MenuTopLimit);
            _core.Scheduler.NextTick(() => OpenAdminPlaytimeMenuInternal(player, rows));
        });
    }

    private void OpenAdminPlaytimeMenuInternal(IPlayer player, IReadOnlyList<AdminPlaytime> entries)
    {
        if (!player.IsValid)
        {
            return;
        }

        var builder = _core.MenusAPI.CreateBuilder();
        builder.BindToParent(BuildAdminMenu(player));
        builder.Design.SetMenuTitle(T("menu_admin_playtime"));

        if (entries.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(T("admintime_no_data")) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var row = entries[i];
                var text = T("admintime_menu_entry", i + 1, row.PlayerName, row.PlaytimeMinutes);
                var button = new ButtonMenuOption(text) { CloseAfterClick = false };
                button.Click += (_, _) => ValueTask.CompletedTask;
                builder.AddOption(button);
            }
        }

        _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }

    private string T(string key, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            return args.Length == 0 ? localizer[key] : LocalizerHelper.Get(_core, key, args);
        }
        catch
        {
            return key;
        }
    }
}


