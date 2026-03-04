using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public class MuteCommands
{
    private readonly ISwiftlyCore _core;
    private readonly MuteManager _muteManager;
    private readonly GagManager _gagManager;
    private readonly AdminDbManager _adminDbManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly DiscordWebhook _discord;
    private readonly CommandsConfig _commands;
    private readonly string _mutePermission;
    private readonly string _gagPermission;
    private readonly string _silencePermission;
    private readonly string _adminRootPermission;
    private readonly MessagesConfig _messagesConfig;

    public MuteCommands(
        ISwiftlyCore core, 
        MuteManager muteManager, 
        GagManager gagManager,
        AdminDbManager adminDbManager,
        AdminLogManager adminLogManager,
        DiscordWebhook discord, 
        CommandsConfig commands,
        string mutePermission,
        string gagPermission,
        string silencePermission,
        string adminRootPermission,
        MessagesConfig messagesConfig)
    {
        _core = core;
        _muteManager = muteManager;
        _gagManager = gagManager;
        _adminDbManager = adminDbManager;
        _adminLogManager = adminLogManager;
        _discord = discord;
        _commands = commands;
        _mutePermission = mutePermission;
        _gagPermission = gagPermission;
        _silencePermission = silencePermission;
        _adminRootPermission = adminRootPermission;
        _messagesConfig = messagesConfig;
    }

    public void OnMuteCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Mute);

        if (!HasPermission(context, _mutePermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["mute_usage"]}");
            return;
        }

        if (RejectGroupTargets(context, args))
        {
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!int.TryParse(args[1], out int duration) || duration < -1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_duration"]}");
            return;
        }

        string reason = args.Length > 2 
            ? string.Join(" ", args.Skip(2)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetSnapshots = targets
            .Select(t => new PunishTargetSnapshot(
                t.SteamID,
                t.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                t.IPAddress))
            .ToList();

        _ = Task.Run(async () =>
        {
            _muteManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                {
                    continue;
                }

                var existingMute = await _muteManager.GetActiveMuteAsync(target.SteamId);
                if (existingMute != null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_already_muted", target.Name]}"));
                    continue;
                }

                await _muteManager.AddMuteAsync(target.SteamId, duration, reason);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] mute apply steamid={SteamId} duration={Duration} reason={Reason}", target.SteamId, duration, reason);
                var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanently"] : PluginLocalizer.Get(_core)["duration_for_minutes", duration];
                
                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["muted_notification", adminName, target.Name, durationText, reason]}");
                    }
                    
                    var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        var durationDisplay = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanent"] : PluginLocalizer.Get(_core)["duration_minutes", duration];
                        PlayerUtils.SendNotification(targetPlayer, _messagesConfig,
                            PluginLocalizer.Get(_core)["muted_personal_html", durationDisplay, reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["muted_personal_chat", durationText, reason]}");
                        targetPlayer.VoiceFlags = VoiceFlagValue.Muted;
                    }
                });

                await _discord.SendMuteNotificationAsync(adminName, target.Name, duration, reason);
                await _adminLogManager.AddLogAsync("mute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} muted {Target} for {Duration} minutes. Reason: {Reason}", 
                    adminName, target.Name, duration, reason);
            }
        });
    }

    public void OnUnmuteCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Unmute);

        if (!HasPermission(context, _mutePermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unmute_usage"]}");
            return;
        }

        if (RejectGroupTargets(context, args))
        {
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        string reason = args.Length > 1 
            ? string.Join(" ", args.Skip(1)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetSnapshots = targets
            .Select(t => new PunishTargetSnapshot(
                t.SteamID,
                t.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                t.IPAddress))
            .ToList();

        _ = Task.Run(async () =>
        {
            _muteManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                {
                    continue;
                }

                var existingMute = await _muteManager.GetActiveMuteAsync(target.SteamId);
                if (existingMute == null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_muted", target.Name]}"));
                    continue;
                }

                await _muteManager.UnmuteAsync(target.SteamId, reason);

                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unmuted_notification", adminName, target.Name, reason]}");
                    }
                    
                    var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        PlayerUtils.SendNotification(targetPlayer, _messagesConfig,
                            PluginLocalizer.Get(_core)["unmuted_personal_html", reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unmuted_personal_chat", reason]}");
                        targetPlayer.VoiceFlags = VoiceFlagValue.Normal;
                    }
                });

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} unmuted {Target}. Reason: {Reason}", 
                    adminName, target.Name, reason);
                await _adminLogManager.AddLogAsync("unmute", adminName, adminSteamId, target.SteamId, target.IpAddress, $"reason={reason}", target.Name);
            }
        });
    }

    public void OnGagCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Gag);

        if (!HasPermission(context, _gagPermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gag_usage"]}");
            return;
        }

        if (RejectGroupTargets(context, args))
        {
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!int.TryParse(args[1], out int duration) || duration < -1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_duration"]}");
            return;
        }

        string reason = args.Length > 2 
            ? string.Join(" ", args.Skip(2)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetSnapshots = targets
            .Select(t => new PunishTargetSnapshot(
                t.SteamID,
                t.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                t.IPAddress))
            .ToList();

        _ = Task.Run(async () =>
        {
            _gagManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                {
                    continue;
                }

                var existingGag = await _gagManager.GetActiveGagAsync(target.SteamId);
                if (existingGag != null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_already_gagged", target.Name]}"));
                    continue;
                }

                await _gagManager.AddGagAsync(target.SteamId, duration, reason);
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug] gag apply steamid={SteamId} duration={Duration} reason={Reason}", target.SteamId, duration, reason);
                var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanently"] : PluginLocalizer.Get(_core)["duration_for_minutes", duration];
                
                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_notification", adminName, target.Name, durationText, reason]}");
                    }
                    
                    var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        var durationDisplay = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanent"] : PluginLocalizer.Get(_core)["duration_minutes", duration];
                        PlayerUtils.SendNotification(targetPlayer, _messagesConfig,
                            PluginLocalizer.Get(_core)["gagged_personal_html", durationDisplay, reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["gagged_personal_chat", durationText, reason]}");
                    }
                });

                await _discord.SendGagNotificationAsync(adminName, target.Name, duration, reason);
                await _adminLogManager.AddLogAsync("gag", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} gagged {Target} for {Duration} minutes. Reason: {Reason}", 
                    adminName, target.Name, duration, reason);
            }
        });
    }

    public void OnUngagCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Ungag);

        if (!HasPermission(context, _gagPermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["ungag_usage"]}");
            return;
        }

        if (RejectGroupTargets(context, args))
        {
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        string reason = args.Length > 1 
            ? string.Join(" ", args.Skip(1)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetSnapshots = targets
            .Select(t => new PunishTargetSnapshot(
                t.SteamID,
                t.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                t.IPAddress))
            .ToList();

        _ = Task.Run(async () =>
        {
            _gagManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                {
                    continue;
                }

                var existingGag = await _gagManager.GetActiveGagAsync(target.SteamId);
                if (existingGag == null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_gagged", target.Name]}"));
                    continue;
                }

                await _gagManager.UngagAsync(target.SteamId, reason);

                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["ungagged_notification", adminName, target.Name, reason]}");
                    }
                    
                    var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        PlayerUtils.SendNotification(targetPlayer, _messagesConfig,
                            PluginLocalizer.Get(_core)["ungagged_personal_html", reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["ungagged_personal_chat", reason]}");
                    }
                });

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} ungagged {Target}. Reason: {Reason}", 
                    adminName, target.Name, reason);
                await _adminLogManager.AddLogAsync("ungag", adminName, adminSteamId, target.SteamId, target.IpAddress, $"reason={reason}", target.Name);
            }
        });
    }

    public void OnSilenceCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Silence);

        if (!HasPermission(context, _silencePermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["silence_usage"]}");
            return;
        }

        if (RejectGroupTargets(context, args))
        {
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        if (!int.TryParse(args[1], out int duration) || duration < -1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_duration"]}");
            return;
        }

        string reason = args.Length > 2 
            ? string.Join(" ", args.Skip(2)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetSnapshots = targets
            .Select(t => new PunishTargetSnapshot(
                t.SteamID,
                t.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                t.IPAddress))
            .ToList();

        _ = Task.Run(async () =>
        {
            _muteManager.SetAdminContext(adminName, adminSteamId);
            _gagManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                {
                    continue;
                }

                var existingMute = await _muteManager.GetActiveMuteAsync(target.SteamId);
                var existingGag = await _gagManager.GetActiveGagAsync(target.SteamId);

                if (existingMute != null && existingGag != null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_already_silenced", target.Name]}"));
                    continue;
                }

                if (existingMute == null)
                    await _muteManager.AddMuteAsync(target.SteamId, duration, reason);
                
                if (existingGag == null)
                    await _gagManager.AddGagAsync(target.SteamId, duration, reason);

                var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanently"] : PluginLocalizer.Get(_core)["duration_for_minutes", duration];
                
                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["silenced_notification", adminName, target.Name, durationText, reason]}");
                    }
                    
                    var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        var durationDisplay = duration <= 0 ? PluginLocalizer.Get(_core)["duration_permanent"] : PluginLocalizer.Get(_core)["duration_minutes", duration];
                        PlayerUtils.SendNotification(targetPlayer, _messagesConfig,
                            PluginLocalizer.Get(_core)["silenced_personal_html", durationDisplay, reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["silenced_personal_chat", durationText, reason]}");
                        targetPlayer.VoiceFlags = VoiceFlagValue.Muted;
                    }
                });

                await _discord.SendSilenceNotificationAsync(adminName, target.Name, duration, reason);
                await _adminLogManager.AddLogAsync("silence", adminName, adminSteamId, target.SteamId, target.IpAddress, $"duration={duration};reason={reason}", target.Name);

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} silenced {Target} for {Duration} minutes. Reason: {Reason}", 
                    adminName, target.Name, duration, reason);
            }
        });
    }

    public void OnUnsilenceCommand(ICommandContext context)
    {
        var args = CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.Unsilence);

        if (!HasPermission(context, _silencePermission))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unsilence_usage"]}");
            return;
        }

        if (RejectGroupTargets(context, args))
        {
            return;
        }

        var targets = PlayerUtils.FindPlayersByTarget(_core, args[0]);
        if (targets.Count == 0)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_found"]}");
            return;
        }

        string reason = args.Length > 1 
            ? string.Join(" ", args.Skip(1)) 
            : PluginLocalizer.Get(_core)["no_reason"];

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;
        var targetSnapshots = targets
            .Select(t => new PunishTargetSnapshot(
                t.SteamID,
                t.Controller.PlayerName ?? PluginLocalizer.Get(_core)["unknown"],
                t.IPAddress))
            .ToList();

        _ = Task.Run(async () =>
        {
            _muteManager.SetAdminContext(adminName, adminSteamId);
            _gagManager.SetAdminContext(adminName, adminSteamId);
            foreach (var target in targetSnapshots)
            {
                if (!await ValidateCanPunishAsync(context, target.SteamId))
                {
                    continue;
                }

                var existingMute = await _muteManager.GetActiveMuteAsync(target.SteamId);
                var existingGag = await _gagManager.GetActiveGagAsync(target.SteamId);

                if (existingMute == null && existingGag == null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["player_not_silenced", target.Name]}"));
                    continue;
                }

                if (existingMute != null)
                    await _muteManager.UnmuteAsync(target.SteamId, reason);
                
                if (existingGag != null)
                    await _gagManager.UngagAsync(target.SteamId, reason);

                _core.Scheduler.NextTick(() =>
                {
                    foreach (var player in _core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
                    {
                        player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unsilenced_notification", adminName, target.Name, reason]}");
                    }
                    
                    var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == target.SteamId);
                    if (targetPlayer != null)
                    {
                        PlayerUtils.SendNotification(targetPlayer, _messagesConfig,
                            PluginLocalizer.Get(_core)["unsilenced_personal_html", reason],
                            $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["unsilenced_personal_chat", reason]}");
                        targetPlayer.VoiceFlags = VoiceFlagValue.Normal;
                    }
                });

                _core.Logger.LogInformationIfEnabled("[CS2_Admin] {Admin} unsilenced {Target}. Reason: {Reason}", 
                    adminName, target.Name, reason);
                await _adminLogManager.AddLogAsync("unsilence", adminName, adminSteamId, target.SteamId, target.IpAddress, $"reason={reason}", target.Name);
            }
        });
    }

    private async Task<bool> ValidateCanPunishAsync(ICommandContext context, ulong targetSteamId)
    {
        if (!context.IsSentByPlayer || context.Sender == null)
        {
            return true;
        }

        if (context.Sender.SteamID == targetSteamId)
        {
            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_self"]}"));
            return false;
        }

        var adminImm = await _adminDbManager.GetEffectiveImmunityAsync(context.Sender.SteamID);
        var targetImm = await _adminDbManager.GetEffectiveImmunityAsync(targetSteamId);
        if (targetImm >= adminImm && targetImm > 0)
        {
            _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["cannot_target_immunity"]}"));
            return false;
        }

        return true;
    }

    private bool RejectGroupTargets(ICommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        if (PlayerUtils.IsGroupTarget(args[0]))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["sanction_group_targets_not_allowed"]}");
            return true;
        }

        return false;
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
            return true;

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _adminRootPermission);
    }

    private readonly record struct PunishTargetSnapshot(ulong SteamId, string Name, string? IpAddress);
}


