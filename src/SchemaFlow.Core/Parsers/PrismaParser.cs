using SchemaFlow.Core.Models;
using System.Text.RegularExpressions;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Node.js ve TypeScript dünyasında standart olan Prisma şema dosyalarını (schema.prisma)
/// doğrudan okuyup veritabanı şemasına (AST) dönüştüren ayrıştırıcıdır.
/// </summary>
public class PrismaParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.Prisma;

    // Prisma standart skaler tipleri
    private static readonly HashSet<string> ScalarTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "String", "Boolean", "Int", "BigInt", "Float", "Decimal", "DateTime", "Json", "Bytes"
    };

    public override Task<DatabaseSchema> ParseSqlAsync(string prismaCode, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "PrismaDatabase",
            Dialect = DatabaseDialect.Prisma
        };

        if (string.IsNullOrWhiteSpace(prismaCode))
            return Task.FromResult(schema);

        // 1. Yorum satırlarını temizle (// ve ///)
        var cleanCode = CleanPrismaComments(prismaCode);

        // 2. Modelleri (model ModelName { ... }) bul ve ayrıştır
        var modelMatches = Regex.Matches(cleanCode, @"model\s+(?<modelName>\w+)\s*\{(?<modelBody>[^\}]+)\}", RegexOptions.Singleline);

        foreach (Match match in modelMatches)
        {
            var modelName = match.Groups["modelName"].Value;
            var body = match.Groups["modelBody"].Value;

            var table = new Table
            {
                Name = modelName,
                Description = "Prisma Model"
            };

            ParseModelFields(body, table);
            ParseBlockAttributes(body, table);

            schema.Tables.Add(table);
        }

        return Task.FromResult(schema);
    }

    private void ParseModelFields(string body, Table table)
    {
        var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Blok özellikleri (@@id, @@index, @@map) alan tanımı değildir
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("@@"))
                continue;

            // Alan formatı: fieldName FieldType Attributes...
            // Örnek 1: id Int @id @default(autoincrement())
            // Örnek 2: author User @relation(fields: [authorId], references: [id])
            // Örnek 3: posts Post[]
            var tokens = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var fieldName = tokens[0].Trim();
            var rawType = tokens[1].Trim();
            var attributes = tokens.Length > 2 ? tokens[2].Trim() : string.Empty;

            var isNullable = rawType.EndsWith('?');
            var isList = rawType.EndsWith("[]");
            var cleanType = rawType.TrimEnd('?', '[', ']');

            // 1. Yabancı Anahtar (Relation) Kontrolü
            if (attributes.Contains("@relation"))
            {
                // @relation(fields: [authorId], references: [id])
                var relationMatch = Regex.Match(attributes, @"@relation\s*\(\s*(?:name:\s*""[^""]+""\s*,\s*)?(?:fields:\s*\[(?<fields>[^\]]+)\])?\s*,?\s*(?:references:\s*\[(?<refs>[^\]]+)\])?(?:\s*,?\s*onDelete:\s*(?<onDel>\w+))?", RegexOptions.IgnoreCase);

                if (relationMatch.Success && relationMatch.Groups["fields"].Success && relationMatch.Groups["refs"].Success)
                {
                    var sourceCols = relationMatch.Groups["fields"].Value.Split(',').Select(s => s.Trim()).ToList();
                    var targetCols = relationMatch.Groups["refs"].Value.Split(',').Select(s => s.Trim()).ToList();
                    var onDel = relationMatch.Groups["onDel"].Success ? relationMatch.Groups["onDel"].Value : "NO ACTION";

                    var fk = new ForeignKey
                    {
                        ConstraintName = $"FK_{table.Name}_{cleanType}_{string.Join("_", sourceCols)}",
                        SourceTable = table.Name,
                        SourceColumns = sourceCols,
                        TargetTable = cleanType, // İlişki kurulan hedef model
                        TargetColumns = targetCols,
                        OnDelete = onDel
                    };

                    table.ForeignKeys.Add(fk);
                }

                // Sanal ilişki alanı (virtual navigation property) veritabanında fiziksel kolon değildir, atla
                continue;
            }

            // Eğer tip bir skaler tip değilse ve liste ise (örn: posts Post[]), sanal ters ilişkidir
            if (isList || (!ScalarTypes.Contains(cleanType) && !attributes.Contains("@id")))
            {
                // Fiziksel kolon değil, ters ilişki
                continue;
            }

            // 2. Normal Fiziksel Kolon
            var isPrimaryKey = attributes.Contains("@id");
            var isIdentity = attributes.Contains("autoincrement()") || attributes.Contains("uuid()");

            // Varsayılan Değer: @default(...)
            string? defaultValue = null;
            var defaultMatch = Regex.Match(attributes, @"@default\s*\((?<def>[^\)]+)\)");
            if (defaultMatch.Success)
            {
                defaultValue = defaultMatch.Groups["def"].Value.Trim();
            }

            // Map desteği: @map("db_col_name")
            var mapMatch = Regex.Match(attributes, @"@map\s*\(\s*""(?<mappedName>[^""]+)""\s*\)");
            if (mapMatch.Success)
            {
                fieldName = mapMatch.Groups["mappedName"].Value;
            }

            var column = new Column
            {
                Name = fieldName,
                DataType = MapPrismaTypeToSql(cleanType),
                IsNullable = isNullable,
                IsPrimaryKey = isPrimaryKey,
                IsIdentity = isIdentity,
                DefaultValue = defaultValue
            };

            table.Columns.Add(column);

            // @unique ise index ekle
            if (attributes.Contains("@unique"))
            {
                table.Indexes.Add(new Index
                {
                    Name = $"UQ_{table.Name}_{fieldName}",
                    Columns = new List<string> { fieldName },
                    IsUnique = true
                });
            }
        }
    }

    private void ParseBlockAttributes(string body, Table table)
    {
        // 1. @@id([col1, col2]) - Bileşik Birincil Anahtar
        var compositePkMatch = Regex.Match(body, @"@@id\s*\(\s*\[(?<cols>[^\]]+)\]\s*\)");
        if (compositePkMatch.Success)
        {
            var cols = compositePkMatch.Groups["cols"].Value.Split(',').Select(c => c.Trim());
            foreach (var colName in cols)
            {
                var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (col != null) col.IsPrimaryKey = true;
            }
        }

        // 2. @@unique([col1, col2]) - Bileşik Benzersiz İndeks
        var uniqueMatches = Regex.Matches(body, @"@@unique\s*\(\s*\[(?<cols>[^\]]+)\](?:\s*,\s*name:\s*""(?<idxName>[^""]+)"")?\s*\)");
        foreach (Match match in uniqueMatches)
        {
            var cols = match.Groups["cols"].Value.Split(',').Select(c => c.Trim()).ToList();
            var name = match.Groups["idxName"].Success ? match.Groups["idxName"].Value : $"UQ_{table.Name}_{string.Join("_", cols)}";

            table.Indexes.Add(new Index
            {
                Name = name,
                Columns = cols,
                IsUnique = true
            });
        }

        // 3. @@index([col1, col2]) - Standart İndeks
        var indexMatches = Regex.Matches(body, @"@@index\s*\(\s*\[(?<cols>[^\]]+)\](?:\s*,\s*name:\s*""(?<idxName>[^""]+)"")?\s*\)");
        foreach (Match match in indexMatches)
        {
            var cols = match.Groups["cols"].Value.Split(',').Select(c => c.Trim()).ToList();
            var name = match.Groups["idxName"].Success ? match.Groups["idxName"].Value : $"IX_{table.Name}_{string.Join("_", cols)}";

            table.Indexes.Add(new Index
            {
                Name = name,
                Columns = cols,
                IsUnique = false
            });
        }

        // 4. @@map("actual_table_name")
        var mapMatch = Regex.Match(body, @"@@map\s*\(\s*""(?<tableName>[^""]+)""\s*\)");
        if (mapMatch.Success)
        {
            table.Name = mapMatch.Groups["tableName"].Value;
        }
    }

    private static string MapPrismaTypeToSql(string prismaType) => prismaType switch
    {
        "Int" => "integer",
        "BigInt" => "bigint",
        "String" => "text",
        "Boolean" => "boolean",
        "DateTime" => "timestamp",
        "Float" => "double precision",
        "Decimal" => "decimal(10,2)",
        "Json" => "jsonb",
        "Bytes" => "bytea",
        _ => prismaType.ToLowerInvariant()
    };

    private static string CleanPrismaComments(string code)
    {
        // Satır sonu yorumlarını temizle: // ... ve /// ...
        return Regex.Replace(code, @"\/{2,3}.*?$", "", RegexOptions.Multiline);
    }
}
