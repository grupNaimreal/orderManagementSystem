// Controllers/ReportsController.cs
// Generates daily, weekly, monthly, and custom-range Sales Reports for Owner and Employee PC users,
// including 12% VAT breakdown, Cash vs. Online (Maya / PayMongo) totals, top-selling products, and CSV export.

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

[Authorize(Roles = "Owner,Employee")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly AuditLogService _auditLog;

    public ReportsController(
        ApplicationDbContext context,
        AuditLogService auditLog)
    {
        _context = context;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string range = "today", DateTime? startDate = null, DateTime? endDate = null)
    {
        var (startLocal, endLocal, normalizedRange) = ResolveDateRange(range, startDate, endDate);

        var phOffset = TimeSpan.FromHours(8);
        var startUtc = startLocal.Date.Subtract(phOffset);
        var endUtc = endLocal.Date.AddDays(1).Subtract(phOffset);

        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.OrderDate >= startUtc && o.OrderDate < endUtc)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var validOrders = orders
            .Where(o => o.Status != OrderStatuses.Cancelled)
            .ToList();

        var cancelledCount = orders.Count(o => o.Status == OrderStatuses.Cancelled);

        // Compute Gross Sales, Net Sales, and 12% VAT in C# memory (safe for SQLite decimal)
        decimal grossSales = validOrders.Sum(o => o.TotalAmount);
        decimal netSales = Math.Round(grossSales / 1.12m, 2, MidpointRounding.AwayFromZero);
        decimal vatAmount = grossSales - netSales;

        decimal cashSales = validOrders
            .Where(o => o.PaymentMethod == OrderPaymentMethods.Cash)
            .Sum(o => o.TotalAmount);

        decimal cashlessSales = validOrders
            .Where(o => o.PaymentMethod == "Maya" || o.PaymentMethod == OrderPaymentMethods.PayMongo)
            .Sum(o => o.TotalAmount);

        var topProducts = validOrders
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductNameSnapshot)
            .Select(g => new TopProductReportRow
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(r => r.QuantitySold)
            .ThenByDescending(r => r.Revenue)
            .Take(10)
            .ToList();

        var vm = new SalesReportViewModel
        {
            Range = normalizedRange,
            StartDate = startLocal.Date,
            EndDate = endLocal.Date,
            TotalOrders = validOrders.Count,
            CompletedOrders = validOrders.Count(o => o.Status == OrderStatuses.Completed),
            CancelledOrders = cancelledCount,
            GrossSales = grossSales,
            NetSales = netSales,
            VatAmount = vatAmount,
            CashSales = cashSales,
            CashlessSales = cashlessSales,
            TopProducts = topProducts,
            Orders = orders
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string range = "today", DateTime? startDate = null, DateTime? endDate = null)
    {
        var (startLocal, endLocal, _) = ResolveDateRange(range, startDate, endDate);

        var phOffset = TimeSpan.FromHours(8);
        var startUtc = startLocal.Date.Subtract(phOffset);
        var endUtc = endLocal.Date.AddDays(1).Subtract(phOffset);
        var manilaTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        var orders = await _context.Orders
            .Where(o => o.OrderDate >= startUtc && o.OrderDate < endUtc)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("OrderNumber,DatePHT,Customer,PaymentMethod,PaymentStatus,OrderStatus,NetSalesPHP,Vat12PercentPHP,GrossTotalPHP");

        foreach (var o in orders)
        {
            var localDt = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(o.OrderDate, DateTimeKind.Utc),
                manilaTz
            );

            var net = Math.Round(o.TotalAmount / 1.12m, 2, MidpointRounding.AwayFromZero);
            var vat = o.TotalAmount - net;
            var safeCustomer = "\"" + (o.CustomerName ?? "").Replace("\"", "\"\"") + "\"";

            sb.AppendLine(string.Join(",",
                $"#{o.Id}",
                localDt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                safeCustomer,
                o.PaymentMethod,
                o.PaymentStatus,
                o.Status,
                net.ToString("F2", CultureInfo.InvariantCulture),
                vat.ToString("F2", CultureInfo.InvariantCulture),
                o.TotalAmount.ToString("F2", CultureInfo.InvariantCulture)
            ));
        }

        await _auditLog.LogAsync(
            "ReportExported",
            "SalesReport",
            $"{startLocal:yyyyMMdd}-{endLocal:yyyyMMdd}",
            $"Exported CSV sales report ({orders.Count} orders) for {startLocal:MMM dd, yyyy} to {endLocal:MMM dd, yyyy}."
        );

        var fileName = $"qrder-beancrate-sales-{startLocal:yyyyMMdd}-to-{endLocal:yyyyMMdd}.csv";
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", fileName);
    }

    private static (DateTime StartLocal, DateTime EndLocal, string NormalizedRange) ResolveDateRange(
        string range,
        DateTime? startDate,
        DateTime? endDate)
    {
        var nowPh = DateTime.UtcNow.AddHours(8).Date;

        if (startDate.HasValue && endDate.HasValue)
        {
            var s = startDate.Value.Date;
            var e = endDate.Value.Date < s ? s : endDate.Value.Date;
            return (s, e, "custom");
        }

        return (range ?? "today").ToLowerInvariant() switch
        {
            "week" => (nowPh.AddDays(-6), nowPh, "week"),
            "month" => (new DateTime(nowPh.Year, nowPh.Month, 1), nowPh, "month"),
            _ => (nowPh, nowPh, "today")
        };
    }
}

public class SalesReportViewModel
{
    public string Range { get; set; } = "today";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal GrossSales { get; set; }
    public decimal NetSales { get; set; }
    public decimal VatAmount { get; set; }
    public decimal CashSales { get; set; }
    public decimal CashlessSales { get; set; }
    public List<TopProductReportRow> TopProducts { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
}

public class TopProductReportRow
{
    public string ProductName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}