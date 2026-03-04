using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_servers")]
public class ServerInfo
{
    [Key]
    public int Id { get; set; }

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("server_ip")]
    public string ServerIp { get; set; } = string.Empty;

    [Column("server_port")]
    public int ServerPort { get; set; }

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }
}
