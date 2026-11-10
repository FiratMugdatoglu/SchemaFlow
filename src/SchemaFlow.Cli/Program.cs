using SchemaFlow.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("schemaflow");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<GenerateCommand>("generate")
        .WithDescription("SQL, C#, Prisma, XML, Python veya Ruby migration dosyalarını okuyarak Mermaid ER diyagramı ve Markdown üretir.")
        .WithExample(new[] { "generate", "--input", "./migrations", "--output", "./docs/database.md" })
        .WithExample(new[] { "generate", "-i", "./schema.sql", "-f", "mermaid" })
        .WithExample(new[] { "generate", "-i", "./schema.prisma", "--inject" });

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("İki şemayı karşılaştırarak canlı sistemi bozabilecek (Breaking Changes) riskleri raporlar.")
        .WithExample(new[] { "diff", "--base", "./v1.sql", "--target", "./v2.sql" })
        .WithExample(new[] { "diff", "-b", "./v1.cs", "-t", "./v2.cs", "--fail-on-breaking" });

    config.AddCommand<LintCommand>("lint")
        .WithDescription("Şema dosyalarını eksik Primary Key, indekssiz Foreign Key ve rezerve SQL anahtar kelimelerine karşı denetler.")
        .WithExample(new[] { "lint", "--input", "./schema.sql" })
        .WithExample(new[] { "lint", "-i", "./schema.prisma", "--fail-on-warnings" });

    config.AddCommand<SeedCommand>("seed")
        .WithDescription("Şema ilişkilerini analiz ederek test ortamları için ilişkisel sahte (dummy) SQL verisi üretir.")
        .WithExample(new[] { "seed", "--input", "./schema.sql", "--count", "25", "--output", "./seed.sql" })
        .WithExample(new[] { "seed", "-i", "./schema.prisma", "-n", "50" });
});

return await app.RunAsync(args);
