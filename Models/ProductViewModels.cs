using System.ComponentModel.DataAnnotations; 
  
namespace orderManagementSystem.Models; 
  
public class ProductFormViewModel 
{ 
    public int Id { get; set; } 
  
    [Required, StringLength(100, MinimumLength = 2)] 
    public string Name { get; set; } = string.Empty; 
  
    [StringLength(1000)] 
    public string? Description { get; set; } 
  
    [Url, StringLength(500)] 
    public string? ImageUrl { get; set; } 
  
    [Required, StringLength(50, MinimumLength = 2)] 
    [Display(Name = "Category")] 
    public string CategoryName { get; set; } = string.Empty; 
  
    [Range(typeof(decimal), "0.01", "1000000.00")] 
    public decimal Price { get; set; } 
  
    public bool IsAvailable { get; set; } = true; 
} 
public class ProductOptionFormViewModel 
{ 
    public int Id { get; set; } 

    public int ProductId { get; set; } 
  
    [Required, StringLength(80, MinimumLength = 1)] 
    public string Name { get; set; } = string.Empty; 
  
    [Required] 
    [RegularExpression("^(Variant|AddOn)$")] 
    public string OptionType { get; set; } = "AddOn"; 
  
    [Range(typeof(decimal), "-1000000.00", "1000000.00")] 
    public decimal PriceAdjustment { get; set; } 
  
    public bool IsAvailable { get; set; } = true; 
}