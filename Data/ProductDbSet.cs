using Microsoft.EntityFrameworkCore; 
using orderManagementSystem.Models; 
  
namespace orderManagementSystem.Data; 
  
public partial class ApplicationDbContext 
{ 
    public DbSet<Category> Categories => Set<Category>(); 
    public DbSet<Product> Products => Set<Product>(); 
    public DbSet<ProductOption> ProductOptions => Set<ProductOption>(); 
} 