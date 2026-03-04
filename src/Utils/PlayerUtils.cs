using System.Text.RegularExpressions;
using CS2_Admin.Config;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Translation;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Utils;

public static class PlayerUtils
{
    public static bool TryParseSteamId(string input, out ulong steamId)
    {
        steamId = 0;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalizedInput = input.Trim();
        if (normalizedInput.StartsWith("@", StringComparison.Ordinal))
        {
            normalizedInput = normalizedInput[1..];
        }

        // Direct SteamID64
        if (ulong.TryParse(normalizedInput, out steamId) && steamId > 76561197960265728)
            return true;

        // STEAM_X:Y:Z format
        var steamIdMatch = Regex.Match(normalizedInput, @"STEAM_(\d):(\d):(\d+)");
        if (steamIdMatch.Success)
        {
            ulong y = ulong.Parse(steamIdMatch.Groups[2].Value);
            ulong z = ulong.Parse(steamIdMatch.Groups[3].Value);
            steamId = 76561197960265728 + z * 2 + y;
            return true;
        }

        // [U:1:X] format
        var steam3Match = Regex.Match(normalizedInput, @"\[U:1:(\d+)\]");
        if (steam3Match.Success)
        {
            ulong accountId = ulong.Parse(steam3Match.Groups[1].Value);
            steamId = 76561197960265728 + accountId;
            return true;
        }

        return false;
    }

    public static IPlayer? FindPlayerByTarget(ISwiftlyCore core, string target)
    {
        var players = core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        var normalizedTarget = target.Trim();

        // Try by #userid
        if (normalizedTarget.StartsWith("#", StringComparison.Ordinal)
            && int.TryParse(normalizedTarget[1..], out var statusId))
        {
            return players.FirstOrDefault(p => p.PlayerID == statusId);
        }

        // Try by player ID/slot
        if (int.TryParse(normalizedTarget, out int playerId))
        {
            return players.FirstOrDefault(p => p.PlayerID == playerId);
        }

        // Try by SteamID
        if (TryParseSteamId(normalizedTarget, out ulong steamId))
        {
            return players.FirstOrDefault(p => p.SteamID == steamId);
        }

        // Try by name (partial match)
        // Exact match first
        var exactMatch = players.FirstOrDefault(p => 
            p.Controller.PlayerName?.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) == true);
        if (exactMatch != null)
            return exactMatch;

