using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public class OrderItemOption
{
    public int Id { get; set; }

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    [Required]
    [StringLength(80)]
    public string NameSnapshot { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string OptionType { get; set; } = string.Empty;

    public decimal PriceAdjustment { get; set; }
}