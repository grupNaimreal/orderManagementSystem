// Models/AuditLog.cs
// Stores chronological audit trail entries for key QRDER (Bean Crate Cafe) operations
// such as order updates, cash payment confirmations, menu changes, and report exports.

using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public class AuditLog
{
    public int Id { get; set; }

    [Required]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [StringLength(256)]
    public string ActorName { get; set; } = "System";

    [Required]
    [StringLength(50)]
    public string ActorRole { get; set; } = "System";

    [Required]
    [StringLength(60)]
    public string ActionType { get; set; } = string.Empty;

    [Required]
    [StringLength(60)]
    public string EntityName { get; set; } = string.Empty;

    [StringLength(60)]
    public string? EntityId { get; set; }

    [Required]
    [StringLength(500)]
    public string Details { get; set; } = string.Empty;
}