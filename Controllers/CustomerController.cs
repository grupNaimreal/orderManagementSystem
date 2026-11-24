// Controllers/CustomerController.cs
// Manages customer table/walk-in sessions for both Staff/Owner POS users
// and anonymous mobile customers.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

[Authorize(Roles = "Owner,Employee")]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerSessionService _customerSessionService;
    private readonly AuditLogService _auditLog;

    public CustomersController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService,
        AuditLogService auditLog)
    {
        _context = context;
        _customerSessionService = customerSessionService;
        _auditLog = auditLog;
    }

    private bool IsStaff => User.IsInRole("Owner") || User.IsInRole("Employee");

    // STAFF / OWNER: View all customer sessions sorted newest-first
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await _customerSessionService.ExpireInactiveSessionsAsync();
        var customers = await _context.Customers
            .AsNoTracking()
            .OrderByDescending(c => c.LastActivityUtc)
            .ThenByDescending(c => c.Id)
            .ToListAsync();
        return View(customers);
    }

    // STAFF / OWNER: Live search customer sessions
    [HttpGet]
    public async Task<IActionResult> Search(string term = "")
    {
        await _customerSessionService.ExpireInactiveSessionsAsync();
        var query = _context.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim();
            query = query.Where(c => c.DisplayName.Contains(term));
        }

        var customers = await query
            .OrderByDescending(c => c.LastActivityUtc)
            .ThenByDescending(c => c.Id)
            .ToListAsync();
        return PartialView("_CustomerRows", customers);
    }

    // STAFF / OWNER: Render form to create a walk-in or table session
    [HttpGet]
    public IActionResult Create()
    {
        return View(new Customer());
    }

    // CUSTOMER (anonymous) or STAFF / OWNER: Create a new customer session
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("DisplayName")] Customer customer,
        string? tableLabel = null,
        string? returnTo = null)
    {
        var isHomeSource = string.Equals(returnTo, "home", StringComparison.OrdinalIgnoreCase);
        var cleanName = (customer.DisplayName ?? string.Empty).Trim();
        var cleanTable = (tableLabel ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(cleanTable)
            && !cleanName.StartsWith(cleanTable, StringComparison.OrdinalIgnoreCase))
        {
            var combined = $"{cleanTable} - {cleanName}";
            customer.DisplayName = combined.Length > 30 ? combined[..30].Trim() : combined;
        }
        else
        {
            customer.DisplayName = cleanName;
        }

        ModelState.Clear();
        if (!TryValidateModel(customer))
        {
            if (isHomeSource)
            {
                var firstError = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Please enter a valid name (2–30 letters or numbers).";

                TempData["SessionError"] = firstError;
                TempData["EnteredName"] = cleanName;
                TempData["EnteredTable"] = cleanTable;
                return RedirectToAction("Index", "Home", new { table = cleanTable });
            }

            return View(customer);
        }

        try
        {
            await _customerSessionService.BeginSessionAsync(customer.DisplayName);

            if (IsStaff)
            {
                var createdId = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.DisplayName == customer.DisplayName && c.IsActive)
                    .OrderByDescending(c => c.Id)
                    .Select(c => (int?)c.Id)
                    .FirstOrDefaultAsync();

                await _auditLog.LogAsync(
                    "CustomerSessionCreated",
                    "Customer",
                    createdId?.ToString(),
                    $"Staff created walk-in customer session \"{customer.DisplayName}\".");

                return RedirectToAction("CreateForCustomer", "Orders", new { customerId = createdId });
            }

            return RedirectToAction("Index", "Products");
        }
        catch (InvalidOperationException ex)
        {
            if (isHomeSource)
            {
                TempData["SessionError"] = ex.Message;
                TempData["EnteredName"] = cleanName;
                TempData["EnteredTable"] = cleanTable;
                return RedirectToAction("Index", "Home", new { table = cleanTable });
            }

            ModelState.AddModelError(nameof(customer.DisplayName), ex.Message);
        }
        catch (DbUpdateException)
        {
            const string duplicateMsg = "That display name is already in use. Choose another name.";
            if (isHomeSource)
            {
                TempData["SessionError"] = duplicateMsg;
                TempData["EnteredName"] = cleanName;
                TempData["EnteredTable"] = cleanTable;
                return RedirectToAction("Index", "Home", new { table = cleanTable });
            }

            ModelState.AddModelError(nameof(customer.DisplayName), duplicateMsg);
        }

        return View(customer);
    }

    // STAFF / OWNER: View session details
    [HttpGet]
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);

        if (customer == null)
        {
            return NotFound();
        }

        return View(customer);
    }

    // STAFF / OWNER: Render edit form for a customer session
    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var customer = await _context.Customers.FindAsync(id);
        if (customer == null)
        {
            return NotFound();
        }

        return View(customer);
    }

    // STAFF / OWNER: Save updates to a customer session
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,DisplayName,IsActive")] Customer customer)
    {
        if (id != customer.Id)
        {
            return NotFound();
        }

        customer.DisplayName = (customer.DisplayName ?? string.Empty).Trim();

        ModelState.Clear();
        if (!TryValidateModel(customer))
        {
            return View(customer);
        }

        var existing = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (existing == null)
        {
            return NotFound();
        }

        existing.DisplayName = customer.DisplayName;
        existing.IsActive = customer.IsActive;
        if (customer.IsActive)
        {
            existing.LastActivityUtc = DateTime.UtcNow;
        }

        try
        {
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(
                "CustomerSessionUpdated",
                "Customer",
                existing.Id.ToString(),
                $"Updated customer session #{existing.Id} to \"{existing.DisplayName}\" (Active: {existing.IsActive}).");

            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(customer.DisplayName), "That display name is already in use.");
            return View(customer);
        }
    }

    // STAFF / OWNER: Confirm ending a customer session
    [HttpGet]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);

        if (customer == null)
        {
            return NotFound();
        }

        return View(customer);
    }

    // STAFF / OWNER: End/deactivate a customer session
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer != null)
        {
            customer.IsActive = false;
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(
                "CustomerSessionEndedByStaff",
                "Customer",
                customer.Id.ToString(),
                $"Staff ended customer session \"{customer.DisplayName}\".");
        }

        return RedirectToAction(nameof(Index));
    }

    // CUSTOMER: Update own display name or Dine-In / Take-Out preference mid-session
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMySession(
        string diningType = "DineIn",
        string? tableNumber = null,
        string displayName = "")
    {
        var currentCustomer = await _customerSessionService.GetCurrentCustomerAsync();
        if (currentCustomer == null)
        {
            TempData["SessionNotice"] = "Your session expired. Start a new session below.";
            return RedirectToAction("Index", "Home");
        }

        var cleanName = (displayName ?? string.Empty).Trim();
        if (cleanName.Length < 2 || cleanName.Length > 30)
        {
            TempData["SessionError"] = "Please enter a valid name (2–30 characters).";
            return RedirectToAction("Index", "Home");
        }

        string prefix;
        if (string.Equals(diningType, "TakeOut", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "Take-Out";
        }
        else
        {
            var rawTable = (tableNumber ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawTable))
            {
                prefix = "Dine-In";
            }
            else if (rawTable.StartsWith("Table", StringComparison.OrdinalIgnoreCase)
                     || rawTable.StartsWith("Take", StringComparison.OrdinalIgnoreCase))
            {
                prefix = rawTable;
            }
            else
            {
                prefix = $"Table {rawTable}";
            }
        }

        var combined = $"{prefix} - {cleanName}";
        var updatedDisplayName = combined.Length > 30 ? combined[..30].Trim() : combined;

        var activeCutoff = DateTime.UtcNow.AddMinutes(-10);
        var nameInUse = await _context.Customers.AnyAsync(c =>
            c.Id != currentCustomer.Id
            && c.IsActive
            && c.LastActivityUtc >= activeCutoff
            && c.DisplayName == updatedDisplayName);

        if (nameInUse)
        {
            TempData["SessionError"] = "That name/table label is already active. Please add an initial or adjust your name.";
            return RedirectToAction("Index", "Home");
        }

        var trackedCustomer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == currentCustomer.Id);

        if (trackedCustomer == null)
        {
            TempData["SessionNotice"] = "Your session ended. Please start a new session below.";
            return RedirectToAction("Index", "Home");
        }

        trackedCustomer.DisplayName = updatedDisplayName;
        trackedCustomer.LastActivityUtc = DateTime.UtcNow;

        // Keep any open (Pending/Preparing) orders aligned with the updated table or take-out label
        var openOrders = await _context.Orders
            .Where(o => o.CustomerId == trackedCustomer.Id
                     && (o.Status == OrderStatuses.Pending || o.Status == OrderStatuses.Preparing))
            .ToListAsync();

        foreach (var order in openOrders)
        {
            order.CustomerName = updatedDisplayName;
        }

        await _context.SaveChangesAsync();

        TempData["SessionNotice"] = $"Session updated to \"{updatedDisplayName}\".";
        return RedirectToAction("Index", "Home");
    }

    // CUSTOMER: End own active session
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndMySession()
    {
        await _customerSessionService.EndSessionAsync();

        return RedirectToAction("Index", "Home");
    }
}