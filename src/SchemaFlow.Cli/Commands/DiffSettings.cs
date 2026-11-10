using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// 'diff' komutunun kabul edeceği komut satırı parametrelerini tanımlar.
/// </summary>
public class DiffSettings : CommandSettings
{
    [Description("Karşılaştırma için temel (eski) şema dosyası yolu (SQL, CS, PRISMA, XML, PY, RB).")]
    [CommandOption("-b|--base <PATH>")]
    public string BasePath { get; set; } = string.Empty;

    [Description("Karşılaştırma için hedef (yeni) şema dosyası yolu (SQL, CS, PRISMA, XML, PY, RB).")]
    [CommandOption("-t|--target <PATH>")]
    public string TargetPath { get; set; } = string.Empty;

    [Description("Eğer kritik bir canlı sistem bozucu (Breaking Change) tespit edilirse komut hata kodu (exit code 1) ile sonlansın mı? (CI/CD PR denetimleri için idealdir).")]
    [CommandOption("--fail-on-breaking")]
    public bool FailOnBreaking { get; set; }

    [Description("Oluşturulan değişiklik raporunun kaydedileceği opsiyonel Markdown dosyası.")]
    [CommandOption("-o|--output <PATH>")]
    public string? OutputPath { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
        {
            return ValidationResult.Error("Lütfen temel (eski) şema dosyasını belirtin: -b|--base <DOSYA>");
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            return ValidationResult.Error("Lütfen hedef (yeni) şema dosyasını belirtin: -t|--target <DOSYA>");
        }

        if (!File.Exists(BasePath))
        {
            return ValidationResult.Error($"Temel dosya bulunamadı: '{BasePath}'");
        }

        if (!File.Exists(TargetPath))
        {
            return ValidationResult.Error($"Hedef dosya bulunamadı: '{TargetPath}'");
        }

        return ValidationResult.Success();
    }
}
