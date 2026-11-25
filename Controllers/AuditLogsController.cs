// Controllers/AuditLogsController.cs
// Owner-only Audit Trail viewer for QRDER (Bean Crate Cafe).
// Safely handles cases where the AuditLogs table migration has not been applied yet.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;

namespace orderManagementSystem.Controllers;

[Authorize(Roles = "Owner")]
public class AuditLogsController : Controller
{
    private readonly ApplicationDbContext _context;

    public AuditLogsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? actionFilter = null)
    {
        try
        {
            var query = _context.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(actionFilter))
            {
                query = query.Where(a => a.ActionType == actionFilter);
            }

            var logs = await query
                .OrderByDescending(a => a.TimestampUtc)
                .Take(250)
                .ToListAsync();

            ViewBag.ActionFilter = actionFilter ?? "";
            ViewBag.MigrationPending = false;
            return View(logs);
        }
        catch (SqliteException)
        {
            ViewBag.ActionFilter = actionFilter ?? "";
            ViewBag.MigrationPending = true;
            return View(new List<AuditLog>());
        }
    }
}