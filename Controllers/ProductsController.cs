using Microsoft.AspNetCore.Authorization; 
using Microsoft.AspNetCore.Mvc; 
using Microsoft.EntityFrameworkCore; 
using orderManagementSystem.Data; 
using orderManagementSystem.Models; 
  
namespace orderManagementSystem.Controllers; 
  
public class ProductsController : Controller 
{ 
    private readonly ApplicationDbContext _context; 
  
    public ProductsController(ApplicationDbContext context) 
    { 
        _context = context; 
    } 
  
    private IQueryable<Product> ProductList() => _context.Products 
        .AsNoTracking() 
        .Include(p => p.Category) 
        .Where(p => p.IsActive); 
  
    public async Task<IActionResult> Index() 
    { 
        return View(await ProductList().OrderBy(p => p.Name).ToListAsync()); 
    } 
  
    [HttpGet] 
    public async Task<IActionResult> Search(string? term) 
    { 
        var query = ProductList(); 
        if (!string.IsNullOrWhiteSpace(term)) 

             { 
            var value = term.Trim(); 
            query = query.Where(p => p.Name.Contains(value) 
                || (p.Description != null && p.Description.Contains(value)) 
                || p.Category!.Name.Contains(value)); 
        } 
        return PartialView("_ProductRows", await query.OrderBy(p => p.Name).ToListAsync()); 
    } 
  
    public async Task<IActionResult> Details(int? id) 
    { 
        if (id == null) return NotFound(); 
        var product = await _context.Products.AsNoTracking() 
            .Include(p => p.Category) 
            .Include(p => p.Options) 
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive); 
        return product == null ? NotFound() : View(product); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpGet] 
    public IActionResult Create() => View(new ProductFormViewModel()); 
  
    private async Task<Category> FindOrCreateCategoryAsync(string categoryName) 
    { 
        var name = categoryName.Trim(); 
        var category = await _context.Categories 
            .FirstOrDefaultAsync(c => c.Name == name); 
        if (category != null) return category; 
  
        category = new Category { Name = name }; 
        _context.Categories.Add(category); 
        return category; 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpPost] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> Create(ProductFormViewModel model) 
    { 
        model.Name = model.Name?.Trim() ?? string.Empty; 
        model.CategoryName = model.CategoryName?.Trim() ?? string.Empty; 
        if (!ModelState.IsValid) return View(model); 
  
        var duplicate = await _context.Products.AnyAsync(p => 
            p.Name.ToLower() == model.Name.ToLower() && p.IsActive); 
        if (duplicate) 
        { 
            ModelState.AddModelError(nameof(model.Name), "An active product already uses that name."); 
            return View(model); 
        } 
  
        var category = await FindOrCreateCategoryAsync(model.CategoryName); 
        _context.Products.Add(new Product 
        { 
            Name = model.Name, 
            Description = model.Description?.Trim(), 
            ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl.Trim(), 
            Category = category, 
            Price = model.Price, 
            IsAvailable = model.IsAvailable, 
            IsActive = true 
        }); 
        try 
        { 
            await _context.SaveChangesAsync(); 
        } 
        catch (DbUpdateException) 
        { 
            ModelState.AddModelError(string.Empty, "The product or category could not be saved. Check for duplicates and try again."); 
            return View(model); 

   } 
        return RedirectToAction(nameof(Index)); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpGet] 
    public async Task<IActionResult> Edit(int? id) 
    { 
        if (id == null) return NotFound(); 
        var product = await _context.Products.Include(p => p.Category) 
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive); 
        if (product == null) return NotFound(); 
        return View(new ProductFormViewModel 
        { 
            Id = product.Id, 
            Name = product.Name, 
            Description = product.Description, 
            ImageUrl = product.ImageUrl, 
            CategoryName = product.Category!.Name, 
            Price = product.Price, 
            IsAvailable = product.IsAvailable 
        }); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpPost] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> Edit(ProductFormViewModel model) 
    { 
        model.Name = model.Name?.Trim() ?? string.Empty; 
        model.CategoryName = model.CategoryName?.Trim() ?? string.Empty; 
        if (!ModelState.IsValid) return View(model); 
  
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == model.Id && p.IsActive); 
        if (product == null) return NotFound(); 
  
        var duplicate = await _context.Products.AnyAsync(p => p.Id != model.Id && p.IsActive 
            && p.Name.ToLower() == model.Name.ToLower()); 
        if (duplicate) 
        { 
            ModelState.AddModelError(nameof(model.Name), "An active product already uses that name."); 
            return View(model); 
        } 
  
        var invalidVariant = await _context.ProductOptions.AnyAsync(o => 
            o.ProductId == product.Id && o.OptionType == "Variant" 
            && model.Price + o.PriceAdjustment < 0); 
        if (invalidVariant) 
        { 
            ModelState.AddModelError(nameof(model.Price), 
                "This price would make an existing variant's final price negative. Update the variant first."); 
            return View(model); 
        } 
  
        product.Name = model.Name; 
        product.Description = model.Description?.Trim(); 
        product.ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl.Trim(); 
        product.Category = await FindOrCreateCategoryAsync(model.CategoryName); 
        product.Price = model.Price; 
        product.IsAvailable = model.IsAvailable; 
        await _context.SaveChangesAsync(); 
        return RedirectToAction(nameof(Details), new { id = product.Id }); 
    } 
  
    // Employees may change availability, but cannot edit product data. 
    [Authorize(Roles = "Owner,Employee")] 
    [HttpPost] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> SetAvailability(int id, bool isAvailable) 
    { 
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id && p.IsActive); 
        if (product == null) return NotFound(); 

