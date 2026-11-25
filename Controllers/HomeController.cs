// Controllers/HomeController.cs
// Populates live counter metrics (Today's Orders, Active Queue, Unpaid/Pending, and Completed Sales)
// for Owner and Employee users on the Overview screen, while keeping customer entry lightweight.

using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerSessionService _customerSessionService;

    public HomeController(
        ApplicationDbContext context,
        CustomerSessionService customerSessionService)
    {
        _context = context;
        _customerSessionService = customerSessionService;
    }

    public async Task<IActionResult> Index(string? table = null)
    {
        var isStaff = User.IsInRole("Owner") || User.IsInRole("Employee");

        if (isStaff)
        {
            // Compute today's UTC boundaries aligned to Philippine Standard Time (UTC+8)
            var phOffset = TimeSpan.FromHours(8);
            var nowPh = DateTime.UtcNow.Add(phOffset);
            var startOfTodayUtc = nowPh.Date.Subtract(phOffset);
            var endOfTodayUtc = startOfTodayUtc.AddDays(1);

            // 1. Non-cancelled orders placed today
            var todayOrdersCount = await _context.Orders
                .CountAsync(o => o.OrderDate >= startOfTodayUtc
                              && o.OrderDate < endOfTodayUtc
                              && o.Status != OrderStatuses.Cancelled);

            // 2. Orders currently waiting or being prepared in the kitchen/counter queue
            var activeOrdersCount = await _context.Orders
                .CountAsync(o => o.Status == OrderStatuses.Pending
                              || o.Status == OrderStatuses.Preparing);

            // 3. Non-cancelled orders that have not yet been marked as Paid
            var pendingPaymentsCount = await _context.Orders
                .CountAsync(o => o.Status != OrderStatuses.Cancelled
                              && o.PaymentStatus != OrderPaymentStatuses.Paid);

            // 4. Today's sales (summed in memory after projection to avoid SQLite decimal SUM limits)
            var todayPaidAmounts = await _context.Orders
                .Where(o => o.OrderDate >= startOfTodayUtc
                         && o.OrderDate < endOfTodayUtc
                         && o.Status != OrderStatuses.Cancelled
                         && (o.PaymentStatus == OrderPaymentStatuses.Paid
                             || o.Status == OrderStatuses.Completed))
                .Select(o => o.TotalAmount)
                .ToListAsync();

            var todaySalesTotal = todayPaidAmounts.Sum();

            ViewBag.TodayOrdersCount = todayOrdersCount;
            ViewBag.ActiveOrdersCount = activeOrdersCount;
            ViewBag.PendingPaymentsCount = pendingPaymentsCount;
            ViewBag.TodaySalesFormatted = "₱" + todaySalesTotal.ToString("N2", CultureInfo.InvariantCulture);
        }
        else
        {
            var currentCustomer = await _customerSessionService.GetCurrentCustomerAsync();
            ViewBag.CurrentCustomer = currentCustomer;
            ViewBag.TableLabel = string.IsNullOrWhiteSpace(table) ? null : table.Trim();

            if (currentCustomer != null)
            {
                ViewBag.MyOrdersCount = await _context.Orders
                    .CountAsync(o => o.CustomerId == currentCustomer.Id);
            }
        }

        return View();
    }

    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Owner,Employee")]
    public IActionResult BusinessDetails()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}