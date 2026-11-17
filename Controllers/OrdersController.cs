using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

public class OrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerSessionService _customerSessionService;
    private readonly PayMongoService _payMongoService;
    private readonly ILogger<OrdersController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public OrdersController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService,
        PayMongoService payMongoService,
        ILogger<OrdersController> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _context = context;
        _customerSessionService = customerSessionService;
        _payMongoService = payMongoService;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    private bool IsStaff =>
        User.IsInRole("Owner") || User.IsInRole("Employee");

    // Load current products, available options, and optional staff customer list.
    private async Task LoadFormChoicesAsync(
        OrderCreateViewModel model,
        bool includeCustomers)
    {
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Options)
            .Where(p => p.IsActive && p.IsAvailable)
            .OrderBy(p => p.Name)
            .ToListAsync();

        model.Products = products.Select(p => new OrderProductChoiceViewModel
        {
            Id = p.Id,
            Name = p.Name,
            BasePrice = p.Price,
            Variants = p.Options
                .Where(o => o.IsAvailable && o.OptionType == "Variant")
                .Select(o => new OrderOptionChoiceViewModel
                {
                    Id = o.Id,
                    ProductId = o.ProductId,
                    Name = o.Name,
                    PriceAdjustment = o.PriceAdjustment
                }).ToList(),
            AddOns = p.Options
                .Where(o => o.IsAvailable && o.OptionType == "AddOn")
                .Select(o => new OrderOptionChoiceViewModel
                {
                    Id = o.Id,
                    ProductId = o.ProductId,
                    Name = o.Name,
                    PriceAdjustment = o.PriceAdjustment
                }).ToList()
        }).ToList();

        if (includeCustomers)
        {
            model.Customers = await _context.Customers
                .AsNoTracking()
                .OrderByDescending(c => c.LastActivityUtc)
                .Select(c => new OrderCustomerChoiceViewModel
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName
                })
                .ToListAsync();
        }
    }

    // Build an order using database prices, never submitted prices.
    private async Task<Order?> BuildOrderAsync(
        OrderCreateViewModel model,
        Customer customer)
    {
        if (model.Items == null || model.Items.Count == 0)
        {
            ModelState.AddModelError("", "Add at least one product.");
            return null;
        }

        var productIds = model.Items
            .Select(i => i.ProductId)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var products = await _context.Products
            .Include(p => p.Options)
            .Where(p => productIds.Contains(p.Id)
                && p.IsActive && p.IsAvailable)
            .ToDictionaryAsync(p => p.Id);

        var orderItems = new List<OrderItem>();

        for (var index = 0; index < model.Items.Count; index++)
        {
            var input = model.Items[index];
            var field = $"Items[{index}]";

            if (input.ProductId <= 0
                || !products.TryGetValue(input.ProductId, out var product))
            {
                ModelState.AddModelError(field,
                    "The selected product is invalid or unavailable.");
                continue;
            }

            if (input.Quantity < 1 || input.Quantity > 99)
            {
                ModelState.AddModelError(field,
                    "Quantity must be between 1 and 99.");
                continue;
            }

            var selectedOptions = new List<ProductOption>();

            if (input.VariantOptionId.HasValue)
            {
                var variant = product.Options.FirstOrDefault(o =>
                    o.Id == input.VariantOptionId.Value
                    && o.OptionType == "Variant"
                    && o.IsAvailable);

                if (variant == null)
                {
                    ModelState.AddModelError(field,
                        "The selected variant is invalid or unavailable.");
                    continue;
                }

                selectedOptions.Add(variant);
            }

            var addOnIds = input.AddOnOptionIds ?? new List<int>();

            if (addOnIds.Distinct().Count() != addOnIds.Count)
            {
                ModelState.AddModelError(field,
                    "Duplicate add-ons are not allowed.");
                continue;
            }

            var addOns = product.Options
                .Where(o => addOnIds.Contains(o.Id)
                    && o.OptionType == "AddOn"
                    && o.IsAvailable)
                .ToList();

            if (addOns.Count != addOnIds.Count)
            {
                ModelState.AddModelError(field,
                    "One or more selected add-ons are invalid or unavailable.");
                continue;
            }

            if (addOns.Any(o => o.PriceAdjustment < 0))
            {
                ModelState.AddModelError(field,
                    "An add-on has an invalid price adjustment.");
                continue;
            }

            selectedOptions.AddRange(addOns);

            var unitPrice = product.Price
                + selectedOptions.Sum(o => o.PriceAdjustment);

            if (unitPrice < 0 || unitPrice > 1000000m)
            {
                ModelState.AddModelError(field,
                    "The calculated product price is invalid.");
                continue;
            }

            var item = new OrderItem
            {
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                BaseUnitPrice = product.Price,
                Quantity = input.Quantity
            };

            foreach (var option in selectedOptions)
            {
                item.SelectedOptions.Add(new OrderItemOption
                {
                    NameSnapshot = option.Name,
                    OptionType = option.OptionType,
                    PriceAdjustment = option.PriceAdjustment
                });
            }

            orderItems.Add(item);
        }

        if (!ModelState.IsValid)
            return null;

        var total = decimal.Round(
            orderItems.Sum(i => i.UnitPrice * i.Quantity), 2);

        if (total <= 0 || total > 1000000m)
        {
            ModelState.AddModelError("",
                "The calculated order total is outside the allowed range.");
            return null;
        }

        return new Order
        {
            CustomerId = customer.Id,
            Customer = customer,
            CustomerName = customer.DisplayName,
            OrderDate = DateTime.UtcNow,
            TotalAmount = total,
            Status = OrderStatuses.Pending,
            Items = orderItems
        };
    }

    // CUSTOMER: display the ordering form.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        var model = new OrderCreateViewModel();

        await LoadFormChoicesAsync(model, false);
        return View(model);
    }

    // CUSTOMER: create an order for their own active session.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderCreateViewModel model)
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        model.IsStaffOrder = false;

        if (model.PaymentMethod != OrderPaymentMethods.Cash
            && model.PaymentMethod != OrderPaymentMethods.PayMongo)
        {
            ModelState.AddModelError(
                nameof(model.PaymentMethod),
                "Select a valid payment method.");
        }

        if (ModelState.IsValid)
        {
            var order = await BuildOrderAsync(model, customer);

            if (order != null)
            {
                order.PaymentMethod = model.PaymentMethod;
                order.PaymentStatus = OrderPaymentStatuses.Pending;

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                if (order.PaymentMethod == OrderPaymentMethods.PayMongo)
                {
                    if (_environment.IsDevelopment()
                        && _configuration.GetValue<bool>("PayMongo:UseMock"))
                    {
                        return RedirectToAction(
                            "Checkout", "DevPayment",
                            new { id = order.Id });
                    }

                    return await CreatePayMongoCheckoutAsync(order);
                }

                return RedirectToAction(nameof(Details),
                    new { id = order.Id });
            }
        }

        await LoadFormChoicesAsync(model, false);
        return View(model);
    }

    // Create a hosted PayMongo Checkout Session for an already-saved order.
    private async Task<IActionResult> CreatePayMongoCheckoutAsync(Order order)
    {
        try
        {
            var successUrl = Url.Action(
                nameof(Details),
                "Orders",
                new { id = order.Id, checkout = "success" },
                Request.Scheme);

            var cancelUrl = Url.Action(
                nameof(Details),
                "Orders",
                new { id = order.Id, checkout = "cancelled" },
                Request.Scheme);

            if (string.IsNullOrWhiteSpace(successUrl)
                || string.IsNullOrWhiteSpace(cancelUrl))
            {
                throw new InvalidOperationException(
                    "Could not create the checkout return URLs.");
            }

            var session = await _payMongoService.CreateCheckoutSessionAsync(
                order,
                successUrl,
                cancelUrl,
                HttpContext.RequestAborted);

            order.PayMongoCheckoutSessionId = session.Id;
            order.PayMongoCheckoutUrl = session.CheckoutUrl;

            await _context.SaveChangesAsync();

            return Redirect(session.CheckoutUrl);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not create PayMongo checkout for order {OrderId}.",
                order.Id);

            TempData["PaymentError"] =
                "Online checkout could not be started. Your order was saved, " +
                "but payment is not confirmed. Please try again.";

            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
    }

    // CUSTOMER: retry or resume checkout for their own order.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartPayMongoCheckout(int id)
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.SelectedOptions)
            .FirstOrDefaultAsync(o =>
                o.Id == id && o.CustomerId == customer.Id);

        if (order == null
            || order.PaymentMethod != OrderPaymentMethods.PayMongo
            || order.PaymentStatus == OrderPaymentStatuses.Paid
            || order.Status == OrderStatuses.Cancelled
            || order.Status == OrderStatuses.Completed)
        {
            return NotFound();
        }

        // Reuse an existing session instead of creating another one.
        if (!string.IsNullOrWhiteSpace(order.PayMongoCheckoutUrl))
            return Redirect(order.PayMongoCheckoutUrl);

        return await CreatePayMongoCheckoutAsync(order);
    }

    // STAFF: display the form for creating an order on behalf of a customer.
    [Authorize(Roles = "Owner,Employee")]
    [HttpGet]
    public async Task<IActionResult> CreateForCustomer()
    {
        var model = new OrderCreateViewModel
        {
            IsStaffOrder = true
        };

        await LoadFormChoicesAsync(model, true);
        return View("Create", model);
    }

    // STAFF: create an order for the selected customer.
    [Authorize(Roles = "Owner,Employee")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateForCustomer(
        OrderCreateViewModel model)
    {
        model.IsStaffOrder = true;

        if (!model.CustomerId.HasValue)
            ModelState.AddModelError(nameof(model.CustomerId),
                "Select a customer.");

        Customer? customer = null;

        if (model.CustomerId.HasValue)
        {
            customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == model.CustomerId.Value);

            if (customer == null)
                ModelState.AddModelError(nameof(model.CustomerId),
                    "The selected customer does not exist.");
        }

        if (ModelState.IsValid && customer != null)
        {
            var order = await BuildOrderAsync(model, customer);

            if (order != null)
            {
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Details),
                    new { id = order.Id });
            }
        }

        await LoadFormChoicesAsync(model, true);
        return View("Create", model);
    }

    // STAFF: view all orders.
    [Authorize(Roles = "Owner,Employee")]
    public async Task<IActionResult> Index()
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }

    // CUSTOMER: view only their own orders.
    [AllowAnonymous]
    public async Task<IActionResult> MyOrders()
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }

    // STAFF or the owning customer: view order details.
    [AllowAnonymous]
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.Items)
                .ThenInclude(i => i.SelectedOptions)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound();

        if (!IsStaff)
        {
            var customer = await _customerSessionService.GetCurrentCustomerAsync();

            if (customer == null)
                return RedirectToAction("Create", "Customers");

            if (order.CustomerId != customer.Id)
                return NotFound();
        }

        return View(order);
    }

    // STAFF: confirm that cash for a customer order was received.
    [Authorize(Roles = "Owner,Employee")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCashPaid(int id)
    {
        var order = await _context.Orders.FindAsync(id);

        if (order == null)
            return NotFound();

        if (order.PaymentMethod != OrderPaymentMethods.Cash
            || order.PaymentStatus != OrderPaymentStatuses.Pending
            || order.Status != OrderStatuses.Pending)
        {
            TempData["OrderError"] =
                "This order cannot be marked as paid.";

            return RedirectToAction(nameof(Details), new { id });
        }

        order.PaymentStatus = OrderPaymentStatuses.Paid;
        order.PaymentPaidAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }

    // STAFF: update only the order status, not its customer or calculated total.
    [Authorize(Roles = "Owner,Employee")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var order = await _context.Orders.FindAsync(id);

        if (order == null)
            return NotFound();

        if (status == OrderStatuses.Preparing
            && order.PaymentStatus != OrderPaymentStatuses.Paid
            && order.PaymentStatus != OrderPaymentStatuses.NotTracked)
        {
            TempData["OrderError"] =
                "Confirm payment before moving this order to preparation.";

            return RedirectToAction(nameof(Details), new { id });
        }

        // A live PayMongo session must be expired before cancelling its order.
        if (status == OrderStatuses.Cancelled
            && order.PaymentMethod == OrderPaymentMethods.PayMongo
            && order.PaymentStatus == OrderPaymentStatuses.Pending
            && !string.IsNullOrWhiteSpace(order.PayMongoCheckoutSessionId))
        {
            TempData["OrderError"] =
                "This PayMongo checkout session must be expired before cancelling the order.";

            return RedirectToAction(nameof(Details), new { id });
        }

        var validTransition =
            (order.Status == OrderStatuses.Pending
                && (status == OrderStatuses.Preparing
                    || status == OrderStatuses.Cancelled))
            || (order.Status == OrderStatuses.Preparing
                && (status == OrderStatuses.Completed
                    || status == OrderStatuses.Cancelled));

        if (!validTransition)
        {
            TempData["OrderError"] =
                "That order status change is not allowed.";

            return RedirectToAction(nameof(Details), new { id });
        }

        order.Status = status;
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }
}
