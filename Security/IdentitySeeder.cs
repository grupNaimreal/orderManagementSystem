// Security/IdentitySeeder.cs
// Seeds the Owner and Employee roles and ensures clean demo accounts exist
// (removing any stale Owner account that no longer matches InitialOwner:Email).

using Microsoft.AspNetCore.Identity;

namespace orderManagementSystem.Security;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        string[] roles = { "Owner", "Employee" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var ownerEmail = (configuration["InitialOwner:Email"] ?? "owner@beancrate.local").Trim();
        var ownerPassword = configuration["InitialOwner:Password"] ?? "BeanCrateOwner2026!";

        if (!string.IsNullOrWhiteSpace(ownerEmail) && !string.IsNullOrWhiteSpace(ownerPassword))
        {
            // Remove any leftover Owner accounts whose email no longer matches InitialOwner:Email
            var existingOwners = await userManager.GetUsersInRoleAsync("Owner");
            foreach (var staleOwner in existingOwners)
            {
                if (!string.Equals(staleOwner.Email, ownerEmail, StringComparison.OrdinalIgnoreCase))
                {
                    await userManager.DeleteAsync(staleOwner);
                }
            }

            var ownerUser = await userManager.FindByEmailAsync(ownerEmail);
            if (ownerUser == null)
            {
                ownerUser = new IdentityUser
                {
                    UserName = ownerEmail,
                    Email = ownerEmail,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(ownerUser, ownerPassword);
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(ownerUser, "Owner");
                }
            }
            else if (!await userManager.IsInRoleAsync(ownerUser, "Owner"))
            {
                await userManager.AddToRoleAsync(ownerUser, "Owner");
            }
        }

        // Seed a dedicated Employee (Staff) demo account if none exists yet
        var staffEmail = (configuration["InitialEmployee:Email"] ?? "staff@beancrate.local").Trim();
        var staffPassword = configuration["InitialEmployee:Password"] ?? "BeanCrateStaff2026!";

        if (!string.IsNullOrWhiteSpace(staffEmail) && !string.IsNullOrWhiteSpace(staffPassword))
        {
            var existingEmployees = await userManager.GetUsersInRoleAsync("Employee");
            if (existingEmployees.Count == 0)
            {
                var staffUser = await userManager.FindByEmailAsync(staffEmail);
                if (staffUser == null)
                {
                    staffUser = new IdentityUser
                    {
                        UserName = staffEmail,
                        Email = staffEmail,
                        EmailConfirmed = true
                    };

                    var staffResult = await userManager.CreateAsync(staffUser, staffPassword);
                    if (staffResult.Succeeded)
                    {
                        await userManager.AddToRoleAsync(staffUser, "Employee");
                    }
                }
                else if (!await userManager.IsInRoleAsync(staffUser, "Employee"))
                {
                    await userManager.AddToRoleAsync(staffUser, "Employee");
                }
            }
        }
    }
}