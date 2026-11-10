using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Renderers;
using SchemaFlow.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// 'schemaflow generate' komutunu icra eden sınıftır.
/// Girdi dosyalarını toplar, ilgili parser'ı çalıştırır ve çıktıyı diske yazar.
/// </summary>
public class GenerateCommand : AsyncCommand<GenerateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GenerateSettings settings, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Şık bir karşılama başlığı
        AnsiConsole.Write(
            new FigletText("SchemaFlow.ai")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold grey]Developer-First Database Schema & Documentation Engine[/]\n");

        // 2. Girdi dosyalarını topla
        var targetDialect = settings.Dialect;
        if (File.Exists(settings.InputPath))
        {
            if (settings.InputPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                targetDialect = DatabaseDialect.EfCore;
            else if (settings.InputPath.EndsWith(".prisma", StringComparison.OrdinalIgnoreCase))
                targetDialect = DatabaseDialect.Prisma;
            else if (settings.InputPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                targetDialect = DatabaseDialect.Liquibase;
            else if (settings.InputPath.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                targetDialect = DatabaseDialect.Django;
            else if (settings.InputPath.EndsWith(".rb", StringComparison.OrdinalIgnoreCase))
                targetDialect = DatabaseDialect.Rails;
        }

        var files = ResolveFiles(settings.InputPath, targetDialect);
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]Hata:[/] '{settings.InputPath}' konumunda ayrıştırılacak migration dosyası (.sql, .cs, .prisma, .xml, .py, .rb) bulunamadı!");
            return 1;
        }

        AnsiConsole.MarkupLine($"[grey]Bulunan Dosya:[/] [yellow]{files.Count}[/] adet ({targetDialect})");

        // 3. Uygun Parser motorunu seç (Strategy Pattern)
        IDatabaseParser parser = targetDialect switch
        {
            DatabaseDialect.Prisma => new PrismaParser(),
            DatabaseDialect.EfCore => new EfCoreParser(),
            DatabaseDialect.Liquibase => new LiquibaseParser(),
            DatabaseDialect.Django => new DjangoParser(),
            DatabaseDialect.Rails => new RailsParser(),
            DatabaseDialect.MySql => new MySqlParser(),
            DatabaseDialect.PostgreSql => new PostgreSqlParser(),
            _ => new PostgreSqlParser()
        };

        DatabaseSchema schema = null!;

        // 4. Ayrıştırma işlemini animasyonlu çalıştır
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Migration dosyaları ayrıştırılıyor ve şema çıkarılıyor...", async ctx =>
            {
                schema = await parser.ParseFilesAsync(files, cancellationToken);
            });

        if (schema.Tables.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Uyarı:[/] SQL dosyaları okundu ancak hiç tablo bulunamadı. Lütfen CREATE TABLE komutlarını kontrol edin.");
            return 0;
        }

        // 5. Uygun Renderer motorunu seç ve çıktıyı üret
        ISchemaRenderer renderer = settings.Format.ToLowerInvariant() switch
        {
            "mermaid" or "mmd" => new MermaidRenderer(),
            "html" => new HtmlRenderer(),
            _ => new MarkdownRenderer()
        };

        var outputContent = renderer.Render(schema);

        // 6. Çıktıyı hedef dosyaya kaydet (Akıllı Enjeksiyon veya Doğrudan Yazma)
        var injector = new MarkdownInjector();
        var isInjection = false;
        var finalContent = outputContent;

        if (File.Exists(settings.OutputPath))
        {
            var existingContent = await File.ReadAllTextAsync(settings.OutputPath, cancellationToken);
            if (settings.Inject || injector.HasMarkers(existingContent))
            {
                finalContent = injector.Inject(existingContent, outputContent);
                isInjection = true;
            }
        }
        else if (settings.Inject)
        {
            finalContent = injector.Inject(string.Empty, outputContent);
            isInjection = true;
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.OutputPath));
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(settings.OutputPath, finalContent, cancellationToken);
        stopwatch.Stop();

        // 7. Başarı Raporu (Spectre Tablosu ve Paneli)
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        var fullOutputPath = Path.GetFullPath(settings.OutputPath).EscapeMarkup();

        grid.AddRow("[grey]Veritabanı:[/]", $"[green]{settings.Dialect}[/]");
        grid.AddRow("[grey]Toplam Tablo:[/]", $"[cyan]{schema.Tables.Count}[/]");
        grid.AddRow("[grey]Toplam İlişki (FK):[/]", $"[magenta]{schema.GetAllForeignKeys().Count()}[/]");
        grid.AddRow("[grey]Çıktı Formatı:[/]", $"[yellow]{renderer.OutputFormat}[/]");
        grid.AddRow("[grey]Çalışma Modu:[/]", isInjection ? "[cyan]Akıllı Enjeksiyon (Smart Injection)[/]" : "[yellow]Doğrudan Yazma (Direct File)[/]");
        grid.AddRow("[grey]Hedef Dosya:[/]", $"[bold white]{fullOutputPath}[/]");
        grid.AddRow("[grey]İşlem Süresi:[/]", $"[green]{stopwatch.ElapsedMilliseconds} ms[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(
            new Panel(grid)
                .Header("[bold green]✓ Dokümantasyon Başarıyla Üretildi![/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green));

        return 0;
    }

    /// <summary>
    /// Verilen yol bir klasörse diyalekte göre (.sql veya .cs) dosyalarını alfabetik sırayla toplar,
    /// tek bir dosyaysa doğrudan o dosyayı döner.
    /// </summary>
    private static List<string> ResolveFiles(string inputPath, DatabaseDialect dialect)
    {
        var files = new List<string>();

        if (File.Exists(inputPath))
        {
            files.Add(inputPath);
            return files;
        }

        if (Directory.Exists(inputPath))
        {
            var searchPattern = dialect switch
            {
                DatabaseDialect.EfCore => "*.cs",
                DatabaseDialect.Prisma => "*.prisma",
                DatabaseDialect.Liquibase => "*.xml",
                DatabaseDialect.Django => "*.py",
                DatabaseDialect.Rails => "*.rb",
                _ => "*.sql"
            };

            var found = Directory.GetFiles(inputPath, searchPattern, SearchOption.AllDirectories)
                                 .OrderBy(f => f)
                                 .ToList();

            // Eğer hedef diyalekt dosyası bulunamadıysa alternatifleri kontrol et (.prisma, .cs)
            if (found.Count == 0 && dialect == DatabaseDialect.PostgreSql)
            {
                found = Directory.GetFiles(inputPath, "*.prisma", SearchOption.AllDirectories).ToList();
                if (found.Count == 0)
                {
                    found = Directory.GetFiles(inputPath, "*.cs", SearchOption.AllDirectories).ToList();
                }
            }

            files.AddRange(found);
        }

        return files;
    }
}
