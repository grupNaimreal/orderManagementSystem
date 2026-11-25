// Controllers/OrdersController.cs
// Handles customer mobile orders and staff counter orders for QRDER (Bean Crate Cafe),
// including Cash and Maya Checkout Sandbox (QRPh / Maya Wallet / Card) flows.

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
    private readonly MayaCheckoutService _mayaCheckoutService;
    private readonly AuditLogService _auditLog;

    public OrdersController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService,
        MayaCheckoutService mayaCheckoutService,
        AuditLogService auditLog)
    {
        _context = context;
        _customerSessionService = customerSessionService;
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
    public async Task<IActionResult> CreateForCustomer(int? customerId = null)
    {
        var model = new OrderCreateViewModel
        {
            IsStaffOrder = true,
            CustomerId = customerId,
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

        var allowedMethods = new[] { OrderPaymentMethods.Cash, "Maya" };
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

        // Redirect customer to Maya Sandbox Checkout
        if (!model.IsStaffOrder && order.PaymentMethod == "Maya")
        {
            return await InitiateMayaRedirectAsync(order);
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
            TempData["PaymentError"] = $"Could not start online checkout: {ex.Message}";
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

        if (order.Status != OrderStatuses.Pending)
        {
            TempData["PaymentError"] = "This order is no longer eligible for online checkout.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        order.PaymentMethod = "Maya";
        return await InitiateMayaRedirectAsync(order);
    }

    // CUSTOMER: Switch an unpaid Pending Maya order to Cash at Counter
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchToCash(int id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
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
            TempData["PaymentNotice"] = "This order is already paid online.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        if (order.Status != OrderStatuses.Pending || order.PaymentStatus != OrderPaymentStatuses.Pending)
        {
            TempData["PaymentError"] = "Payment method can only be changed while the order is Pending.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        // Guard against race conditions if a Maya payment just succeeded
        if (!string.IsNullOrWhiteSpace(order.PayMongoCheckoutSessionId))
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

                    TempData["PaymentNotice"] = "Your Maya payment was already confirmed! Your order is queued for preparation.";
                    return RedirectToAction(nameof(Details), new { id = order.Id });
                }
            }
            catch
            {
                // Proceed with switching to Cash if Maya session was never paid
            }
        }

        order.PaymentMethod = OrderPaymentMethods.Cash;
        order.PayMongoCheckoutSessionId = null;
        order.PayMongoCheckoutUrl = null;
        await _context.SaveChangesAsync();

        await _auditLog.LogAsync(
            "PaymentMethodSwitchedToCash",
            "Order",
            order.Id.ToString(),
            $"Customer switched Order #{order.Id} from Maya to Cash at Counter.");

        TempData["PaymentNotice"] = "Switched to Cash at Counter. Please pay at the register to start preparation.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // CUSTOMER: Return callback URL from Maya Sandbox Checkout
    // Includes a short retry loop because Maya Sandbox often reports PAYMENT_PENDING
    // for 1-3 seconds immediately after card/3DS or wallet redirect.
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
                var isFailureCallback = string.Equals(outcome, "failure", StringComparison.OrdinalIgnoreCase);
                var isCancelCallback = string.Equals(outcome, "cancel", StringComparison.OrdinalIgnoreCase);

                var status = await _mayaCheckoutService.GetCheckoutStatusAsync(order.PayMongoCheckoutSessionId);

                if (!isFailureCallback && !isCancelCallback
                    && !status.IsPaid && !status.IsFailed && !status.IsExpired)
                {
                    int[] retryDelaysMs = { 1200, 1800, 2500 };
                    foreach (var delayMs in retryDelaysMs)
                    {
                        await Task.Delay(delayMs);
                        status = await _mayaCheckoutService.GetCheckoutStatusAsync(order.PayMongoCheckoutSessionId);
                        if (status.IsPaid || status.IsFailed || status.IsExpired)
                        {
                            break;
                        }
                    }
                }

                if (status.IsPaid && !isFailureCallback)
                {
                    order.PaymentStatus = OrderPaymentStatuses.Paid;
                    order.PaymentPaidAtUtc = DateTime.UtcNow;
                    order.PayMongoPaymentId = status.PaymentReference ?? order.PayMongoCheckoutSessionId;
                    await _context.SaveChangesAsync();

                    await _auditLog.LogAsync(
                        "MayaPaymentConfirmed",
                        "Order",
                        order.Id.ToString(),
                        $"Online payment verified for Order #{order.Id} (Ref: {order.PayMongoPaymentId}).");

                    TempData["PaymentNotice"] = "Online payment confirmed! Your order is now queued for preparation.";
                }
                else if (status.IsFailed || isFailureCallback)
                {
                    var failedSessionId = order.PayMongoCheckoutSessionId;
                    order.PayMongoCheckoutSessionId = null;
                    order.PayMongoCheckoutUrl = null;
                    await _context.SaveChangesAsync();

                    var reasonDetail = !string.IsNullOrWhiteSpace(status.FailureReason)
                        ? status.FailureReason
                        : (!string.IsNullOrWhiteSpace(status.RawPaymentStatus) ? status.RawPaymentStatus : "DECLINED");

                    await _auditLog.LogAsync(
                        "MayaPaymentDeclined",
                        "Order",
                        order.Id.ToString(),
                        $"Online payment declined/failed for Order #{order.Id} (Session: {failedSessionId}, Status: {status.RawStatus}, PaymentStatus: {status.RawPaymentStatus}, Reason: {reasonDetail}).");

                    TempData["PaymentError"] = "Payment was declined or failed. Your order has NOT been marked as paid—start a new Maya session below or switch to Cash at the counter.";
                }
                else if (status.IsExpired)
                {
                    var expiredSessionId = order.PayMongoCheckoutSessionId;
                    order.PayMongoCheckoutSessionId = null;
                    order.PayMongoCheckoutUrl = null;
                    await _context.SaveChangesAsync();

                    await _auditLog.LogAsync(
                        "MayaPaymentExpired",
                        "Order",
                        order.Id.ToString(),
                        $"Online checkout session {expiredSessionId} expired for Order #{order.Id}.");

                    TempData["PaymentError"] = "Your online checkout session expired. Start a new Maya session below or switch to Cash at the counter.";
                }
                else if (isCancelCallback)
                {
                    // Maya invalidates a checkout session once cancelled on the hosted page
                    var cancelledSessionId = order.PayMongoCheckoutSessionId;
                    order.PayMongoCheckoutSessionId = null;
                    order.PayMongoCheckoutUrl = null;
                    await _context.SaveChangesAsync();

                    await _auditLog.LogAsync(
                        "MayaCheckoutCancelled",
                        "Order",
                        order.Id.ToString(),
                        $"Customer cancelled online checkout (Session: {cancelledSessionId}) before completing payment for Order #{order.Id}.");

                    TempData["PaymentNotice"] = "Online checkout was cancelled. Start a new Maya payment session below or switch to Cash at the counter.";
                }
                else
                {
                    TempData["PaymentVerifying"] = "true";
                }
            }
            catch (Exception ex)
            {
                TempData["PaymentError"] = $"Could not verify online payment right now: {ex.Message}";
            }
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // CUSTOMER / LIVE POLL: Lightweight JSON status check that queries Maya API while Pending
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> CheckMayaStatus(int id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound();

        if (!IsStaff)
        {
            var customer = await _customerSessionService.GetCurrentCustomerAsync();
            if (customer == null || order.CustomerId != customer.Id)
                return NotFound();
        }

        if (order.PaymentMethod == "Maya"
            && order.PaymentStatus == OrderPaymentStatuses.Pending
            && order.Status == OrderStatuses.Pending
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
                        $"Online payment verified automatically for Order #{order.Id} (Ref: {order.PayMongoPaymentId}).");

                    TempData["PaymentNotice"] = "Online payment confirmed! Your order is now queued for preparation.";
                    return Json(new { paymentStatus = order.PaymentStatus, orderStatus = order.Status, reload = true });
                }

                if (status.IsFailed)
                {
                    var failedSessionId = order.PayMongoCheckoutSessionId;
                    order.PayMongoCheckoutSessionId = null;
                    order.PayMongoCheckoutUrl = null;
                    await _context.SaveChangesAsync();

                    var reasonDetail = !string.IsNullOrWhiteSpace(status.FailureReason)
                        ? status.FailureReason
                        : (!string.IsNullOrWhiteSpace(status.RawPaymentStatus) ? status.RawPaymentStatus : "DECLINED");

                    await _auditLog.LogAsync(
                        "MayaPaymentDeclined",
                        "Order",
                        order.Id.ToString(),
                        $"Online payment declined/failed for Order #{order.Id} (Session: {failedSessionId}, Status: {status.RawStatus}, PaymentStatus: {status.RawPaymentStatus}, Reason: {reasonDetail}).");

                    TempData["PaymentError"] = "Payment was declined or failed. Your order has NOT been marked as paid—start a new Maya session below or switch to Cash at the counter.";
                    return Json(new { paymentStatus = "Failed", orderStatus = order.Status, reload = true });
                }

                if (status.IsExpired)
                {
                    var expiredSessionId = order.PayMongoCheckoutSessionId;
                    order.PayMongoCheckoutSessionId = null;
                    order.PayMongoCheckoutUrl = null;
                    await _context.SaveChangesAsync();

                    await _auditLog.LogAsync(
                        "MayaPaymentExpired",
                        "Order",
                        order.Id.ToString(),
                        $"Online checkout session {expiredSessionId} expired for Order #{order.Id}.");

                    TempData["PaymentError"] = "Your online checkout session expired. Start a new Maya session below or switch to Cash at the counter.";
                    return Json(new { paymentStatus = "Expired", orderStatus = order.Status, reload = true });
                }
            }
            catch
            {
                // Ignore transient sandbox network errors; next poll tick will retry
            }
        }

        return Json(new
        {
            paymentStatus = order.PaymentStatus,
            orderStatus = order.Status,
            reload = false
        });
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
            TempData["PaymentError"] = "No online checkout session ID is attached to this order yet.";
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
                    $"Staff verified online payment for Order #{order.Id}.");

                TempData["PaymentNotice"] = "Maya confirmed this order is PAID.";
            }
            else if (status.IsFailed)
            {
                order.PayMongoCheckoutUrl = null;
                await _context.SaveChangesAsync();

                await _auditLog.LogAsync(
                    "MayaPaymentDeclined",
                    "Order",
                    order.Id.ToString(),
                    $"Staff verified online payment was DECLINED/FAILED for Order #{order.Id} ({status.RawPaymentStatus}).");

                TempData["PaymentError"] = $"Maya reported this payment was DECLINED / FAILED ({status.RawPaymentStatus}). Order remains Unpaid.";
            }
            else if (status.IsExpired)
            {
                order.PayMongoCheckoutUrl = null;
                await _context.SaveChangesAsync();
                TempData["PaymentError"] = "Maya reported this checkout session has EXPIRED. Order remains Unpaid.";
            }
            else
            {
                TempData["PaymentNotice"] = $"Maya status: {status.RawPaymentStatus} ({status.RawStatus}).";
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

            TempData["PaymentNotice"] = "Online / QRPh payment confirmed manually.";
        }

        return RedirectToAction(nameof(Details), new { id = order.Id });
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
    public async Task<IActionResult> UpdateStatus(
        int id,
        string status,
        string? cancellationPreset = null,
        string? cancellationReason = null)
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

        if ((status == OrderStatuses.Preparing || status == OrderStatuses.Completed)
            && order.PaymentStatus != OrderPaymentStatuses.Paid
            && order.PaymentStatus != OrderPaymentStatuses.NotTracked)
        {
            TempData["OrderError"] = "Confirm payment before advancing this order.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        var oldStatus = order.Status;
        order.Status = status;
        await _context.SaveChangesAsync();

        if (status == OrderStatuses.Cancelled)
        {
            var preset = (cancellationPreset ?? string.Empty).Trim();
            var customNote = (cancellationReason ?? string.Empty).Trim();
            var combinedReason = string.Join(" — ", new[] { preset, customNote }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(combinedReason))
            {
                combinedReason = "Cancelled by staff";
            }

            await _auditLog.LogAsync(
                "OrderCancelled",
                "Order",
                order.Id.ToString(),
                $"Cancelled Order #{order.Id} (was {oldStatus}). Reason: {combinedReason}");

            TempData["OrderNotice"] = $"Order #{order.Id} cancelled ({combinedReason}) and recorded in the Audit Trail.";
        }
        else
        {
            await _auditLog.LogAsync(
                "OrderStatusUpdated",
                "Order",
                order.Id.ToString(),
                $"Updated Order #{order.Id} status from {oldStatus} to {status}.");
        }

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

        var webRootPath = HttpContext?.RequestServices.GetService<IWebHostEnvironment>()?.WebRootPath;

        ViewBag.ProductMeta = orderedProducts.ToDictionary(
            p => p.Id,
            p => new
            {
                CategoryName = string.IsNullOrWhiteSpace(p.Category?.Name) ? "Other" : p.Category!.Name.Trim(),
                ImageUrl = MenuSeeder.ResolveProductImageUrl(p.ImageUrl, p.Name, webRootPath)
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