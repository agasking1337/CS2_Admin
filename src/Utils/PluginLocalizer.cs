using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Translation;

namespace CS2_Admin.Utils;

public static class PluginLocalizer
{
    private static ILocalizer? _overrideLocalizer;
    private static readonly object Sync = new();

    public static void SetOverride(ILocalizer? localizer)
    {
        lock (Sync)
        {
            _overrideLocalizer = localizer;
        }
    }

    public static ILocalizer Get(ISwiftlyCore core)
    {
        lock (Sync)
        {
            return _overrideLocalizer ?? core.Localizer;
        }
    }
}
