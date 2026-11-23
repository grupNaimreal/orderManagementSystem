// Data/MenuSeeder.cs
// Seeds the complete 11-page Bean Crate Cafe menu (Categories, Products, Variants, and Add-ons)
// into ApplicationDbContext safely and idempotently on startup.

using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Models;

namespace orderManagementSystem.Data;

public static class MenuSeeder
{
    // Exact 11-page physical menu category sequence for Bean Crate Cafe
    public static readonly IReadOnlyList<string> OrderedCategoryNames = new[]
    {
        "Coffee",                     // Page 1
        "Non-Coffee",                 // Page 1
        "Pasta",                      // Page 2
        "Sandwiches",                 // Page 2
        "Snacks",                     // Page 2
        "Juices & Lemonades",         // Page 3
        "House Blend",                // Page 3
        "Lemon Soda",                 // Page 3
        "Thai Series & Floats",       // Page 3
        "Parfait Series",             // Page 4
        "Specialty Drinks",           // Page 5
        "Desserts & Banana Pudding",  // Page 6
        "Buldak Series",              // Page 7
        "Matcha Foam Series",         // Page 8
        "Ube Series",                 // Page 9
        "Pistachio Series",           // Page 10
        "Rice Bowls"                  // Page 11
    };

    // Exact product order across all 11 menu pages so split/added items stay in menu position
    public static readonly IReadOnlyList<string> OrderedProductNames = new[]
    {
        // Coffee
        "Americano", "Latte", "Cappuccino", "Spanish Latte", "Caramel Macchiato",
        "Mocha Latte", "Brown Sugar Latte", "Dirty Matcha", "Honey Crumble Latte",
        "Peppermint Mocha", "White Peppermint Mocha", "Nutella Latte", "Biscoff Latte",
        "Tiramisu Latte", "Spanish Dalgona Latte", "Bean Crate Latte",
        // Non-Coffee
        "Choco", "Toasted Marshmallow Hot Choco", "Matcha Latte", "Ube Milk",
        "Strawberry Milk", "Mango Milk", "Milo Thicc Dinosaur", "Strawberry Choco",
        "Rose Milk", "Matcha Biscoff", "Strawberry Matcha", "Matcha Oreo", "Matcha Lemonade",
        // Pasta
        "Mac & Cheese", "Chicken Parmigiana with Pomodoro Pasta", "Pasta Diablo",
        "Meaty Lasagna", "Mac & Cheese Overload",
        // Sandwiches
        "Tuna Melt", "Monte Cristo", "French Toast — Banana Nutella",
        "French Toast — Maple Banana", "Quarter Pounder with Cheese", "Chili Cheesedog",
        "Chicken Katsu Burger", "Bean Crate Clubhouse",
        // Snacks
        "Chili Cheese Fries", "Flavored Fries - Cheese", "Flavored Fries - BBQ",
        "Flavored Fries - Sour Cream", "Bean Crate Snack Box",
        // Juices & Lemonades
        "Freshly Squeezed Lemonade", "Yakult Lemonade", "Strawberry Lemonade",
        "Mango Lemonade", "Mango-Strawberry Lemonade",
        // House Blend
        "House Blend Iced Tea", "Pink Lemonade", "Blue Lemonade", "Cucumber Lemonade",
        // Lemon Soda
        "Strawberry Lemon Soda", "Mango Lemon Soda", "Lychee Lemon Soda",
        "Butterfly Pea Lemon Soda", "Green Apple Lemon Soda",
        // Thai Series & Floats
        "Thai Milktea", "Thai Tea Choco", "Thai Tea Coffee",
        "Thai Honey Lemon Tea", "Root Beer Float",
        // Parfait Series
        "Strawberry Parfait", "Banana Caramel Parfait", "Ube Parfait", "Black Forest Parfait",
        // Specialty Drinks
        "Red Velvet Mocha", "Rose Matcha Latte", "Neopolitan Latte",
        // Desserts & Banana Pudding
        "Brownie Ala Mode", "Banana Pudding", "Banana Pudding Matcha Latte",
        // Buldak Series
        "Cheesy Buldak", "Buldak Omelet", "Buldak Overload",
        // Matcha Foam Series
        "Matcha Salted Caramel Foam", "Matcha Mango Foam",
        "Matcha Strawberry Foam", "Matcha Ube Foam",
        // Ube Series
        "Corn Milk Ube", "Dirty Ube", "Ube Matcha",
        // Pistachio Series
        "Pistachio Latte with Choco Foam", "Choco Pistachio Milk", "Pistachio Matcha",
        // Rice Bowls
        "Luncheon Meat w/ Egg", "Pork Tonkatsu", "Fish and Chips", "Chicken Ala King",
        "Pulled Pork BBQ", "Grilled Liempo w/ Egg", "Hungarian Sausage w/ Egg",
        "Buffalo Wings", "Omurice w/ Luncheon Meat", "Omurice w/ 4 Cheese Sausage",
        "Mac and Cheese Overload Rice Bowl", "Burger Steak Overload"
    };

