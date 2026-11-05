using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext(options)
{
    public DbSet<Order> Orders { get; set; }

public DbSet<orderManagementSystem.Models.Employees> Employees { get; set; } = default!;
}