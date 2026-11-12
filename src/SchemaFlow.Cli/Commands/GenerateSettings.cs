using SchemaFlow.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// 'generate' komutunun kabul edeceği komut satırı parametrelerini ve ayarlarını tanımlar.
/// </summary>
public class GenerateSettings : CommandSettings
{
    [Description("Ayrıştırılacak SQL dosyası veya migration dosyalarını içeren klasör yolu.")]
    [CommandOption("-i|--input <PATH>")]
    public string InputPath { get; set; } = string.Empty;

    [Description("Oluşturulacak çıktı dosyasının kaydedileceği yol (örn: ./docs/database.md).")]
    [CommandOption("-o|--output <PATH>")]
    [DefaultValue("docs_output.md")]
    public string OutputPath { get; set; } = "docs_output.md";

    [Description("Hedef veritabanı motoru: postgres, mysql, mssql, sqlite.")]
    [CommandOption("-d|--dialect <DIALECT>")]
    [DefaultValue(DatabaseDialect.PostgreSql)]
    public DatabaseDialect Dialect { get; set; } = DatabaseDialect.PostgreSql;

    [Description("Çıktı biçimi: 'markdown' (Veri sözlüğü + diyagram) veya 'mermaid' (Sadece erDiagram).")]
    [CommandOption("-f|--format <FORMAT>")]
    [DefaultValue("markdown")]
    public string Format { get; set; } = "markdown";

    [Description("Hedef dosyadaki <!-- BEGIN_SCHEMAFLOW --> etiketleri arasına akıllı enjeksiyon yapar (README.md için idealdir).")]
    [CommandOption("--inject")]
    public bool Inject { get; set; }

    [Description("Şema oluşturulduktan sonra interaktif görsel diyagramı varsayılan tarayıcıda (Chrome/Edge) anında açar.")]
    [CommandOption("--open")]
    public bool OpenInBrowser { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            return ValidationResult.Error("Lütfen ayrıştırılacak SQL dosyasını veya klasörünü belirtin: --input <YOL>");
        }

        if (!File.Exists(InputPath) && !Directory.Exists(InputPath))
        {
            return ValidationResult.Error($"Belirtilen girdi yolu bulunamadı: '{InputPath}'");
        }

        return ValidationResult.Success();
    }
}
