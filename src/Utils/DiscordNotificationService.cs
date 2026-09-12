using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CS2_Admin.Models;
using CS2_Admin.Services;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Utils;

public class DiscordNotificationService
{
    private static readonly Regex ReportMenuMessageRegex = new(
        @"^Target:\s*(?<target>.+?)\s*\|\s*Reason:\s*(?<reason>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HttpClient GeoHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly string _serverName;
    private readonly string _defaultChannelId;
    private readonly string _connectionChannelId;
    private readonly string _chatChannelId;
    private readonly string _callAdminChannelId;
    private readonly string _reportChannelId;
    private readonly string _adminTimeChannelId;
    private readonly HashSet<string> _commandAliases;

    public DiscordNotificationService(ISwiftlyCore core, DiscordRestClient restClient,
        string serverName,
        string defaultChannelId,
        string connectionChannelId,
        string chatChannelId,
        string callAdminChannelId,
        string reportChannelId,
        string adminTimeChannelId,
        HashSet<string>? commandAliases = null)
    {
        _core = core;
        _restClient = restClient;
        _serverName = serverName;
        _defaultChannelId = defaultChannelId;
        _connectionChannelId = connectionChannelId;
        _chatChannelId = chatChannelId;
        _callAdminChannelId = callAdminChannelId;
        _reportChannelId = reportChannelId;
        _adminTimeChannelId = adminTimeChannelId;
        _commandAliases = commandAliases ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SendConnectNotificationAsync(IPlayer player)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][Debug][DiscordConnect] skipped reason=invalid_or_fake");
            return;
        }

