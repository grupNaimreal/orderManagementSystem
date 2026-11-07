using System.ComponentModel.DataAnnotations; 
  
namespace orderManagementSystem.Models; 
  
public class ProductOption 
{ 
    public int Id { get; set; } 
  
    [Required] 
    public int ProductId { get; set; } 
    public Product? Product { get; set; } 
  
    [Required] 
    [StringLength(80, MinimumLength = 1)] 
    public string Name { get; set; } = string.Empty; 
  
    [Required] 
    [RegularExpression("^(Variant|AddOn)$", ErrorMessage = "Option type must be Variant or AddOn.")] 
    public string OptionType { get; set; } = "AddOn"; 
  
    [Range(typeof(decimal), "-1000000.00", "1000000.00")] 
    [Display(Name = "Price adjustment")] 
    public decimal PriceAdjustment { get; set; } 
  
    public bool IsAvailable { get; set; } = true;
}