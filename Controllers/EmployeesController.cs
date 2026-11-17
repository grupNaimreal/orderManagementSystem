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
    private async Task<bool> HasOtherActiveOwnerAsync(int excludedEmployeeId)
    {
        var otherActiveEmployees = await _context.Employees
            .AsNoTracking()
            .Include(e => e.IdentityUser)
            .Where(e => e.IsActive
                && e.Id != excludedEmployeeId
                && e.IdentityUserId != null)
            .ToListAsync();

        foreach (var otherEmployee in otherActiveEmployees)
        {
            if (otherEmployee.IdentityUser == null)
                continue;

            var roles = await _userManager.GetRolesAsync(otherEmployee.IdentityUser);

            if (roles.Contains("Owner"))
                return true;
        }

        return false;
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
                EmployeeCode = employee.EmployeeCode,
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
                IsActive = true,
                EmployeeCode = string.IsNullOrWhiteSpace(model.EmployeeCode)
                    ? null
                    : model.EmployeeCode.Trim()
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
            EmployeeCode = employee.EmployeeCode,
            Email = employee.IdentityUser.Email ?? "",
            Role = roles.FirstOrDefault() ?? "Employee",
            IsActive = employee.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(StaffEditViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var employee = await _context.Employees
            .Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.Id == model.Id);

        if (employee?.IdentityUser == null)
            return NotFound();

        var user = employee.IdentityUser;
        var email = model.Email.Trim();

        var duplicate = await _userManager.FindByEmailAsync(email);

        if (duplicate != null && duplicate.Id != user.Id)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "That email is already used by another account.");

            return View(model);
        }

        var currentRoles = await _userManager.GetRolesAsync(user);

        var isCurrentlyActiveOwner =
            employee.IsActive && currentRoles.Contains("Owner");

        var willRemainActiveOwner =
            model.IsActive && model.Role == "Owner";

        if (isCurrentlyActiveOwner
            && !willRemainActiveOwner
            && !await HasOtherActiveOwnerAsync(employee.Id))
        {
            ModelState.AddModelError(
                string.Empty,
                "You cannot demote or deactivate the last active Owner account.");

            return View(model);
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        async Task<IActionResult> RollbackWithErrors(
            IEnumerable<IdentityError> errors)
        {
            await transaction.RollbackAsync();

            foreach (var error in errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        var emailResult = await _userManager.SetEmailAsync(user, email);

        if (!emailResult.Succeeded)
            return await RollbackWithErrors(emailResult.Errors);

        var usernameResult = await _userManager.SetUserNameAsync(user, email);

        if (!usernameResult.Succeeded)
            return await RollbackWithErrors(usernameResult.Errors);

        var removeResult =
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

        if (!removeResult.Succeeded)
            return await RollbackWithErrors(removeResult.Errors);

        var addResult =
            await _userManager.AddToRoleAsync(user, model.Role);

        if (!addResult.Succeeded)
            return await RollbackWithErrors(addResult.Errors);

        var lockoutResult =
            await _userManager.SetLockoutEnabledAsync(user, true);

        if (!lockoutResult.Succeeded)
            return await RollbackWithErrors(lockoutResult.Errors);

        var lockoutEndResult =
            await _userManager.SetLockoutEndDateAsync(
                user,
                model.IsActive
                    ? DateTimeOffset.UtcNow
                    : DateTimeOffset.MaxValue);

        if (!lockoutEndResult.Succeeded)
            return await RollbackWithErrors(lockoutEndResult.Errors);

        var stampResult = await _userManager.UpdateSecurityStampAsync(user);

        if (!stampResult.Succeeded)
            return await RollbackWithErrors(stampResult.Errors);

        employee.Name = model.Name.Trim();

        employee.EmployeeCode =
            string.IsNullOrWhiteSpace(model.EmployeeCode)
                ? null
                : model.EmployeeCode.Trim();

        employee.IsActive = model.IsActive;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

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
        var employee = await _context.Employees
            .Include(e => e.IdentityUser)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee?.IdentityUser == null)
            return NotFound();

        var user = employee.IdentityUser;
        var roles = await _userManager.GetRolesAsync(user);

        if (employee.IsActive
            && roles.Contains("Owner")
            && !await HasOtherActiveOwnerAsync(employee.Id))
        {
            TempData["StaffError"] =
                "You cannot deactivate the last active Owner account.";

            return RedirectToAction(nameof(Index));
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var lockoutResult =
            await _userManager.SetLockoutEnabledAsync(user, true);

        var lockoutEndResult =
            await _userManager.SetLockoutEndDateAsync(
                user, DateTimeOffset.MaxValue);

        var stampResult =
            await _userManager.UpdateSecurityStampAsync(user);

        var errors = lockoutResult.Errors
            .Concat(lockoutEndResult.Errors)
            .Concat(stampResult.Errors)
            .ToList();

        if (errors.Count > 0)
        {
            await transaction.RollbackAsync();

            TempData["StaffError"] =
                string.Join(" ", errors.Select(e => e.Description));

            return RedirectToAction(nameof(Index));
        }

        employee.IsActive = false;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return RedirectToAction(nameof(Index));
    }
}
