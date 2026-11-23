// Data/AuditLogDbSet.cs
// Partial ApplicationDbContext extension exposing the AuditLogs table,
// matching the project's existing CustomerDbSet / OrderDbSet / ProductDbSet pattern.

using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data;

public partial class ApplicationDbContext
{
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
}