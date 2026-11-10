using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// 'seed' komutunun parametrelerini tanımlar.
/// </summary>
public class SeedSettings : CommandSettings
{
    [Description("Test verisi üretilecek şema veya migration dosyasının yolu (.sql, .prisma, .cs, .py, .rb, .xml).")]
    [CommandOption("-i|--input <PATH>")]
    public string InputPath { get; set; } = string.Empty;

    [Description("Her bir tablo için üretilecek sahte kayıt (satır) sayısı.")]
    [CommandOption("-n|--count <COUNT>")]
    [DefaultValue(10)]
    public int Count { get; set; } = 10;

    [Description("Oluşturulan SQL seed komutlarının kaydedileceği hedef dosya yolu (örn: seed.sql).")]
    [CommandOption("-o|--output <PATH>")]
    [DefaultValue("seed.sql")]
    public string OutputPath { get; set; } = "seed.sql";

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            return ValidationResult.Error("Lütfen şema dosyasını belirtin: -i|--input <DOSYA>");
        }

        if (!File.Exists(InputPath))
        {
            return ValidationResult.Error($"Şema dosyası bulunamadı: '{InputPath}'");
        }

        if (Count <= 0)
        {
            return ValidationResult.Error("Kayıt sayısı (count) 0'dan büyük olmalıdır.");
        }

        return ValidationResult.Success();
    }
}
