using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.CustomerName)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(o => o.TotalAmount)
            .HasPrecision(10, 2);

        builder.Property(o => o.PaymentMethod)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(OrderPaymentMethods.Cash);

        builder.Property(o => o.PaymentStatus)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(OrderPaymentStatuses.NotTracked);

        builder.Property(o => o.PayMongoCheckoutSessionId)
            .HasMaxLength(100);

        builder.Property(o => o.PayMongoPaymentId)
            .HasMaxLength(100);

        builder.Property(o => o.PayMongoCheckoutUrl)
            .HasMaxLength(1000);

        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.CustomerId);
        builder.HasIndex(o => o.OrderDate);
    }
}