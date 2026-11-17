using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data.Configurations;

public class OrderItemOptionConfiguration
    : IEntityTypeConfiguration<OrderItemOption>
{
    public void Configure(EntityTypeBuilder<OrderItemOption> builder)
    {
        builder.Property(o => o.NameSnapshot)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(o => o.OptionType)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(o => o.PriceAdjustment)
            .HasPrecision(10, 2);

        builder.HasOne(o => o.OrderItem)
            .WithMany(i => i.SelectedOptions)
            .HasForeignKey(o => o.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => o.OrderItemId);
    }
}