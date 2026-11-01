using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;

namespace CS2_Admin.Utils;

public sealed class CommandBlockerService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ISwiftlyCore _core;
    private readonly DiscordLinkDbManager _links;
    private readonly CommandBlockerFileConfig _config;
    private readonly DiscordFileConfig _discord;
    private readonly HashSet<string> _blocked;
    private readonly ConcurrentDictionary<ulong, (bool Linked, bool Allowed, DateTime Expires)> _cache = new();
    private readonly ConcurrentDictionary<ulong, byte> _refreshing = new();
    private readonly ConcurrentDictionary<ulong, int> _versions = new();
    private readonly ConcurrentDictionary<ulong, DateTime> _lastNotice = new();
    // If a command is blocked while WarmUp() is already refreshing this SteamID,
    // remember the player so the in-flight refresh can still send the result.
    private readonly ConcurrentDictionary<ulong, int> _pendingNoticePlayers = new();
    private Guid _chatHook;
    private Guid _consoleHook;
    private bool _active;

    public CommandBlockerService(ISwiftlyCore core, CommandBlockerFileConfig config, DiscordFileConfig discord, CommandsConfig commands, DiscordLinkDbManager links)
    {
        _core = core;
        _config = config;
        _discord = discord;
        _links = links;
        _blocked = new HashSet<string>(
            (config.BlockedCommands ?? []).Select(Normalize).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        foreach (var alias in commands.Verify.Concat(commands.Unverify))
            _blocked.Remove(Normalize(alias));
    }

    public void Start()
    {
        if (!_config.Enabled || _blocked.Count == 0)
            return;
        if (!_discord.VerifyEnabled || string.IsNullOrWhiteSpace(_discord.BotToken)
            || string.IsNullOrWhiteSpace(_discord.GuildId) || string.IsNullOrWhiteSpace(_discord.VerifiedRoleId))
        {
            _core.Logger.LogWarningIfEnabled("Command blocker requires VerifyEnabled, BotToken, GuildId and VerifiedRoleId in discord.json.");
            return;
        }
        _active = true;
        _chatHook = _core.Command.HookClientChat(OnChat);
        _consoleHook = _core.Command.HookClientCommand(OnClientCommand);
    }

    public void Stop()
    {
        _active = false;
        if (_chatHook != Guid.Empty) _core.Command.UnhookClientChat(_chatHook);
        if (_consoleHook != Guid.Empty) _core.Command.UnhookClientCommand(_consoleHook);
        _chatHook = Guid.Empty;
        _consoleHook = Guid.Empty;
        _cache.Clear();
        _pendingNoticePlayers.Clear();
    }

    private HookResult OnChat(int playerId, string text, bool teamOnly)
    {
        if (string.IsNullOrWhiteSpace(text) || (text[0] != '!' && text[0] != '/'))
            return HookResult.Continue;
        var alias = text[1..].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return ShouldBlock(playerId, alias) ? HookResult.Stop : HookResult.Continue;
    }

    private HookResult OnClientCommand(int playerId, string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return HookResult.Continue;
        var first = commandLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (first.Equals("say", StringComparison.OrdinalIgnoreCase)
            || first.Equals("say_team", StringComparison.OrdinalIgnoreCase))
        {
            var space = commandLine.IndexOfAny(new[] { ' ', '\t' });
            if (space < 0) return HookResult.Continue;
            var chat = commandLine[(space + 1)..].Trim().Trim('"');
            if (chat.Length == 0 || (chat[0] != '!' && chat[0] != '/')) return HookResult.Continue;
            var alias = chat[1..].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return ShouldBlock(playerId, alias) ? HookResult.Stop : HookResult.Continue;
        }
        return ShouldBlock(playerId, first) ? HookResult.Stop : HookResult.Continue;
    }

    public bool ShouldBlock(int playerId, string alias)
    {
        if (!_active || !_blocked.Contains(Normalize(alias))) return false;
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || !player.IsValid || player.IsFakeClient) return false;

        var steamId = player.SteamID;
        if (_cache.TryGetValue(steamId, out var state) && state.Expires > DateTime.UtcNow)
        {
            if (state.Allowed) return false;
            Notify(playerId, steamId, state.Linked ? "command_blocker_role_required" : "command_blocker_link_required");
            return true;
        }

        // The command is blocked while verification is unknown. Remember who tried it,
        // including when WarmUp() already owns the refresh, so the eventual result is shown.
        _pendingNoticePlayers[steamId] = playerId;
        StartRefresh(steamId);
        return true;
    }

    public void WarmUp(ulong steamId)
    {
        if (!_active || steamId == 0 || _cache.TryGetValue(steamId, out var cached) && cached.Expires > DateTime.UtcNow)
            return;
        StartRefresh(steamId);
    }

    private void StartRefresh(ulong steamId)
    {
        if (!_active || steamId == 0) return;
        if (_refreshing.TryAdd(steamId, 0))
            _ = Task.Run(() => RefreshRoleAsync(steamId, _versions.GetOrAdd(steamId, 0)));
    }

    public void OnLinkChanged(ulong steamId, bool linked)
    {
        _versions.AddOrUpdate(steamId, 1, (_, value) => value + 1);
        if (linked) _cache.TryRemove(steamId, out _);
        else _cache[steamId] = (false, false, DateTime.UtcNow.AddSeconds(30));

        // If a command was attempted during a refresh, use the new state (or refresh it)
        // rather than leaving the pending notification stranded.
        if (_pendingNoticePlayers.ContainsKey(steamId))
        {
            if (!linked)
                CompletePendingNotice(steamId, "command_blocker_link_required");
            else
                StartRefresh(steamId);
        }
    }

    private async Task RefreshRoleAsync(ulong steamId, int version)
    {
        try
        {
            var link = await _links.GetBySteamIdAsync(steamId);
            if (_versions.GetOrAdd(steamId, 0) != version) return;
            if (link == null)
            {
                _cache[steamId] = (false, false, DateTime.UtcNow.AddSeconds(30));
                CompletePendingNotice(steamId, "command_blocker_link_required");
                return;
            }

            var url = $"https://discord.com/api/v10/guilds/{_discord.GuildId}/members/{link.DiscordId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _discord.BotToken);
            using var response = await Http.SendAsync(request);
            if (_versions.GetOrAdd(steamId, 0) != version) return;

            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var roles = json.RootElement.GetProperty("roles");
                var allowed = roles.EnumerateArray().Any(role => role.GetString() == _discord.VerifiedRoleId);
                if (_versions.GetOrAdd(steamId, 0) != version) return;
                _cache[steamId] = (true, allowed, DateTime.UtcNow.AddSeconds(allowed ? Math.Clamp(_config.RoleCacheSeconds, 10, 600) : 10));
                CompletePendingNotice(steamId, allowed ? null : "command_blocker_role_required");
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _cache[steamId] = (true, false, DateTime.UtcNow.AddSeconds(10));
                CompletePendingNotice(steamId, "command_blocker_role_required");
            }
            else
            {
                _core.Logger.LogWarningIfEnabled("Could not check Discord verified role (HTTP {Status}).", (int)response.StatusCode);
                _cache[steamId] = (true, false, DateTime.UtcNow.AddSeconds(10));
                CompletePendingNotice(steamId, "command_blocker_role_required");
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Could not check Discord verified role: {Message}", ex.Message);
            if (_versions.GetOrAdd(steamId, 0) != version) return;
            _cache[steamId] = (false, false, DateTime.UtcNow.AddSeconds(10));
            // The status could not be determined; show the general link/verification prompt
            // rather than silently blocking the command.
            CompletePendingNotice(steamId, "command_blocker_link_required");
        }
        finally
        {
            _refreshing.TryRemove(steamId, out _);

            // If a link change invalidated this refresh while a command was waiting,
            // immediately retry with the current version or resolve from the fresh cache.
            if (_active && _pendingNoticePlayers.ContainsKey(steamId))
            {
                if (_cache.TryGetValue(steamId, out var state) && state.Expires > DateTime.UtcNow)
                    CompletePendingNotice(steamId, state.Allowed ? null : state.Linked ? "command_blocker_role_required" : "command_blocker_link_required");
                else
                    StartRefresh(steamId);
            }
        }
    }

    private void CompletePendingNotice(ulong steamId, string? key)
    {
        if (!_pendingNoticePlayers.TryRemove(steamId, out var playerId)) return;
        if (key != null)
            Notify(playerId, steamId, key);
    }

    private void Notify(int playerId, ulong steamId, string key)
    {
        if (playerId <= 0) return;
        if (_lastNotice.TryGetValue(steamId, out var previous) && DateTime.UtcNow - previous < TimeSpan.FromSeconds(3)) return;
        _lastNotice[steamId] = DateTime.UtcNow;
        var message = LocalizerHelper.GetWithFallback(_core, key, "Link your Steam and Discord accounts and get the verified role to use this command.");
        _core.Scheduler.NextTick(() =>
        {
            var player = _core.PlayerManager.GetPlayer(playerId);
            if (player?.IsValid == true && player.SteamID == steamId)
            {
                var prefix = LocalizerHelper.Get(_core, "prefix");
                foreach (var line in message.Replace("\\n", "\n").Replace("\r\n", "\n").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        player.SendChat($" \x02{prefix}\x01 {line}");
                }
            }
        });
    }

    private static string Normalize(string? alias)
    {
        var name = (alias ?? "").Trim().TrimStart('!', '/');
        if (name.StartsWith("sw_", StringComparison.OrdinalIgnoreCase)) name = name[3..];
        return name.ToLowerInvariant();
    }
}