product.IsAvailable = isAvailable; 
        await _context.SaveChangesAsync(); 
        return RedirectToAction(nameof(Details), new { id }); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpGet] 
    public async Task<IActionResult> Delete(int? id) 
    { 
        if (id == null) return NotFound(); 
        var product = await _context.Products.Include(p => p.Category) 
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive); 
        return product == null ? NotFound() : View(product); 
    } 
  
    // Retire a product instead of removing a product that may already appear in order history. 
    [Authorize(Roles = "Owner")] 
    [HttpPost, ActionName("Delete")] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> DeleteConfirmed(int id) 
    { 
        var product = await _context.Products.FindAsync(id); 
        if (product == null) return NotFound(); 
        product.IsActive = false; 
        product.IsAvailable = false; 
        await _context.SaveChangesAsync(); 
        return RedirectToAction(nameof(Index)); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpPost] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> AddOption(ProductOptionFormViewModel model) 
    { 
        if (!ModelState.IsValid || (model.OptionType == "AddOn" && model.PriceAdjustment < 0)) 
        { 
            TempData["OptionError"] = "Check the option name, type, and price adjustment. Add-ons cannot have a negative price adjustment."; 
            return RedirectToAction(nameof(Details), new { id = model.ProductId }); 
        } 
  
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == model.ProductId && p.IsActive); 
        if (product == null) return NotFound(); 
        if (model.OptionType == "Variant" && product.Price + model.PriceAdjustment < 0) 
        { 
            TempData["OptionError"] = "A variant's final price cannot be negative."; 
            return RedirectToAction(nameof(Details), new { id = model.ProductId }); 
        } 
        _context.ProductOptions.Add(new ProductOption 
        { 
            ProductId = product.Id, 
            Name = model.Name.Trim(), 
            OptionType = model.OptionType, 
            PriceAdjustment = model.PriceAdjustment, 
            IsAvailable = model.IsAvailable 
        }); 
        try 
        { 
            await _context.SaveChangesAsync(); 
        } 
        catch (DbUpdateException) 
        { 
            TempData["OptionError"] = "That option already exists for this product and type."; 
        } 
        return RedirectToAction(nameof(Details), new { id = model.ProductId }); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpGet] 
    public async Task<IActionResult> EditOption(int? id) 
    { 
        if (id == null) return NotFound(); 

 var option = await _context.ProductOptions.FindAsync(id); 
        if (option == null) return NotFound(); 
        return View(new ProductOptionFormViewModel 
        { 
            Id = option.Id, 
            ProductId = option.ProductId, 
            Name = option.Name, 
            OptionType = option.OptionType, 
            PriceAdjustment = option.PriceAdjustment, 
            IsAvailable = option.IsAvailable 
        }); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpPost] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> EditOption(ProductOptionFormViewModel model) 
    { 
        if (!ModelState.IsValid || (model.OptionType == "AddOn" && model.PriceAdjustment < 0)) 
        { 
            ModelState.AddModelError(nameof(model.PriceAdjustment), "Add-ons cannot have a negative price adjustment."); 
            return View(model); 
        } 
        var option = await _context.ProductOptions.FindAsync(model.Id); 
        if (option == null) return NotFound(); 
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == option.ProductId && p.IsActive); 
        if (product == null) return NotFound(); 
        if (model.OptionType == "Variant" && product.Price + model.PriceAdjustment < 0) 
        { 
            ModelState.AddModelError(nameof(model.PriceAdjustment), 
                "A variant's final price cannot be negative."); 
            return View(model); 
        } 
        option.Name = model.Name.Trim(); 
        option.OptionType = model.OptionType; 
        option.PriceAdjustment = model.PriceAdjustment; 
        option.IsAvailable = model.IsAvailable; 
        await _context.SaveChangesAsync(); 
        return RedirectToAction(nameof(Details), new { id = option.ProductId }); 
    } 
  
    [Authorize(Roles = "Owner")] 
    [HttpPost] 
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> DeleteOption(int id) 
    { 
        var option = await _context.ProductOptions.FindAsync(id); 
        if (option == null) return NotFound(); 
        var productId = option.ProductId; 
        _context.ProductOptions.Remove(option); 
        await _context.SaveChangesAsync(); 
        return RedirectToAction(nameof(Details), new { id = productId }); 
    } 
} 
