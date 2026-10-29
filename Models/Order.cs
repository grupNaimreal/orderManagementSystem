using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public class Order
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Customer Name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Order Date")]
    public DateTime OrderDate { get; set; } = DateTime.Now;

    [Required]
    [Range(0.01, 1000000)]
    [Display(Name = "Total Amount")]
    public decimal TotalAmount { get; set; }

    [Required]
    public string Status { get; set; } = "Pending";
}