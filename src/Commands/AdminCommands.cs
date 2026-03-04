using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Menu;
using CS2_Admin.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public class AdminCommands
{
    private readonly ISwiftlyCore _core;
    private readonly AdminDbManager _adminManager;
    private readonly GroupDbManager _groupManager;
    private readonly AdminLogManager _adminLogManager;
    private readonly PermissionsConfig _permissions;
    private readonly TagsConfig _tags;
    private readonly CommandsConfig _commands;
    private readonly AdminMenuManager _menuManager;

    public AdminCommands(
        ISwiftlyCore core,
        AdminDbManager adminManager,
        GroupDbManager groupManager,
        AdminLogManager adminLogManager,
        PermissionsConfig permissions,
        TagsConfig tags,
        CommandsConfig commands,
        AdminMenuManager menuManager)
    {
        _core = core;
        _adminManager = adminManager;
        _groupManager = groupManager;
        _adminLogManager = adminLogManager;
        _permissions = permissions;
        _tags = tags;
        _commands = commands;
        _menuManager = menuManager;
    }

    public void OnAdminRootCommand(ICommandContext context)
    {
        var args = NormalizeArgs(context.Args);
        if (args.Length == 1 && IsAdminAlias(args[0]))
        {
            args = [];
        }

        if (args.Length == 0)
        {
            if (context.Sender == null)
            {
                context.Reply(GetAdminRootUsage(context));
                return;
            }

            if (!HasPermission(context, _permissions.AdminMenu))
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
                return;
            }

            _menuManager.OpenAdminMenu(context.Sender);
            return;
        }

        if (!HasPermission(context, _permissions.AdminRoot))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "addadmin":
                AddAdmin(context, args.Skip(1).ToArray());
                break;
            case "editadmin":
                EditAdmin(context, args.Skip(1).ToArray());
                break;
            case "removeadmin":
                RemoveAdmin(context, args.Skip(1).ToArray());
                break;
            case "listadmins":
                OnListAdminsCommand(context);
                break;
            case "addgroup":
                AddGroup(context, args.Skip(1).ToArray());
                break;
            case "editgroup":
                EditGroup(context, args.Skip(1).ToArray());
                break;
            case "removegroup":
                RemoveGroup(context, args.Skip(1).ToArray());
                break;
            case "listgroups":
                OnListGroupsCommand(context);
                break;
            case "adminreload":
                OnAdminReloadCommand(context);
                break;
            default:
                if (context.Sender != null && HasPermission(context, _permissions.AdminMenu))
                {
                    _menuManager.OpenAdminMenu(context.Sender);
                }
                else
                {
                    context.Reply(GetAdminRootUsage(context));
                }
                break;
        }
    }

    public void OnAddAdminCommand(ICommandContext context)
    {
        AddAdmin(context, CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.AddAdmin));
    }

    private void AddAdmin(ICommandContext context, string[] args)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 3)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetAddAdminUsage(context)}");
            return;
        }

        if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_steamid"]}");
            return;
        }

        var name = args[1];
        if (!TryParseGroupsArgument(args[2], out var groups, out var groupList))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetAddAdminUsage(context)}");
            return;
        }

        if (args.Length > 4)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetAddAdminUsage(context)}");
            return;
        }

        int? durationDays = null;
        if (args.Length == 4)
        {
            if (!int.TryParse(args[3], out var parsedDuration))
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetAddAdminUsage(context)}");
                return;
            }

            durationDays = parsedDuration > 0 ? parsedDuration : null;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            var maxGroupImmunity = 0;
            foreach (var groupName in groupList)
            {
                var group = await _groupManager.GetGroupAsync(groupName);
                if (group == null)
                {
                    _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addadmin_group_not_found", groupName]}"));
                    return;
                }

                maxGroupImmunity = Math.Max(maxGroupImmunity, group.Immunity);
            }

            var resolvedImmunity = maxGroupImmunity;
            var success = await _adminManager.AddAdminAsync(targetSteamId, name, string.Empty, resolvedImmunity, groups, adminName, adminSteamId, durationDays);
            if (!success)
            {
                _core.Scheduler.NextTick(() => context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addadmin_failed"]}"));
                return;
            }

            var effectiveFlags = await _adminManager.GetEffectiveFlagsAsync(targetSteamId);
            _core.Scheduler.NextTick(() =>
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addadmin_success", name, targetSteamId, string.Join(",", effectiveFlags)]}");
            });

            NotifyOnlinePlayer(targetSteamId, PluginLocalizer.Get(_core)["addadmin_granted"]);
            await TryAutoReloadAsync();
            await ApplyTagToOnlinePlayerAsync(targetSteamId);
            await _adminLogManager.AddLogAsync("addadmin", adminName, adminSteamId, targetSteamId, null, $"groups={groups};immunity={resolvedImmunity};duration_days={durationDays?.ToString() ?? "0"}", name);
        });
    }

    public void OnEditAdminCommand(ICommandContext context)
    {
        EditAdmin(context, CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.EditAdmin));
    }

    private void EditAdmin(ICommandContext context, string[] args)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 3)
        {
            context.Reply(GetEditAdminUsage(context));
            return;
        }

        if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_steamid"]}");
            return;
        }

        var field = args[1].ToLowerInvariant();
        var value = string.Join(" ", args.Skip(2));
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        if (field == "flags")
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["editadmin_groups_only"]}");
            return;
        }

        if (field == "groups")
        {
            if (!TryParseGroupsArgument(value, out var normalizedGroups, out _))
            {
                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetAddAdminUsage(context)}");
                return;
            }

            value = normalizedGroups;
        }

        _ = Task.Run(async () =>
        {
            var success = await _adminManager.EditAdminAsync(targetSteamId, field, value);
            _core.Scheduler.NextTick(() =>
            {
                context.Reply(success
                    ? $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["editadmin_success"]}"
                    : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["editadmin_failed"]}");
            });

            if (success)
            {
                await TryAutoReloadAsync();
                await ApplyTagToOnlinePlayerAsync(targetSteamId);
                await _adminLogManager.AddLogAsync("editadmin", adminName, adminSteamId, targetSteamId, null, $"{field}={value}");
            }
        });
    }

    public void OnRemoveAdminCommand(ICommandContext context)
    {
        RemoveAdmin(context, CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.RemoveAdmin));
    }

    private void RemoveAdmin(ICommandContext context, string[] args)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetRemoveAdminUsage(context)}");
            return;
        }

        if (!PlayerUtils.TryParseSteamId(args[0], out var targetSteamId))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["invalid_steamid"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            var success = await _adminManager.RemoveAdminAsync(targetSteamId);
            _core.Scheduler.NextTick(() =>
            {
                context.Reply(success
                    ? $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["removeadmin_success", targetSteamId, targetSteamId]}"
                    : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["removeadmin_failed"]}");
            });

            if (success)
            {
                NotifyOnlinePlayer(targetSteamId, PluginLocalizer.Get(_core)["removeadmin_revoked"]);
                await TryAutoReloadAsync();
                await ApplyTagToOnlinePlayerAsync(targetSteamId);
                await _adminLogManager.AddLogAsync("removeadmin", adminName, adminSteamId, targetSteamId, null, "");
            }
        });
    }

    public void OnListAdminsCommand(ICommandContext context)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        _ = Task.Run(async () =>
        {
            var admins = await _adminManager.GetAllAdminsAsync();
            _core.Scheduler.NextTick(() =>
            {
                if (admins.Count == 0)
                {
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["listadmins_none"]}");
                    return;
                }

                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["listadmins_header", admins.Count]}");
                foreach (var admin in admins)
                {
                    var expiry = admin.IsPermanent
                        ? PluginLocalizer.Get(_core)["admin_permanent"]
                        : PluginLocalizer.Get(_core)["admin_expires", admin.ExpiresAt?.ToString("yyyy-MM-dd") ?? PluginLocalizer.Get(_core)["who_unknown"]];
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["listadmins_entry", admin.Name, admin.SteamId, admin.Groups, admin.Immunity, expiry]}");
                }
            });
        });
    }

    public void OnAddGroupCommand(ICommandContext context)
    {
        AddGroup(context, CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.AddGroup));
    }

    private void AddGroup(ICommandContext context, string[] args)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetAddGroupUsage(context)}");
            return;
        }

        var name = args[0];
        var flags = args[1];
        var immunity = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : 0;
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            var success = await _groupManager.AddOrUpdateGroupAsync(name, flags, immunity);
            _core.Scheduler.NextTick(() => context.Reply(success
                ? $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addgroup_success"]}"
                : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["addgroup_failed"]}"));
            if (success)
            {
                await TryAutoReloadAsync();
                await _adminLogManager.AddLogAsync("addgroup", adminName, adminSteamId, null, null, $"name={name};flags={flags};immunity={immunity}");
            }
        });
    }

    public void OnEditGroupCommand(ICommandContext context)
    {
        EditGroup(context, CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.EditGroup));
    }

    private void EditGroup(ICommandContext context, string[] args)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 2)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetEditGroupUsage(context)}");
            return;
        }

        var name = args[0];
        var flags = args[1];
        var immunity = args.Length > 2 && int.TryParse(args[2], out var parsed) ? parsed : 0;
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            var success = await _groupManager.AddOrUpdateGroupAsync(name, flags, immunity);
            _core.Scheduler.NextTick(() => context.Reply(success
                ? $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["editgroup_success"]}"
                : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["editgroup_failed"]}"));
            if (success)
            {
                await TryAutoReloadAsync();
                await _adminLogManager.AddLogAsync("editgroup", adminName, adminSteamId, null, null, $"name={name};flags={flags};immunity={immunity}");
            }
        });
    }

    public void OnRemoveGroupCommand(ICommandContext context)
    {
        RemoveGroup(context, CommandAliasUtils.NormalizeCommandArgs(context.Args, _commands.RemoveGroup));
    }

    private void RemoveGroup(ICommandContext context, string[] args)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        if (args.Length < 1)
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {GetRemoveGroupUsage(context)}");
            return;
        }

        var name = args[0];
        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0;

        _ = Task.Run(async () =>
        {
            var success = await _groupManager.RemoveGroupAsync(name);
            _core.Scheduler.NextTick(() => context.Reply(success
                ? $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["removegroup_success"]}"
                : $" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["removegroup_failed"]}"));
            if (success)
            {
                await TryAutoReloadAsync();
                await _adminLogManager.AddLogAsync("removegroup", adminName, adminSteamId, null, null, $"name={name}");
            }
        });
    }

    public void OnListGroupsCommand(ICommandContext context)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        _ = Task.Run(async () =>
        {
            var groups = await _groupManager.GetAllGroupsAsync();
            _core.Scheduler.NextTick(() =>
            {
                if (groups.Count == 0)
                {
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["listgroups_none"]}");
                    return;
                }

                context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["listgroups_header", groups.Count]}");
                foreach (var group in groups)
                {
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["listgroups_entry", group.Name, group.Immunity, group.Flags]}");
                }
            });
        });
    }

    public void OnAdminReloadCommand(ICommandContext context)
    {
        if (!HasRootPermission(context))
        {
            context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["no_permission"]}");
            return;
        }

        var adminName = context.Sender?.Controller.PlayerName ?? PluginLocalizer.Get(_core)["console_name"];
        var adminSteamId = context.Sender?.SteamID ?? 0UL;

        _ = Task.Run(async () =>
        {
            try
            {
                ReloadPermissionsConfig();
                var onlineCount = await ReloadAdminsAndTagsAsync();

                _core.Scheduler.NextTick(() =>
                {
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["adminreload_success"]}");
                });

                await _adminLogManager.AddLogAsync("adminreload", adminName, adminSteamId, null, null, $"online={onlineCount}");
            }
            catch (Exception ex)
            {
                _core.Logger.LogErrorIfEnabled("[CS2_Admin] adminreload failed: {Message}", ex.Message);
                _core.Scheduler.NextTick(() =>
                {
                    context.Reply($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {PluginLocalizer.Get(_core)["adminreload_failed"]}");
                });
            }
        });
    }

    private bool HasPermission(ICommandContext context, string permission)
    {
        if (!context.IsSentByPlayer)
        {
            return true;
        }

        var steamId = context.Sender!.SteamID;
        return _core.Permission.PlayerHasPermission(steamId, permission)
               || _core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot);
    }

    private bool HasRootPermission(ICommandContext context)
    {
        if (!context.IsSentByPlayer)
        {
            return true;
        }

        return _core.Permission.PlayerHasPermission(context.Sender!.SteamID, _permissions.AdminRoot);
    }

    private bool TryParseGroupsArgument(string rawGroupsArg, out string normalizedGroups, out List<string> groups)
    {
        groups = rawGroupsArg
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => g.Trim().TrimStart('#', '@'))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        normalizedGroups = string.Join(",", groups);
        return groups.Count > 0;
    }

    private async Task ApplyTagToOnlinePlayerAsync(ulong steamId)
    {
        if (!_tags.Enabled)
        {
            return;
        }

        var player = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
        if (player == null)
        {
            return;
        }

        var admin = await _adminManager.GetAdminAsync(steamId);
        if (admin == null)
        {
            admin = (await _adminManager.GetAllAdminsAsync()).FirstOrDefault(a => a.SteamId == steamId && a.IsActive);
        }

        string tag;
        if (admin != null && admin.IsActive)
        {
            tag = _groupManager.GetPrimaryGroupNameSync(admin.GroupList)
                  ?? admin.GroupList.FirstOrDefault()
                  ?? "ADMIN";
        }
        else if (_core.Permission.PlayerHasPermission(steamId, _permissions.AdminRoot))
        {
            tag = "ADMIN";
        }
        else
        {
            tag = _tags.PlayerTag;
        }

        PlayerUtils.SetScoreTagReliable(_core, player.PlayerID, tag);
    }

    private void NotifyOnlinePlayer(ulong steamId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _core.Scheduler.NextTick(() =>
        {
            var player = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player == null)
            {
                return;
            }

            player.SendChat($" \x02{PluginLocalizer.Get(_core)["prefix"]}\x01 {message}");
        });
    }

    private static IEnumerable<string> SplitPermissions(string rawPermissions)
    {
        return rawPermissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));
    }

    private async Task TryAutoReloadAsync()
    {
        try
        {
            ReloadPermissionsConfig();
            await ReloadAdminsAndTagsAsync();
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Auto adminreload failed: {Message}", ex.Message);
        }
    }

    private void ReloadPermissionsConfig()
    {
        try
        {
            var permissionsPath = _core.Configuration.GetConfigPath("permissions.json");
            if (!File.Exists(permissionsPath))
            {
                return;
            }

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(permissionsPath, optional: false, reloadOnChange: false)
                .Build();

            var section = configuration.GetSection("CS2AdminPermissions");
            if (!section.Exists())
            {
                return;
            }

            section.Bind(_permissions);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to reload permissions.json: {Message}", ex.Message);
        }
    }

    private async Task<int> ReloadAdminsAndTagsAsync()
    {
        _groupManager.ClearCache();
        _adminManager.ClearCache();

        var groups = await _groupManager.GetAllGroupsAsync();
        var admins = await _adminManager.GetAllAdminsAsync();
        var adminsBySteamId = admins.ToDictionary(a => a.SteamId, a => a);

        var managedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var flag in SplitPermissions(group.Flags))
            {
                managedPermissions.Add(flag);
            }
        }

        foreach (var admin in admins)
        {
            foreach (var flag in SplitPermissions(admin.Flags))
            {
                managedPermissions.Add(flag);
            }
        }

        foreach (var bypassPermission in _permissions.RootBypassPermissions)
        {
            if (!string.IsNullOrWhiteSpace(bypassPermission))
            {
                managedPermissions.Add(bypassPermission.Trim());
            }
        }

        var onlinePlayers = _core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .Select(p => (p.PlayerID, p.SteamID))
            .ToList();

        var resolvedTags = new Dictionary<int, string>();

        foreach (var snapshot in onlinePlayers)
        {
            foreach (var permission in managedPermissions)
            {
                _core.Permission.RemovePermission(snapshot.SteamID, permission);
            }

            var effectiveFlags = await _adminManager.GetEffectiveFlagsAsync(snapshot.SteamID);
            var hasRoot = false;

            foreach (var flag in effectiveFlags)
            {
                if (string.IsNullOrWhiteSpace(flag))
                {
                    continue;
                }

                var normalizedFlag = flag.Trim();
                _core.Permission.AddPermission(snapshot.SteamID, normalizedFlag);
                if (string.Equals(normalizedFlag, _permissions.AdminRoot, StringComparison.OrdinalIgnoreCase))
                {
                    hasRoot = true;
                }
            }

            if (hasRoot)
            {
                foreach (var bypassPermission in _permissions.RootBypassPermissions)
                {
                    if (!string.IsNullOrWhiteSpace(bypassPermission))
                    {
                        _core.Permission.AddPermission(snapshot.SteamID, bypassPermission.Trim());
                    }
                }
            }

            if (_tags.Enabled)
            {
                adminsBySteamId.TryGetValue(snapshot.SteamID, out var activeAdmin);
                var adminGroupTag = activeAdmin != null && activeAdmin.IsActive
                    ? _groupManager.GetPrimaryGroupNameSync(activeAdmin.GroupList) ?? activeAdmin.GroupList.FirstOrDefault()
                    : null;

                if (string.IsNullOrWhiteSpace(adminGroupTag) &&
                    _core.Permission.PlayerHasPermission(snapshot.SteamID, _permissions.AdminRoot))
                {
                    adminGroupTag = "ADMIN";
                }

                resolvedTags[snapshot.PlayerID] = string.IsNullOrWhiteSpace(adminGroupTag)
                    ? _tags.PlayerTag
                    : adminGroupTag;
            }
        }

        _core.Scheduler.NextTick(() =>
        {
            if (!_tags.Enabled)
            {
                return;
            }

            foreach (var pair in resolvedTags)
            {
                PlayerUtils.SetScoreTagReliable(_core, pair.Key, pair.Value);
            }
        });

        return onlinePlayers.Count;
    }

    private string[] NormalizeArgs(string[] args)
    {
        return args
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToArray();
    }

    private string GetAdminRootUsage(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return PluginLocalizer.Get(_core)["admin_root_usage"];
        }

        return "Use: sw_addadmin | sw_editadmin | sw_removeadmin | sw_listadmins | sw_addgroup | sw_editgroup | sw_removegroup | sw_listgroups | sw_adminreload";
    }

    private string GetAddAdminUsage(ICommandContext context)
    {
        return context.IsSentByPlayer
            ? PluginLocalizer.Get(_core)["addadmin_usage"]
            : "Usage: sw_addadmin <steamid> <name> <#group or group1,group2> [duration_days]";
    }

    private string GetEditAdminUsage(ICommandContext context)
    {
        return context.IsSentByPlayer
            ? PluginLocalizer.Get(_core)["editadmin_usage"]
            : "Usage: sw_editadmin <steamid> <name|groups|immunity|duration> <value>";
    }

    private string GetRemoveAdminUsage(ICommandContext context)
    {
        return context.IsSentByPlayer
            ? PluginLocalizer.Get(_core)["removeadmin_usage"]
            : "Usage: sw_removeadmin <steamid>";
    }

    private string GetAddGroupUsage(ICommandContext context)
    {
        return context.IsSentByPlayer
            ? PluginLocalizer.Get(_core)["addgroup_usage"]
            : "Usage: sw_addgroup <name> <flags> [immunity]";
    }

    private string GetEditGroupUsage(ICommandContext context)
    {
        return context.IsSentByPlayer
            ? PluginLocalizer.Get(_core)["editgroup_usage"]
            : "Usage: sw_editgroup <name> <flags> [immunity]";
    }

    private string GetRemoveGroupUsage(ICommandContext context)
    {
        return context.IsSentByPlayer
            ? PluginLocalizer.Get(_core)["removegroup_usage"]
            : "Usage: sw_removegroup <name>";
    }

    private bool IsAdminAlias(string raw)
    {
        var value = raw.Trim();
        return _commands.AdminRoot.Any(c => c.Equals(value, StringComparison.OrdinalIgnoreCase))
               || _commands.AdminMenu.Any(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
    }
}


