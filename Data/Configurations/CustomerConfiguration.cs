using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.DisplayName)
            .IsRequired()
            .HasMaxLength(30)
            .UseCollation("NOCASE");

        // The same display name may be reused after the previous session ends.
        // Only active customers must have unique names.
        builder.HasIndex(c => c.DisplayName)
            .IsUnique()
            .HasFilter("\"IsActive\" = 1");

        builder.HasIndex(c => c.LastActivityUtc);
    }
}
