using System.ComponentModel.DataAnnotations;

namespace orderManagementSystem.Models;

public class StaffCreateViewModel
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Owner|Employee)$", ErrorMessage = "Choose Owner or Employee.")]
    public string Role { get; set; } = "Employee";
}

public class StaffEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Owner|Employee)$", ErrorMessage = "Choose Owner or Employee.")]
    public string Role { get; set; } = "Employee";

    public bool IsActive { get; set; }
}

public class StaffPasswordViewModel
{
    public int Id { get; set; }

    [Required, MinLength(8)]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;
}

public class StaffRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
