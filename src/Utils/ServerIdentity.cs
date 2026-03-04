using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public static class ServerIdentity
{
    public static string GetName(ISwiftlyCore core)
    {
        try
        {
            var cvar = core.ConVar.Find<string>("hostname");
            if (cvar != null && !string.IsNullOrWhiteSpace(cvar.Value))
            {
                return cvar.Value.Trim();
            }
        }
        catch
        {
        }

        return "Unknown Server";
    }

    public static string GetIp(ISwiftlyCore core)
    {
        try
        {
            var cvar = core.ConVar.Find<string>("ip");
            if (cvar != null && !string.IsNullOrWhiteSpace(cvar.Value))
            {
                return cvar.Value.Trim();
            }
        }
        catch
        {
        }

        return "0.0.0.0";
    }

    public static int GetPort(ISwiftlyCore core)
    {
        try
        {
            var cvar = core.ConVar.Find<int>("hostport");
            if (cvar != null)
            {
                return cvar.Value;
            }
        }
        catch
        {
        }

        return 0;
    }

    public static string GetServerId(ISwiftlyCore core)
    {
        return $"{GetIp(core)}:{GetPort(core)}";
    }
}
