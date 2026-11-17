using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data;

public partial class ApplicationDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}
