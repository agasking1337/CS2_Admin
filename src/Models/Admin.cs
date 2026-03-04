using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CS2_Admin.Models;

[Table("admin_admins")]
public class Admin
{
    [Key]
    public int Id { get; set; }

    [Column("steamid")]
    public ulong SteamId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("flags")]
    public string Flags { get; set; } = string.Empty;

    [Column("groups")]
    public string Groups { get; set; } = string.Empty;

    [Column("immunity")]
    public int Immunity { get; set; } = 0;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("added_by")]
    public string? AddedBy { get; set; }

    [Column("added_by_steamid")]
    public ulong? AddedBySteamId { get; set; }

    [NotMapped]
    public bool IsPermanent => ExpiresAt == null;
    
    [NotMapped]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    
    [NotMapped]
    public bool IsActive => !IsExpired;

    [NotMapped]
    public IReadOnlyList<string> GroupList =>
        Groups
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => g.Trim().TrimStart('#', '@'))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
