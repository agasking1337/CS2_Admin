using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Menu.Handlers;
using CS2_Admin.Utils;
using CS2_Admin.Models;
using System.Linq;

namespace CS2_Admin.Menu;

public class AdminMenuManager
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly WarnManager _warnManager;
    private readonly AdminPlaytimeDbManager _adminPlaytimeDbManager;
    private readonly Dictionary<string, IAdminMenuHandler> _handlers;

    public AdminMenuManager(
        ISwiftlyCore core,
        PluginConfig config,
        WarnManager warnManager,
        AdminDbManager adminDbManager,
        GroupDbManager groupDbManager,
        AdminLogManager adminLogManager,
        AdminPlaytimeDbManager adminPlaytimeDbManager)
    {
        _core = core;
        _config = config;
        _warnManager = warnManager;
        _adminPlaytimeDbManager = adminPlaytimeDbManager;
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
        // Create a menu builder
        var builder = _core.MenusAPI.CreateBuilder();
        
        // Set Title using the Design API
        string title;
        try
        {
            title = PluginLocalizer.Get(_core)["menu_admin_title"];
        }
        catch
        {
            title = "Admin Menu";
        }

        // Match Swiftly Admins navigation styling using configurable color
        builder.Design
            .SetMenuTitle(title)
            .Design.SetMenuFooterColor("#00FEED")
            .Design.SetVisualGuideLineColor("#00FEED")
            .Design.SetNavigationMarkerColor("#00FEED");

        // Add Options for each handler
        AddHandlerOption(player, builder, "server_management", "menu_server_management");
        AddHandlerOption(player, builder, "player_management", "menu_player_management");
        AddHandlerOption(player, builder, "fun_commands", "menu_fun_commands");
        if (HasPermission(player, _config.Permissions.AdminTime))
        {
            var playtimeButton = new ButtonMenuOption(PluginLocalizer.Get(_core)["menu_admin_playtime"]) { CloseAfterClick = false };
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

        // Build and Open
        var menu = builder.Build();
        _core.MenusAPI.OpenMenuForPlayer(player, menu);
    }

    private void AddHandlerOption(IPlayer player, IMenuBuilderAPI builder, string key, string translationKey)
    {
        if (!_handlers.TryGetValue(key, out var handler))
            return;

        string text;
        try
        {
            text = PluginLocalizer.Get(_core)[translationKey];
        }
        catch
        {
            // Fallback to a readable version of the key
            text = translationKey.Replace("menu_", "").Replace("_", " ");
            // Capitalize first letter of each word
            text = string.Join(" ", text.Split(' ').Select(word => char.ToUpper(word[0]) + word.Substring(1)));
        }

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
        builder.Design.SetMenuTitle(PluginLocalizer.Get(_core)["menu_admin_playtime"]);

        if (entries.Count == 0)
        {
            var emptyButton = new ButtonMenuOption(PluginLocalizer.Get(_core)["admintime_no_data"]) { CloseAfterClick = true };
            emptyButton.Click += (_, _) => ValueTask.CompletedTask;
            builder.AddOption(emptyButton);
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var row = entries[i];
                var text = PluginLocalizer.Get(_core)["admintime_menu_entry", i + 1, row.PlayerName, row.PlaytimeMinutes];
                var button = new ButtonMenuOption(text) { CloseAfterClick = false };
                button.Click += (_, _) => ValueTask.CompletedTask;
                builder.AddOption(button);
            }
        }

        _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }
}


