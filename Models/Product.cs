using System.ComponentModel.DataAnnotations; 
  
namespace orderManagementSystem.Models; 
  
public class Product 
{ 
    public int Id { get; set; } 
  
    [Required] 
    [StringLength(100, MinimumLength = 2)] 
    public string Name { get; set; } = string.Empty; 
  
    [StringLength(1000)] 
    public string? Description { get; set; } 
  
    [Url] 
    [StringLength(500)] 
    [Display(Name = "Image URL")] 
    public string? ImageUrl { get; set; } 
  
    [Required] 
    public int CategoryId { get; set; } 
    public Category? Category { get; set; } 
  
    [Range(typeof(decimal), "0.01", "1000000.00")] 
    public decimal Price { get; set; } 
  
    public bool IsAvailable { get; set; } = true; 
    public bool IsActive { get; set; } = true; 
  
    public ICollection<ProductOption> Options { get; set; } = new List<ProductOption>();
}