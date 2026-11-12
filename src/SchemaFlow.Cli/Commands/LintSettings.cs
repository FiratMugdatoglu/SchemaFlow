using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// 'lint' komutunun parametrelerini tanımlar.
/// </summary>
public class LintSettings : CommandSettings
{
    [Description("Denetlenecek şema veya migration dosyasının yolu (.sql, .prisma, .cs, .py, .rb, .xml).")]
    [CommandOption("-i|--input <PATH>")]
    public string InputPath { get; set; } = string.Empty;

    [Description("Linter kritik bir hata (Error) bulduğunda komut hata koduyla (exit code 1) sonlansın mı? (CI/CD hatları için varsayılan true).")]
    [CommandOption("--fail-on-errors")]
    [DefaultValue(true)]
    public bool FailOnErrors { get; set; } = true;

    [Description("Linter uyarı (Warning) bulduğunda da hata koduyla (exit code 1) sonlansın mı?")]
    [CommandOption("--fail-on-warnings")]
    public bool FailOnWarnings { get; set; }

    [Description("Oluşturulan denetim raporunun kaydedileceği opsiyonel Markdown dosyası.")]
    [CommandOption("-o|--output <PATH>")]
    public string? OutputPath { get; set; }

    [Description("Denetim raporunu ve şema görselini otomatik olarak tarayıcıda açar.")]
    [CommandOption("--open")]
    public bool OpenInBrowser { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            return ValidationResult.Error("Lütfen denetlenecek şema dosyasını belirtin: -i|--input <DOSYA>");
        }

        if (!File.Exists(InputPath))
        {
            return ValidationResult.Error($"Belirtilen dosya bulunamadı: '{InputPath}'");
        }

        return ValidationResult.Success();
    }
}
