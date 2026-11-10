using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// İki veritabanı şema dosyası arasındaki farkları (Diff) hesaplayarak
/// canlı sistemi bozabilecek değişiklikleri (Breaking Changes) raporlayan CLI komutudur.
/// </summary>
public class DiffCommand : AsyncCommand<DiffSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DiffSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("SchemaFlow Diff")
                .LeftJustified()
                .Color(Color.Orange1));

        AnsiConsole.MarkupLine("[bold grey]Akıllı Veritabanı Şema Karşılaştırma & Breaking Change Dedektörü[/]\n");

        // 1. Dosyaların Parser'larını otomatik tespit et ve ayrıştır
        var baseParser = ResolveParserForFile(settings.BasePath);
        var targetParser = ResolveParserForFile(settings.TargetPath);

        DatabaseSchema baseSchema = null!;
        DatabaseSchema targetSchema = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync("Şemalar ayrıştırılıyor ve karşılaştırılıyor...", async ctx =>
            {
                var baseContent = await File.ReadAllTextAsync(settings.BasePath, cancellationToken);
                var targetContent = await File.ReadAllTextAsync(settings.TargetPath, cancellationToken);

                baseSchema = await baseParser.ParseSqlAsync(baseContent, cancellationToken);
                targetSchema = await targetParser.ParseSqlAsync(targetContent, cancellationToken);
            });

        // 2. Diff Motorunu Çalıştır
        var diffEngine = new SchemaDiffEngine();
        var diffResult = diffEngine.Compare(baseSchema, targetSchema);

        // 3. Hiç Değişiklik Yoksa
        if (diffResult.TotalChanges == 0)
        {
            AnsiConsole.MarkupLine("[bold green]✓ İki şema arasında hiçbir fark bulunamadı! Şemalar tamamen özdeş.[/]\n");
            return 0;
        }

        // 4. Değişiklikleri Tablo Olarak Göster
        var table = new Spectre.Console.Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]Durum[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Tür[/]"));
        table.AddColumn(new TableColumn("[bold]Tablo[/]"));
        table.AddColumn(new TableColumn("[bold]Açıklama[/]"));

        foreach (var change in diffResult.Changes)
        {
            var statusIcon = change.IsBreaking
                ? "[bold red]🔴 BREAKING[/]"
                : "[bold green]🟢 SAFE[/]";

            var typeColor = change.IsBreaking ? "red" : "green";

            table.AddRow(
                statusIcon,
                $"[{typeColor}]{change.Type}[/]",
                $"[bold white]{change.TableName}[/]",
                change.Description.EscapeMarkup()
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // 5. Özet Paneli
        var summaryGrid = new Grid();
        summaryGrid.AddColumn();
        summaryGrid.AddColumn();

        summaryGrid.AddRow("[grey]Temel Şema:[/]", $"[yellow]{Path.GetFileName(settings.BasePath)}[/] ({baseSchema.Tables.Count} Tablo)");
        summaryGrid.AddRow("[grey]Hedef Şema:[/]", $"[yellow]{Path.GetFileName(settings.TargetPath)}[/] ({targetSchema.Tables.Count} Tablo)");
        summaryGrid.AddRow("[grey]Toplam Değişiklik:[/]", $"[bold white]{diffResult.TotalChanges}[/] adet");
        summaryGrid.AddRow("[grey]Güvenli Değişiklik (Safe):[/]", $"[bold green]{diffResult.SafeCount}[/] adet");
        summaryGrid.AddRow("[grey]Tehlikeli Değişiklik (Breaking):[/]", diffResult.BreakingCount > 0 
            ? $"[bold red]{diffResult.BreakingCount} adet (DİKKAT!)[/]" 
            : "[bold green]0 adet[/]");

        var panelColor = diffResult.HasBreakingChanges ? Color.Red : Color.Green;
        var panelTitle = diffResult.HasBreakingChanges 
            ? "[bold red]⚠️ DİKKAT: Canlı Sistemi Bozabilecek (Breaking) Değişiklikler Var![/]" 
            : "[bold green]✓ Tüm Değişiklikler Güvenli Görünüyor[/]";

        AnsiConsole.Write(
            new Panel(summaryGrid)
                .Header(panelTitle)
                .Border(BoxBorder.Rounded)
                .BorderColor(panelColor));

        // 6. Opsiyonel Markdown Çıktısı
        if (!string.IsNullOrEmpty(settings.OutputPath))
        {
            var md = GenerateMarkdownDiffReport(diffResult, settings.BasePath, settings.TargetPath);
            await File.WriteAllTextAsync(settings.OutputPath, md, cancellationToken);
            AnsiConsole.MarkupLine($"\n[grey]Markdown Diff Raporu Kaydedildi:[/] [green]{settings.OutputPath}[/]");
        }

        // 7. CI/CD Kontrolü: Eğer --fail-on-breaking aktifse ve breaking change varsa 1 dön
        if (settings.FailOnBreaking && diffResult.HasBreakingChanges)
        {
            AnsiConsole.MarkupLine("\n[bold red]HATA:[/] '--fail-on-breaking' aktif olduğundan işlem başarısız (exit code 1) olarak sonlandırıldı.");
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

    private static string GenerateMarkdownDiffReport(SchemaDiffResult result, string basePath, string targetPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 🔍 SchemaFlow Şema Değişiklik & Breaking Change Raporu");
        sb.AppendLine();
        sb.AppendLine($"> **Temel Dosya:** `{Path.GetFileName(basePath)}`  ");
        sb.AppendLine($"> **Hedef Dosya:** `{Path.GetFileName(targetPath)}`  ");
        sb.AppendLine($"> **Tarih:** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`  ");
        sb.AppendLine();

        if (result.HasBreakingChanges)
        {
            sb.AppendLine("## ⚠️ DİKKAT: Canlı Sistemi Bozabilecek Değişiklikler Bulundu!");
            sb.AppendLine($"- 🔴 **Kritik Değişiklik Sayısı:** `{result.BreakingCount}`");
            sb.AppendLine($"- 🟢 **Güvenli Değişiklik Sayısı:** `{result.SafeCount}`");
        }
        else
        {
            sb.AppendLine("## ✅ Tüm Değişiklikler Güvenli");
            sb.AppendLine("- Canlı sistemi (production) etkileyecek yıkıcı bir değişiklik tespit edilmedi.");
        }

        sb.AppendLine();
        sb.AppendLine("### 📋 Değişiklik Listesi");
        sb.AppendLine("| Durum | Tür | Tablo | Açıklama |");
        sb.AppendLine("| :---: | :--- | :--- | :--- |");

        foreach (var change in result.Changes)
        {
            var badge = change.IsBreaking ? "🔴 BREAKING" : "🟢 SAFE";
            sb.AppendLine($"| **{badge}** | `{change.Type}` | `{change.TableName}` | {change.Description} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Bu rapor [SchemaFlow.ai](https://github.com) tarafından otomatik oluşturulmuştur.*");

        return sb.ToString();
    }
}
