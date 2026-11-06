using Microsoft.AspNetCore.Identity;

namespace orderManagementSystem.Security;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var roleName in new[] { "Owner", "Employee" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!roleResult.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }
        }

        // Supply these values through user-secrets. Never place the initial password in appsettings.json.
        var email = configuration["InitialOwner:Email"];
        var password = configuration["InitialOwner:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var owner = await userManager.FindByEmailAsync(email);

        if (owner == null)
        {
            owner = new IdentityUser
            {
                UserName = email.Trim(),
                Email = email.Trim(),
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(owner, password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        if (!await userManager.IsInRoleAsync(owner, "Owner"))
        {
            var addRoleResult = await userManager.AddToRoleAsync(owner, "Owner");
            if (!addRoleResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", addRoleResult.Errors.Select(e => e.Description)));
        }
    }
}