    public static int GetCategorySortOrder(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return int.MaxValue;
        }

        for (var i = 0; i < OrderedCategoryNames.Count; i++)
        {
            if (string.Equals(OrderedCategoryNames[i], categoryName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return OrderedCategoryNames.Count;
    }

    public static int GetProductSortOrder(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return int.MaxValue;
        }

        for (var i = 0; i < OrderedProductNames.Count; i++)
        {
            if (string.Equals(OrderedProductNames[i], productName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return OrderedProductNames.Count;
    }

    public static string ToProductSlug(string productName)
    {
        var chars = productName
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        return string.Join("-", new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    public static async Task SeedAsync(ApplicationDbContext context)
    {
        var activeCount = await context.Products.CountAsync(p => p.IsActive);

        if (activeCount > 0)
        {
            // Backfill or migrate local photo paths (/images/products/{slug}.png)
            // for active items that have empty or legacy .jpg paths.
            var productsNeedingPngUpdate = await context.Products
                .Where(p => p.IsActive && (
                    p.ImageUrl == null ||
                    p.ImageUrl == "" ||
                    (p.ImageUrl.StartsWith("/images/products/") && p.ImageUrl.EndsWith(".jpg"))))
                .ToListAsync();

            if (productsNeedingPngUpdate.Count > 0)
            {
                foreach (var prod in productsNeedingPngUpdate)
                {
                    prod.ImageUrl = $"/images/products/{ToProductSlug(prod.Name)}.png";
                }
                await context.SaveChangesAsync();
            }

            // Split legacy single "Flavored Fries" into Cheese, BBQ, and Sour Cream items
            var legacyFlavoredFries = await context.Products
                .Include(p => p.Options)
                .FirstOrDefaultAsync(p => p.IsActive && p.Name == "Flavored Fries");

            if (legacyFlavoredFries != null)
            {
                var snacksCategory = await context.Categories
                    .FirstOrDefaultAsync(c => c.Name == "Snacks");

                if (snacksCategory != null)
                {
                    var isLinkedToOrders = await context.OrderItems
                        .AnyAsync(oi => oi.ProductId == legacyFlavoredFries.Id);

                    if (!isLinkedToOrders)
                    {
                        context.ProductOptions.RemoveRange(legacyFlavoredFries.Options);
                        context.Products.Remove(legacyFlavoredFries);
                    }
                    else
                    {
                        legacyFlavoredFries.IsActive = false;
                        legacyFlavoredFries.IsAvailable = false;
                    }

                    var splitFries = new[]
                    {
                        ("Flavored Fries - Cheese", "Golden french fries seasoned with savory cheese powder."),
                        ("Flavored Fries - BBQ", "Golden french fries seasoned with smoky barbecue flavor."),
                        ("Flavored Fries - Sour Cream", "Golden french fries seasoned with tangy sour cream & onion flavor.")
                    };

                    foreach (var (fryName, fryDesc) in splitFries)
                    {
                        var alreadyExists = await context.Products
                            .AnyAsync(p => p.IsActive && p.Name == fryName);

                        if (!alreadyExists)
                        {
                            context.Products.Add(new Product
                            {
                                CategoryId = snacksCategory.Id,
                                Name = fryName,
                                Price = 80m,
                                Description = fryDesc,
                                ImageUrl = $"/images/products/{ToProductSlug(fryName)}.png",
                                IsAvailable = true,
                                IsActive = true
                            });
                        }
                    }

                    await context.SaveChangesAsync();
                }
            }

            // Replace the 8 legacy starter items so the full Bean Crate Cafe menu can seed
            if (activeCount == 8)
            {
                var starterProducts = await context.Products
                    .Where(p => p.IsActive)
                    .ToListAsync();

                var hasLinkedOrderItems = await context.OrderItems.AnyAsync();
                if (!hasLinkedOrderItems)
                {
                    context.ProductOptions.RemoveRange(context.ProductOptions);
                    context.Products.RemoveRange(starterProducts);
                    context.Categories.RemoveRange(context.Categories);
                }
                else
                {
                    foreach (var item in starterProducts)
                    {
                        item.IsActive = false;
                        item.IsAvailable = false;
                    }
                }

                await context.SaveChangesAsync();
            }
            else
            {
                return;
            }
        }

        // 1. Ensure Categories exist in exact 11-page physical menu order
        var existingCategories = await context.Categories
            .ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var catName in OrderedCategoryNames)
        {
            if (!existingCategories.ContainsKey(catName))
            {
                var created = new Category { Name = catName };
                context.Categories.Add(created);
                existingCategories[catName] = created;
            }
        }

        await context.SaveChangesAsync();

        int CatId(string name) => existingCategories[name].Id;

        Product MakeItem(
            string category,
            string name,
            decimal basePrice,
            string? description = null,
            IEnumerable<(string Name, string Type, decimal Adj)>? options = null)
        {
            var product = new Product
            {
                CategoryId = CatId(category),
                Name = name,
                Price = basePrice,
                Description = description,
                ImageUrl = $"/images/products/{ToProductSlug(name)}.png",
                IsAvailable = true,
                IsActive = true
            };

            if (options != null)
            {
                foreach (var (optName, optType, adj) in options)
                {
                    product.Options.Add(new ProductOption
                    {
                        Name = optName,
                        OptionType = optType,
                        PriceAdjustment = adj,
                        IsAvailable = true
                    });
                }
            }

            return product;
        }

        var drinkAddOns = new (string Name, string Type, decimal Adj)[]
        {
            ("Whipped Cream", "AddOn", 45m),
            ("Espresso Shot", "AddOn", 35m),
            ("Sauce / Syrup", "AddOn", 35m),
            ("Ice Cream", "AddOn", 35m),
            ("Banana Pudding", "AddOn", 60m)
        };

        IEnumerable<(string Name, string Type, decimal Adj)> HotIcedDrink(decimal hotPrice, decimal icedPrice) =>
            new (string, string, decimal)[]
            {
                ("Hot (8oz)", "Variant", 0m),
                ("Iced (16oz)", "Variant", icedPrice - hotPrice)
            }.Concat(drinkAddOns);

        IEnumerable<(string Name, string Type, decimal Adj)> HotColdDrink(decimal hotPrice, decimal coldPrice) =>
            new (string, string, decimal)[]
            {
                ("Hot (8oz)", "Variant", 0m),
                ("Cold (16oz)", "Variant", coldPrice - hotPrice)
            }.Concat(drinkAddOns);

        IEnumerable<(string Name, string Type, decimal Adj)> IcedOnlyDrink() =>
            new (string, string, decimal)[]
            {
                ("Iced (16oz)", "Variant", 0m)
            }.Concat(drinkAddOns);

        IEnumerable<(string Name, string Type, decimal Adj)> MediumLargeDrink(decimal medPrice, decimal largePrice) =>
            new (string, string, decimal)[]
            {
                ("Medium (16oz)", "Variant", 0m),
                ("Large (22oz)", "Variant", largePrice - medPrice)
            };

        IEnumerable<(string Name, string Type, decimal Adj)> PastaSizes(decimal smallPrice, decimal regularPrice) =>
            new (string, string, decimal)[]
            {
                ("Small", "Variant", 0m),
                ("Regular", "Variant", regularPrice - smallPrice)
            };

        var items = new List<Product>
        {
            // ================= PAGE 1: COFFEE =================
            MakeItem("Coffee", "Americano", 80m, "Classic espresso with water.", HotIcedDrink(80m, 100m)),
            MakeItem("Coffee", "Latte", 90m, "Smooth espresso with steamed or cold milk.", HotIcedDrink(90m, 110m)),
            MakeItem("Coffee", "Cappuccino", 90m, "Rich espresso topped with velvety milk foam.", HotIcedDrink(90m, 110m)),
            MakeItem("Coffee", "Spanish Latte", 100m, "Sweetened condensed milk espresso latte.", HotIcedDrink(100m, 130m)),
            MakeItem("Coffee", "Caramel Macchiato", 100m, "Vanilla-infused milk marked with espresso and caramel drizzle.", HotIcedDrink(100m, 130m)),
            MakeItem("Coffee", "Mocha Latte", 100m, "Espresso blended with rich chocolate and milk.", HotIcedDrink(100m, 130m)),
            MakeItem("Coffee", "Brown Sugar Latte", 100m, "Caramelized brown sugar syrup with espresso and milk.", HotIcedDrink(100m, 130m)),
            MakeItem("Coffee", "Dirty Matcha", 140m, "Ceremonial matcha layered with a shot of espresso.", HotIcedDrink(140m, 170m)),
            MakeItem("Coffee", "Honey Crumble Latte", 140m, "Iced espresso latte topped with honey and crunchy crumble.", IcedOnlyDrink()),
            MakeItem("Coffee", "Peppermint Mocha", 150m, "Refreshing peppermint chocolate espresso drink.", IcedOnlyDrink()),
            MakeItem("Coffee", "White Peppermint Mocha", 160m, "Creamy white chocolate peppermint iced latte.", IcedOnlyDrink()),
            MakeItem("Coffee", "Nutella Latte", 160m, "Hazelnut chocolate spread blended with espresso and milk.", IcedOnlyDrink()),
            MakeItem("Coffee", "Biscoff Latte", 160m, "Spiced caramelized biscuit spread espresso latte.", IcedOnlyDrink()),
            MakeItem("Coffee", "Tiramisu Latte", 160m, "Mascarpone-inspired cocoa espresso latte.", IcedOnlyDrink()),
            MakeItem("Coffee", "Spanish Dalgona Latte", 160m, "Whipped coffee foam over sweet Spanish milk.", IcedOnlyDrink()),
            MakeItem("Coffee", "Bean Crate Latte", 160m, "House signature specialty iced latte.", IcedOnlyDrink()),

            // ================= PAGE 1: NON-COFFEE =================
            MakeItem("Non-Coffee", "Choco", 90m, "Rich classic chocolate drink.", HotColdDrink(90m, 120m)),
            MakeItem("Non-Coffee", "Toasted Marshmallow Hot Choco", 100m, "Hot chocolate topped with toasted marshmallow.", new (string, string, decimal)[] { ("Hot (8oz)", "Variant", 0m) }.Concat(drinkAddOns)),
            MakeItem("Non-Coffee", "Matcha Latte", 130m, "Earthy green tea matcha with creamy milk.", HotColdDrink(130m, 150m)),
            MakeItem("Non-Coffee", "Ube Milk", 140m, "Creamy purple yam milk drink.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Strawberry Milk", 140m, "Sweet strawberry fruit puree layered with milk.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Mango Milk", 140m, "Tropical mango puree blended with cold milk.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Milo Thicc Dinosaur", 140m, "Rich iced Milo topped with a generous heap of Milo powder.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Strawberry Choco", 150m, "Berry-sweet strawberry layered with rich chocolate milk.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Rose Milk", 140m, "Fragrant floral rose-infused cold milk.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Matcha Biscoff", 160m, "Matcha latte swirled with Lotus Biscoff spread.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Strawberry Matcha", 160m, "Layered strawberry puree, milk, and matcha.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Matcha Oreo", 160m, "Creamy matcha topped with crushed Oreo cookies.", IcedOnlyDrink()),
            MakeItem("Non-Coffee", "Matcha Lemonade", 160m, "Zesty fresh lemonade layered with ceremonial matcha.", IcedOnlyDrink()),

            // ================= PAGE 2: PASTA =================
            MakeItem("Pasta", "Mac & Cheese", 80m, "Creamy baked macaroni and cheese.", PastaSizes(80m, 160m)),
            MakeItem("Pasta", "Chicken Parmigiana with Pomodoro Pasta", 170m, "Crispy chicken parmigiana served over tangy pomodoro pasta.", new[] { ("Regular", "Variant", 0m) }),
            MakeItem("Pasta", "Pasta Diablo", 190m, "Spicy tomato and herb pasta.", new[] { ("Regular", "Variant", 0m) }),
            MakeItem("Pasta", "Meaty Lasagna", 95m, "Layered pasta with rich meat ragu and melted cheese.", PastaSizes(95m, 190m)),
            MakeItem("Pasta", "Mac & Cheese Overload", 190m, "Loaded mac and cheese topped with savory extras.", new[] { ("Regular", "Variant", 0m) }),

            // ================= PAGE 2: SANDWICHES =================
            MakeItem("Sandwiches", "Tuna Melt", 90m, "Toasted sandwich with creamy tuna filling and melted cheese. Comes with fries."),
            MakeItem("Sandwiches", "Monte Cristo", 105m, "Golden griddled savory ham and cheese sandwich. Comes with fries."),
            MakeItem("Sandwiches", "French Toast — Banana Nutella", 105m, "Thick-cut French toast topped with sliced banana and Nutella. Comes with fries."),
            MakeItem("Sandwiches", "French Toast — Maple Banana", 105m, "Thick-cut French toast drizzled with maple syrup and sliced banana. Comes with fries."),
            MakeItem("Sandwiches", "Quarter Pounder with Cheese", 160m, "Juicy quarter-pound beef patty with melted cheddar. Comes with fries."),
            MakeItem("Sandwiches", "Chili Cheesedog", 150m, "Grilled hotdog smothered in savory chili and cheese sauce. Comes with fries."),
            MakeItem("Sandwiches", "Chicken Katsu Burger", 150m, "Crispy Japanese-style breaded chicken cutlet burger. Comes with fries."),
            MakeItem("Sandwiches", "Bean Crate Clubhouse", 160m, "Triple-decker house clubhouse sandwich. Comes with fries."),

            // ================= PAGE 2: SNACKS =================
            MakeItem("Snacks", "Chili Cheese Fries", 140m, "Crispy fries loaded with savory chili and melted cheese."),
            MakeItem("Snacks", "Flavored Fries - Cheese", 80m, "Golden french fries seasoned with savory cheese powder."),
            MakeItem("Snacks", "Flavored Fries - BBQ", 80m, "Golden french fries seasoned with smoky barbecue flavor."),
            MakeItem("Snacks", "Flavored Fries - Sour Cream", 80m, "Golden french fries seasoned with tangy sour cream & onion flavor."),
            MakeItem("Snacks", "Bean Crate Snack Box", 190m, "Assorted shareable snack platter."),

            // ================= PAGE 3: JUICES, HOUSE BLEND, LEMON SODA, THAI & FLOATS =================
            MakeItem("Juices & Lemonades", "Freshly Squeezed Lemonade", 90m, "Refreshing 22oz freshly squeezed lemonade.", new[] { ("Large (22oz)", "Variant", 0m) }),
            MakeItem("Juices & Lemonades", "Yakult Lemonade", 105m, "22oz tangy lemonade blended with Yakult.", new[] { ("Large (22oz)", "Variant", 0m) }),
            MakeItem("Juices & Lemonades", "Strawberry Lemonade", 115m, "22oz strawberry-infused freshly squeezed lemonade.", new[] { ("Large (22oz)", "Variant", 0m) }),
            MakeItem("Juices & Lemonades", "Mango Lemonade", 115m, "22oz tropical mango freshly squeezed lemonade.", new[] { ("Large (22oz)", "Variant", 0m) }),
            MakeItem("Juices & Lemonades", "Mango-Strawberry Lemonade", 115m, "22oz dual fruit mango and strawberry lemonade.", new[] { ("Large (22oz)", "Variant", 0m) }),

            MakeItem("House Blend", "House Blend Iced Tea", 70m, "Classic refreshing house-brewed iced tea.", MediumLargeDrink(70m, 85m)),
            MakeItem("House Blend", "Pink Lemonade", 70m, "Sweet-tart house pink lemonade.", MediumLargeDrink(70m, 85m)),
            MakeItem("House Blend", "Blue Lemonade", 70m, "Chilled citrus blue lemonade.", MediumLargeDrink(70m, 85m)),
            MakeItem("House Blend", "Cucumber Lemonade", 70m, "Crisp cucumber-infused house lemonade.", MediumLargeDrink(70m, 85m)),

            MakeItem("Lemon Soda", "Strawberry Lemon Soda", 80m, "Sparkling strawberry citrus soda.", MediumLargeDrink(80m, 105m)),
            MakeItem("Lemon Soda", "Mango Lemon Soda", 80m, "Sparkling tropical mango lemon soda.", MediumLargeDrink(80m, 105m)),
            MakeItem("Lemon Soda", "Lychee Lemon Soda", 80m, "Sparkling fragrant lychee lemon soda.", MediumLargeDrink(80m, 105m)),
            MakeItem("Lemon Soda", "Butterfly Pea Lemon Soda", 80m, "Color-changing butterfly pea flower citrus fizz.", MediumLargeDrink(80m, 105m)),
            MakeItem("Lemon Soda", "Green Apple Lemon Soda", 80m, "Crisp green apple sparkling lemon soda.", MediumLargeDrink(80m, 105m)),

            MakeItem("Thai Series & Floats", "Thai Milktea", 150m, "Authentic brewed Thai tea with creamy milk (16oz).", IcedOnlyDrink()),
            MakeItem("Thai Series & Floats", "Thai Tea Choco", 150m, "Thai milk tea layered with rich chocolate (16oz).", IcedOnlyDrink()),
            MakeItem("Thai Series & Floats", "Thai Tea Coffee", 150m, "Bold espresso fused with Thai milk tea (16oz).", IcedOnlyDrink()),
            MakeItem("Thai Series & Floats", "Thai Honey Lemon Tea", 150m, "Iced Thai tea brightened with honey and lemon (16oz).", IcedOnlyDrink()),
            MakeItem("Thai Series & Floats", "Root Beer Float", 85m, "Chilled root beer topped with a scoop of vanilla ice cream.", MediumLargeDrink(85m, 110m)),

            // ================= PAGE 4: PARFAIT SERIES =================
            MakeItem("Parfait Series", "Strawberry Parfait", 190m, "Layered strawberry dessert cup topped with ice cream, granola, and wafer stick."),
            MakeItem("Parfait Series", "Banana Caramel Parfait", 190m, "Banana caramel layered parfait topped with cream, caramel popcorn, and wafer stick."),
            MakeItem("Parfait Series", "Ube Parfait", 190m, "Creamy ube parfait topped with vanilla ice cream and crispy cornflakes."),
            MakeItem("Parfait Series", "Black Forest Parfait", 190m, "Chocolate and cherry Black Forest parfait topped with a maraschino cherry."),

            // ================= PAGE 5: SPECIALTY DRINKS =================
            MakeItem("Specialty Drinks", "Red Velvet Mocha", 190m, "Velvety red velvet cocoa espresso specialty latte.", IcedOnlyDrink()),
            MakeItem("Specialty Drinks", "Rose Matcha Latte", 190m, "Fragrant rose milk layered with ceremonial matcha.", IcedOnlyDrink()),
            MakeItem("Specialty Drinks", "Neopolitan Latte", 190m, "Triple-layered strawberry, vanilla, and chocolate espresso latte.", IcedOnlyDrink()),

            // ================= PAGE 6: DESSERTS & BANANA PUDDING SERIES =================
            MakeItem("Desserts & Banana Pudding", "Brownie Ala Mode", 110m, "Warm fudgy brownie topped with vanilla ice cream, chocolate drizzle, and cherry."),
            MakeItem("Desserts & Banana Pudding", "Banana Pudding", 90m, "Creamy house banana pudding cup drizzled with caramel."),
            MakeItem("Desserts & Banana Pudding", "Banana Pudding Matcha Latte", 190m, "Iced matcha latte topped with rich house banana pudding.", IcedOnlyDrink()),

            // ================= PAGE 7: BULDAK SERIES =================
            MakeItem("Buldak Series", "Cheesy Buldak", 160m, "Spicy Korean fire noodles topped with melted cheese and nori flakes."),
            MakeItem("Buldak Series", "Buldak Omelet", 160m, "Spicy Buldak noodles wrapped in a fluffy omelet with mayo and nori."),
            MakeItem("Buldak Series", "Buldak Overload", 250m, "Loaded spicy Buldak noodles with sausage, fried egg, cheese, and roasted seaweed."),

            // ================= PAGE 8: MATCHA FOAM SERIES =================
            MakeItem("Matcha Foam Series", "Matcha Salted Caramel Foam", 190m, "16oz iced matcha topped with salted caramel cold foam.", IcedOnlyDrink()),
            MakeItem("Matcha Foam Series", "Matcha Mango Foam", 190m, "16oz iced matcha topped with sweet mango cold foam.", IcedOnlyDrink()),
            MakeItem("Matcha Foam Series", "Matcha Strawberry Foam", 190m, "16oz iced matcha topped with creamy strawberry cold foam.", IcedOnlyDrink()),
            MakeItem("Matcha Foam Series", "Matcha Ube Foam", 190m, "16oz iced matcha topped with purple yam ube cold foam.", IcedOnlyDrink()),

            // ================= PAGE 9: UBE SERIES =================
            MakeItem("Ube Series", "Corn Milk Ube", 160m, "Sweet cream corn milk layered over rich ube halaya.", IcedOnlyDrink()),
            MakeItem("Ube Series", "Dirty Ube", 170m, "Creamy purple yam milk topped with bold espresso shots.", IcedOnlyDrink()),
            MakeItem("Ube Series", "Ube Matcha", 170m, "Layered ube milk topped with ceremonial green tea matcha.", IcedOnlyDrink()),

            // ================= PAGE 10: PISTACHIO SERIES =================
            MakeItem("Pistachio Series", "Pistachio Latte with Choco Foam", 180m, "Nutty pistachio espresso latte crowned with chocolate cold foam.", IcedOnlyDrink()),
            MakeItem("Pistachio Series", "Choco Pistachio Milk", 150m, "Rich chocolate milk blended with creamy pistachio.", IcedOnlyDrink()),
            MakeItem("Pistachio Series", "Pistachio Matcha", 170m, "Creamy pistachio milk layered with ceremonial matcha.", IcedOnlyDrink()),

            // ================= PAGE 11: RICE BOWLS =================
            MakeItem("Rice Bowls", "Luncheon Meat w/ Egg", 130m, "Steamed rice bowl topped with pan-fried luncheon meat and egg."),
            MakeItem("Rice Bowls", "Pork Tonkatsu", 150m, "Crispy breaded pork cutlet served over steamed rice."),
            MakeItem("Rice Bowls", "Fish and Chips", 150m, "Golden battered fish fillet served with fries/rice."),
            MakeItem("Rice Bowls", "Chicken Ala King", 150m, "Crispy chicken fillet smothered in creamy ala king sauce over rice."),
            MakeItem("Rice Bowls", "Pulled Pork BBQ", 160m, "Slow-cooked smoky pulled pork BBQ rice bowl."),
            MakeItem("Rice Bowls", "Grilled Liempo w/ Egg", 160m, "Marinated grilled pork belly served with egg and rice."),
            MakeItem("Rice Bowls", "Hungarian Sausage w/ Egg", 160m, "Savory grilled Hungarian sausage with egg over rice."),
            MakeItem("Rice Bowls", "Buffalo Wings", 160m, "Glazed spicy-tangy buffalo chicken wings with rice."),
            MakeItem("Rice Bowls", "Omurice w/ Luncheon Meat", 160m, "Japanese-style omelet rice topped with luncheon meat."),
            MakeItem("Rice Bowls", "Omurice w/ 4 Cheese Sausage", 170m, "Japanese-style omelet rice paired with four-cheese sausage."),
            MakeItem("Rice Bowls", "Mac and Cheese Overload Rice Bowl", 190m, "Hearty overload combo with creamy mac and cheese."),
            MakeItem("Rice Bowls", "Burger Steak Overload", 200m, "Savory beef burger patties in rich mushroom gravy over rice.")
        };

        context.Products.AddRange(items);
        await context.SaveChangesAsync();
    }
}