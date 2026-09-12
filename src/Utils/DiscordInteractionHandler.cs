using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CS2_Admin.Database;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordInteractionHandler
{
    private const string DiscordApiBaseUrl = "https://discord.com/api/v10";
    private static readonly TimeSpan InteractionDedupTtl = TimeSpan.FromSeconds(30);

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly ConcurrentDictionary<string, DateTime> _processedInteractions = new(StringComparer.Ordinal);

    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly string _botToken;
    private WarnManager? _warnManager;
    private AdminLogManager? _adminLogManager;
    private DiscordVerifyService? _verifyService;

    public DiscordInteractionHandler(ISwiftlyCore core, DiscordRestClient restClient, string botToken)
    {
        _core = core;
        _restClient = restClient;
        _botToken = botToken;
    }

    public void SetDatabaseManagers(WarnManager? wm, AdminLogManager? alm)
    {
        _warnManager = wm;
        _adminLogManager = alm;
    }

    public void SetVerifyService(DiscordVerifyService verifyService)
    {
        _verifyService = verifyService;
    }

    public async Task HandleInteractionAsync(JsonElement data)
    {
        try
        {
            var id = data.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var token = data.TryGetProperty("token", out var tokenElement) ? tokenElement.GetString() : null;
            var applicationId = data.TryGetProperty("application_id", out var applicationIdElement) ? applicationIdElement.GetString() : null;
            var type = data.TryGetProperty("type", out var typeElement) ? typeElement.GetInt32() : 0;

            if (id != null)
            {
                var now = DateTime.UtcNow;
                if (_processedInteractions.TryGetValue(id, out var lastSeen) && (now - lastSeen) < InteractionDedupTtl)
                {
                    return;
                }
                _processedInteractions[id] = now;
                CleanupExpiredDedupEntries();
            }

            if (type == 3 && id != null && token != null && data.TryGetProperty("data", out var componentData))
            {
                var customId = componentData.TryGetProperty("custom_id", out var customIdElement) ? customIdElement.GetString() : null;
                if (customId == "verify_open")
                {
                    await HandleVerifyOpenAsync(id, token);
                }
                else if (customId != null && customId.StartsWith("report_resolve_"))
                {
                    await HandleReportResolveAsync(id, token, applicationId, data, customId);
                }
                else if (customId != null && customId.StartsWith("report_punish_"))
                {
                    await HandleReportPunishAsync(id, token, applicationId, data, customId);
                }
            }
            else if (type == 5 && id != null && token != null && data.TryGetProperty("data", out var modalData))
            {
                var modalCustomId = modalData.TryGetProperty("custom_id", out var modalCustomIdElement) ? modalCustomIdElement.GetString() : null;
                if (modalCustomId == "verify_modal")
                {
                    await HandleVerifyModalSubmitAsync(id, token, data);
                }
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error handling interaction: {Message}", ex.Message);
        }
    }

    private async Task HandleReportResolveAsync(string interactionId, string interactionToken, string? applicationId, JsonElement data, string customId)
    {
        try
        {
            if (!await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 6))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord resolve interaction defer failed for custom_id={CustomId}", customId);
                return;
            }

            var member = data.GetProperty("member");
            var user = member.GetProperty("user");
            var userId = user.GetProperty("id").GetString();

            var message = data.GetProperty("message");
            var messageId = message.TryGetProperty("id", out var messageIdElement) ? messageIdElement.GetString() : null;
            var channelId = message.TryGetProperty("channel_id", out var channelIdElement) ? channelIdElement.GetString() : null;
            var embeds = message.GetProperty("embeds");
            if (embeds.GetArrayLength() == 0)
            {
                await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_missing_embed", "Report message embed could not be found."));
                return;
            }

            var oldEmbed = embeds[0];

            var newEmbed = JsonObject.Create(oldEmbed);
            if (newEmbed == null)
            {
                await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_parse_failed", "Report message embed could not be parsed."));
                return;
            }

            if (newEmbed != null)
            {
                newEmbed["color"] = 65433;

                if (newEmbed.TryGetPropertyValue("description", out var descNode) && descNode != null)
                {
                    var desc = descNode.GetValue<string>();
                    newEmbed["description"] = $"{desc}\n\n**Status:** ✅ {T("discord_report_resolved_status", "Resolved by <@{0}>", userId ?? "0")}";
                }
            }

            var payload = new
            {
                embeds = new JsonObject[] { newEmbed! },
                components = Array.Empty<object>()
            };

            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId) || !await UpdateMessageAsync(channelId, messageId, payload))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord resolve interaction message update failed for custom_id={CustomId}", customId);
                await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_failed", "Report could not be marked as resolved."));
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in resolve interaction: {Message}", ex.Message);
            await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_internal_error", "An internal error occurred while resolving the report."));
        }
    }

    private async Task HandleReportPunishAsync(string interactionId, string interactionToken, string? applicationId, JsonElement data, string customId)
    {
        try
        {
            if (!await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 5, new { flags = 64 }))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord punish interaction defer failed for custom_id={CustomId}", customId);
                return;
            }

            var parts = customId.Split('_');
            if (parts.Length < 3 || !ulong.TryParse(parts[2], out var targetSteamId))
            {
                await EditOriginalResponseAsync(applicationId, interactionToken, BuildEditErrorPayload(T("discord_report_target_parse_failed", "Report target SteamID could not be parsed.")));
                return;
            }

            var warns = _warnManager != null ? await _warnManager.GetWarnHistoryAsync(targetSteamId, WarnHistoryFilter.All, 5) : [];
            var logs = _adminLogManager != null ? await _adminLogManager.GetTargetHistoryAsync(targetSteamId, 5) : [];

            var descBuilder = new StringBuilder();
            if (warns.Count == 0 && logs.Count == 0)
            {
                descBuilder.AppendLine(T("discord_report_no_punishments", "No recent punishments found for this player."));
            }
            else
            {
                if (warns.Count > 0)
                {
                    descBuilder.AppendLine(T("discord_report_recent_warnings", "**Recent Warnings:**"));
                    foreach (var warn in warns)
                    {
                        descBuilder.AppendLine($"- [{warn.CreatedAt:yyyy-MM-dd}] `{warn.Reason}` by {warn.AdminName}");
                    }
                    descBuilder.AppendLine();
                }

                if (logs.Count > 0)
                {
                    descBuilder.AppendLine(T("discord_report_recent_actions", "**Recent Actions:**"));
                    foreach (var log in logs)
                    {
                        descBuilder.AppendLine($"- [{log.CreatedAt:yyyy-MM-dd}] `{log.Action}`: {log.Details}");
                    }
                }
            }

            var embed = new
            {
                title = T("discord_report_punishments_title", "Player Punishments"),
                description = descBuilder.ToString(),
                color = 16711680
            };

            var payload = new
            {
                content = "",
                embeds = new[] { embed }
            };

            if (!await EditOriginalResponseAsync(applicationId, interactionToken, payload))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord punish interaction response edit failed for custom_id={CustomId}", customId);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in punish interaction: {Message}", ex.Message);
            await EditOriginalResponseAsync(applicationId, interactionToken, BuildEditErrorPayload(T("discord_report_punishments_internal_error", "An internal error occurred while loading punishments.")));
        }
    }

    private async Task HandleVerifyOpenAsync(string interactionId, string interactionToken)
    {
        if (_verifyService == null || !_verifyService.IsEnabled)
        {
            await SendErrorAsync(interactionId, interactionToken, T("discord_verify_disabled", "Verification is currently disabled."));
            return;
        }

        var modal = new
        {
            title = T("discord_verify_modal_title", "Link your Steam account"),
            custom_id = "verify_modal",
            components = new object[]
            {
                new
                {
                    type = 1,
                    components = new object[]
                    {
                        new
                        {
                            type = 4,
                            custom_id = "verify_code",
                            label = T("discord_verify_modal_code_label", "In-game verification code"),
                            style = 1,
                            min_length = 4,
                            max_length = 12,
                            required = true,
                            placeholder = T("discord_verify_modal_code_placeholder", "e.g. A1B2C3")
                        }
                    }
                }
            }
        };

        if (!await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 9, modal))
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord verify modal response failed");
        }
    }

    private async Task HandleVerifyModalSubmitAsync(string interactionId, string interactionToken, JsonElement data)
    {
        try
        {
            if (_verifyService == null)
            {
                await SendErrorAsync(interactionId, interactionToken, T("discord_verify_disabled", "Verification is currently disabled."));
                return;
            }

            var user = GetInteractionUser(data);
            if (user == null || !ulong.TryParse(user.Value.UserId, out var discordId))
            {
                await SendErrorAsync(interactionId, interactionToken, T("discord_verify_error", "Something went wrong. Please try again."));
                return;
            }

            var guildId = data.TryGetProperty("guild_id", out var guildIdElement) && guildIdElement.ValueKind == JsonValueKind.String
                ? guildIdElement.GetString()
                : null;

            var code = ExtractModalInputValue(data, "verify_code");
            var outcome = await _verifyService.CompleteVerificationAsync(code, discordId, user.Value.DisplayName, guildId);

            var message = outcome.Result switch
            {
                DiscordVerifyResult.Success => T("discord_verify_success", "✅ Linked! SteamID `{steamid}` is now bound to your Discord account.", outcome.SteamId),
                DiscordVerifyResult.DiscordAlreadyLinked => T("discord_verify_discord_already_linked", "❌ This Discord account is already linked to another Steam account. Contact an admin to unlink it."),
                DiscordVerifyResult.Disabled => T("discord_verify_disabled", "Verification is currently disabled."),
                DiscordVerifyResult.DbError => T("discord_verify_error", "Something went wrong. Please try again."),
                _ => T("discord_verify_invalid_code", "❌ Invalid or expired code. Use `!verify` in-game to get a new one.")
            };

            await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 4, new { content = message, flags = 64 });
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in verify modal submit: {Message}", ex.Message);
            await SendErrorAsync(interactionId, interactionToken, T("discord_verify_error", "Something went wrong. Please try again."));
        }
    }

    private static (string UserId, string DisplayName)? GetInteractionUser(JsonElement data)
    {
        JsonElement user;
        if (data.TryGetProperty("member", out var member) && member.TryGetProperty("user", out var memberUser))
        {
            user = memberUser;
        }
        else if (data.TryGetProperty("user", out var directUser))
        {
            user = directUser;
        }
        else
        {
            return null;
        }

        var userId = user.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var displayName = user.TryGetProperty("global_name", out var globalNameElement)
            && globalNameElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(globalNameElement.GetString())
                ? globalNameElement.GetString()!
                : user.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() ?? userId : userId;

        return (userId, displayName);
    }

    private static string? ExtractModalInputValue(JsonElement data, string inputCustomId)
    {
        if (!data.TryGetProperty("data", out var modalData)
            || !modalData.TryGetProperty("components", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("components", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var input in inputs.EnumerateArray())
            {
                if (input.TryGetProperty("custom_id", out var customIdElement)
                    && string.Equals(customIdElement.GetString(), inputCustomId, StringComparison.Ordinal)
                    && input.TryGetProperty("value", out var valueElement))
                {
                    return valueElement.GetString();
                }
            }
        }

        return null;
    }

    private async Task SendErrorAsync(string interactionId, string interactionToken, string message)
    {
        await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 4, BuildErrorPayload(message));
    }

    private async Task<bool> SendFollowupAsync(string? applicationId, string interactionToken, string message)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/webhooks/{applicationId}/{interactionToken}";
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, BuildErrorPayload(message));
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("send interaction followup", response);
        return false;
    }

    private async Task<bool> EditOriginalResponseAsync(string? applicationId, string interactionToken, object payload)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/webhooks/{applicationId}/{interactionToken}/messages/@original";
        using var request = BuildDiscordRequest(HttpMethod.Patch, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("edit interaction response", response);
        return false;
    }

    private async Task<bool> UpdateMessageAsync(string channelId, string messageId, object payload)
    {
        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages/{messageId}";
        using var request = new HttpRequestMessage(HttpMethod.Patch, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static object BuildErrorPayload(string message)
    {
        return new
        {
            content = $"CS2_Admin: {message}",
            flags = 64
        };
    }

    private static object BuildEditErrorPayload(string message)
    {
        return new
        {
            content = $"CS2_Admin: {message}",
            embeds = Array.Empty<object>()
        };
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

    private HttpRequestMessage BuildDiscordRequest(HttpMethod method, string endpoint, object payload)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task LogDiscordFailureAsync(string action, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        _core.Logger.LogWarningIfEnabled(
            "[CS2_Admin] Discord bot {Action} failed with status {StatusCode}. Response: {Body}",
            action,
            response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? "-" : body);
    }

    private static void CleanupExpiredDedupEntries()
    {
        var cutoff = DateTime.UtcNow - InteractionDedupTtl;
        foreach (var kvp in _processedInteractions)
        {
            if (kvp.Value < cutoff)
            {
                _processedInteractions.TryRemove(kvp.Key, out _);
            }
        }
    }
}
