using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;

namespace orderManagementSystem.Controllers;

[Authorize(Roles = "Owner,Employee")]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerSessionService _customerSessionService;

    public CustomersController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService)
    {
        _context = context;
        _customerSessionService = customerSessionService;
    }

    private Task<bool> ActiveNameExistsAsync(string name, int? exceptId = null)
    {
        var normalized = name.Trim().ToLower();
        return _context.Customers.AnyAsync(c =>
            c.IsActive && c.Id != exceptId &&
            c.DisplayName.ToLower() == normalized);
    }

    public async Task<IActionResult> Index()
    {
        await _customerSessionService.ExpireInactiveSessionsAsync();
        var customers = await _context.Customers
            .AsNoTracking()
            .OrderBy(c => c.DisplayName)
            .ToListAsync();
        return View(customers);
    }

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

        var customers = await query.OrderBy(c => c.DisplayName).ToListAsync();
        return PartialView("_CustomerRows", customers);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Create()
    {
        await _customerSessionService.ExpireInactiveSessionsAsync();
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("DisplayName")] Customer customer)
    {
        customer.DisplayName = (customer.DisplayName ?? string.Empty).Trim();

        if (!ModelState.IsValid)
            return View(customer);

        try
        {
            await _customerSessionService.BeginSessionAsync(customer.DisplayName);

            return RedirectToAction("MySession", "Customers");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(customer.DisplayName), ex.Message);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(customer.DisplayName),
                "That display name is already in use. Choose another name.");
        }

        return View(customer);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        await _customerSessionService.ExpireInactiveSessionsAsync();

        var customer = await _context.Customers
            .AsNoTracking()
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id);

        return customer == null ? NotFound() : View(customer);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        await _customerSessionService.ExpireInactiveSessionsAsync();
        var customer = await _context.Customers.FindAsync(id);
        return customer == null ? NotFound() : View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,DisplayName")] Customer submitted)
    {
        if (id != submitted.Id) return NotFound();
        await _customerSessionService.ExpireInactiveSessionsAsync();
        submitted.DisplayName = (submitted.DisplayName ?? string.Empty).Trim();

        if (await ActiveNameExistsAsync(submitted.DisplayName, id))
            ModelState.AddModelError(nameof(submitted.DisplayName),
                "That display name is already in use by an active customer.");

        if (!ModelState.IsValid) return View(submitted);

        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        customer.DisplayName = submitted.DisplayName;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(submitted.DisplayName),
                "That display name is already in use. Choose another name.");
            return View(submitted);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id);
        return customer == null ? NotFound() : View(customer);
    }

    // End the session; do not delete the customer row or its order history.
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();

        customer.IsActive = false;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> MySession()
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction(nameof(Create));

        return View(customer);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndMySession()
    {
        await _customerSessionService.EndSessionAsync();

        return RedirectToAction(nameof(Create));
    }
}
