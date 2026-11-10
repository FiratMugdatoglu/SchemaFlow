using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// Şema dosyalarını mimari eksiklikler, performans darboğazları ve anti-pattern'lara karşı denetleyen CLI komutu.
/// </summary>
public class LintCommand : AsyncCommand<LintSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, LintSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("SchemaFlow Lint")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold grey]Veritabanı Şema Sağlık & Anti-Pattern Denetleyicisi[/]\n");

        var parser = ResolveParserForFile(settings.InputPath);
        DatabaseSchema schema = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Şema ayrıştırılıyor ve kurallar denetleniyor...", async ctx =>
            {
                var content = await File.ReadAllTextAsync(settings.InputPath, cancellationToken);
                schema = await parser.ParseSqlAsync(content, cancellationToken);
            });

        // Linter Motorunu Çalıştır
        var linter = new SchemaLinterEngine();
        var issues = linter.Analyze(schema);

        var errorCount = issues.Count(i => i.Severity == LintSeverity.Error);
        var warningCount = issues.Count(i => i.Severity == LintSeverity.Warning);
        var infoCount = issues.Count(i => i.Severity == LintSeverity.Info);

        // Hiç sorun yoksa
        if (issues.Count == 0)
        {
            AnsiConsole.MarkupLine("[bold green]✓ Tebrikler! Şemada hiçbir mimari hata veya performans riski tespit edilmedi.[/]\n");
            return 0;
        }

        // Bulguları Tablo Olarak Göster
        var table = new Spectre.Console.Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]Derece[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Kural[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Hedef[/]"));
        table.AddColumn(new TableColumn("[bold]Açıklama & Tavsiye[/]"));

        foreach (var issue in issues)
        {
            var badge = issue.Severity switch
            {
                LintSeverity.Error => "[bold red]🔴 ERROR[/]",
                LintSeverity.Warning => "[bold yellow]🟡 WARN[/]",
                _ => "[bold blue]🔵 INFO[/]"
            };

            var target = string.IsNullOrEmpty(issue.ColumnName)
                ? $"[cyan]{issue.TableName}[/]"
                : $"[cyan]{issue.TableName}[/].[yellow]{issue.ColumnName}[/]";

            var desc = $"{issue.Message}\n[grey]Tavsiye: {issue.Recommendation}[/]";

            table.AddRow(badge, $"[bold]{issue.RuleId}[/]", target, desc);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Özet Paneli
        var summaryGrid = new Grid();
        summaryGrid.AddColumn();
        summaryGrid.AddColumn();

        summaryGrid.AddRow("[bold]Dosya:[/]", settings.InputPath);
        summaryGrid.AddRow("[bold]Toplam Tablo:[/]", schema.Tables.Count.ToString());
        summaryGrid.AddRow("[bold]Kritik Hata (Error):[/]", errorCount > 0 ? $"[red]{errorCount} adet[/]" : "[green]0[/]");
        summaryGrid.AddRow("[bold]Uyarı (Warning):[/]", warningCount > 0 ? $"[yellow]{warningCount} adet[/]" : "[green]0[/]");
        summaryGrid.AddRow("[bold]Öneri (Info):[/]", $"{infoCount} adet");

        var panelColor = errorCount > 0 ? Color.Red : (warningCount > 0 ? Color.Yellow : Color.Green);
        var panelHeader = errorCount > 0 
            ? "⚠️ Denetim Tamamlandı: Kritik Hatalar Bulundu!" 
            : "🔍 Denetim Tamamlandı";

        AnsiConsole.Write(
            new Panel(summaryGrid)
                .Header(panelHeader)
                .Border(BoxBorder.Rounded)
                .BorderColor(panelColor));

        // Opsiyonel Markdown Çıktısı
        if (!string.IsNullOrEmpty(settings.OutputPath))
        {
            var md = GenerateMarkdownLintReport(issues, settings.InputPath, schema);
            await File.WriteAllTextAsync(settings.OutputPath, md, cancellationToken);
            AnsiConsole.MarkupLine($"\n[grey]Markdown Linter Raporu Kaydedildi:[/] [green]{settings.OutputPath}[/]");
        }

        // CI/CD Hata Çıkışı
        if (settings.FailOnErrors && errorCount > 0)
        {
            AnsiConsole.MarkupLine("\n[bold red]HATA:[/] Şemada kritik mimari hata(lar) bulunduğu için işlem başarısız (exit code 1) olarak sonlandırıldı.");
            return 1;
        }

        if (settings.FailOnWarnings && warningCount > 0)
        {
            AnsiConsole.MarkupLine("\n[bold yellow]UYARI:[/] '--fail-on-warnings' aktif olduğundan işlem başarısız (exit code 1) olarak sonlandırıldı.");
            return 1;
        }

        return 0;
    }

    private static IDatabaseParser ResolveParserForFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => new EfCoreParser(),
            ".prisma" => new PrismaParser(),
            ".xml" => new LiquibaseParser(),
            ".py" => new DjangoParser(),
            ".rb" => new RailsParser(),
            _ => new PostgreSqlParser()
        };
    }

    private static string GenerateMarkdownLintReport(List<LintIssue> issues, string inputPath, DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 🛡️ SchemaFlow Şema Sağlık & Linter Raporu");
        sb.AppendLine();
        sb.AppendLine($"> **Dosya:** `{Path.GetFileName(inputPath)}`  ");
        sb.AppendLine($"> **Toplam Tablo:** `{schema.Tables.Count}`  ");
        sb.AppendLine($"> **Tarih:** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`  ");
        sb.AppendLine();

        sb.AppendLine("| Derece | Kural | Hedef | Mesaj | Tavsiye |");
        sb.AppendLine("| :---: | :---: | :--- | :--- | :--- |");

        foreach (var i in issues)
        {
            var target = string.IsNullOrEmpty(i.ColumnName) ? i.TableName : $"{i.TableName}.{i.ColumnName}";
            sb.AppendLine($"| `{i.Severity}` | `{i.RuleId}` | `{target}` | {i.Message} | {i.Recommendation} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Bu rapor [SchemaFlow.ai](https://github.com) tarafından otomatik oluşturulmuştur.*");

        return sb.ToString();
    }
}
