using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_warns")]
public class Warn
{
    [Key]
    public long Id { get; set; }

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
    public WarnStatus Status { get; set; } = WarnStatus.Active;

    [Column("unwarn_admin_name")]
    public string? UnwarnAdminName { get; set; }

    [Column("unwarn_admin_steamid")]
    public ulong? UnwarnAdminSteamId { get; set; }

    [Column("unwarn_reason")]
    public string? UnwarnReason { get; set; }

    [Column("unwarn_date")]
    public DateTime? UnwarnDate { get; set; }

    [NotMapped]
    public bool IsPermanent => ExpiresAt == null;

    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    [NotMapped]
    public bool IsActive => Status == WarnStatus.Active && !IsExpired;

    [NotMapped]
    public TimeSpan? TimeRemaining => ExpiresAt?.Subtract(DateTime.UtcNow);
}

public enum WarnStatus
{
    Active,
    Expired,
    Removed
}
