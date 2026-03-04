namespace CS2_Admin.Utils;

public record RecentPlayerInfo(
    ulong SteamId,
    string Name,
    string IpAddress,
    DateTime LastSeenAt
);

public class RecentPlayersTracker
{
    private readonly object _lock = new();
    private readonly List<RecentPlayerInfo> _recent = [];
    private readonly int _maxEntries;

    public RecentPlayersTracker(int maxEntries = 30)
    {
        _maxEntries = Math.Max(5, maxEntries);
    }

    public void Add(RecentPlayerInfo info)
    {
        lock (_lock)
        {
            _recent.RemoveAll(x => x.SteamId == info.SteamId);
            _recent.Insert(0, info);
            if (_recent.Count > _maxEntries)
            {
                _recent.RemoveRange(_maxEntries, _recent.Count - _maxEntries);
            }
        }
    }

    public List<RecentPlayerInfo> GetRecent()
    {
        lock (_lock)
        {
            return [.. _recent];
        }
    }
}
