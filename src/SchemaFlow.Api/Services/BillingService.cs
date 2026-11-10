using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SchemaFlow.Api.Services;

/// <summary>
/// Lemon Squeezy ödeme platformundan gelen Webhook bildirimlerini doğrulayan
/// ve kullanıcı aboneliklerini yöneten servis sınıfıdır.
/// </summary>
public class BillingService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BillingService> _logger;

    public BillingService(IConfiguration configuration, ILogger<BillingService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lemon Squeezy'nin gönderdiği HMAC-SHA256 imzasını kontrol ederek
    /// isteğin gerçekten Lemon Squeezy'den gelip gelmediğini doğrular (Sahte ödeme koruması).
    /// </summary>
    public bool VerifySignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var secret = _configuration["LemonSqueezy:WebhookSecret"] ?? "dev_secret_key";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(bodyBytes);
        var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

        return string.Equals(computedSignature, signatureHeader.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gelen webhook bildirimini olay türüne (eventName) göre işler.
    /// </summary>
    public async Task<bool> ProcessWebhookAsync(string eventName, JsonElement payload)
    {
        _logger.LogInformation("Lemon Squeezy Webhook Alındı: {EventName}", eventName);

        switch (eventName.ToLowerInvariant())
        {
            case "subscription_created":
            case "subscription_updated":
                return await HandleSubscriptionCreatedOrUpdated(payload);

            case "subscription_cancelled":
            case "subscription_expired":
                return await HandleSubscriptionCancelled(payload);

            default:
                _logger.LogInformation("Desteklenmeyen veya dikkate alınmayan webhook olayı: {EventName}", eventName);
                return true;
        }
    }

    private Task<bool> HandleSubscriptionCreatedOrUpdated(JsonElement payload)
    {
        // Lemon Squeezy JSON yapısından müşteri verilerini çek
        try
        {
            var data = payload.GetProperty("data");
            var attributes = data.GetProperty("attributes");

            var userEmail = attributes.GetProperty("user_email").GetString();
            var customerId = attributes.GetProperty("customer_id").GetInt64().ToString();
            var status = attributes.GetProperty("status").GetString(); // 'active', 'on_trial', 'past_due'
            var productName = attributes.GetProperty("product_name").GetString() ?? "SchemaFlow Pro";

            var tier = productName.Contains("Team", StringComparison.OrdinalIgnoreCase) ? "team" : "pro";

            _logger.LogInformation("Kullanıcı aboneliği güncellendi: Email: {Email}, Paket: {Tier}, Durum: {Status}", userEmail, tier, status);

            // Gerçek senaryoda burada Supabase 'profiles' tablosundaki 'subscription_tier' güncellenir
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook abonelik verisi çözülürken hata oluştu.");
            return Task.FromResult(false);
        }
    }

    private Task<bool> HandleSubscriptionCancelled(JsonElement payload)
    {
        try
        {
            var data = payload.GetProperty("data");
            var attributes = data.GetProperty("attributes");
            var userEmail = attributes.GetProperty("user_email").GetString();

            _logger.LogInformation("Kullanıcı aboneliği iptal edildi: Email: {Email}. Paket 'free'ye çekiliyor.", userEmail);

            // Supabase 'profiles' tablosunda 'subscription_tier = free' olarak güncellenir
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Abonelik iptal verisi çözülürken hata oluştu.");
            return Task.FromResult(false);
        }
    }
}
