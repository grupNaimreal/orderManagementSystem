using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data;

public partial class ApplicationDbContext
{
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemOption> OrderItemOptions => Set<OrderItemOption>();
}