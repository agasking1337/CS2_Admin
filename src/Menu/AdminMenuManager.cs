using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
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
        TagDbManager tagDbManager,
        ChatTagConfigManager chatTagConfigManager)
    {
        _core = core;
        _config = config;
        _warnManager = warnManager;
        _adminPlaytimeDbManager = adminPlaytimeDbManager;
        _tagDbManager = tagDbManager;
        _handlers = new Dictionary<string, IAdminMenuHandler>();

        RegisterHandler("server_management", new ServerManagementHandler(_core, _config));
        RegisterHandler("player_management", new PlayerManagementHandler(_core, _config, _warnManager));
        RegisterHandler("admin_management", new AdminManagementHandler(_core, _config, adminDbManager, groupDbManager, adminLogManager, chatTagConfigManager));
        RegisterHandler("fun_commands", new FunCommandsMenuHandler(_core, _config));
    }

    private void RegisterHandler(string key, IAdminMenuHandler handler)
    {
        _handlers[key] = handler;
    }

    public void OpenAdminMenu(IPlayer player)
    {
        var menu = BuildAdminMenu(player);
        menu.Open(player);
    }

    public IMenu BuildAdminMenu(IPlayer player)
    {
        var builder = _core.Menu.CreateMenu("cs2_admin.main")
            .WithTitle(T("menu_admin_title"));

        AddHandlerOption(player, builder, "server_management", "menu_server_management");
        AddHandlerOption(player, builder, "player_management", "menu_player_management");
        AddHandlerOption(player, builder, "fun_commands", "menu_fun_commands");

        if (HasPermission(player, _config.Permissions.AdminTime))
        {
            builder.Add(new ButtonComponent(T("menu_admin_playtime"), ctx =>
            {
                OpenAdminPlaytimeMenu(ctx.Player);
                return ValueTask.CompletedTask;
            }));
        }

        if (_core.Permission.PlayerHasPermission(player.SteamID, _config.Permissions.AdminRoot))
        {
            AddHandlerOption(player, builder, "admin_management", "menu_admin_management");
        }

        return builder.Build();
    }

    private void AddHandlerOption(IPlayer player, IMenuBuilder builder, string key, string translationKey)
    {
        if (!_handlers.TryGetValue(key, out var handler))
            return;

        var text = T(translationKey);
        builder.Add(new ButtonComponent(text, ctx =>
        {
            var parentMenu = BuildAdminMenu(ctx.Player);
            var subMenu = handler.CreateMenu(ctx.Player, parentMenu);
            subMenu.Open(ctx.Player);
            return ValueTask.CompletedTask;
        }));
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

        var parent = BuildAdminMenu(player);
        var builder = _core.Menu.CreateMenu("cs2_admin.playtime")
            .WithParent(parent)
            .WithTitle(T("menu_admin_playtime"));

        if (entries.Count == 0)
        {
            builder.Add(new ButtonComponent(T("admintime_no_data"), _ => ValueTask.CompletedTask));
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var row = entries[i];
                var text = T("admintime_menu_entry", i + 1, row.PlayerName, row.PlaytimeMinutes);
                builder.Add(new ButtonComponent(text, _ => ValueTask.CompletedTask));
            }
        }

        builder.Build().Open(player);
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
