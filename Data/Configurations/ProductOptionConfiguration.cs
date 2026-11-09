using Microsoft.EntityFrameworkCore; 
using Microsoft.EntityFrameworkCore.Metadata.Builders; 
using orderManagementSystem.Models; 
  
namespace orderManagementSystem.Data.Configurations; 
  
public class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption> 
{ 
    public void Configure(EntityTypeBuilder<ProductOption> builder) 
    { 
        builder.Property(o => o.Name).IsRequired().HasMaxLength(80); 
        builder.Property(o => o.OptionType).IsRequired().HasMaxLength(10); 
        builder.Property(o => o.PriceAdjustment).HasPrecision(10, 2); 
        builder.HasIndex(o => new { o.ProductId, o.Name, o.OptionType }).IsUnique(); 
  
        builder.HasOne(o => o.Product) 
            .WithMany(p => p.Options) 
            .HasForeignKey(o => o.ProductId) 
            .OnDelete(DeleteBehavior.Cascade); 
    } 
} 