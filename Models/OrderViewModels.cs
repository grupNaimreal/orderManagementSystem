using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public class OrderCreateViewModel
{
    // Used by staff when creating an order for a customer.
    public int? CustomerId { get; set; }

    [Required]
    [StringLength(20)]
    public string PaymentMethod { get; set; } = OrderPaymentMethods.Cash;

    // The customer can submit multiple products in one order.
    public List<OrderItemInputViewModel> Items { get; set; } =
        new() { new OrderItemInputViewModel() };

    // Display-only data populated by the controller.
    public List<OrderCustomerChoiceViewModel> Customers { get; set; } =
        new();

    public List<OrderProductChoiceViewModel> Products { get; set; } =
        new();

    // Set by the controller to control which form fields are displayed.
    public bool IsStaffOrder { get; set; }
}

public class OrderItemInputViewModel
{
    public int ProductId { get; set; }

    [Range(1, 99)]
    public int Quantity { get; set; } = 1;

    // A product can have one selected variant.
    public int? VariantOptionId { get; set; }

    // A product can have multiple selected add-ons.
    public List<int> AddOnOptionIds { get; set; } = new();
}

public class OrderCustomerChoiceViewModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class OrderProductChoiceViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }

    public List<OrderOptionChoiceViewModel> Variants { get; set; } =
        new();

    public List<OrderOptionChoiceViewModel> AddOns { get; set; } =
        new();
}

public class OrderOptionChoiceViewModel
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
}
