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

        // Opsiyonel Markdown Çıktısı veya Tarayıcıda Açma
        if (!string.IsNullOrEmpty(settings.OutputPath) || settings.OpenInBrowser)
        {
            var mdPath = settings.OutputPath ?? "lint_report.md";
            var md = GenerateMarkdownLintReport(issues, settings.InputPath, schema);
            await File.WriteAllTextAsync(mdPath, md, cancellationToken);
            AnsiConsole.MarkupLine($"\n[grey]Markdown Linter Raporu Kaydedildi:[/] [green]{mdPath}[/]");

            if (settings.OpenInBrowser)
            {
                try
                {
                    var htmlPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mdPath)) ?? Directory.GetCurrentDirectory(), "lint_preview.html");
                    var htmlContent = GenerateHtmlLintReport(issues, schema, settings.InputPath);
                    await File.WriteAllTextAsync(htmlPath, htmlContent, cancellationToken);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = htmlPath,
                        UseShellExecute = true
                    });

                    AnsiConsole.MarkupLine($"[bold cyan]🚀 Görsel Şema Denetim Raporu tarayıcıda açıldı:[/] [underline]{htmlPath}[/]\n");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Uyarı: Tarayıcı otomatik açılamadı ({ex.Message}).[/]");
                }
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]💡 İpucu: Şemayı ve hataları görsel diyagram üzerinde incelemek için:[/] [cyan]--open[/] [grey]parametresini ekleyebilirsiniz.[/]");
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
        var mermaidRenderer = new MermaidRenderer();
        var mermaid = mermaidRenderer.Render(schema);

        var errorCount = issues.Count(i => i.Severity == LintSeverity.Error);
        var warningCount = issues.Count(i => i.Severity == LintSeverity.Warning);

        var sb = new StringBuilder();
        sb.AppendLine("# 🛡️ SchemaFlow Şema Sağlık & Linter Raporu");
        sb.AppendLine();
        sb.AppendLine($"> **Dosya:** `{Path.GetFileName(inputPath)}`  ");
        sb.AppendLine($"> **Toplam Tablo:** `{schema.Tables.Count}`  ");
        sb.AppendLine($"> **Tarih:** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`  ");
        sb.AppendLine($"> **Durum:** 🔴 `{errorCount}` Kritik Hata | 🟡 `{warningCount}` Uyarı  ");
        sb.AppendLine();

        sb.AppendLine("## 📊 Veritabanı ER Şeması");
        sb.AppendLine("```mermaid");
        sb.AppendLine(mermaid);
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## 📋 Tespit Edilen Mimari & Performans Sorunları");
        sb.AppendLine("| Derece | Kural | Hedef | Mesaj | Tavsiye |");
        sb.AppendLine("| :---: | :---: | :--- | :--- | :--- |");

        foreach (var i in issues)
        {
            var badge = i.Severity switch
            {
                LintSeverity.Error => "🔴 **ERROR**",
                LintSeverity.Warning => "🟡 **WARN**",
                _ => "🔵 **INFO**"
            };
            var target = string.IsNullOrEmpty(i.ColumnName) ? $"`{i.TableName}`" : $"`{i.TableName}.{i.ColumnName}`";
            sb.AppendLine($"| {badge} | `{i.RuleId}` | {target} | {i.Message} | {i.Recommendation} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 💡 Önerilen Hızlı SQL Çözümleri (Quick Fixes)");
        sb.AppendLine("```sql");
        foreach (var i in issues)
        {
            sb.AppendLine($"-- [{i.RuleId}] {i.TableName}: {i.Message}");
            sb.AppendLine(GenerateSqlFix(i));
            sb.AppendLine();
        }
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Bu görsel denetim raporu [SchemaFlow.ai](https://github.com/FiratMugdatoglu/SchemaFlow) tarafından otomatik oluşturulmuştur.*");

        return sb.ToString();
    }

    private static string GenerateHtmlLintReport(List<LintIssue> issues, DatabaseSchema schema, string inputPath)
    {
        var mermaidRenderer = new MermaidRenderer();
        var mermaid = mermaidRenderer.Render(schema);

        var errorCount = issues.Count(i => i.Severity == LintSeverity.Error);
        var warningCount = issues.Count(i => i.Severity == LintSeverity.Warning);
        var infoCount = issues.Count(i => i.Severity == LintSeverity.Info);

        var healthScore = Math.Max(0, 100 - (errorCount * 25) - (warningCount * 10));
        var scoreColor = healthScore >= 80 ? "text-emerald-400 border-emerald-500/30 bg-emerald-500/10"
                       : healthScore >= 50 ? "text-amber-400 border-amber-500/30 bg-amber-500/10"
                       : "text-rose-400 border-rose-500/30 bg-rose-500/10";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"tr\" class=\"dark\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\" />");
        sb.AppendLine("  <title>SchemaFlow Linter Studio - Şema Sağlık & Denetim</title>");
        sb.AppendLine("  <script src=\"https://cdn.tailwindcss.com\"></script>");
        sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js\"></script>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { background-color: #0b0f17; color: #e2e8f0; font-family: ui-sans-serif, system-ui, sans-serif; }");
        sb.AppendLine("    .mermaid svg { max-width: 100% !important; height: auto !important; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body class=\"p-6 max-w-7xl mx-auto space-y-6\">");

        // Header
        sb.AppendLine("  <header class=\"border-b border-slate-800 pb-5 flex flex-wrap items-center justify-between gap-4\">");
        sb.AppendLine("    <div>");
        sb.AppendLine("      <div class=\"flex items-center space-x-3\">");
        sb.AppendLine("        <div class=\"w-9 h-9 rounded-lg bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center text-cyan-400 text-lg font-bold\">🛡️</div>");
        sb.AppendLine("        <div>");
        sb.AppendLine("          <h1 class=\"text-2xl font-bold text-white\">SchemaFlow Şema Sağlık & Linter Stüdyosu</h1>");
        sb.AppendLine($"         <p class=\"text-xs text-slate-400 font-mono\">Dosya: <span class=\"text-cyan-400 font-bold\">{Path.GetFileName(inputPath)}</span> ({schema.Tables.Count} Tablo)</p>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine($"   <div class=\"flex items-center space-x-3\">");
        sb.AppendLine($"     <div class=\"px-4 py-2 rounded-xl border {scoreColor} text-center\">");
        sb.AppendLine($"       <div class=\"text-xs uppercase font-bold tracking-wider text-slate-400\">Sağlık Skoru</div>");
        sb.AppendLine($"       <div class=\"text-2xl font-black\">{healthScore} <span class=\"text-xs font-normal\">/ 100</span></div>");
        sb.AppendLine($"     </div>");
        sb.AppendLine($"   </div>");
        sb.AppendLine("  </header>");

        // Summary Badges
        sb.AppendLine("  <div class=\"grid grid-cols-1 sm:grid-cols-3 gap-4\">");
        sb.AppendLine($"   <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-4 flex items-center justify-between\"><div><div class=\"text-xs text-slate-400 uppercase\">Kritik Hatalar</div><div class=\"text-2xl font-bold text-rose-400\">{errorCount}</div></div><div class=\"text-2xl\">🔴</div></div>");
        sb.AppendLine($"   <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-4 flex items-center justify-between\"><div><div class=\"text-xs text-slate-400 uppercase\">Performans Uyarıları</div><div class=\"text-2xl font-bold text-amber-400\">{warningCount}</div></div><div class=\"text-2xl\">🟡</div></div>");
        sb.AppendLine($"   <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-4 flex items-center justify-between\"><div><div class=\"text-xs text-slate-400 uppercase\">Mimari Tavsiyeler</div><div class=\"text-2xl font-bold text-cyan-400\">{infoCount}</div></div><div class=\"text-2xl\">💡</div></div>");
        sb.AppendLine("  </div>");

        // Two Column Layout: Diagram on Left, Issues on Right
        sb.AppendLine("  <div class=\"grid grid-cols-1 lg:grid-cols-2 gap-6\">");

        // Sol Sütun: Canlı ER Şeması
        sb.AppendLine("    <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-xl flex flex-col\">");
        sb.AppendLine("      <div class=\"flex items-center justify-between mb-3 border-b border-slate-800 pb-3\">");
        sb.AppendLine("        <h2 class=\"text-sm font-bold text-white flex items-center space-x-2\"><span>📊 Görsel Veritabanı Şeması</span></h2>");
        sb.AppendLine("        <span class=\"text-[11px] text-slate-500 font-mono\">Mermaid ERD</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"mermaid bg-slate-950 p-4 rounded-lg overflow-x-auto text-xs flex-1\">");
        sb.AppendLine(mermaid);
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"mt-3 pt-3 border-t border-slate-800 text-[11px] text-slate-400 flex items-center justify-between\">");
        sb.AppendLine("        <span>💡 Hatalı tablolar sağ panelde kırmızı/sarı rozetlerle işaretlenmiştir.</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");

        // Sağ Sütun: Bulgular & Hızlı SQL Çözümleri
        sb.AppendLine("    <div class=\"space-y-4\">");
        sb.AppendLine("      <div class=\"bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-xl\">");
        sb.AppendLine("        <h2 class=\"text-sm font-bold text-white mb-4 border-b border-slate-800 pb-3 flex items-center space-x-2\"><span>⚠️ Tespit Edilen Mimari & Performans Hataları</span></h2>");
        sb.AppendLine("        <div class=\"space-y-3\">");

        foreach (var issue in issues)
        {
            var isErr = issue.Severity == LintSeverity.Error;
            var badgeBorder = isErr ? "border-rose-500/30 bg-rose-500/10 text-rose-400" : "border-amber-500/30 bg-amber-500/10 text-amber-400";
            var icon = isErr ? "🔴 KRİTİK HATA" : "🟡 PERFORMANS UYARISI";
            var targetName = string.IsNullOrEmpty(issue.ColumnName) ? issue.TableName : $"{issue.TableName}.{issue.ColumnName}";
            var sqlFix = GenerateSqlFix(issue);

            sb.AppendLine($"         <div class=\"border {badgeBorder} rounded-lg p-4 space-y-2\">");
            sb.AppendLine($"           <div class=\"flex items-center justify-between\">");
            sb.AppendLine($"             <span class=\"px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider border {badgeBorder}\">{icon} ({issue.RuleId})</span>");
            sb.AppendLine($"             <span class=\"font-mono text-xs text-white font-semibold\">{targetName}</span>");
            sb.AppendLine($"           </div>");
            sb.AppendLine($"           <p class=\"text-xs text-slate-300\">{issue.Message}</p>");
            sb.AppendLine($"           <p class=\"text-[11px] text-slate-400\"><strong>Tavsiye:</strong> {issue.Recommendation}</p>");
            if (!string.IsNullOrEmpty(sqlFix))
            {
                sb.AppendLine($"           <div class=\"mt-2 bg-slate-950 p-2.5 rounded border border-slate-800\">");
                sb.AppendLine($"             <div class=\"text-[10px] text-cyan-400 font-bold uppercase mb-1 flex items-center justify-between\"><span>⚡ Önerilen SQL Düzeltmesi</span></div>");
                sb.AppendLine($"             <code class=\"text-xs font-mono text-emerald-400 block whitespace-pre-wrap\">{sqlFix}</code>");
                sb.AppendLine($"           </div>");
            }
            sb.AppendLine($"         </div>");
        }

        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");

        sb.AppendLine("  </div>");

        sb.AppendLine("  <script>mermaid.initialize({ startOnLoad: true, theme: 'dark' });</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string GenerateSqlFix(LintIssue issue)
    {
        return issue.RuleId switch
        {
            "SF001" => $"ALTER TABLE {issue.TableName} ADD COLUMN id SERIAL PRIMARY KEY;",
            "SF002" => $"CREATE INDEX idx_{issue.TableName}_{issue.ColumnName} ON {issue.TableName}({issue.ColumnName});",
            "SF003" => string.IsNullOrEmpty(issue.ColumnName)
                ? $"ALTER TABLE \"{issue.TableName}\" RENAME TO \"app_{issue.TableName}\";"
                : $"ALTER TABLE \"{issue.TableName}\" RENAME COLUMN \"{issue.ColumnName}\" TO \"{issue.ColumnName}_val\";",
            "SF004" => $"ALTER TABLE {issue.TableName} ALTER COLUMN {issue.ColumnName} SET NOT NULL;",
            _ => $"-- {issue.Recommendation}"
        };
    }
}
