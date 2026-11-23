// Controllers/OrdersController.cs
// Handles customer mobile orders and staff counter orders for QRDER (Bean Crate Cafe),
// including Cash, Maya Checkout Sandbox (QRPh / Maya Wallet / Card), and PayMongo flows.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

[Authorize(Roles = "Owner,Employee")]
public class OrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerSessionService _customerSessionService;
    private readonly PayMongoService _payMongoService;
    private readonly MayaCheckoutService _mayaCheckoutService;
    private readonly AuditLogService _auditLog;

    public OrdersController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService,
        PayMongoService payMongoService,
        MayaCheckoutService mayaCheckoutService,
        AuditLogService auditLog)
    {
        _context = context;
        _customerSessionService = customerSessionService;
        _payMongoService = payMongoService;
        _mayaCheckoutService = mayaCheckoutService;
        _auditLog = auditLog;
    }

    private bool IsStaff => User.IsInRole("Owner") || User.IsInRole("Employee");

    // STAFF: view all orders (Order Board)
    public async Task<IActionResult> Index()
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }

    // CUSTOMER: display the ordering form
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
        {
            TempData["SessionNotice"] = "Start a customer session first to place an order.";
            return RedirectToAction("Index", "Home");
        }

        var model = new OrderCreateViewModel
        {
            PaymentMethod = "Cash"
        };

        await LoadFormChoicesAsync(model, false);
        return View(model);
    }

    // CUSTOMER: create an order for their own active session
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderCreateViewModel model)
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
        {
            TempData["SessionNotice"] = "Your session expired. Start a new session to place an order.";
            return RedirectToAction("Index", "Home");
        }

        model.IsStaffOrder = false;
        return await CreateOrderAsync(model, customer);
    }

    // STAFF: display the counter ordering form
    [HttpGet]
    public async Task<IActionResult> CreateForCustomer()
    {
        var model = new OrderCreateViewModel
        {
            IsStaffOrder = true,
            PaymentMethod = OrderPaymentMethods.Cash
        };

        await LoadFormChoicesAsync(model, true);
        return View("Create", model);
    }

    // STAFF: create an order for an existing customer session
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateForCustomer(OrderCreateViewModel model)
    {
        model.IsStaffOrder = true;
        Customer? customer = null;

        if (!model.CustomerId.HasValue)
        {
            ModelState.AddModelError(nameof(model.CustomerId), "Please select an active customer session.");
        }
        else
        {
            customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == model.CustomerId.Value);
            if (customer == null)
            {
                ModelState.AddModelError(nameof(model.CustomerId), "The selected customer session no longer exists.");
            }
        }

        return await CreateOrderAsync(model, customer);
    }

    // Builds and persists the order using server-side product/option prices
    private async Task<IActionResult> CreateOrderAsync(OrderCreateViewModel model, Customer? customer)
    {
        var requestedItems = (model.Items ?? new List<OrderItemInputViewModel>())
            .Where(i => i.ProductId > 0)
            .ToList();

        if (requestedItems.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please add at least one product to the order.");
        }

        var allowedMethods = new[] { OrderPaymentMethods.Cash, "Maya", OrderPaymentMethods.PayMongo };
        if (string.IsNullOrWhiteSpace(model.PaymentMethod)
            || !allowedMethods.Contains(model.PaymentMethod, StringComparer.OrdinalIgnoreCase))
        {
            model.PaymentMethod = OrderPaymentMethods.Cash;
        }
        else if (model.PaymentMethod.Equals("Maya", StringComparison.OrdinalIgnoreCase))
        {
            model.PaymentMethod = "Maya";
        }

        if (!ModelState.IsValid || customer == null)
        {
            await LoadFormChoicesAsync(model, model.IsStaffOrder);
            return View("Create", model);
        }

        var productIds = requestedItems.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.Options)
            .Where(p => productIds.Contains(p.Id) && p.IsActive && p.IsAvailable)
            .ToDictionaryAsync(p => p.Id);

        var order = new Order
        {
            CustomerId = customer.Id,
            CustomerName = customer.DisplayName,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatuses.Pending,
            PaymentMethod = model.IsStaffOrder ? OrderPaymentMethods.Cash : model.PaymentMethod,
            PaymentStatus = model.IsStaffOrder
                ? OrderPaymentStatuses.NotTracked
                : OrderPaymentStatuses.Pending
        };

        decimal grandTotal = 0m;

        foreach (var req in requestedItems)
        {
            if (!products.TryGetValue(req.ProductId, out var product))
            {
                ModelState.AddModelError(string.Empty, "One or more selected products are no longer available.");
                await LoadFormChoicesAsync(model, model.IsStaffOrder);
                return View("Create", model);
            }

            var qty = Math.Clamp(req.Quantity, 1, 99);
            decimal unitPrice = product.Price;
            var selectedSnapshots = new List<OrderItemOption>();

            if (req.VariantOptionId.HasValue)
            {
                var variant = product.Options.FirstOrDefault(o =>
                    o.Id == req.VariantOptionId.Value && o.IsAvailable && o.OptionType == "Variant");

                if (variant != null)
                {
                    unitPrice += variant.PriceAdjustment;
                    selectedSnapshots.Add(new OrderItemOption
                    {
                        NameSnapshot = variant.Name,
                        OptionType = variant.OptionType,
                        PriceAdjustment = variant.PriceAdjustment
                    });
                }
            }

            var distinctAddOnIds = (req.AddOnOptionIds ?? new List<int>()).Distinct().ToList();
            foreach (var addOnId in distinctAddOnIds)
            {
                var addOn = product.Options.FirstOrDefault(o =>
                    o.Id == addOnId && o.IsAvailable && o.OptionType == "AddOn");

                if (addOn != null)
                {
                    unitPrice += addOn.PriceAdjustment;
                    selectedSnapshots.Add(new OrderItemOption
                    {
                        NameSnapshot = addOn.Name,
                        OptionType = addOn.OptionType,
                        PriceAdjustment = addOn.PriceAdjustment
                    });
                }
            }

            var orderItem = new OrderItem
            {
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                BaseUnitPrice = product.Price,
                Quantity = qty,
                SelectedOptions = selectedSnapshots
            };

            grandTotal += orderItem.LineTotal;
            order.Items.Add(orderItem);
        }

        order.TotalAmount = grandTotal;

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _auditLog.LogAsync(
            "OrderCreated",
            "Order",
            order.Id.ToString(),
            $"Created Order #{order.Id} for {order.CustomerName} ({order.PaymentMethod}, ₱{order.TotalAmount:N2}).");

        // Option 5B: Redirect customer to Maya Sandbox Checkout
        if (!model.IsStaffOrder && order.PaymentMethod == "Maya")
        {
            return await InitiateMayaRedirectAsync(order);
        }

        if (!model.IsStaffOrder && order.PaymentMethod == OrderPaymentMethods.PayMongo)
        {
            return await StartPayMongoCheckout(order.Id);
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    private async Task<IActionResult> InitiateMayaRedirectAsync(Order order)
    {
        try
        {
            var successUrl = BuildCallbackUrl(nameof(MayaReturn), "Orders", new { id = order.Id, outcome = "success" });
            var failureUrl = BuildCallbackUrl(nameof(MayaReturn), "Orders", new { id = order.Id, outcome = "failure" });
            var cancelUrl = BuildCallbackUrl(nameof(MayaReturn), "Orders", new { id = order.Id, outcome = "cancel" });

            var session = await _mayaCheckoutService.CreateCheckoutAsync(
                order,
                successUrl,
                failureUrl,
                cancelUrl);

            order.PayMongoCheckoutSessionId = session.CheckoutId;
            order.PayMongoCheckoutUrl = session.RedirectUrl;
            await _context.SaveChangesAsync();

            return Redirect(session.RedirectUrl);
        }
        catch (Exception ex)
        {
            TempData["PaymentError"] = $"Could not start Maya Checkout: {ex.Message}";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
    }

    private string BuildCallbackUrl(string action, string controller, object routeValues)
    {
        var relativePath = Url.Action(action, controller, routeValues)
            ?? throw new InvalidOperationException($"Could not generate callback URL for {controller}/{action}.");

        var forwardedHost = Request.Headers["X-Forwarded-Host"]
            .FirstOrDefault()?.Split(',')[0].Trim();
        var forwardedProto = Request.Headers["X-Forwarded-Proto"]
            .FirstOrDefault()?.Split(',')[0].Trim();

        var host = !string.IsNullOrWhiteSpace(forwardedHost) ? forwardedHost : Request.Host.Value;
        var scheme = !string.IsNullOrWhiteSpace(forwardedProto) ? forwardedProto : Request.Scheme;

        return $"{scheme}://{host}{relativePath}";
    }

    // CUSTOMER: Resume or retry Maya Sandbox Checkout from Order Details
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartMayaCheckout(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound();

        if (!IsStaff)
        {
            var customer = await _customerSessionService.GetCurrentCustomerAsync();
            if (customer == null)
                return RedirectToAction("Index", "Home");
            if (order.CustomerId != customer.Id)
                return NotFound();
        }

        if (order.PaymentStatus == OrderPaymentStatuses.Paid)
        {
            TempData["PaymentNotice"] = "This order is already marked as Paid.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        return await InitiateMayaRedirectAsync(order);
    }

    // CUSTOMER: Return callback URL from Maya Sandbox Checkout (verifies payment via Maya Secret Key)
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> MayaReturn(int id, string? outcome = null)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound();

        if (order.PaymentMethod != "Maya")
            return RedirectToAction(nameof(Details), new { id = order.Id });

        if (order.PaymentStatus != OrderPaymentStatuses.Paid
            && !string.IsNullOrWhiteSpace(order.PayMongoCheckoutSessionId))
        {
            try
            {
                var status = await _mayaCheckoutService.GetCheckoutStatusAsync(order.PayMongoCheckoutSessionId);

                if (status.IsPaid)
                {
                    order.PaymentStatus = OrderPaymentStatuses.Paid;
                    order.PaymentPaidAtUtc = DateTime.UtcNow;
                    order.PayMongoPaymentId = status.PaymentReference ?? order.PayMongoCheckoutSessionId;
                    await _context.SaveChangesAsync();

                    await _auditLog.LogAsync(
                        "MayaPaymentConfirmed",
                        "Order",
                        order.Id.ToString(),
                        $"Maya Sandbox payment verified for Order #{order.Id} (Ref: {order.PayMongoPaymentId}).");

                    TempData["PaymentNotice"] = "Maya payment confirmed! Your order is now queued for preparation.";
                }
                else if (status.IsFailed || string.Equals(outcome, "failure", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["PaymentError"] = "Maya payment was declined or did not complete. You can retry checkout below.";
                }
                else if (string.Equals(outcome, "cancel", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["PaymentNotice"] = "Maya checkout was cancelled. Click Complete Maya Checkout below to resume payment.";
                }
                else
                {
                    TempData["PaymentNotice"] = $"Maya checkout status is currently {status.RawPaymentStatus}. If you just completed payment, refresh in a moment.";
                }
            }
            catch (Exception ex)
            {
                TempData["PaymentError"] = $"Could not verify Maya payment right now: {ex.Message}";
            }
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // STAFF: Query Maya API on demand to check if a customer's Maya payment succeeded
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyMayaPayment(int id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(order.PayMongoCheckoutSessionId))
        {
            TempData["PaymentError"] = "No Maya checkout session ID is attached to this order yet.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        try
        {
            var status = await _mayaCheckoutService.GetCheckoutStatusAsync(order.PayMongoCheckoutSessionId);
            if (status.IsPaid)
            {
                order.PaymentStatus = OrderPaymentStatuses.Paid;
                order.PaymentPaidAtUtc = DateTime.UtcNow;
                order.PayMongoPaymentId = status.PaymentReference ?? order.PayMongoCheckoutSessionId;
                await _context.SaveChangesAsync();

                await _auditLog.LogAsync(
                    "MayaPaymentVerifiedByStaff",
                    "Order",
                    order.Id.ToString(),
                    $"Staff verified Maya Sandbox payment for Order #{order.Id}.");

                TempData["PaymentNotice"] = "Maya Sandbox confirmed this order is PAID.";
            }
            else
            {
                TempData["PaymentNotice"] = $"Maya Sandbox status: {status.RawPaymentStatus} ({status.RawStatus}).";
            }
        }
        catch (Exception ex)
        {
            TempData["PaymentError"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // STAFF: Manual counter confirmation for Maya / QRPh payments
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkMayaPaid(int id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound();

        if (order.PaymentMethod == "Maya" && order.PaymentStatus == OrderPaymentStatuses.Pending)
        {
            order.PaymentStatus = OrderPaymentStatuses.Paid;
            order.PaymentPaidAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(
                "MayaManualConfirmed",
                "Order",
                order.Id.ToString(),
                $"Staff manually confirmed Maya/QRPh payment for Order #{order.Id} (₱{order.TotalAmount:N2}).");

            TempData["PaymentNotice"] = "Maya payment confirmed manually.";
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // CUSTOMER: retry or resume PayMongo checkout if selected
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartPayMongoCheckout(int id)
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Index", "Home");

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customer.Id);

        if (order == null)
            return NotFound();

        try
        {
            var successUrl = BuildCallbackUrl(nameof(Details), "Orders", new { id = order.Id });
            var cancelUrl = BuildCallbackUrl(nameof(Details), "Orders", new { id = order.Id });

            var session = await _payMongoService.CreateCheckoutSessionAsync(order, successUrl, cancelUrl);
            order.PayMongoCheckoutSessionId = session.Id;
            order.PayMongoCheckoutUrl = session.CheckoutUrl;
            await _context.SaveChangesAsync();

            return Redirect(session.CheckoutUrl);
        }
        catch (Exception ex)
        {
            TempData["PaymentError"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
    }

    // CUSTOMER: view only their own orders
    [AllowAnonymous]
    public async Task<IActionResult> MyOrders()
    {
        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Index", "Home");

        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }

    // STAFF or owning customer: view order details
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
                return RedirectToAction("Index", "Home");

            if (order.CustomerId != customer.Id)
                return NotFound();
        }

        return View(order);
    }

    // STAFF: confirm cash payment received
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCashPaid(int id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound();

        if (order.PaymentMethod == OrderPaymentMethods.Cash
            && order.PaymentStatus == OrderPaymentStatuses.Pending)
        {
            order.PaymentStatus = OrderPaymentStatuses.Paid;
            order.PaymentPaidAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(
                "CashPaymentConfirmed",
                "Order",
                order.Id.ToString(),
                $"Confirmed cash payment of ₱{order.TotalAmount:N2} for Order #{order.Id}.");
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // STAFF: advance order preparation / completion status
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound();

        var allowed = new[] { OrderStatuses.Preparing, OrderStatuses.Completed, OrderStatuses.Cancelled };
        if (!allowed.Contains(status))
        {
            TempData["OrderError"] = "Invalid status transition.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        if (status == OrderStatuses.Preparing
            && order.PaymentStatus != OrderPaymentStatuses.Paid
            && order.PaymentStatus != OrderPaymentStatuses.NotTracked)
        {
            TempData["OrderError"] = "Confirm payment before moving the order to Preparing.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        var oldStatus = order.Status;
        order.Status = status;
        await _context.SaveChangesAsync();

        await _auditLog.LogAsync(
            "OrderStatusUpdated",
            "Order",
            order.Id.ToString(),
            $"Updated Order #{order.Id} status from {oldStatus} to {status}.");

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    private async Task LoadFormChoicesAsync(OrderCreateViewModel model, bool includeCustomers)
    {
        var rawProducts = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Options)
            .Where(p => p.IsActive && p.IsAvailable)
            .ToListAsync();

        var orderedProducts = rawProducts
            .OrderBy(p => MenuSeeder.GetCategorySortOrder(p.Category?.Name))
            .ThenBy(p => MenuSeeder.GetProductSortOrder(p.Name))
            .ThenBy(p => p.Name)
            .ToList();

        model.Products = orderedProducts.Select(p => new OrderProductChoiceViewModel
        {
            Id = p.Id,
            Name = p.Name,
            BasePrice = p.Price,
            Variants = p.Options
                .Where(o => o.IsAvailable && o.OptionType == "Variant")
                .OrderBy(o => o.PriceAdjustment)
                .ThenBy(o => o.Name)
                .Select(o => new OrderOptionChoiceViewModel
                {
                    Id = o.Id,
                    Name = o.Name,
                    PriceAdjustment = o.PriceAdjustment
                })
                .ToList(),
            AddOns = p.Options
                .Where(o => o.IsAvailable && o.OptionType == "AddOn")
                .OrderBy(o => o.PriceAdjustment)
                .ThenBy(o => o.Name)
                .Select(o => new OrderOptionChoiceViewModel
                {
                    Id = o.Id,
                    Name = o.Name,
                    PriceAdjustment = o.PriceAdjustment
                })
                .ToList()
        }).ToList();

        ViewBag.ProductMeta = orderedProducts.ToDictionary(
            p => p.Id,
            p => new
            {
                CategoryName = string.IsNullOrWhiteSpace(p.Category?.Name) ? "Other" : p.Category!.Name.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(p.ImageUrl)
                    ? $"/images/products/{MenuSeeder.ToProductSlug(p.Name)}.png"
                    : (p.ImageUrl.StartsWith("/images/products/", StringComparison.OrdinalIgnoreCase)
                       && p.ImageUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        ? p.ImageUrl[..^4] + ".png"
                        : p.ImageUrl)
            });

        if (includeCustomers)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            model.Customers = await _context.Customers
                .AsNoTracking()
                .Where(c => c.LastActivityUtc >= cutoff)
                .OrderByDescending(c => c.LastActivityUtc)
                .Select(c => new OrderCustomerChoiceViewModel
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName
                })
                .ToListAsync();
        }
    }
}