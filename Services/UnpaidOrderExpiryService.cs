// Services/UnpaidOrderExpiryService.cs
// Background worker that cancels unpaid Pending orders after 10 minutes,
// checking Maya Checkout Sandbox and PayMongo before expiring online orders.

using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;

namespace orderManagementSystem.Services;

public sealed class UnpaidOrderExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnpaidOrderExpiryService> _logger;

    public UnpaidOrderExpiryService(
        IServiceScopeFactory scopeFactory,
        ILogger<UnpaidOrderExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireDueOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The unpaid-order expiry check failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ExpireDueOrdersAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payMongo = scope.ServiceProvider.GetRequiredService<PayMongoService>();
        var mayaCheckout = scope.ServiceProvider.GetRequiredService<MayaCheckoutService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-10);

        var orders = await context.Orders
            .Where(o =>
                o.Status == OrderStatuses.Pending
                && o.PaymentStatus == OrderPaymentStatuses.Pending
                && o.OrderDate <= cutoff)
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            if (order.PaymentMethod == OrderPaymentMethods.Cash)
            {
                MarkExpired(order);
                continue;
            }

            if (order.PaymentMethod == "Maya")
            {
                if (string.IsNullOrWhiteSpace(order.PayMongoCheckoutSessionId))
                {
                    MarkExpired(order);
                    continue;
                }

                try
                {
                    var status = await mayaCheckout.GetCheckoutStatusAsync(
                        order.PayMongoCheckoutSessionId,
                        cancellationToken);

                    if (status.IsPaid)
                    {
                        order.PaymentStatus = OrderPaymentStatuses.Paid;
                        order.PaymentPaidAtUtc = DateTime.UtcNow;
                        order.PayMongoPaymentId = status.PaymentReference ?? order.PayMongoCheckoutSessionId;
                    }
                    else
                    {
                        MarkExpired(order);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        exception,
                        "Could not verify Maya Checkout status for order {OrderId}; expiring locally.",
                        order.Id);
                    MarkExpired(order);
                }

                continue;
            }

            if (order.PaymentMethod == OrderPaymentMethods.PayMongo)
            {
                if (string.IsNullOrWhiteSpace(order.PayMongoCheckoutSessionId))
                {
                    MarkExpired(order);
                    continue;
                }

                try
                {
                    var session = await payMongo.ExpireAndRetrieveCheckoutSessionAsync(
                        order.PayMongoCheckoutSessionId,
                        cancellationToken);

                    if (!string.IsNullOrWhiteSpace(session.PaidPaymentId))
                    {
                        order.PayMongoPaymentId = session.PaidPaymentId;
                        order.PaymentStatus = OrderPaymentStatuses.Paid;
                        order.PaymentPaidAtUtc = session.PaidAtUtc ?? DateTime.UtcNow;
                    }
                    else if (session.Status.Equals("expired", StringComparison.OrdinalIgnoreCase))
                    {
                        MarkExpired(order);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        exception,
                        "Could not expire or verify PayMongo session for order {OrderId}.",
                        order.Id);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static void MarkExpired(Order order)
    {
        order.PaymentStatus = OrderPaymentStatuses.Expired;
        order.Status = OrderStatuses.Cancelled;
    }
}