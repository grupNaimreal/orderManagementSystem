using Microsoft.EntityFrameworkCore; 
using Microsoft.EntityFrameworkCore.Metadata.Builders; 
using orderManagementSystem.Models; 
  
namespace orderManagementSystem.Data.Configurations; 
  
public class ProductConfiguration : IEntityTypeConfiguration<Product> 
{ 
    public void Configure(EntityTypeBuilder<Product> builder) 
    { 
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100).UseCollation("NOCASE"); 
        builder.Property(p => p.Description).HasMaxLength(1000); 
        builder.Property(p => p.ImageUrl).HasMaxLength(500); 
        builder.Property(p => p.Price).HasPrecision(10, 2); 
  
        builder.HasIndex(p => p.Name).IsUnique().HasFilter("\"IsActive\" = 1"); 
        builder.HasIndex(p => p.CategoryId); 
        builder.HasIndex(p => new { p.IsActive, p.IsAvailable }); 
  
        builder.HasOne(p => p.Category) 
            .WithMany(c => c.Products) 
            .HasForeignKey(p => p.CategoryId) 
            .OnDelete(DeleteBehavior.Restrict); 
    } 
} 