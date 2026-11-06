using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;

namespace orderManagementSystem.Controllers;

[Authorize(Roles = "Owner")]
public class EmployeesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public EmployeesController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private async Task<List<StaffRowViewModel>> GetRowsAsync()
    {
        var employees = await _context.Employees
            .AsNoTracking()
            .Include(e => e.IdentityUser)
            .Where(e => e.IdentityUser != null)
            .OrderBy(e => e.Name)
            .ToListAsync();

        var rows = new List<StaffRowViewModel>();
        foreach (var employee in employees)
        {
            var user = employee.IdentityUser!;
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new StaffRowViewModel
            {
                Id = employee.Id,
                Name = employee.Name,
                Email = user.Email ?? user.UserName ?? "",
                Role = string.Join(", ", roles),
                IsActive = employee.IsActive
            });
        }
        return rows;
    }

    public async Task<IActionResult> Index(string? term)
    {
        var rows = await GetRowsAsync();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var value = term.Trim();
            rows = rows.Where(e => e.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                || e.Email.Contains(value, StringComparison.OrdinalIgnoreCase)
                || e.Role.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? term)
    {
        var rows = await GetRowsAsync();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var value = term.Trim();
            rows = rows.Where(e => e.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                || e.Email.Contains(value, StringComparison.OrdinalIgnoreCase)
                || e.Role.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return PartialView("_EmployeeRows", rows);
    }

    [HttpGet]
    public IActionResult Create() => View(new StaffCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffCreateViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (await _userManager.FindByEmailAsync(model.Email.Trim()) != null)
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            return View(model);
        }

        var user = new IdentityUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true
        };
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            foreach (var error in roleResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        try
        {
            _context.Employees.Add(new Employees
            {
                Name = model.Name.Trim(),
                IdentityUserId = user.Id,
                IsActive = true
            });
            await _context.SaveChangesAsync();
        }
        catch
        {
            await _userManager.DeleteAsync(user);
            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var row = (await GetRowsAsync()).FirstOrDefault(e => e.Id == id);
        return row == null ? NotFound() : View(row);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var employee = await _context.Employees.Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (employee?.IdentityUser == null) return NotFound();
        var roles = await _userManager.GetRolesAsync(employee.IdentityUser);
        return View(new StaffEditViewModel
        {
            Id = employee.Id,
            Name = employee.Name,
            Email = employee.IdentityUser.Email ?? "",
            Role = roles.FirstOrDefault() ?? "Employee",
            IsActive = employee.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(StaffEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var employee = await _context.Employees.Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.Id == model.Id);
        if (employee?.IdentityUser == null) return NotFound();
        var user = employee.IdentityUser;
        var email = model.Email.Trim();
        var duplicate = await _userManager.FindByEmailAsync(email);
        if (duplicate != null && duplicate.Id != user.Id)
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already used by another account.");
            return View(model);
        }

        employee.Name = model.Name.Trim();
        var emailResult = await _userManager.SetEmailAsync(user, email);
        var nameResult = await _userManager.SetUserNameAsync(user, email);
        if (!emailResult.Succeeded || !nameResult.Succeeded)
        {
            foreach (var error in emailResult.Errors.Concat(nameResult.Errors))
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
        var addResult = await _userManager.AddToRoleAsync(user, model.Role);
        if (!removeResult.Succeeded || !addResult.Succeeded)
        {
            foreach (var error in removeResult.Errors.Concat(addResult.Errors))
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        employee.IsActive = model.IsActive;
        var lockoutResult = await _userManager.SetLockoutEnabledAsync(user, true);
        var lockoutEndResult = await _userManager.SetLockoutEndDateAsync(
            user, model.IsActive ? DateTimeOffset.UtcNow : DateTimeOffset.MaxValue);
        if (!lockoutResult.Succeeded || !lockoutEndResult.Succeeded)
        {
            foreach (var error in lockoutResult.Errors.Concat(lockoutEndResult.Errors))
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var exists = await _context.Employees.AnyAsync(e => e.Id == id && e.IdentityUserId != null);
        return exists ? View(new StaffPasswordViewModel { Id = id }) : NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(StaffPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var employee = await _context.Employees.Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.Id == model.Id);
        if (employee?.IdentityUser == null) return NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(employee.IdentityUser);
        var result = await _userManager.ResetPasswordAsync(employee.IdentityUser, token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(nameof(model.NewPassword), error.Description);
            return View(model);
        }
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var employee = await _context.Employees.Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (employee == null) return NotFound();

        employee.IsActive = false;
        if (employee.IdentityUser != null)
        {
            await _userManager.SetLockoutEnabledAsync(employee.IdentityUser, true);
            await _userManager.SetLockoutEndDateAsync(employee.IdentityUser, DateTimeOffset.MaxValue);
        }
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
