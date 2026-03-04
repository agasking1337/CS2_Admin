using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_player_ips")]
public class PlayerIpRecord
{
    [Key]
    public long Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("player_name")]
    public string PlayerName { get; set; } = string.Empty;

    [Column("ip_address")]
    public string IpAddress { get; set; } = string.Empty;

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }
}
