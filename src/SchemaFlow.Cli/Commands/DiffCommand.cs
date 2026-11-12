using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Renderers;
using SchemaFlow.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;
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

        // 6. Opsiyonel Markdown Çıktısı veya Tarayıcıda Açma
        if (!string.IsNullOrEmpty(settings.OutputPath) || settings.OpenInBrowser)
        {
            var mdPath = settings.OutputPath ?? "diff_report.md";
            var md = GenerateMarkdownDiffReport(diffResult, baseSchema, targetSchema, settings.BasePath, settings.TargetPath);
            await File.WriteAllTextAsync(mdPath, md, cancellationToken);
            AnsiConsole.MarkupLine($"\n[grey]Markdown Görsel Diff Raporu Kaydedildi:[/] [green]{mdPath}[/]");

            if (settings.OpenInBrowser)
            {
                try
                {
                    var htmlPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mdPath)) ?? Directory.GetCurrentDirectory(), "diff_preview.html");
                    var htmlContent = GenerateHtmlDiffReport(diffResult, baseSchema, targetSchema, settings.BasePath, settings.TargetPath);
                    await File.WriteAllTextAsync(htmlPath, htmlContent, cancellationToken);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = htmlPath,
                        UseShellExecute = true
                    });

                    AnsiConsole.MarkupLine($"[bold cyan]🚀 Görsel Öncesi/Sonrası karşılaştırması tarayıcıda açıldı:[/] [underline]{htmlPath}[/]\n");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Uyarı: Tarayıcı otomatik açılamadı ({ex.Message}).[/]");
                }
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]💡 İpucu: Bu farkı görsel olarak tarayıcıda incelemek için:[/] [cyan]--open[/] [grey]parametresini ekleyebilirsiniz.[/]");
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

    private static string GenerateMarkdownDiffReport(SchemaDiffResult result, DatabaseSchema baseSchema, DatabaseSchema targetSchema, string basePath, string targetPath)
    {
        var mermaidRenderer = new MermaidRenderer();
        var baseMermaid = mermaidRenderer.Render(baseSchema);
        var targetMermaid = mermaidRenderer.Render(targetSchema);

        var sb = new StringBuilder();
        sb.AppendLine("# 🔍 SchemaFlow Görsel Şema Değişiklik & Breaking Change Raporu");
        sb.AppendLine();
        sb.AppendLine($"> **Temel Şema (Eski):** `{Path.GetFileName(basePath)}` ({baseSchema.Tables.Count} Tablo)  ");
        sb.AppendLine($"> **Hedef Şema (Yeni):** `{Path.GetFileName(targetPath)}` ({targetSchema.Tables.Count} Tablo)  ");
        sb.AppendLine($"> **Tarih:** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`  ");
        sb.AppendLine();

        if (result.HasBreakingChanges)
        {
            sb.AppendLine("## ⚠️ DİKKAT: Canlı Sistemi Bozabilecek (Breaking) Değişiklikler!");
            sb.AppendLine($"- 🔴 **Kritik Değişiklik:** `{result.BreakingCount}` adet (Canlıyı patlatabilir)");
            sb.AppendLine($"- 🟢 **Güvenli Değişiklik:** `{result.SafeCount}` adet");
        }
        else
        {
            sb.AppendLine("## ✅ Tüm Değişiklikler Güvenli");
            sb.AppendLine("- Canlı sistemi (production) etkileyecek yıkıcı bir değişiklik tespit edilmedi.");
        }

        sb.AppendLine();
        sb.AppendLine("### 📋 Değişiklik Listesi (Öncesi ➡️ Sonrası)");
        sb.AppendLine("| Durum | Değişiklik Türü | Tablo | Açıklama & Etki |");
        sb.AppendLine("| :---: | :--- | :--- | :--- |");

        foreach (var change in result.Changes)
        {
            var badge = change.IsBreaking ? "🔴 **BREAKING**" : "🟢 **SAFE**";
            sb.AppendLine($"| {badge} | `{change.Type}` | `{change.TableName}` | {change.Description} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## 📊 Görsel Mimari Karşılaştırması (Before vs After)");
        sb.AppendLine();
        sb.AppendLine($"### ⏪ 1. Eski Şema Hali (`{Path.GetFileName(basePath)}`)");
        sb.AppendLine("```mermaid");
        sb.AppendLine(baseMermaid);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"### ⏩ 2. Yeni Şema Hali (`{Path.GetFileName(targetPath)}`)");
        sb.AppendLine("```mermaid");
        sb.AppendLine(targetMermaid);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Bu görsel rapor [SchemaFlow.ai](https://github.com/FiratMugdatoglu/SchemaFlow) tarafından otomatik oluşturulmuştur.*");

        return sb.ToString();
    }

    private static string GenerateHtmlDiffReport(SchemaDiffResult result, DatabaseSchema baseSchema, DatabaseSchema targetSchema, string basePath, string targetPath)
    {
        var mermaidRenderer = new MermaidRenderer();
        var baseMermaid = mermaidRenderer.Render(baseSchema);
        var targetMermaid = mermaidRenderer.Render(targetSchema);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"tr\" class=\"dark\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\" />");
        sb.AppendLine("  <title>SchemaFlow Diff Studio - Görsel Karşılaştırma</title>");
        sb.AppendLine("  <script src=\"https://cdn.tailwindcss.com\"></script>");
        sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js\"></script>");
        sb.AppendLine("  <style>body { background-color: #0b0f17; color: #e2e8f0; font-family: ui-sans-serif, system-ui, sans-serif; }</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body class=\"p-6 max-w-6xl mx-auto space-y-8\">");

        // Header
        sb.AppendLine("  <header class=\"border-b border-slate-800 pb-5 flex items-center justify-between\">");
        sb.AppendLine("    <div>");
        sb.AppendLine("      <h1 class=\"text-2xl font-bold text-white flex items-center space-x-2\"><span>🔍 SchemaFlow Görsel Karşılaştırma (Diff)</span></h1>");
        sb.AppendLine($"     <p class=\"text-xs text-slate-400 mt-1\">Eski: <span class=\"text-cyan-400 font-mono\">{Path.GetFileName(basePath)}</span> ➡️ Yeni: <span class=\"text-cyan-400 font-mono\">{Path.GetFileName(targetPath)}</span></p>");
        sb.AppendLine("    </div>");
        var badgeColor = result.HasBreakingChanges ? "bg-rose-500/20 text-rose-400 border-rose-500/30" : "bg-emerald-500/20 text-emerald-400 border-emerald-500/30";
        var badgeText = result.HasBreakingChanges ? $"⚠️ {result.BreakingCount} BREAKING CHANGE" : "✅ TÜMÜ GÜVENLİ";
        sb.AppendLine($"   <span class=\"px-3 py-1 rounded-full text-xs font-bold border {badgeColor}\">{badgeText}</span>");
        sb.AppendLine("  </header>");

        // Tablo
        sb.AppendLine("  <div class=\"bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-xl\">");
        sb.AppendLine("    <div class=\"px-5 py-3 bg-slate-800/60 font-semibold text-sm text-slate-200 border-b border-slate-800\">📋 Değişiklik ve Risk Tablosu</div>");
        sb.AppendLine("    <table class=\"w-full text-left text-xs text-slate-300\">");
        sb.AppendLine("      <thead class=\"bg-slate-950 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-800\">");
        sb.AppendLine("        <tr><th class=\"px-5 py-3\">Durum</th><th class=\"px-4 py-3\">Tür</th><th class=\"px-4 py-3\">Tablo</th><th class=\"px-5 py-3\">Açıklama</th></tr>");
        sb.AppendLine("      </thead>");
        sb.AppendLine("      <tbody class=\"divide-y divide-slate-800/60\">");
        foreach (var change in result.Changes)
        {
            var rowBadge = change.IsBreaking ? "<span class=\"px-2 py-0.5 rounded bg-rose-500/20 text-rose-400 font-bold border border-rose-500/30\">🔴 BREAKING</span>" : "<span class=\"px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-400 font-bold border border-emerald-500/30\">🟢 SAFE</span>";
            sb.AppendLine($"       <tr class=\"hover:bg-slate-800/30\"><td class=\"px-5 py-3\">{rowBadge}</td><td class=\"px-4 py-3 font-mono text-cyan-400\">{change.Type}</td><td class=\"px-4 py-3 font-semibold text-white\">{change.TableName}</td><td class=\"px-5 py-3\">{change.Description}</td></tr>");
        }
        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </div>");

        // Yan Yana Diyagramlar (Side-by-Side Before & After)
        sb.AppendLine("  <div class=\"grid grid-cols-1 md:grid-cols-2 gap-6\">");
        sb.AppendLine("    <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-xl\">");
        sb.AppendLine($"     <h3 class=\"text-sm font-bold text-slate-300 mb-3 flex items-center space-x-2\"><span class=\"w-2.5 h-2.5 rounded-full bg-slate-500\"></span><span>⏪ Eski Şema ({Path.GetFileName(basePath)})</span></h3>");
        sb.AppendLine("      <div class=\"mermaid bg-slate-950 p-4 rounded-lg overflow-x-auto text-xs\">");
        sb.AppendLine(baseMermaid);
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");

        sb.AppendLine("    <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-xl\">");
        sb.AppendLine($"     <h3 class=\"text-sm font-bold text-slate-300 mb-3 flex items-center space-x-2\"><span class=\"w-2.5 h-2.5 rounded-full bg-cyan-400\"></span><span>⏩ Yeni Şema ({Path.GetFileName(targetPath)})</span></h3>");
        sb.AppendLine("      <div class=\"mermaid bg-slate-950 p-4 rounded-lg overflow-x-auto text-xs\">");
        sb.AppendLine(targetMermaid);
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");

        sb.AppendLine("  <script>mermaid.initialize({ startOnLoad: true, theme: 'dark' });</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
