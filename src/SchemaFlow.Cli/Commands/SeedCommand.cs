using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace SchemaFlow.Cli.Commands;

/// <summary>
/// Şema ilişkilerini analiz ederek test ortamları için akıllı sahte (dummy / seed) SQL verisi üreten CLI komutu.
/// </summary>
public class SeedCommand : AsyncCommand<SeedSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SeedSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("SchemaFlow Seed")
                .LeftJustified()
                .Color(Color.Green1));

        AnsiConsole.MarkupLine("[bold grey]İlişkisel Bütünlüklü Akıllı Sahte Test Verisi (Seed Data) Üreteci[/]\n");

        var sw = Stopwatch.StartNew();
        var parser = ResolveParserForFile(settings.InputPath);
        DatabaseSchema schema = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync("Şema analiz ediliyor ve Foreign Key hiyerarşisi çözümleniyor...", async ctx =>
            {
                var content = await File.ReadAllTextAsync(settings.InputPath, cancellationToken);
                schema = await parser.ParseSqlAsync(content, cancellationToken);
            });

        if (schema.Tables.Count == 0)
        {
            AnsiConsole.MarkupLine("[bold yellow]Uyarı:[/] Verilen dosyada veri üretilecek hiçbir tablo bulunamadı.");
            return 1;
        }

        // Seed Motorunu Çalıştır
        var seedEngine = new SchemaSeedEngine();
        string seedSql = string.Empty;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync($"{schema.Tables.Count} tablo için {settings.Count}'ar adet ilişkisel test verisi üretiliyor...", async ctx =>
            {
                seedSql = seedEngine.GenerateSeedSql(schema, settings.Count);
                await File.WriteAllTextAsync(settings.OutputPath, seedSql, cancellationToken);
            });

        sw.Stop();

        // Başarı Özeti Paneli
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[bold]Kaynak Şema:[/] ", Path.GetFileName(settings.InputPath));
        grid.AddRow("[bold]Hedef Dosya:[/] ", $"[green]{settings.OutputPath}[/]");
        grid.AddRow("[bold]Toplam Tablo:[/] ", schema.Tables.Count.ToString());
        grid.AddRow("[bold]Tablo Başına Kayıt:[/] ", settings.Count.ToString());
        grid.AddRow("[bold]Toplam Üretilen Satır:[/] ", $"[bold green]{schema.Tables.Count * settings.Count}[/] adet SQL INSERT");
        grid.AddRow("[bold]İşlem Süresi:[/] ", $"{sw.ElapsedMilliseconds} ms");

        AnsiConsole.Write(
            new Panel(grid)
                .Header("✓ Test Verisi (Seed SQL) Başarıyla Üretildi!")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green));

        AnsiConsole.MarkupLine($"\n[grey]Bu SQL dosyasını veritabanınızda çalıştırarak test verilerinizi anında doldurabilirsiniz:[/]");
        AnsiConsole.MarkupLine($"[cyan]psql -d veritabani_adi -f {settings.OutputPath}[/] veya [cyan]mysql -u root -p db_adi < {settings.OutputPath}[/]\n");

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
}
