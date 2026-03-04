using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_mutes")]
public class Mute
{
    [Key]
    public int Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("admin_name")]
    public string AdminName { get; set; } = string.Empty;

    [Column("admin_steamid")]
    public ulong AdminSteamId { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("status")]
    public MuteStatus Status { get; set; } = MuteStatus.Active;

    [Column("unmute_admin_name")]
    public string? UnmuteAdminName { get; set; }

    [Column("unmute_admin_steamid")]
    public ulong? UnmuteAdminSteamId { get; set; }

    [Column("unmute_reason")]
    public string? UnmuteReason { get; set; }

    [Column("unmute_date")]
    public DateTime? UnmuteDate { get; set; }

    [NotMapped]
    public bool IsPermanent => ExpiresAt == null;
    
    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    
    [NotMapped]
    public bool IsActive => Status == MuteStatus.Active && !IsExpired;
    
    [NotMapped]
    public TimeSpan? TimeRemaining => ExpiresAt?.Subtract(DateTime.UtcNow);
}

public enum MuteStatus
{
    Active,
    Expired,
    Unmuted
}
