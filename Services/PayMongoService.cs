using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using orderManagementSystem.Models;

namespace orderManagementSystem.Services;

public sealed record PayMongoCheckoutSession(
    string Id,
    string CheckoutUrl);

public sealed record PayMongoSessionSnapshot(
    string Status,
    string? PaidPaymentId,
    DateTime? PaidAtUtc);

public class PayMongoService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public PayMongoService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<PayMongoCheckoutSession> CreateCheckoutSessionAsync(
        Order order,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default)
    {
        var secretKey = _configuration["PayMongo:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "PayMongo API credentials are not configured.");
        }

        ValidateHttpsUrl(successUrl);
        ValidateHttpsUrl(cancelUrl);

        if (order.Items == null || order.Items.Count == 0)
            throw new InvalidOperationException("The order has no items.");

        static long ToCentavos(decimal amount)
        {
            return decimal.ToInt64(
                decimal.Round(amount * 100m, 0,
                    MidpointRounding.AwayFromZero));
        }

        var lineItems = order.Items.Select(item => new
        {
            name = item.ProductNameSnapshot,
            amount = ToCentavos(item.UnitPrice),
            currency = "PHP",
            quantity = item.Quantity
        }).ToArray();

        var requestBody = new
        {
            data = new
            {
                attributes = new
                {
                    line_items = lineItems,
                    payment_method_types = new[] { "gcash", "qrph" },
                    success_url = successUrl,
                    cancel_url = cancelUrl,
                    reference_number = $"BC-ORDER-{order.Id}",
                    metadata = new Dictionary<string, string>
                    {
                        ["order_id"] = order.Id.ToString()
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.paymongo.com/v2/checkout_sessions");
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            "bean-crate-order-" + order.Id);

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(secretKey + ":"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        request.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"PayMongo returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(responseBody);

        var data = document.RootElement.GetProperty("data");
        var sessionId = data.GetProperty("id").GetString();
        var checkoutUrl = data
            .GetProperty("attributes")
            .GetProperty("checkout_url")
            .GetString();

        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(checkoutUrl))
        {
            throw new InvalidOperationException(
                "PayMongo returned an incomplete checkout session.");
        }

        if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var checkoutUri)
            || checkoutUri.Scheme != Uri.UriSchemeHttps
            || checkoutUri.Host != "checkout.paymongo.com")
        {
            throw new InvalidOperationException(
                "PayMongo returned an invalid checkout URL.");
        }

        return new PayMongoCheckoutSession(sessionId, checkoutUrl);
    }

    public bool VerifyWebhookSignature(
        string rawBody,
        string? signatureHeader)
    {
        var webhookSecret = _configuration["PayMongo:WebhookSecret"];

        if (string.IsNullOrWhiteSpace(webhookSecret)
            || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var parts = signatureHeader.Split(
            ',',
            StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);

        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');

            if (separator <= 0)
                continue;

            values[part[..separator]] = part[(separator + 1)..];
        }

        if (!values.TryGetValue("t", out var timestamp)
            || !long.TryParse(timestamp, out var timestampSeconds))
        {
            return false;
        }

        DateTimeOffset signatureTime;

        try
        {
            signatureTime = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if ((DateTimeOffset.UtcNow - signatureTime).Duration()
            > TimeSpan.FromMinutes(5))
        {
            return false;
        }

        var mode = _configuration["PayMongo:Mode"] ?? "test";
        var signatureKey = mode.Equals(
            "live", StringComparison.OrdinalIgnoreCase) ? "li" : "te";

        if (!values.TryGetValue(signatureKey, out var suppliedSignature)
            || string.IsNullOrWhiteSpace(suppliedSignature))
            return false;

        var signedPayload = timestamp + "." + rawBody;

        var expectedSignature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(webhookSecret),
            Encoding.UTF8.GetBytes(signedPayload));

        try
        {
            var suppliedBytes = Convert.FromHexString(suppliedSignature);

            return CryptographicOperations.FixedTimeEquals(
                expectedSignature, suppliedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }


    public async Task<PayMongoSessionSnapshot>
        ExpireAndRetrieveCheckoutSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/v1/checkout_sessions/" +
            Uri.EscapeDataString(sessionId) + "/expire");

        using var response = await _httpClient.SendAsync(
            request, cancellationToken);

        // A 400 may mean paid, processing, or already expired.
        // Retrieve the session to distinguish these cases.
        if (!response.IsSuccessStatusCode
            && response.StatusCode != System.Net.HttpStatusCode.BadRequest)
        {
            response.EnsureSuccessStatusCode();
        }

        return await RetrieveCheckoutSessionAsync(
            sessionId, cancellationToken);
    }

    private async Task<PayMongoSessionSnapshot>
        RetrieveCheckoutSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/v1/checkout_sessions/" + Uri.EscapeDataString(sessionId));

        using var response = await _httpClient.SendAsync(
            request, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);

        var data = document.RootElement.GetProperty("data");
        var attributes = data.GetProperty("attributes");
        var status = attributes.GetProperty("status").GetString() ?? "unknown";

        string? paymentId = null;
        DateTime? paidAtUtc = null;

        if (attributes.TryGetProperty("payments", out var payments)
            && payments.ValueKind == JsonValueKind.Array)
        {
            foreach (var payment in payments.EnumerateArray())
            {
                if (!payment.TryGetProperty("attributes", out var paymentAttributes)
                    || paymentAttributes.ValueKind != JsonValueKind.Object)
                    continue;

                if (paymentAttributes.TryGetProperty("status", out var paymentStatus)
                    && paymentStatus.GetString() == "paid")
                {
                    paymentId = payment.TryGetProperty("id", out var id)
                        ? id.GetString()
                        : null;

                    if (paymentAttributes.TryGetProperty("paid_at", out var paidAt)
                        && paidAt.TryGetInt64(out var unixTime))
                    {
                        try
                        {
                            paidAtUtc = DateTimeOffset
                                .FromUnixTimeSeconds(unixTime).UtcDateTime;
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // Keep paidAtUtc null; caller will use current UTC time.
                        }
                    }

                    break;
                }
            }
        }

        return new PayMongoSessionSnapshot(status, paymentId, paidAtUtc);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path)
    {
        var secretKey = _configuration["PayMongo:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "PayMongo API credentials are not configured.");
        }

        var request = new HttpRequestMessage(
            method, "https://api.paymongo.com" + path);

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(secretKey + ":"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        return request;
    }

    private static void ValidateHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "PayMongo redirect URLs must use HTTPS.");
        }
    }
}
