using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

[Route("dev-payment")]
public class DevPaymentController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerSessionService _customerSessionService;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public DevPaymentController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _context = context;
        _customerSessionService = customerSessionService;
        _environment = environment;
        _configuration = configuration;
    }

    private bool IsMockEnabled =>
        _environment.IsDevelopment()
        && _configuration.GetValue<bool>("PayMongo:UseMock");

    [HttpGet("checkout/{id:int}")]
    public async Task<IActionResult> Checkout(int id)
    {
        if (!IsMockEnabled)
            return NotFound();

        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o =>
                o.Id == id
                && o.CustomerId == customer.Id
                && o.PaymentMethod == OrderPaymentMethods.PayMongo);

        if (order == null)
            return NotFound();

        if (order.PaymentStatus != OrderPaymentStatuses.Pending)
            return RedirectToAction("Details", "Orders", new { id });

        return View(order);
    }

    [HttpPost("paid/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulatePaid(int id)
    {
        if (!IsMockEnabled)
            return NotFound();

        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        var order = await _context.Orders.FirstOrDefaultAsync(o =>
            o.Id == id
            && o.CustomerId == customer.Id
            && o.PaymentMethod == OrderPaymentMethods.PayMongo);

        if (order == null)
            return NotFound();

        if (order.PaymentStatus != OrderPaymentStatuses.Pending)
            return BadRequest("This order is no longer awaiting payment.");

        order.PaymentStatus = OrderPaymentStatuses.Paid;
        order.PaymentPaidAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["PaymentNotice"] =
            "Development simulation only: payment marked Paid. No money was transferred.";

        return RedirectToAction("Details", "Orders", new { id });
    }

    [HttpPost("failed/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulateFailed(int id)
    {
        if (!IsMockEnabled)
            return NotFound();

        var customer = await _customerSessionService.GetCurrentCustomerAsync();

        if (customer == null)
            return RedirectToAction("Create", "Customers");

        var order = await _context.Orders.FirstOrDefaultAsync(o =>
            o.Id == id
            && o.CustomerId == customer.Id
            && o.PaymentMethod == OrderPaymentMethods.PayMongo);

        if (order == null)
            return NotFound();

        if (order.PaymentStatus != OrderPaymentStatuses.Pending)
            return BadRequest("This order is no longer awaiting payment.");

        order.PaymentStatus = OrderPaymentStatuses.Failed;

        await _context.SaveChangesAsync();

        TempData["PaymentNotice"] =
            "Development simulation only: payment marked Failed. No transaction occurred.";

        return RedirectToAction("Details", "Orders", new { id });
    }
}
