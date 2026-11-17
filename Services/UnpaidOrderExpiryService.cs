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

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireDueOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "The unpaid-order expiry check failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ExpireDueOrdersAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var payMongo = scope.ServiceProvider
            .GetRequiredService<PayMongoService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-10);

        var orders = await context.Orders
            .Where(o =>
                o.Status == OrderStatuses.Pending
                && o.PaymentStatus == OrderPaymentStatuses.Pending
                && o.OrderDate <= cutoff)
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            if (order.PaymentMethod == OrderPaymentMethods.Cash
                || (order.PaymentMethod == OrderPaymentMethods.PayMongo
                    && string.IsNullOrWhiteSpace(
                        order.PayMongoCheckoutSessionId)))
            {
                MarkExpired(order);
                continue;
            }

            if (order.PaymentMethod != OrderPaymentMethods.PayMongo)
                continue;

            try
            {
                var session = await payMongo
                    .ExpireAndRetrieveCheckoutSessionAsync(
                        order.PayMongoCheckoutSessionId!,
                        cancellationToken);

                if (!string.IsNullOrWhiteSpace(session.PaidPaymentId))
                {
                    order.PayMongoPaymentId = session.PaidPaymentId;
                    order.PaymentStatus = OrderPaymentStatuses.Paid;
                    order.PaymentPaidAtUtc =
                        session.PaidAtUtc ?? DateTime.UtcNow;
                }
                else if (session.Status.Equals(
                    "expired", StringComparison.OrdinalIgnoreCase))
                {
                    MarkExpired(order);
                }
                // If payment is processing, leave it pending.
                // The next check or the paid webhook will resolve it.
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not expire or verify PayMongo session for order {OrderId}.",
                    order.Id);
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
