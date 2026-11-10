using SchemaFlow.Core.Models;
using System.Text.RegularExpressions;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Ruby on Rails ekosisteminde standart olan veritabanı şema dosyalarını (db/schema.rb)
/// doğrudan okuyup veritabanı şemasına (AST) dönüştüren ayrıştırıcıdır.
/// </summary>
public class RailsParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.Rails;

    public override Task<DatabaseSchema> ParseSqlAsync(string rubyCode, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "RailsDatabase",
            Dialect = DatabaseDialect.Rails
        };

        if (string.IsNullOrWhiteSpace(rubyCode))
            return Task.FromResult(schema);

        // 1. Yorum satırlarını temizle (# ile başlayan satırlar)
        var cleanCode = Regex.Replace(rubyCode, @"#.*?$", "", RegexOptions.Multiline);

        // 2. create_table "table_name" ... do |t| ... end bloklarını bul
        ParseCreateTables(cleanCode, schema);

        // 3. add_foreign_key "articles", "users" ifadelerini bul
        ParseForeignKeys(cleanCode, schema);

        return Task.FromResult(schema);
    }

    private void ParseCreateTables(string code, DatabaseSchema schema)
    {
        var tableMatches = Regex.Matches(code, @"create_table\s+""(?<tableName>\w+)""(?<options>[^|]*?)\s*do\s*\|t\|(?<bodyText>.*?)end", RegexOptions.Singleline);

        foreach (Match match in tableMatches)
        {
            var tableName = match.Groups["tableName"].Value;
            var options = match.Groups["options"].Value;
            var bodyText = match.Groups["bodyText"].Value;

            var table = new Table
            {
                Name = tableName,
                Description = "Ruby on Rails Model"
            };

            // Rails'de aksi belirtilmedikçe (id: false) her tabloda otomatik id sütunu vardır
            if (!options.Contains("id: false"))
            {
                table.Columns.Add(new Column
                {
                    Name = "id",
                    DataType = "bigint",
                    IsPrimaryKey = true,
                    IsIdentity = true,
                    IsNullable = false
                });
            }

            // Sütunları Çöz: t.string "title", t.text "body", t.datetime "created_at"
            ParseRailsColumns(bodyText, table);

            // İndeksleri Çöz: t.index ["email"], name: "...", unique: true
            ParseRailsIndexes(bodyText, table);

            schema.Tables.Add(table);
        }
    }

    private void ParseRailsColumns(string body, Table table)
    {
        // t.string "name", null: false, default: ""
        var colMatches = Regex.Matches(body, @"t\.(?<colType>\w+)\s+""(?<colName>\w+)""(?<props>[^\r\n]*)");

        foreach (Match match in colMatches)
        {
            var colType = match.Groups["colType"].Value;
            var colName = match.Groups["colName"].Value;
            var props = match.Groups["props"].Value;

            // t.index alan tanımı değil, indekstir; atla
            if (colType.Equals("index", StringComparison.OrdinalIgnoreCase))
                continue;

            var isNullable = !props.Contains("null: false");
            
            string? defaultValue = null;
            var defMatch = Regex.Match(props, @"default:\s*(?:""([^""]*)""|'([^']*)'|(\S+))");
            if (defMatch.Success)
            {
                defaultValue = defMatch.Groups[1].Success ? defMatch.Groups[1].Value :
                               defMatch.Groups[2].Success ? defMatch.Groups[2].Value :
                               defMatch.Groups[3].Value;
            }

            var column = new Column
            {
                Name = colName,
                DataType = MapRailsTypeToSql(colType),
                IsNullable = isNullable,
                DefaultValue = defaultValue
            };

            table.Columns.Add(column);
        }
    }

    private void ParseRailsIndexes(string body, Table table)
    {
        // t.index ["email"], name: "index_users_on_email", unique: true
        var indexMatches = Regex.Matches(body, @"t\.index\s+\[(?<cols>[^\]]+)\](?:\s*,\s*name:\s*""(?<idxName>[^""]+)"")?(?<options>[^\r\n]*)");

        foreach (Match match in indexMatches)
        {
            var cols = match.Groups["cols"].Value
                .Split(',')
                .Select(c => c.Trim().Trim('"', '\'', ':', ' '))
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            var options = match.Groups["options"].Value;
            var isUnique = options.Contains("unique: true");
            var name = match.Groups["idxName"].Success ? match.Groups["idxName"].Value : $"idx_{table.Name}_{string.Join("_", cols)}";

            table.Indexes.Add(new Index
            {
                Name = name,
                Columns = cols,
                IsUnique = isUnique
            });
        }
    }

    private void ParseForeignKeys(string code, DatabaseSchema schema)
    {
        // add_foreign_key "articles", "users"
        // add_foreign_key "articles", "users", column: "author_id", primary_key: "id", on_delete: :cascade
        var fkMatches = Regex.Matches(code, @"add_foreign_key\s+""(?<fromTable>\w+)""\s*,\s*""(?<toTable>\w+)""(?<options>[^\r\n]*)");

        foreach (Match match in fkMatches)
        {
            var fromTable = match.Groups["fromTable"].Value;
            var toTable = match.Groups["toTable"].Value;
            var options = match.Groups["options"].Value;

            var table = schema.FindTable(fromTable);
            if (table == null)
                continue;

            // column: "author_id" belirtilmemişse varsayılan toTable tekil hali + "_id" (users -> user_id)
            string srcCol;
            var colMatch = Regex.Match(options, @"column:\s*""(?<col>\w+)""");
            if (colMatch.Success)
            {
                srcCol = colMatch.Groups["col"].Value;
            }
            else
            {
                // Basit çoğuldan tekile dönüştürme (users -> user_id)
                var singular = toTable.EndsWith('s') ? toTable.Substring(0, toTable.Length - 1) : toTable;
                srcCol = $"{singular}_id";
            }

            // primary_key: "id"
            var pkMatch = Regex.Match(options, @"primary_key:\s*""(?<pk>\w+)""");
            var targetCol = pkMatch.Success ? pkMatch.Groups["pk"].Value : "id";

            // on_delete: :cascade
            var onDelMatch = Regex.Match(options, @"on_delete:\s*:(?<onDel>\w+)");
            var onDel = onDelMatch.Success ? onDelMatch.Groups["onDel"].Value.ToUpperInvariant() : "NO ACTION";

            var fk = new ForeignKey
            {
                ConstraintName = $"FK_{fromTable}_{toTable}_{srcCol}",
                SourceTable = fromTable,
                SourceColumns = new List<string> { srcCol },
                TargetTable = toTable,
                TargetColumns = new List<string> { targetCol },
                OnDelete = onDel
            };

            table.ForeignKeys.Add(fk);
        }
    }

    private static string MapRailsTypeToSql(string railsType) => railsType.ToLowerInvariant() switch
    {
        "string" => "varchar(255)",
        "text" => "text",
        "integer" => "integer",
        "bigint" => "bigint",
        "float" => "double precision",
        "decimal" => "decimal(10,2)",
        "datetime" => "timestamp",
        "timestamp" => "timestamp",
        "time" => "time",
        "date" => "date",
        "binary" => "bytea",
        "boolean" => "boolean",
        "json" or "jsonb" => "jsonb",
        "uuid" => "uuid",
        _ => railsType
    };
}
