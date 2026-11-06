using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace orderManagementSystem.Models;

public class Employees
{
    public int Id { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    // Nullable so existing legacy employee rows can be retained during migration.
    public string? IdentityUserId { get; set; }
    public IdentityUser? IdentityUser { get; set; }

    public bool IsActive { get; set; } = true;
}
