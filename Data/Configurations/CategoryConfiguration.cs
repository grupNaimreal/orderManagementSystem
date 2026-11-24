using Microsoft.EntityFrameworkCore; 
using Microsoft.EntityFrameworkCore.Metadata.Builders; 
using orderManagementSystem.Models; 
  
namespace orderManagementSystem.Data.Configurations; 
  
public class CategoryConfiguration : IEntityTypeConfiguration<Category> 
{ 
    public void Configure(EntityTypeBuilder<Category> builder) 
    { 
        builder.Property(c => c.Name) 
            .IsRequired() 
            .HasMaxLength(50) 
            ; 
  
        builder.HasIndex(c => c.Name).IsUnique(); 
    } 
} 