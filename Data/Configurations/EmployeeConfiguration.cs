using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employees>
{
    public void Configure(EntityTypeBuilder<Employees> builder)
    {
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.IdentityUserId).IsUnique();
        builder.HasOne(e => e.IdentityUser)
            .WithOne()
            .HasForeignKey<Employees>(e => e.IdentityUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
