using SchemaFlow.Api.Contracts;
using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Renderers;
using SchemaFlow.Api.Services;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 1. CORS İzinleri (Web Viewer ve SaaS arayüzünün API ile haberleşebilmesi için)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddScoped<BillingService>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ==============================================================
// 1. Sağlık Kontrolü (Health Check)
// ==============================================================
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Service = "SchemaFlow.Api",
    Version = "1.0.0",
    Timestamp = DateTime.UtcNow
}))
.WithName("HealthCheck")
.WithDescription("API servisinin ayakta olup olmadığını kontrol eder.");

// ==============================================================
// 2. SQL Ayrıştırma Uç Noktası (POST /api/v1/schema/parse)
// ==============================================================
app.MapPost("/api/v1/schema/parse", async (ParseRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Sql))
    {
        return Results.BadRequest(new { Error = "Lütfen ayrıştırılacak SQL metnini gönderin." });
    }

    var stopwatch = Stopwatch.StartNew();

    IDatabaseParser parser = ResolveParser(request.Dialect);
    var schema = await parser.ParseSqlAsync(request.Sql);

    stopwatch.Stop();

    var response = new ParseResponse(
        Success: true,
        Dialect: schema.Dialect.ToString(),
        TotalTables: schema.Tables.Count,
        TotalRelations: schema.GetAllForeignKeys().Count(),
        Schema: schema,
        ElapsedMilliseconds: stopwatch.ElapsedMilliseconds
    );

    return Results.Ok(response);
})
.WithName("ParseSql")
.WithDescription("Ham SQL metnini ayrıştırarak yapılandırılmış JSON şema nesnesi döner.");

// ==============================================================
// 3. Şema Render Uç Noktası (POST /api/v1/schema/render)
// ==============================================================
app.MapPost("/api/v1/schema/render", async (RenderRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Sql))
    {
        return Results.BadRequest(new { Error = "Lütfen ayrıştırılacak SQL metnini gönderin." });
    }

    var stopwatch = Stopwatch.StartNew();

    // 1. Parse et
    IDatabaseParser parser = ResolveParser(request.Dialect);
    var schema = await parser.ParseSqlAsync(request.Sql);

    // 2. Render et (Mermaid, Markdown veya HTML)
    ISchemaRenderer renderer = (request.Format?.ToLowerInvariant()) switch
    {
        "markdown" or "md" => new MarkdownRenderer(),
        "html" => new HtmlRenderer(),
        _ => new MermaidRenderer()
    };

    var content = renderer.Render(schema);
    stopwatch.Stop();

    var response = new RenderResponse(
        Success: true,
        Format: renderer.OutputFormat,
        Content: content,
        ElapsedMilliseconds: stopwatch.ElapsedMilliseconds
    );

    return Results.Ok(response);
})
.WithName("RenderSchema")
.WithDescription("Ham SQL'den doğrudan Mermaid.js ER diyagramı veya Markdown dokümantasyonu üretir.");

// ==============================================================
// 4. Fiyatlandırma Paketleri (GET /api/v1/billing/plans)
// ==============================================================
app.MapGet("/api/v1/billing/plans", () => Results.Ok(new[]
{
    new
    {
        Id = "free",
        Name = "Free (Developer)",
        PriceMonthly = 0,
        PriceYearly = 0,
        Interval = "month",
        Features = new[] 
        { 
            "Sınırsız yerel CLI kullanımı", 
            "PostgreSQL & MySQL DDL desteği", 
            "Mermaid ER diyagramı & Markdown çıktısı", 
            "Web Stüdyosu şema inceleme", 
            "Ayda 10 GitHub PR şema analizi" 
        }
    },
    new
    {
        Id = "pro",
        Name = "Pro (Solo Builder)",
        PriceMonthly = 9,
        PriceYearly = 79,
        Interval = "month",
        Features = new[] 
        { 
            "Tüm Free özellikleri", 
            "Sınırsız GitHub PR yorum botu", 
            "Breaking Change & Schema Diff uyarısı", 
            "Paylaşılabilir canlı şema web linki", 
            "PDF, SVG ve Yüksek Çözünürlüklü PNG çıktısı", 
            "Sınırsız bulut proje kaydetme" 
        }
    },
    new
    {
        Id = "team",
        Name = "Team (Ajanslar & Ekipler)",
        PriceMonthly = 29,
        PriceYearly = 249,
        Interval = "month",
        Features = new[] 
        { 
            "Tüm Pro özellikleri", 
            "5 Takım arkadaşı lisansı", 
            "White-Label (Özel şirket logosu & filigransız)", 
            "Çoklu proje ve ekip paylaşımı", 
            "Öncelikli e-posta desteği" 
        }
    }
}))
.WithName("GetPlans")
.WithDescription("SchemaFlow abonelik paketlerini ve özelliklerini döner.");

// ==============================================================
// 5. Lemon Squeezy Webhook Dinleyicisi (POST /api/v1/billing/webhook)
// ==============================================================
app.MapPost("/api/v1/billing/webhook", async (HttpRequest request, BillingService billingService) =>
{
    using var reader = new StreamReader(request.Body);
    var rawBody = await reader.ReadToEndAsync();

    // 1. İmzayı doğrula (Güvenlik / Sahte İstek Engelleme)
    var signature = request.Headers["X-Signature"].FirstOrDefault();
    if (!billingService.VerifySignature(rawBody, signature))
    {
        return Results.Unauthorized();
    }

    // 2. Olay türünü çek (X-Event-Name veya payload içinden)
    var eventName = request.Headers["X-Event-Name"].FirstOrDefault();

    using var doc = JsonDocument.Parse(rawBody);
    var root = doc.RootElement;

    if (string.IsNullOrEmpty(eventName) && root.TryGetProperty("meta", out var meta))
    {
        if (meta.TryGetProperty("event_name", out var evProp))
        {
            eventName = evProp.GetString();
        }
    }

    if (string.IsNullOrEmpty(eventName))
    {
        return Results.BadRequest(new { Error = "Etkinlik adı (event_name) tespit edilemedi." });
    }

    // 3. Webhook'u işle
    var success = await billingService.ProcessWebhookAsync(eventName, root);
    return success ? Results.Ok(new { Received = true }) : Results.StatusCode(500);
})
.WithName("LemonSqueezyWebhook")
.WithDescription("Lemon Squeezy'den gelen abonelik ödeme bildirimlerini dinler ve işler.");

app.Run();

// Yardımcı Parser Çözücü
static IDatabaseParser ResolveParser(string? dialect) => dialect?.ToLowerInvariant() switch
{
    "prisma" => new PrismaParser(),
    "efcore" or "csharp" or "dotnet" => new EfCoreParser(),
    "liquibase" or "xml" => new LiquibaseParser(),
    "django" or "python" => new DjangoParser(),
    "rails" or "ruby" => new RailsParser(),
    "mysql" or "mariadb" => new MySqlParser(),
    "postgres" or "postgresql" => new PostgreSqlParser(),
    _ => new PostgreSqlParser()
};
