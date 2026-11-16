using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public class Customer
{
    public int Id { get; set; }

    [Required]
    [StringLength(30, MinimumLength = 2)]
    [RegularExpression(@"^[\p{L}\p{N}][\p{L}\p{N} .'-]*$",
        ErrorMessage = "Use 2-30 letters, numbers, spaces, periods, apostrophes, or hyphens.")]
    [Display(Name = "Customer Name")]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
