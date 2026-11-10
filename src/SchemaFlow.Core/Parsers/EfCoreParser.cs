using SchemaFlow.Core.Models;
using System.Text.RegularExpressions;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Entity Framework Core (EF Core) C# migration dosyalarını (.cs) doğrudan okuyup
/// veritabanı şemasına (AST) dönüştüren ayrıştırıcıdır.
/// </summary>
public class EfCoreParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.EfCore;

    public override Task<DatabaseSchema> ParseSqlAsync(string csharpCode, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "EfCoreDatabase",
            Dialect = DatabaseDialect.EfCore
        };

        if (string.IsNullOrWhiteSpace(csharpCode))
            return Task.FromResult(schema);

        // 1. CreateTable bloklarını yakala
        ParseCreateTables(csharpCode, schema);

        // 2. CreateIndex bloklarını yakala
        ParseCreateIndexes(csharpCode, schema);

        return Task.FromResult(schema);
    }

    private void ParseCreateTables(string code, DatabaseSchema schema)
    {
        // migrationBuilder.CreateTable( ... );
        var tablePattern = new Regex(@"migrationBuilder\.CreateTable\s*\(\s*name:\s*""(?<tableName>[^""]+)""", RegexOptions.IgnoreCase);
        var matches = tablePattern.Matches(code);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var tableName = match.Groups["tableName"].Value;
            var startIndex = match.Index;

            var blockContent = ExtractMethodCallBlock(code, startIndex);
            if (string.IsNullOrEmpty(blockContent))
                continue;

            var table = new Table
            {
                Name = tableName,
                Description = "EF Core Migration Table"
            };

            // Kolonları Çöz
            ParseColumns(blockContent, table);

            // Constraints (PK & FK) Çöz
            ParseConstraints(blockContent, table);

            schema.Tables.Add(table);
        }
    }

    private void ParseColumns(string blockContent, Table table)
    {
        // columns: table => new { ... }
        var columnsBlockMatch = Regex.Match(blockContent, @"columns:\s*table\s*=>\s*new\s*\{(?<colsBlock>.*?)\}\s*,", RegexOptions.Singleline);
        if (!columnsBlockMatch.Success)
            return;

        var colsBlock = columnsBlockMatch.Groups["colsBlock"].Value;

        // Her bir satırı / tanımı yakala:
        // Örnek: Id = table.Column<Guid>(type: "uuid", nullable: false),
        var columnRegex = new Regex(@"(?<colName>\w+)\s*=\s*table\.Column<(?<clrType>[^>]+)>\s*\((?<props>.*?)\)\s*(?:,|$)", RegexOptions.Singleline);
        var matches = columnRegex.Matches(colsBlock);

        foreach (Match colMatch in matches)
        {
            var colName = colMatch.Groups["colName"].Value;
            var clrType = colMatch.Groups["clrType"].Value;
            var props = colMatch.Groups["props"].Value;

            // SQL tipini çek: type: "uuid" veya clrType
            var typeMatch = Regex.Match(props, @"type:\s*""(?<sqlType>[^""]+)""", RegexOptions.IgnoreCase);
            var dataType = typeMatch.Success ? typeMatch.Groups["sqlType"].Value : clrType;

            // Nullable çek: nullable: false
            var nullableMatch = Regex.Match(props, @"nullable:\s*(?<nullable>true|false)", RegexOptions.IgnoreCase);
            var isNullable = nullableMatch.Success && bool.Parse(nullableMatch.Groups["nullable"].Value);

            // DefaultValue çek: defaultValue: ...
            var defaultMatch = Regex.Match(props, @"defaultValue(?:Sql)?:\s*(?<def>[^,\)]+)", RegexOptions.IgnoreCase);
            var defaultValue = defaultMatch.Success ? defaultMatch.Groups["def"].Value.Trim() : null;

            var column = new Column
            {
                Name = colName,
                DataType = dataType,
                IsNullable = isNullable,
                DefaultValue = defaultValue
            };

            table.Columns.Add(column);
        }
    }

    private void ParseConstraints(string blockContent, Table table)
    {
        // constraints: table => { ... }
        var constraintsBlockMatch = Regex.Match(blockContent, @"constraints:\s*table\s*=>\s*\{(?<constraints>.*?)\}\s*\)", RegexOptions.Singleline);
        if (!constraintsBlockMatch.Success)
            return;

        var constraints = constraintsBlockMatch.Groups["constraints"].Value;

        // 1. Primary Key: table.PrimaryKey("PK_Feedbacks", x => x.Id); veya x => new { x.Id, x.Col2 }
        var pkMatches = Regex.Matches(constraints, @"table\.PrimaryKey\s*\(\s*""(?<pkName>[^""]+)""\s*,\s*x\s*=>\s*(?:x\.(?<singleCol>\w+)|new\s*\{\s*(?<multiCols>[^\}]+)\}\s*)\)", RegexOptions.Singleline);
        foreach (Match pkMatch in pkMatches)
        {
            if (pkMatch.Groups["singleCol"].Success)
            {
                var colName = pkMatch.Groups["singleCol"].Value;
                var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (col != null) col.IsPrimaryKey = true;
            }
            else if (pkMatch.Groups["multiCols"].Success)
            {
                var cols = Regex.Matches(pkMatch.Groups["multiCols"].Value, @"x\.(?<col>\w+)");
                foreach (Match c in cols)
                {
                    var colName = c.Groups["col"].Value;
                    var col = table.Columns.FirstOrDefault(cl => cl.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                    if (col != null) col.IsPrimaryKey = true;
                }
            }
        }

        // 2. Foreign Key:
        // table.ForeignKey(
        //     name: "FK_ReportSubCategories_ReportCategories_CategoryId",
        //     column: x => x.CategoryId,
        //     principalTable: "ReportCategories",
        //     principalColumn: "Id",
        //     onDelete: ReferentialAction.Cascade);
        var fkMatches = Regex.Matches(constraints, 
            @"table\.ForeignKey\s*\(\s*name:\s*""(?<fkName>[^""]+)""\s*,\s*column:\s*x\s*=>\s*x\.(?<srcCol>\w+)\s*,\s*principalTable:\s*""(?<targetTable>[^""]+)""\s*,\s*principalColumn:\s*""(?<targetCol>[^""]+)""(?:\s*,\s*onDelete:\s*ReferentialAction\.(?<onDel>\w+))?", 
            RegexOptions.Singleline);

        foreach (Match fkMatch in fkMatches)
        {
            var fkName = fkMatch.Groups["fkName"].Value;
            var srcCol = fkMatch.Groups["srcCol"].Value;
            var targetTable = fkMatch.Groups["targetTable"].Value;
            var targetCol = fkMatch.Groups["targetCol"].Value;
            var onDel = fkMatch.Groups["onDel"].Success ? fkMatch.Groups["onDel"].Value : "NO ACTION";

            var fk = new ForeignKey
            {
                ConstraintName = fkName,
                SourceTable = table.Name,
                SourceColumns = new List<string> { srcCol },
                TargetTable = targetTable,
                TargetColumns = new List<string> { targetCol },
                OnDelete = onDel
            };

            table.ForeignKeys.Add(fk);
        }
    }

    private void ParseCreateIndexes(string code, DatabaseSchema schema)
    {
        // migrationBuilder.CreateIndex(
        //     name: "IX_FeedbackNames_Name",
        //     table: "FeedbackNames",
        //     column: "Name",
        //     unique: true);
        var indexMatches = Regex.Matches(code,
            @"migrationBuilder\.CreateIndex\s*\(\s*name:\s*""(?<idxName>[^""]+)""\s*,\s*table:\s*""(?<tblName>[^""]+)""\s*,\s*(?:column:\s*""(?<singleCol>[^""]+)""|columns:\s*new\[\]\s*\{(?<multiCols>[^\}]+)\})(?:\s*,\s*unique:\s*(?<isUnique>true|false))?",
            RegexOptions.Singleline);

        foreach (Match match in indexMatches)
        {
            var idxName = match.Groups["idxName"].Value;
            var tblName = match.Groups["tblName"].Value;
            var isUnique = match.Groups["isUnique"].Success && bool.Parse(match.Groups["isUnique"].Value);

            var table = schema.FindTable(tblName);
            if (table == null)
                continue;

            var cols = new List<string>();
            if (match.Groups["singleCol"].Success)
            {
                cols.Add(match.Groups["singleCol"].Value);
            }
            else if (match.Groups["multiCols"].Success)
            {
                var cleanCols = Regex.Matches(match.Groups["multiCols"].Value, @"""(?<col>[^""]+)""");
                foreach (Match c in cleanCols)
                {
                    cols.Add(c.Groups["col"].Value);
                }
            }

            var index = new Index
            {
                Name = idxName,
                Columns = cols,
                IsUnique = isUnique
            };

            table.Indexes.Add(index);
        }
    }

    /// <summary>
    /// Bir metod çağrısının başladığı noktadan (parantez açılışından) itibaren
    /// eşleşen parantez kapanışına kadar olan kod bloğunu dengeli şekilde çıkarır.
    /// </summary>
    private static string ExtractMethodCallBlock(string code, int startIndex)
    {
        var openParenIndex = code.IndexOf('(', startIndex);
        if (openParenIndex == -1)
            return string.Empty;

        int depth = 0;
        for (int i = openParenIndex; i < code.Length; i++)
        {
            if (code[i] == '(') depth++;
            else if (code[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return code.Substring(startIndex, i - startIndex + 1);
                }
            }
        }

        return string.Empty;
    }
}
