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
});

return await app.RunAsync(args);
