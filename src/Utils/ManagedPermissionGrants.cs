using System;
using System.Collections.Generic;
using System.Linq;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

/// <summary>
/// Tracks only permissions which CS2_Admin actually introduced to a SteamID.
/// Existing permissions remain untouched. An external plugin adding the same
/// permission later is indistinguishable in SwiftlyS2's shared permission API.
/// </summary>
internal static class ManagedPermissionGrants
{
    private static readonly object Sync = new();
    private static readonly Dictionary<ulong, HashSet<string>> Owned = new();

    internal static void Apply(ISwiftlyCore core, ulong steamId, IEnumerable<string> permissions)
    {
        var wanted = new HashSet<string>(permissions.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        lock (Sync)
        {
            if (!Owned.TryGetValue(steamId, out var owned))
                Owned[steamId] = owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var flag in owned.Where(x => !wanted.Contains(x)).ToArray())
            {
                core.Permission.RemovePermission(steamId, flag);
                owned.Remove(flag);
            }
            foreach (var flag in wanted)
            {
                if (!core.Permission.PlayerHasPermission(steamId, flag))
                {
                    core.Permission.AddPermission(steamId, flag);
                    owned.Add(flag);
                }
            }
            if (owned.Count == 0) Owned.Remove(steamId);
        }
    }
}
