using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_discord_links")]
public class DiscordLink
{
    [Key]
    public long Id { get; set; }

    [Column("steam_id")]
    public ulong SteamId { get; set; }

    [Column("discord_id")]
    public ulong DiscordId { get; set; }

    [Column("discord_name")]
    public string DiscordName { get; set; } = string.Empty;

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("linked_at")]
    public DateTime LinkedAt { get; set; }
}
