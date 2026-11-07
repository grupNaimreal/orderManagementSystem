using System.ComponentModel.DataAnnotations; 
  
namespace orderManagementSystem.Models; 
  
public class Category 
{ 
    public int Id { get; set; } 
  
    [Required] 
    [StringLength(50, MinimumLength = 2)] 
    public string Name { get; set; } = string.Empty; 
  
    public ICollection<Product> Products { get; set; } = new List<Product>();
}