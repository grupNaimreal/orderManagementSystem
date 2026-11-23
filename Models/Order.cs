using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public static class OrderStatuses
{
    public const string Pending = "Pending";
    public const string Preparing = "Preparing";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class OrderPaymentMethods
{
    public const string Cash = "Cash";
}

public static class OrderPaymentStatuses
{
    public const string NotTracked = "NotTracked";
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Expired = "Expired";
}

public class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [Required]
    [StringLength(30)]
    [Display(Name = "Customer Name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Order Date")]
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    [Range(typeof(decimal), "0.00", "1000000.00")]
    [Display(Name = "Total Amount")]
    public decimal TotalAmount { get; set; }

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = OrderStatuses.Pending;

    [Required]
    [StringLength(20)]
    public string PaymentMethod { get; set; } = OrderPaymentMethods.Cash;

    [Required]
    [StringLength(20)]
    public string PaymentStatus { get; set; } = OrderPaymentStatuses.NotTracked;

    [StringLength(100)]
    public string? PayMongoCheckoutSessionId { get; set; }

    [StringLength(100)]
    public string? PayMongoPaymentId { get; set; }

    [StringLength(1000)]
    public string? PayMongoCheckoutUrl { get; set; }

    public DateTime? PaymentPaidAtUtc { get; set; }

    public ICollection<OrderItem> Items { get; set; } =
        new List<OrderItem>();
}