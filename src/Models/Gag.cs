using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_gags")]
public class Gag
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
    public GagStatus Status { get; set; } = GagStatus.Active;

    [Column("ungag_admin_name")]
    public string? UngagAdminName { get; set; }

    [Column("ungag_admin_steamid")]
    public ulong? UngagAdminSteamId { get; set; }

    [Column("ungag_reason")]
    public string? UngagReason { get; set; }

    [Column("ungag_date")]
    public DateTime? UngagDate { get; set; }

    [NotMapped]
    public bool IsPermanent => ExpiresAt == null;
    
    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    
    [NotMapped]
    public bool IsActive => Status == GagStatus.Active && !IsExpired;
    
    [NotMapped]
    public TimeSpan? TimeRemaining => ExpiresAt?.Subtract(DateTime.UtcNow);
}

public enum GagStatus
{
    Active,
    Expired,
    Ungagged
}