        // Partial match
        var partialMatches = players.Where(p => 
            p.Controller.PlayerName?.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase) == true).ToList();
        
        if (partialMatches.Count == 1)
            return partialMatches[0];

        return null;
    }

    public static List<IPlayer> FindPlayersByTarget(ISwiftlyCore core, string target, bool includeDeadPlayers = true)
    {
        var players = core.PlayerManager.GetAllPlayers().Where(p => p.IsValid).ToList();
        var normalizedTarget = target.Trim();

        if (!includeDeadPlayers)
        {
            players = players.Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0).ToList();
        }

        // Special group targets
        switch (normalizedTarget.ToLowerInvariant())
        {
            case "@all":
                return players;
            case "@t":
            case "@terrorists":
                return players.Where(p => p.Controller.TeamNum == 2).ToList();
            case "@ct":
            case "@counterterrorists":
                return players.Where(p => p.Controller.TeamNum == 3).ToList();
            case "@alive":
                return players.Where(p => p.PlayerPawn?.IsValid == true && p.PlayerPawn.Health > 0).ToList();
            case "@dead":
                return players.Where(p => p.PlayerPawn?.IsValid != true || p.PlayerPawn.Health <= 0).ToList();
            case "@bots":
                return players.Where(p => p.IsFakeClient).ToList();
            case "@humans":
                return players.Where(p => !p.IsFakeClient).ToList();
        }

        // Single player target
        var player = FindPlayerByTarget(core, normalizedTarget);
        return player != null ? new List<IPlayer> { player } : new List<IPlayer>();
    }

    public static bool IsGroupTarget(string target)
    {
        return target.Trim().ToLowerInvariant() switch
        {
            "@all" => true,
            "@t" => true,
            "@terrorists" => true,
            "@ct" => true,
            "@counterterrorists" => true,
            "@alive" => true,
            "@dead" => true,
            "@bots" => true,
            "@humans" => true,
            _ => false
        };
    }

    public static void Freeze(IPlayer player)
    {
        if (player.PlayerPawn?.IsValid == true)
        {
            player.PlayerPawn.MoveType = MoveType_t.MOVETYPE_NONE;
            player.PlayerPawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            player.PlayerPawn.MoveTypeUpdated();
            
            // Zero out velocity
            player.PlayerPawn.AbsVelocity = new Vector(0, 0, 0);
        }
    }

    public static void Unfreeze(IPlayer player)
    {
        if (player.PlayerPawn?.IsValid == true)
        {
            // Remove FL_FROZEN flag
            player.PlayerPawn.Flags &= ~(uint)Flags_t.FL_FROZEN;
            player.PlayerPawn.FlagsUpdated();
            
            // Restore MoveType to WALK
            player.PlayerPawn.MoveType = MoveType_t.MOVETYPE_WALK;
            player.PlayerPawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
            player.PlayerPawn.MoveTypeUpdated();
        }
    }

    public static void SetNoclip(ISwiftlyCore core, IPlayer player, bool enabled)
    {
        if (player?.IsValid != true || player.PlayerPawn?.IsValid != true)
        {
            return;
        }

        var pawn = player.PlayerPawn!;
        if (enabled)
        {
            pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;
            pawn.ActualMoveType = MoveType_t.MOVETYPE_NOCLIP;
            pawn.MoveTypeUpdated();
            return;
        }

        pawn.MoveType = MoveType_t.MOVETYPE_WALK;
        pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
        pawn.MoveTypeUpdated();
    }

    public static bool IsNoclipEnabled(IPlayer player)
    {
        return player.Controller?.NoClipEnabled == true || 
               player.PlayerPawn?.MoveType == MoveType_t.MOVETYPE_NOCLIP;
    }

    public static string GetTeamName(int teamNum, ILocalizer localizer)
    {
        return teamNum switch
        {
            0 => localizer["team_unassigned"],
            1 => localizer["team_spectator"],
            2 => localizer["team_terrorist"],
            3 => localizer["team_ct"],
            _ => localizer["team_unknown"]
        };
    }

    public static Team? ParseTeam(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "t" or "terrorist" or "terrorists" or "2" => Team.T,
            "ct" or "counterterrorist" or "counterterrorists" or "3" => Team.CT,
            "spec" or "spectator" or "spectators" or "1" => Team.Spectator,
            _ => null
        };
    }

    public static void SetScoreTag(IPlayer player, string? tag)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        var normalized = BuildScoreboardTag(player, tag);
        if (player.Controller.Clan == normalized)
        {
            return;
        }

        player.Controller.Clan = normalized;
        player.Controller.ClanUpdated();
    }

    public static void SetScoreTagReliable(ISwiftlyCore core, int playerId, string? tag)
    {
        void ApplyTag()
        {
            var player = core.PlayerManager.GetPlayer(playerId);
            if (player?.IsValid == true)
            {
                SetScoreTag(player, tag);
            }
        }

        core.Scheduler.NextTick(ApplyTag);
        core.Scheduler.DelayBySeconds(0.2f, ApplyTag);
        core.Scheduler.DelayBySeconds(1.0f, ApplyTag);
        core.Scheduler.DelayBySeconds(3.0f, ApplyTag);
    }

    public static string GetScoreTag(IPlayer player, string fallbackTag)
    {
        if (player == null || !player.IsValid)
        {
            return fallbackTag;
        }

        var current = player.Controller.Clan;
        if (!string.IsNullOrWhiteSpace(current))
        {
            return BuildScoreboardTag(player, current);
        }

        return BuildScoreboardTag(player, fallbackTag);
    }

    public static string BuildScoreboardTag(IPlayer player, string? baseTag)
    {
        var normalizedBaseTag = ExtractBaseTag(baseTag);
        // Some servers/UI paths strip a leading '#'. Prefix with zero-width space so '#'
        // remains visible in scoreboard while keeping rendered text as "#id".
        var playerIdPart = $"\u200B#{player.PlayerID}";
        if (string.IsNullOrWhiteSpace(normalizedBaseTag))
        {
            return $"{playerIdPart} | - |";
        }

        return $"{playerIdPart} | {normalizedBaseTag} |";
    }

    private static string ExtractBaseTag(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return string.Empty;
        }

        var normalized = rawTag.Trim();
        normalized = normalized.Replace("\u200B", string.Empty);
        var segments = normalized
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length >= 3 && Regex.IsMatch(segments[0], @"^#?\d+$"))
        {
            return segments[1].Trim();
        }

        if (segments.Length == 2)
        {
            if (Regex.IsMatch(segments[0], @"^#?\d+$"))
            {
                return segments[1].Trim();
            }

            return segments[0].Trim();
        }

        normalized = Regex.Replace(normalized, @"^#?\d+\s*\|?\s*", string.Empty).Trim();
        return normalized;
    }

    /// <summary>
    /// Sends a notification message to a player, using CenterHTML if enabled in config, otherwise chat.
    /// </summary>
    public static void SendNotification(IPlayer player, MessagesConfig config, string htmlMessage, string chatMessage)
    {
        if (config.EnableCenterHtmlMessages)
        {
            player.SendCenterHTML(htmlMessage, config.CenterHtmlDurationMs);
        }
        else
        {
            player.SendChat(chatMessage);
        }
    }
}
