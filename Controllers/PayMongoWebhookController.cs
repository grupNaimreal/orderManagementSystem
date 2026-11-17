using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;
using orderManagementSystem.Services;

namespace orderManagementSystem.Controllers;

[ApiController]
[Route("webhooks/paymongo")]
public class PayMongoWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly PayMongoService _payMongoService;
    private readonly ILogger<PayMongoWebhookController> _logger;

    public PayMongoWebhookController(
        ApplicationDbContext context,
        PayMongoService payMongoService,
        ILogger<PayMongoWebhookController> logger)
    {
        _context = context;
        _payMongoService = payMongoService;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Receive(
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        var signature = Request.Headers["Paymongo-Signature"]
            .FirstOrDefault();

        if (!_payMongoService.VerifyWebhookSignature(rawBody, signature))
            return Unauthorized();

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (!TryGetObject(root, "data", out var eventData))
                return BadRequest();

            JsonElement session;
            string? eventType;

            // Current Hosted Checkout quick-start envelope.
            if (TryGetObject(eventData, "data", out session)
                && ReadString(eventData, "type") is string quickStartType)
            {
                eventType = quickStartType;
            }
            // Also accept the event-resource envelope.
            else if (TryGetObject(eventData, "attributes", out var eventAttributes)
                && TryGetObject(eventAttributes, "data", out session))
            {
                eventType = ReadString(eventAttributes, "type");
            }
            else
            {
                return BadRequest();
            }

            if (eventType != "checkout_session.payment.paid")
                return Ok();

            if (!TryGetObject(session, "attributes", out var sessionAttributes))
                return BadRequest();

            var sessionId = ReadString(session, "id");
            var referenceNumber =
                ReadString(sessionAttributes, "reference_number");

            if (string.IsNullOrWhiteSpace(sessionId)
                || string.IsNullOrWhiteSpace(referenceNumber)
                || !referenceNumber.StartsWith(
                    "BC-ORDER-", StringComparison.Ordinal)
                || !int.TryParse(
                    referenceNumber["BC-ORDER-".Length..],
                    out var orderId))
            {
                return Ok();
            }

            var order = await _context.Orders.FirstOrDefaultAsync(
                o => o.Id == orderId
                    && o.PayMongoCheckoutSessionId == sessionId
                    && o.PaymentMethod == OrderPaymentMethods.PayMongo,
                cancellationToken);

            if (order == null)
                return Ok();

            // Webhooks can be delivered more than once.
            if (order.PaymentStatus == OrderPaymentStatuses.Paid)
                return Ok();

            if (!sessionAttributes.TryGetProperty(
                    "payments", out var payments)
                || payments.ValueKind != JsonValueKind.Array)
            {
                return BadRequest();
            }

            foreach (var payment in payments.EnumerateArray())
            {
                if (!TryGetObject(
                        payment, "attributes", out var paymentAttributes))
                    continue;

                if (ReadString(paymentAttributes, "status") != "paid")
                    continue;

                var paymentId = ReadString(payment, "id");
                var currency = ReadString(paymentAttributes, "currency");
                var amount = ReadInt64(paymentAttributes, "amount");

                var expectedAmount = decimal.ToInt64(
                    decimal.Round(
                        order.TotalAmount * 100m,
                        0,
                        MidpointRounding.AwayFromZero));

                // This checkout request does not enable pass-on fees.
                // Therefore the payment amount must equal the order total.
                if (string.IsNullOrWhiteSpace(paymentId)
                    || currency != "PHP"
                    || amount != expectedAmount)
                {
                    _logger.LogWarning(
                        "PayMongo payment validation failed for order {OrderId}.",
                        order.Id);

                    return Ok();
                }

                order.PayMongoPaymentId = paymentId;
                order.PaymentStatus = OrderPaymentStatuses.Paid;
                order.PaymentPaidAtUtc = ReadPaidAt(paymentAttributes);

                await _context.SaveChangesAsync(cancellationToken);
                return Ok();
            }

            return Ok();
        }
        catch (JsonException)
        {
            return BadRequest();
        }
    }

    private static bool TryGetObject(
        JsonElement parent,
        string property,
        out JsonElement value)
    {
        value = default;

        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static string? ReadString(
        JsonElement element,
        string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? ReadInt64(
        JsonElement element,
        string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static DateTime ReadPaidAt(JsonElement paymentAttributes)
    {
        if (paymentAttributes.TryGetProperty("paid_at", out var value))
        {
            if (value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var unixTime))
            {
                try
                {
                    return DateTimeOffset
                        .FromUnixTimeSeconds(unixTime).UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Use current UTC time below.
                }
            }

            if (value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    value.GetString(), out var parsed))
            {
                return parsed.UtcDateTime;
            }
        }

        return DateTime.UtcNow;
    }
}
