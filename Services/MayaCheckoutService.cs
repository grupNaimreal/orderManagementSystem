// Services/MayaCheckoutService.cs
// Integrates QRDER (Bean Crate Cafe) with Maya Checkout v1 Sandbox API
// (https://pg-sandbox.paymaya.com/checkout/v1/checkouts) supporting QRPh, Maya Wallet, and Cards.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using orderManagementSystem.Models;

namespace orderManagementSystem.Services;

public class MayaCheckoutService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _publicKey;
    private readonly string _secretKey;

    public MayaCheckoutService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = (configuration["Maya:BaseUrl"] ?? "https://pg-sandbox.paymaya.com").TrimEnd('/');
        // Official Maya Developer Hub public sandbox credentials scoped for Maya Checkout (/checkout/v1/checkouts)
        _publicKey = configuration["Maya:PublicKey"] ?? "pk-Z0OSzLvIcOI2UIvDhdTGVVfRSSeiGStnceqwUE7n0Ah";
        _secretKey = configuration["Maya:SecretKey"] ?? "sk-X8qolYjy62kIzEbr0QRK1h4b4KDVHaNcwMYk39jInSl";
    }

    public async Task<MayaCheckoutSessionResult> CreateCheckoutAsync(
        Order order,
        string successUrl,
        string failureUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default)
    {
        var nameParts = (order.CustomerName ?? "Bean Crate Guest")
            .Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "Guest";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "Customer";

        var lineItems = (order.Items ?? new List<OrderItem>())
            .Select(item =>
            {
                var unitVal = Math.Round(item.UnitPrice, 2, MidpointRounding.AwayFromZero);
                var totalVal = Math.Round(item.LineTotal, 2, MidpointRounding.AwayFromZero);
                return new
                {
                    name = item.ProductNameSnapshot,
                    quantity = item.Quantity,
                    code = $"ITEM-{item.ProductId}",
                    amount = new
                    {
                        value = unitVal
                    },
                    totalAmount = new
                    {
                        value = totalVal
                    }
                };
            })
            .ToList();

        var payload = new
        {
            totalAmount = new
            {
                value = Math.Round(order.TotalAmount, 2, MidpointRounding.AwayFromZero),
                currency = "PHP"
            },
            buyer = new
            {
                firstName,
                lastName
            },
            items = lineItems,
            redirectUrl = new
            {
                success = successUrl,
                failure = failureUrl,
                cancel = cancelUrl
            },
            requestReferenceNumber = $"QRDER-{order.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/checkout/v1/checkouts");
        request.Headers.Authorization = BuildBasicAuth(_publicKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Maya Checkout returned HTTP {(int)response.StatusCode}: {ExtractMayaError(responseBody)}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var checkoutId = root.TryGetProperty("checkoutId", out var idEl) ? idEl.GetString() : null;
        var redirectUrl = root.TryGetProperty("redirectUrl", out var urlEl) ? urlEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(checkoutId) || string.IsNullOrWhiteSpace(redirectUrl))
        {
            throw new InvalidOperationException("Maya Checkout response did not include checkoutId or redirectUrl.");
        }

        return new MayaCheckoutSessionResult(checkoutId, redirectUrl);
    }

    public async Task<MayaCheckoutStatusResult> GetCheckoutStatusAsync(
        string checkoutId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(checkoutId))
        {
            return new MayaCheckoutStatusResult(false, false, false, "MISSING", "MISSING", null);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_baseUrl}/checkout/v1/checkouts/{Uri.EscapeDataString(checkoutId)}");
        request.Headers.Authorization = BuildBasicAuth(_secretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Unable to verify Maya Checkout status (HTTP {(int)response.StatusCode}): {ExtractMayaError(responseBody)}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var statusEl)
            ? (statusEl.GetString() ?? string.Empty).ToUpperInvariant()
            : string.Empty;

        var paymentStatus = root.TryGetProperty("paymentStatus", out var payStatusEl)
            ? (payStatusEl.GetString() ?? string.Empty).ToUpperInvariant()
            : string.Empty;

        string? paymentRef = null;
        if (root.TryGetProperty("transactionReferenceNumber", out var txRefEl)
            && txRefEl.ValueKind == JsonValueKind.String)
        {
            paymentRef = txRefEl.GetString();
        }
        else if (root.TryGetProperty("receiptNumber", out var receiptEl)
            && receiptEl.ValueKind == JsonValueKind.String)
        {
            paymentRef = receiptEl.GetString();
        }

        var isPaid = paymentStatus == "PAYMENT_SUCCESS" || status == "COMPLETED";
        var isFailed = paymentStatus == "PAYMENT_FAILED" || status == "FAILED";
        var isExpired = paymentStatus == "PAYMENT_EXPIRED" || status == "EXPIRED";

        return new MayaCheckoutStatusResult(
            isPaid,
            isFailed,
            isExpired,
            status,
            paymentStatus,
            paymentRef ?? checkoutId);
    }

    private static AuthenticationHeaderValue BuildBasicAuth(string apiKey)
    {
        var raw = Encoding.UTF8.GetBytes($"{apiKey.Trim()}:");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private static string ExtractMayaError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Empty response from Maya Sandbox.";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString() ?? body;
            }
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                return err.GetString() ?? body;
            }
        }
        catch
        {
            // Return truncated plain body if not JSON
        }

        return body.Length > 200 ? body[..200] : body;
    }
}

public sealed record MayaCheckoutSessionResult(string CheckoutId, string RedirectUrl);

public sealed record MayaCheckoutStatusResult(
    bool IsPaid,
    bool IsFailed,
    bool IsExpired,
    string RawStatus,
    string RawPaymentStatus,
    string? PaymentReference);