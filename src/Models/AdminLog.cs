using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_log")]
public class AdminLog
{
    [Key]
    public long Id { get; set; }

    [Column("admin_name")]
    public string AdminName { get; set; } = string.Empty;

    [Column("admin_steamid")]
    public ulong AdminSteamId { get; set; }

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("target_steamid")]
    public ulong? TargetSteamId { get; set; }

    [Column("target_ip")]
    public string? TargetIp { get; set; }

    [Column("details")]
    public string Details { get; set; } = string.Empty;

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("server_ip")]
    public string ServerIp { get; set; } = string.Empty;

    [Column("server_port")]
    public int ServerPort { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
