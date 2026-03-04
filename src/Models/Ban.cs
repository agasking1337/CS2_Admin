using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_bans")]
public class Ban
{
    [Key]
    public int Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("target_name")]
    public string TargetName { get; set; } = string.Empty;

    [Column("target_type")]
    public BanTargetType TargetType { get; set; } = BanTargetType.SteamId;

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("admin_name")]
    public string AdminName { get; set; } = string.Empty;

    [Column("admin_steamid")]
    public ulong AdminSteamId { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("is_global")]
    public bool IsGlobal { get; set; }

    [Column("server_id")]
    public string ServerId { get; set; } = string.Empty;

    [Column("server_ip")]
    public string ServerIp { get; set; } = string.Empty;

    [Column("server_port")]
    public int ServerPort { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("status")]
    public BanStatus Status { get; set; } = BanStatus.Active;

    [Column("unban_admin_name")]
    public string? UnbanAdminName { get; set; }

    [Column("unban_admin_steamid")]
    public ulong? UnbanAdminSteamId { get; set; }

    [Column("unban_reason")]
    public string? UnbanReason { get; set; }

    [Column("unban_date")]
    public DateTime? UnbanDate { get; set; }

    [NotMapped]
    public bool IsPermanent => ExpiresAt == null;
    
    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    
    [NotMapped]
    public bool IsActive => Status == BanStatus.Active && !IsExpired;
    
    [NotMapped]
    public TimeSpan? TimeRemaining => ExpiresAt?.Subtract(DateTime.UtcNow);
}

public enum BanStatus
{
    Active,
    Expired,
    Unbanned
}

public enum BanTargetType
{
    SteamId,
    Ip
}
