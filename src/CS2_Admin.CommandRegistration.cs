using CS2_Admin.Config;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using System.Reflection;

namespace CS2_Admin;

// CS2_Admin'in komut kaydı bölümü. Ana dosyayı (CS2_Admin.cs) küçük tutmak için
// alias temizliği + kayıt/kaldırma mantığı burada yaşar.
public partial class CS2_Admin
{
    private static readonly HashSet<string> BlockedAliases = new(StringComparer.OrdinalIgnoreCase) { "groups" };
    private static readonly HashSet<string> RawConCollisions = new(StringComparer.OrdinalIgnoreCase) { "say", "kick", "noclip", "give", "map", "restart", "rcon" };

    // Bu instance'a ait kayıtlı komut isimleri ve GUID'leri (temiz kaldırma için).
    private readonly HashSet<string> _registeredCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Guid> _commandGuids = new();

    private void SanitizeCommandAliases()
    {
        foreach (var prop in typeof(CommandsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(List<string>))
                continue;

            var aliases = prop.GetValue(_config.Commands) as List<string>;
            if (aliases == null || aliases.Count == 0)
                continue;

            var blocked = aliases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => BlockedAliases.Contains(x))
                .ToList();

            if (blocked.Count > 0)
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Removed blocked command alias(es) [{Blocked}] from {Property}.", string.Join(", ", blocked), prop.Name);

            var cleaned = aliases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !BlockedAliases.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            prop.SetValue(_config.Commands, cleaned);
        }
    }

    private void RegisterCommands()
    {
        UnregisterAllCommands();

        RegisterCmdList(["admin"], _adminMenuCmd.Execute);
        RegisterCmdList(["asay"], _asayCmd.Execute);
        RegisterCmdList(["say"], _sayCmd.Execute);
        RegisterCmdList(["psay"], _psayCmd.Execute);
        RegisterCmdList(["csay"], _csayCmd.Execute);
        RegisterCmdList(["hsay"], _hsayCmd.Execute);
        RegisterCmdList(["calladmin"], _callAdminCmd.Execute);
        RegisterCmdList(["report"], _reportCmd.Execute);
        RegisterCmdList(_config.Commands.Verify, _verifyCmd.Execute);
        RegisterCmdList(["unverify", "unlink"], _unverifyCmd.Execute);
        RegisterCmdList(["admintime"], _adminTimeCmd.Execute);
        RegisterCmdList(["admintimesend"], _adminTimeSendCmd.Execute);
        RegisterCmdList(["ban"], _banCmd.Execute);
        RegisterCmdList(["ipban"], _ipBanCmd.Execute);
        RegisterCmdList(_config.Commands.LastBan, _lastBanCmd.Execute);
        RegisterCmdList(_config.Commands.LastPlayers, _lastPlayersCmd.Execute);
        RegisterCmdList(["addban"], _addBanCmd.Execute);
        RegisterCmdList(["unban"], _unbanCmd.Execute);
        RegisterCmdList(["warn"], _warnCmd.Execute);
        RegisterCmdList(["unwarn"], _unwarnCmd.Execute);
        RegisterCmdList(["mute"], _muteCmd.Execute);
        RegisterCmdList(["unmute"], _unmuteCmd.Execute);
        RegisterCmdList(["gag"], _gagCmd.Execute);
        RegisterCmdList(["ungag"], _ungagCmd.Execute);
        RegisterCmdList(["silence"], _silenceCmd.Execute);
        RegisterCmdList(["unsilence"], _unsilenceCmd.Execute);
        RegisterCmdList(["kick"], _kickCmd.Execute);
        RegisterCmdList(["slap"], _slapCmd.Execute);
        RegisterCmdList(["slay"], _slayCmd.Execute);
        RegisterCmdList(["god"], _godCmd.Execute);
        RegisterCmdList(["respawn", "revive"], _respawnCmd.Execute);
        RegisterCmdList(["team", "swap"], _teamCmd.Execute);
        RegisterCmdList(["mixteam"], _mixTeamCmd.Execute);
        RegisterCmdList(["noclip"], _noClipCmd.Execute);
        RegisterCmdList(["goto"], _gotoCmd.Execute);
        RegisterCmdList(["bring"], _bringCmd.Execute);
        RegisterCmdList(["freeze"], _freezeCmd.Execute);
        RegisterCmdList(["unfreeze"], _unfreezeCmd.Execute);
        RegisterCmdList(["resize"], _resizeCmd.Execute);

        RegisterCmdList(["blind"], _blindCmd.Execute);
        RegisterCmdList(["glow", "glove"], _glowCmd.Execute);
        RegisterCmdList(["rgb", "rainbow"], _rgbCmd.Execute);
        RegisterCmdList(["beacon"], _beaconCmd.Execute);
        RegisterCmdList(["bury"], _buryCmd.Execute);
        RegisterCmdList(["unbury"], _unburyCmd.Execute);
        RegisterCmdList(["burn"], _burnCmd.Execute);
        RegisterCmdList(["disarm"], _disarmCmd.Execute);
        RegisterCmdList(["speed", "setspeed"], _speedCmd.Execute);
        RegisterCmdList(["gravity", "setgravity"], _gravityCmd.Execute);
        RegisterCmdList(["rename"], _renameCmd.Execute);
        RegisterCmdList(["unrename"], _unrenameCmd.Execute);
        RegisterCmdList(["hp"], _hpCmd.Execute);
        RegisterCmdList(["money", "setmoney", "givemoney"], _moneyCmd.Execute);
        RegisterCmdList(["give", "giveitem"], _giveCmd.Execute);
        RegisterCmdList(["vote"], _voteCmd.Execute);
        RegisterCmdList(["map"], _mapCmd.Execute);
        RegisterCmdList(["wsmap"], _wsMapCmd.Execute);
        RegisterCmdList(["rr", "restart"], _restartCmd.Execute);
        RegisterCmdList(["hson"], _hsToggleCmd.Execute);
        RegisterCmdList(["hsoff"], _hsToggleCmd.Execute);
        RegisterCmdList(["bhopon", "bunnyon"], _bunnyToggleCmd.Execute);
        RegisterCmdList(["bhopoff", "bunnyoff"], _bunnyToggleCmd.Execute);
        RegisterCmdList(["respawnon"], _respawnToggleCmd.Execute);
        RegisterCmdList(["respawnoff"], _respawnToggleCmd.Execute);
        RegisterCmdList(["rcon"], _rconCmd.Execute);
        RegisterCmdList(["cvar"], _cvarCmd.Execute);
        RegisterCmdList(_config.Commands.ListPlayers, _listPlayersCmd.Execute);
        RegisterCmdList(_config.Commands.Who, _whoCmd.Execute);

        RegisterCmdList(["addadmin"], _addAdminCmd.Execute);
        RegisterCmdList(["editadmin"], _editAdminCmd.Execute);
        RegisterCmdList(["removeadmin"], _removeAdminCmd.Execute);
        RegisterCmdList(["listadmins", "admins"], _listAdminsCmd.Execute);
        RegisterCmdList(["addgroup"], _addGroupCmd.Execute);
        RegisterCmdList(["editgroup"], _editGroupCmd.Execute);
        RegisterCmdList(["removegroup"], _removeGroupCmd.Execute);
        RegisterCmdList(["listgroups"], _listGroupsCmd.Execute);
        RegisterCmdList(["adminreload"], _adminReloadCmd.Execute);
        RegisterCmdList(["afk"], ctx => _afkManager.OnAfkCommand(ctx));
    }

    private void RegisterCmdList(IReadOnlyList<string> aliases, ICommandService.CommandListener handler)
    {
        if (aliases == null) return;
        foreach (var alias in aliases)
            RegisterCommand(alias, handler);
    }

    private void RegisterCommand(string name, ICommandService.CommandListener handler)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var dedupWrapper = (ICommandService.CommandListener)(ctx =>
        {
            // Reload sonrası eski (stale) instance'ın handler'ı ise hiçbir şey yapma.
            if (!_commandsActive) return;
            if (ctx.IsSentByPlayer && ctx.Sender != null && _commandBlocker.ShouldBlock(ctx.Sender.PlayerID, name)) return;
            // Konsoldan direkt komut yazılabilmesi için sw_ kontrolünü kaldırdık.
            if (!CommandExecutionCache.ShouldExecute(ctx, handler.Target?.GetType() ?? typeof(object)))
                return;
            handler(ctx);
        });

        var swAlias = "sw_" + name;
        if (!RawConCollisions.Contains(name))
            TryRegister(name, dedupWrapper);
        if (!string.Equals(swAlias, name, StringComparison.OrdinalIgnoreCase))
            TryRegister(swAlias, dedupWrapper);
    }

    private void TryRegister(string name, ICommandService.CommandListener handler)
    {
        if (BlockedAliases.Contains(name)) return;
        try
        {
            var guid = Core.Command.RegisterCommand(name, handler, registerRaw: true);
            _commandGuids.Add(guid);
            _registeredCommands.Add(name);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to register command '{Name}': {Msg}", name, ex.Message);
        }
    }

    private void UnregisterAllCommands()
    {
        // GUID bazlı kaldırma: SADECE bu instance'ın callback'lerini hem native taraftan
        // hem de SwiftlyS2'nin static command sözlüğünden düşürür (isim bazlı kaldırma
        // case-sensitive olduğu ve başka instance'ları kaçırabildiği için tercih edilmez).
        // NOT: Bu yalnızca komut dispatch'i DIŞINDA güvenlidir (Load / round-start).
        // Unload() içinde ÇAĞRILMAZ; orada bunun yerine _commandsActive=false kullanılır.
        foreach (var guid in _commandGuids)
        {
            try { Core.Command.UnregisterCommand(guid); }
            catch { }
        }
        _commandGuids.Clear();
        _registeredCommands.Clear();
    }

    private void EnsureCommandsRegistered()
    {
        try
        {
            if (!Core.Command.IsCommandRegistered("admin"))
                RegisterCommands();
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Failed to ensure commands");
        }
    }
}
