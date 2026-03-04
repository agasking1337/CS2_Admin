namespace CS2_Admin.Database;

public static class PunishmentQueryCompat
{
    private const string StatusExpr = "LOWER(COALESCE(CAST(`status` AS CHAR), ''))";
    private const string TargetTypeExpr = "LOWER(COALESCE(CAST(`target_type` AS CHAR), ''))";

    public static string ActiveStatusWhere => $"({StatusExpr} IN ('', '0', '1', 'active'))";
    public static string ActiveSteamTargetWhere => $"({TargetTypeExpr} IN ('', '0', 'steamid'))";
    public static string ActiveIpTargetWhere => $"({TargetTypeExpr} IN ('1', 'ip'))";
}
