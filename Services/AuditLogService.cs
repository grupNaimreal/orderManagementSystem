// Services/AuditLogService.cs
// Safe helper for recording audit trail events from controllers.
// Wraps persistence in try/catch so missing migrations or transient DB locks never block counter operations.

using System.Security.Claims;
using orderManagementSystem.Data;
using orderManagementSystem.Models;

namespace orderManagementSystem.Services;

public class AuditLogService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogService(
        ApplicationDbContext context,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(
        string actionType,
        string entityName,
        string? entityId,
        string details)
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var actorName = user?.Identity?.IsAuthenticated == true
                ? (user.Identity?.Name ?? "Authenticated User")
                : "Guest / Customer";

            var actorRole = "Customer";
            if (user?.IsInRole("Owner") == true)
            {
                actorRole = "Owner";
            }
            else if (user?.IsInRole("Employee") == true)
            {
                actorRole = "Employee";
            }
            else if (user?.Identity?.IsAuthenticated != true)
            {
                actorRole = "Customer";
            }

            var entry = new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                ActorName = actorName.Length > 256 ? actorName[..256] : actorName,
                ActorRole = actorRole,
                ActionType = actionType.Length > 60 ? actionType[..60] : actionType,
                EntityName = entityName.Length > 60 ? entityName[..60] : entityName,
                EntityId = entityId?.Length > 60 ? entityId[..60] : entityId,
                Details = details.Length > 500 ? details[..500] : details
            };

            _context.AuditLogs.Add(entry);
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Keep primary POS workflows resilient if AuditLogs migration is pending.
        }
    }
}