        await SendConnectNotificationAsync(
            player.Controller.PlayerName,
            player.SteamID,
            player.IPAddress,
            GetActiveHumanPlayerCount());
    }

    public async Task SendConnectNotificationAsync(string? playerName, ulong steamId, string? ipAddress, int activePlayers)
    {
        var channelId = ResolveLogChannelId(_connectionChannelId);
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][DiscordConnect] Sending connect notification to Discord... | SteamID: {SteamId} | Name: {Name} | IP: {Ip} | Channel ID: {ResolvedChannel}",
            steamId,
            string.IsNullOrWhiteSpace(playerName) ? "-" : playerName,
            string.IsNullOrWhiteSpace(ipAddress) ? "-" : ipAddress,
            DiscordHelpers.MaskChannelId(channelId));

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][DiscordConnect] Skipped sending to Discord (Reason: Connect channel not set) | SteamID: {SteamId}", steamId);
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(
                playerName,
                steamId,
                ipAddress,
                includeGeoLookup: true);

            var embed = new
            {
                title = T("discord_connect_title", "Player {0} connected", DiscordHelpers.EscapeMarkdown(snapshot.DisplayName)),
                url = DiscordHelpers.BuildSteamProfileUrl(snapshot.SteamId),
                description = T(
                    "discord_connect_description",
                    "{0} connected from {1} {2}\n**SteamID:** `{3}`\n**IP:** ||{4}||",
                    DiscordHelpers.EscapeMarkdown(snapshot.DisplayName),
                    DiscordHelpers.CountryCodeToDiscordFlag(snapshot.CountryCode),
                    snapshot.CountryName,
                    snapshot.SteamId,
                    snapshot.IpAddress),
                color = 65280,
                footer = new
                {
                    text = T(
                        "discord_active_players_footer",
                        "Active Players: {0}/{1} | Server: {2}",
                        activePlayers,
                        ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
                        GetServerLabel())
                }
            };

            var messageId = await _restClient.SendEmbedAsync(channelId, embed);
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Debug][DiscordConnect] result steamid={SteamId} channel={ChannelId} success={Success} messageId={MessageId}",
                steamId,
                DiscordHelpers.MaskChannelId(channelId),
                !string.IsNullOrWhiteSpace(messageId),
                string.IsNullOrWhiteSpace(messageId) ? "-" : messageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error sending connect notification: {Message}", ex.Message);
        }
    }

    public async Task SendDisconnectNotificationAsync(string? playerName, ulong steamId, string? ipAddress)
    {
        var channelId = ResolveLogChannelId(_connectionChannelId);
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][DiscordDisconnect] Sending disconnect notification to Discord... | SteamID: {SteamId} | Name: {Name} | IP: {Ip} | Channel ID: {ResolvedChannel}",
            steamId,
            string.IsNullOrWhiteSpace(playerName) ? "-" : playerName,
            string.IsNullOrWhiteSpace(ipAddress) ? "-" : ipAddress,
            DiscordHelpers.MaskChannelId(channelId));

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][DiscordDisconnect] Skipped sending to Discord (Reason: Connect channel not set) | SteamID: {SteamId}", steamId);
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(playerName, steamId, ipAddress, includeGeoLookup: true);
            var activePlayers = GetActiveHumanPlayerCount();

            var embed = new
            {
                title = T("discord_disconnect_title", "Player {0} disconnected", DiscordHelpers.EscapeMarkdown(snapshot.DisplayName)),
                url = DiscordHelpers.BuildSteamProfileUrl(snapshot.SteamId),
                description = T(
                    "discord_disconnect_description",
                    "{0} disconnected from {1} {2}\n**SteamID:** `{3}`\n**IP:** ||{4}||",
                    DiscordHelpers.EscapeMarkdown(snapshot.DisplayName),
                    DiscordHelpers.CountryCodeToDiscordFlag(snapshot.CountryCode),
                    snapshot.CountryName,
                    snapshot.SteamId,
                    snapshot.IpAddress),
                color = 16711680,
                footer = new
                {
                    text = T(
                        "discord_active_players_footer",
                        "Active Players: {0}/{1} | Server: {2}",
                        activePlayers,
                        ServerIdentity.GetMaxPlayers(_core, _core.PlayerManager.PlayerCap),
                        GetServerLabel())
                }
            };

            var messageId = await _restClient.SendEmbedAsync(channelId, embed);
            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin][Debug][DiscordDisconnect] result steamid={SteamId} channel={ChannelId} success={Success} messageId={MessageId}",
                steamId,
                DiscordHelpers.MaskChannelId(channelId),
                !string.IsNullOrWhiteSpace(messageId),
                string.IsNullOrWhiteSpace(messageId) ? "-" : messageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error sending disconnect notification: {Message}", ex.Message);
        }
    }

    public async Task SendChatNotificationAsync(IPlayer player, string message, bool teamOnly)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][DiscordChat] Skipped sending to Discord (Reason: Invalid or Fake Client)");
            return;
        }

        var trimmed = message.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][DiscordChat] Skipped sending to Discord (Reason: Empty message) | SteamID: {SteamId}", player.SteamID);
            return;
        }

        if (_commandAliases.Count > 0)
        {
            var firstToken = trimmed.Split(' ', 2)[0].TrimStart('!', '/');
            if (!string.IsNullOrEmpty(firstToken) && _commandAliases.Contains(firstToken))
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin][DiscordChat] Skipped sending to Discord (Reason: Text is a plugin command) | SteamID: {SteamId} | Command: {Text}", player.SteamID, trimmed);
                return;
            }
        }

        var channelId = ResolveLogChannelId(_chatChannelId);
        _core.Logger.LogInformationIfEnabled(
            "[CS2_Admin][DiscordChat] Sending message to Discord... | SteamID: {SteamId} | Name: {Name} | Team Chat: {TeamOnly} | Channel ID: {ResolvedChannel}",
            player.SteamID,
            player.Controller.PlayerName ?? "-",
            teamOnly,
            DiscordHelpers.MaskChannelId(channelId));

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin][DiscordChat] Skipped sending to Discord (Reason: Chat channel not set) | SteamID: {SteamId}", player.SteamID);
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(
                player.Controller.PlayerName,
                player.SteamID,
                player.IPAddress);

            var scopePrefix = teamOnly ? "[Team] " : string.Empty;
            var line = $"{DiscordHelpers.CountryCodeToDiscordFlag(snapshot.CountryCode)} [{DiscordHelpers.EscapeMarkdown(GetServerLabel())}] | {scopePrefix}**{DiscordHelpers.EscapeMarkdown(snapshot.DisplayName)}** (`{snapshot.SteamId}`): {DiscordHelpers.EscapeMarkdown(trimmed)}";
            var messageId = await _restClient.SendMessageAsync(channelId, line);
            // _core.Logger.LogInformationIfEnabled(
            //    "[CS2_Admin][Debug][DiscordChat] service result steamid={SteamId} channel={ChannelId} success={Success} messageId={MessageId}",
            //    player.SteamID,
            //    DiscordHelpers.MaskChannelId(channelId),
            //    !string.IsNullOrWhiteSpace(messageId),
            //    string.IsNullOrWhiteSpace(messageId) ? "-" : messageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error sending chat notification: {Message}", ex.Message);
        }
    }

    public async Task SendBanNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["permanent"] : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, "discord_minutes", "{0} minutes", duration);
            var embed = BuildModerationActionEmbed("Ban", 15158332, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending ban notification: {Message}", ex.Message);
        }
    }

    public async Task SendUnbanNotificationAsync(string adminName, string targetSteamId, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var embed = BuildModerationActionEmbed("Unban", 3066993, adminName, "0", "-", targetSteamId, $"Reason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending unban notification: {Message}", ex.Message);
        }
    }

    public async Task SendMuteNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["permanent"] : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, "discord_minutes", "{0} minutes", duration);
            var embed = BuildModerationActionEmbed("Mute", 15105570, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending mute notification: {Message}", ex.Message);
        }
    }

    public async Task SendGagNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["permanent"] : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, "discord_minutes", "{0} minutes", duration);
            var embed = BuildModerationActionEmbed("Gag", 15105570, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending gag notification: {Message}", ex.Message);
        }
    }

    public async Task SendKickNotificationAsync(string adminName, string targetName, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var embed = BuildModerationActionEmbed("Kick", 15844367, adminName, "0", targetName, "-", $"Reason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending kick notification: {Message}", ex.Message);
        }
    }

    public async Task SendSilenceNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["permanent"] : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, "discord_minutes", "{0} minutes", duration);
            var embed = BuildModerationActionEmbed("Silence", 10181046, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending silence notification: {Message}", ex.Message);
        }
    }

    public async Task SendWarnNotificationAsync(string adminName, string targetName, int duration, string reason)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var durationText = duration <= 0 ? PluginLocalizer.Get(_core)["permanent"] : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, "discord_minutes", "{0} minutes", duration);
            var embed = BuildModerationActionEmbed("Warn", 16098851, adminName, "0", targetName, "-", $"Duration: `{durationText}`\nReason: `{reason}`");

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending warn notification: {Message}", ex.Message);
        }
    }

    public async Task SendCallAdminNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        var channelId = ResolveChannelId(_callAdminChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var reporterValue = $"[{DiscordHelpers.EscapeMarkdown(playerName)}]({DiscordHelpers.BuildSteamProfileUrl(playerSteamId)})";
            var embed = new
            {
                title = $"🚨 {T("discord_calladmin_title_embed", "{0} CallAdmin", GetServerLabel())}",
                description = T(
                    "discord_calladmin_description",
                    "**Reporter:** {0}\n**Server:** `{1}`",
                    reporterValue,
                    string.IsNullOrWhiteSpace(serverId) ? GetServerLabel() : serverId),
                color = 10181046,
                fields = new[]
                {
                    new { name = T("discord_calladmin_message", "Message"), value = string.IsNullOrWhiteSpace(message) ? "-" : DiscordHelpers.EscapeMarkdown(message), inline = false }
                },
                footer = new { text = T("discord_calladmin_footer", "CS2_Admin | CallAdmin | {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await _restClient.SendEmbedAsync(
                channelId,
                embed,
                "@everyone",
                DiscordHelpers.BuildLinkButtonComponents(
                    (T("discord_reporter_profile_button", "Reporter Profile"), DiscordHelpers.BuildSteamProfileUrl(playerSteamId)),
                    (T("discord_server_status_button", "Server Status"), BuildSteamServerUrl())),
                allowEveryoneMention: true);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending calladmin notification: {Message}", ex.Message);
        }
    }

    public async Task SendReportNotificationAsync(string playerName, ulong playerSteamId, string message, string serverId)
    {
        var channelId = ResolveChannelId(_reportChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var rawMessage = string.IsNullOrWhiteSpace(message) ? "-" : message.Trim();
            var targetValue = "-";
            var displayMessage = rawMessage;
            ulong? targetSteamId = null;
            var targetName = "-";

            var parsed = ReportMenuMessageRegex.Match(rawMessage);
            if (parsed.Success)
            {
                targetValue = parsed.Groups["target"].Value.Trim();
                displayMessage = parsed.Groups["reason"].Value.Trim();
                targetSteamId = DiscordHelpers.TryExtractSteamId(targetValue);
                targetName = DiscordHelpers.StripTrailingSteamId(targetValue);
            }
            else
            {
                targetName = targetValue;
            }

            var reporterValue = $"[{DiscordHelpers.EscapeMarkdown(playerName)}]({DiscordHelpers.BuildSteamProfileUrl(playerSteamId)})";
            var targetDisplayValue = targetSteamId is > 0
                ? $"[{DiscordHelpers.EscapeMarkdown(targetName)}]({DiscordHelpers.BuildSteamProfileUrl(targetSteamId.Value)})"
                : DiscordHelpers.EscapeMarkdown(targetName);
            var embed = new
            {
                title = $"🚩 {T("discord_report_title_embed", "{0} Player Report", GetServerLabel())}",
                description = T("discord_report_description", "**Reporter:** {0}\n**Target:** {1}", reporterValue, targetDisplayValue),
                color = 16763904,
                fields = new[]
                {
                    new { name = T("discord_server", "Server"), value = $"`{(string.IsNullOrWhiteSpace(serverId) ? GetServerLabel() : serverId)}`", inline = true },
                    new { name = T("discord_reporter_steamid", "Reporter SteamID"), value = $"`{playerSteamId}`", inline = true },
                    new { name = T("discord_target_steamid", "Target SteamID"), value = targetSteamId is > 0 ? $"`{targetSteamId.Value}`" : "-", inline = true },
                    new { name = T("discord_reason", "Reason"), value = string.IsNullOrWhiteSpace(displayMessage) ? "-" : DiscordHelpers.EscapeMarkdown(displayMessage), inline = false }
                },
                footer = new { text = T("discord_report_footer", "CS2_Admin | Report System | {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")) },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await _restClient.SendEmbedAsync(
                channelId,
                embed,
                "@everyone",
                BuildReportInteractiveComponents(serverId, targetSteamId ?? 0, playerSteamId),
                allowEveryoneMention: true);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending report notification: {Message}", ex.Message);
        }
    }

    public async Task SendAdminTimeNotificationAsync(IReadOnlyList<AdminPlaytime> entries, List<string>? zeroPlaytimeAdmins = null)
    {
        var channelId = ResolveChannelId(_adminTimeChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var topEntries = entries.Take(10).ToList();
            var fields = new List<object>();
            if (topEntries.Count == 0)
            {
                fields.Add(new { name = "No data", value = "No admin playtime data yet.", inline = false });
            }
            else
            {
                var leftColumn = topEntries.Take((topEntries.Count + 1) / 2).ToList();
                var rightColumn = topEntries.Skip(leftColumn.Count).ToList();
                fields.Add(new { name = "\u200B", value = BuildAdminPlaytimeColumn(leftColumn, 1), inline = true });
                if (rightColumn.Count > 0)
                {
                    fields.Add(new { name = "\u200B", value = BuildAdminPlaytimeColumn(rightColumn, leftColumn.Count + 1), inline = true });
                }
            }

            var embed = new
            {
                title = $":trophy: {GetServerLabel()} Admin Leaderboard (TOP {topEntries.Count})",
                color = 3447003,
                description = $"Admin playtime ranking for `{GetServerLabel()}`.",
                fields = fields.ToArray(),
                footer = new { text = $"Last update | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await _restClient.SendEmbedAsync(channelId, embed);

            if (zeroPlaytimeAdmins != null && zeroPlaytimeAdmins.Count > 0)
            {
                var zeroFields = zeroPlaytimeAdmins.Select(name => new { name = "\u200B", value = $":zzz: **{DiscordHelpers.EscapeMarkdown(name)}** - 0 min", inline = false }).Cast<object>().ToList();
                var zeroEmbed = new
                {
                    title = $":sleeping: {GetServerLabel()} Zero-Playtime Admins ({zeroPlaytimeAdmins.Count})",
                    color = 15158332,
                    description = "Admins who have never played on the server.",
                    fields = zeroFields.ToArray(),
                    footer = new { text = $"Last update | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" },
                    timestamp = DateTime.UtcNow.ToString("o")
                };
                await _restClient.SendEmbedAsync(channelId, zeroEmbed);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending admin playtime notification: {Message}", ex.Message);
        }
    }

    public async Task SendAdminActionNotificationAsync(string action, string adminName, ulong adminSteamId, ulong? targetSteamId, string? details, string serverId, string? targetName = null)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId))
            return;

        try
        {
            var embed = BuildModerationActionEmbed(
                string.IsNullOrWhiteSpace(action) ? "Admin Action" : action.Trim(),
                3447003,
                string.IsNullOrWhiteSpace(adminName) ? "-" : adminName,
                adminSteamId.ToString(),
                string.IsNullOrWhiteSpace(targetName) ? "-" : targetName,
                targetSteamId?.ToString() ?? "-",
                string.IsNullOrWhiteSpace(details) ? "-" : details,
                serverId);

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending admin action notification: {Message}", ex.Message);
        }
    }

    public async Task SendAltAccountAlertAsync(string? playerName, ulong steamId, string? ipAddress, AltAccountReport report)
    {
        var channelId = ResolveChannelId(_defaultChannelId);
        if (string.IsNullOrEmpty(channelId) || report.IsEmpty)
        {
            return;
        }

        try
        {
            var snapshot = await BuildPlayerSnapshotAsync(playerName, steamId, ipAddress, includeGeoLookup: true);

            var fields = new List<object>();
            foreach (var match in report.Matches.Take(5))
            {
                var lines = new List<string>();
                foreach (var sanction in match.Sanctions.Take(6))
                {
                    var statusLabel = sanction.IsActive
                        ? T("discord_alt_alert_status_active", "ACTIVE")
                        : T("discord_alt_alert_status_ended", "ended");
                    var expiry = sanction.ExpiresAt.HasValue
                        ? sanction.ExpiresAt.Value.ToString("yyyy-MM-dd")
                        : T("discord_permanent", "Permanent");
                    lines.Add($"• {FormatSanctionType(sanction.Type)} — {DiscordHelpers.EscapeMarkdown(TruncateForEmbed(sanction.Reason, 80))} `{statusLabel}` `{expiry}`");
                }
                lines.Add(T("discord_alt_alert_last_seen", "IP last seen: `{0} UTC`", match.Account.LastSeenAt.ToString("yyyy-MM-dd HH:mm")));

                var linkedName = string.IsNullOrWhiteSpace(match.Account.PlayerName)
                    ? match.Account.SteamId.ToString()
                    : match.Account.PlayerName;
                fields.Add(new
                {
                    name = T("discord_alt_alert_linked_field", "Linked account: {0} (`{1}`)", DiscordHelpers.EscapeMarkdown(linkedName), match.Account.SteamId),
                    value = string.Join("\n", lines),
                    inline = false
                });
            }

            if (report.IpBans.Count > 0)
            {
                var ipBanLines = report.IpBans.Take(3).Select(ban =>
                {
                    var statusLabel = ban.IsActive
                        ? T("discord_alt_alert_status_active", "ACTIVE")
                        : T("discord_alt_alert_status_ended", "ended");
                    var who = string.IsNullOrWhiteSpace(ban.TargetName)
                        ? (ban.SteamId > 0 ? ban.SteamId.ToString() : "-")
                        : ban.TargetName;
                    return $"• {DiscordHelpers.EscapeMarkdown(who)} — {DiscordHelpers.EscapeMarkdown(TruncateForEmbed(ban.Reason, 80))} `{statusLabel}`";
                });
                fields.Add(new
                {
                    name = T("discord_alt_alert_ip_bans_field", "IP-targeted bans on this address"),
                    value = string.Join("\n", ipBanLines),
                    inline = false
                });
            }

            var color = report.Severity switch
            {
                AltAlertSeverity.High => 15158332,
                AltAlertSeverity.Medium => 15105570,
                _ => 16763904
            };

            var playerLink = $"[{DiscordHelpers.EscapeMarkdown(snapshot.DisplayName)}]({DiscordHelpers.BuildSteamProfileUrl(snapshot.SteamId)})";
            var embed = new
            {
                title = $"⚠️ {T("discord_alt_alert_title", "Possible alt account connected")}",
                url = DiscordHelpers.BuildSteamProfileUrl(snapshot.SteamId),
                description = T(
                    "discord_alt_alert_description",
                    "**New connection:** {0}\n**SteamID:** `{1}`\n**IP:** ||{2}|| {3} {4}",
                    playerLink,
                    snapshot.SteamId,
                    snapshot.IpAddress,
                    DiscordHelpers.CountryCodeToDiscordFlag(snapshot.CountryCode),
                    snapshot.CountryName),
                color,
                fields = fields.ToArray(),
                footer = new
                {
                    text = T(
                        "discord_alt_alert_footer",
                        "CS2_Admin | Alt Detection | {0} | {1} UTC",
                        GetServerLabel(),
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
                },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            await _restClient.SendEmbedAsync(channelId, embed);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error sending alt account alert: {Message}", ex.Message);
        }
    }

    private static string FormatSanctionType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "ban" => "🔨 Ban",
            "mute" => "🔇 Mute",
            "gag" => "🚫 Gag",
            "warn" => "⚠️ Warn",
            _ => type
        };
    }

    private static string TruncateForEmbed(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..(maxLength - 1)] + "…";
    }

    private string T(string key, string fallback, params object[] args)
    {
        // İsimli placeholder'lı ({player}, {reason}, {minutes}...) çeviri anahtarları chat ile AYNI
        // şekilde çalışsın diye LocalizerHelper üzerinden normalize edip biçimlendiriyoruz. Native
        // localizer[key, args] yalnızca pozisyonel {0} destekler ve isimli placeholder'ları bozardı.
        return args.Length == 0
            ? global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback)
            : global::CS2_Admin.Services.LocalizerHelper.GetWithFallback(_core, key, fallback, args);
    }

    private static string BuildAdminPlaytimeColumn(IReadOnlyList<AdminPlaytime> entries, int startRank)
    {
        var lines = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rank = startRank + i;
            lines.Add($"{DiscordHelpers.GetRankPrefix(rank)} {DiscordHelpers.BuildSteamProfileMarkdown(entry.PlayerName, entry.SteamId)}");
            lines.Add($"Playtime: `{DiscordHelpers.FormatPlaytime(entry.PlaytimeMinutes)}`");
            if (i < entries.Count - 1)
            {
                lines.Add(string.Empty);
            }
        }

        return string.Join("\n", lines);
    }

    private string GetServerLabel()
    {
        if (!string.IsNullOrWhiteSpace(_serverName))
        {
            return _serverName.Trim();
        }

        var configuredName = ServerIdentity.GetName(_core);
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName;
        }

        var serverId = ServerIdentity.GetServerId(_core);
        return string.IsNullOrWhiteSpace(serverId) ? "Server" : serverId;
    }

    private int GetActiveHumanPlayerCount()
    {
        return _core.PlayerManager
            .GetAllPlayers()
            .Count(player => player.IsValid && !player.IsFakeClient);
    }

    private async Task<PlayerSnapshot> BuildPlayerSnapshotAsync(string? playerName, ulong steamId, string? ipAddress, bool includeGeoLookup = true)
    {
        var displayName = string.IsNullOrWhiteSpace(playerName) ? "Unknown" : playerName.Trim();
        var normalizedIp = DiscordHelpers.NormalizeIp(ipAddress);
        var location = includeGeoLookup
            ? await ResolveGeoLocationAsync(normalizedIp)
            : GeoLocation.Unknown();
        var resolvedIp = string.IsNullOrWhiteSpace(normalizedIp) ? "Unknown" : normalizedIp;
        return new PlayerSnapshot(displayName, steamId, resolvedIp, location.CountryCode, location.CountryName);
    }

    private async Task<GeoLocation> ResolveGeoLocationAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || DiscordHelpers.IsPrivateOrLocalIp(ipAddress))
        {
            return GeoLocation.Unknown();
        }

        try
        {
            using var response = await GeoHttpClient.GetAsync($"https://ipwho.is/{Uri.EscapeDataString(ipAddress)}");
            if (!response.IsSuccessStatusCode)
            {
                return GeoLocation.Unknown();
            }

            var json = await response.Content.ReadAsStringAsync();
            var geoResponse = JsonSerializer.Deserialize<GeoIpResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (geoResponse is null || !geoResponse.Success || string.IsNullOrWhiteSpace(geoResponse.CountryCode))
            {
                return GeoLocation.Unknown();
            }

            return new GeoLocation(
                geoResponse.CountryCode.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(geoResponse.Country) ? "Unknown" : geoResponse.Country.Trim());
        }
        catch
        {
            return GeoLocation.Unknown();
        }
    }

    private object BuildModerationActionEmbed(
        string action,
        int color,
        string adminName,
        string adminSteamId,
        string playerName,
        string playerId,
        string details,
        string? serverId = null)
    {
        var resolvedAction = string.IsNullOrWhiteSpace(action) ? "Admin Action" : action.Trim();
        var resolvedServer = string.IsNullOrWhiteSpace(serverId) ? ServerIdentity.GetServerId(_core) : serverId.Trim();
        var adminValue = string.IsNullOrWhiteSpace(adminSteamId) || adminSteamId == "0"
            ? DiscordHelpers.EscapeMarkdown(adminName)
            : $"[{DiscordHelpers.EscapeMarkdown(adminName)}]({DiscordHelpers.BuildSteamProfileUrl(adminSteamId)})";
        var playerValue = string.IsNullOrWhiteSpace(playerId) || playerId == "-" || playerId == "0"
            ? DiscordHelpers.EscapeMarkdown(playerName)
            : $"[{DiscordHelpers.EscapeMarkdown(playerName)}]({DiscordHelpers.BuildSteamProfileUrl(playerId)})";

        return new
        {
            title = $"{GetServerLabel()} | Admin Log",
            description = $":shield: **{DiscordHelpers.EscapeMarkdown(resolvedAction)}** action executed on `{(string.IsNullOrWhiteSpace(resolvedServer) ? GetServerLabel() : resolvedServer)}`.",
            color,
            fields = new object[]
            {
                new { name = "Action", value = $"`{DiscordHelpers.EscapeMarkdown(resolvedAction)}`", inline = true },
                new { name = "Admin", value = adminValue, inline = true },
                new { name = "Target", value = playerValue, inline = true },
                new { name = "Details", value = string.IsNullOrWhiteSpace(details) ? "-" : details, inline = false }
            },
            footer = new { text = "CS2_Admin | Admin Logs" },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private string ResolveChannelId(string preferredChannelId)
    {
        if (!string.IsNullOrWhiteSpace(preferredChannelId))
        {
            return preferredChannelId;
        }

        return _defaultChannelId;
    }

    private string ResolveLogChannelId(string preferredChannelId)
    {
        if (!string.IsNullOrWhiteSpace(preferredChannelId))
        {
            return preferredChannelId;
        }

        return string.Empty;
    }

    private string BuildSteamServerUrl()
    {
        var ip = ServerIdentity.GetIp(_core);
        var port = ServerIdentity.GetPort(_core);
        if (string.IsNullOrWhiteSpace(ip) || port <= 0 || ip == "0.0.0.0")
        {
            return DiscordHelpers.BuildSteamProfileUrl(0);
        }

        return $"https://steamcommunity.com/linkfilter/?url=steam://connect/{ip}:{port}";
    }

    private object[]? BuildReportInteractiveComponents(string serverId, ulong targetSteamId, ulong reporterSteamId)
    {
        return new object[]
        {
            new
            {
                type = 1,
                components = new object[]
                {
                    new
                    {
                        type = 2,
                        style = 3,
                        label = T("discord_report_mark_resolved_button", "Mark as resolved"),
                        custom_id = $"report_resolve_{serverId}_{targetSteamId}_{reporterSteamId}",
                        emoji = new { name = "✅" }
                    },
                    new
                    {
                        type = 2,
                        style = 4,
                        label = T("discord_report_player_punishments_button", "Player Punishments"),
                        custom_id = $"report_punish_{targetSteamId}",
                        emoji = new { name = "🚫" }
                    }
                }
            }
        };
    }

    private sealed record PlayerSnapshot(string DisplayName, ulong SteamId, string IpAddress, string CountryCode, string CountryName)
    {
        public string CountryLabel => $"{CountryName} ({CountryCode})";
    }

    private sealed record GeoLocation(string CountryCode, string CountryName)
    {
        public static GeoLocation Unknown() => new("??", "Unknown");
    }

    private sealed class GeoIpResponse
    {
        public bool Success { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }
}
