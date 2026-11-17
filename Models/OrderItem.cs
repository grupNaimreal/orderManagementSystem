using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace orderManagementSystem.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string ProductNameSnapshot { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.00", "1000000.00")]
    public decimal BaseUnitPrice { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    public ICollection<OrderItemOption> SelectedOptions { get; set; } =
        new List<OrderItemOption>();

    [NotMapped]
    public decimal UnitPrice =>
        BaseUnitPrice + SelectedOptions.Sum(o => o.PriceAdjustment);

    [NotMapped]
    public decimal LineTotal => UnitPrice * Quantity;
}