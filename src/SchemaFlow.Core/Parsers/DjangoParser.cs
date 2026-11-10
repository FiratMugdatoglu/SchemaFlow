using SchemaFlow.Core.Models;
using System.Text.RegularExpressions;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Python ekosisteminde (Django, Wagtail vb.) standart olan Python migration dosyalarını (.py)
/// doğrudan okuyup veritabanı şemasına (AST) dönüştüren ayrıştırıcıdır.
/// </summary>
public class DjangoParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.Django;

    public override Task<DatabaseSchema> ParseSqlAsync(string pythonCode, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "DjangoDatabase",
            Dialect = DatabaseDialect.Django
        };

        if (string.IsNullOrWhiteSpace(pythonCode))
            return Task.FromResult(schema);

        // 1. migrations.CreateModel(name='...', fields=[...]) bloklarını bul
        var createModelMatches = Regex.Matches(pythonCode, @"migrations\.CreateModel\s*\(\s*name=['""](?<modelName>\w+)['""]\s*,\s*fields=\s*\[(?<fieldsBlock>.*?)\]", RegexOptions.Singleline);

        foreach (Match modelMatch in createModelMatches)
        {
            var modelName = modelMatch.Groups["modelName"].Value;
            var fieldsBlock = modelMatch.Groups["fieldsBlock"].Value;

            var table = new Table
            {
                Name = modelName,
                Description = "Django Model"
            };

            ParseDjangoFields(fieldsBlock, table);

            schema.Tables.Add(table);
        }

        return Task.FromResult(schema);
    }

    private void ParseDjangoFields(string fieldsBlock, Table table)
    {
        // Alan tanımı: ('col_name', models.FieldType(...))
        var fieldMatches = Regex.Matches(fieldsBlock, @"\(\s*['""](?<colName>\w+)['""]\s*,\s*models\.(?<fieldType>\w+)\s*\((?<props>.*?)\)\s*\)", RegexOptions.Singleline);

        foreach (Match match in fieldMatches)
        {
            var colName = match.Groups["colName"].Value;
            var fieldType = match.Groups["fieldType"].Value;
            var props = match.Groups["props"].Value;

            // 1. Yabancı Anahtar (ForeignKey) Kontrolü
            if (fieldType.Equals("ForeignKey", StringComparison.OrdinalIgnoreCase) ||
                fieldType.Equals("OneToOneField", StringComparison.OrdinalIgnoreCase))
            {
                // to='app.Author' veya to='Author' veya to=settings.AUTH_USER_MODEL
                var targetMatch = Regex.Match(props, @"to=['""](?:[\w\.]+\.)?(?<targetTable>\w+)['""]", RegexOptions.IgnoreCase);
                var targetTable = targetMatch.Success ? targetMatch.Groups["targetTable"].Value : "User";

                // on_delete=django.db.models.deletion.CASCADE
                var onDelMatch = Regex.Match(props, @"on_delete=(?:django\.db\.models\.deletion\.)?(?<onDel>\w+)", RegexOptions.IgnoreCase);
                var onDel = onDelMatch.Success ? onDelMatch.Groups["onDel"].Value : "NO ACTION";

                var fk = new ForeignKey
                {
                    ConstraintName = $"FK_{table.Name}_{targetTable}_{colName}",
                    SourceTable = table.Name,
                    SourceColumns = new List<string> { colName + "_id" },
                    TargetTable = targetTable,
                    TargetColumns = new List<string> { "id" },
                    OnDelete = onDel
                };

                table.ForeignKeys.Add(fk);

                // Django veritabanında fiziksel olarak col_name_id sütununu oluşturur
                table.Columns.Add(new Column
                {
                    Name = colName + "_id",
                    DataType = "bigint",
                    IsNullable = props.Contains("null=True")
                });

                continue;
            }

            // 2. Normal Kolon Tanımı
            var isPrimaryKey = props.Contains("primary_key=True") || fieldType.Equals("BigAutoField", StringComparison.OrdinalIgnoreCase);
            var isIdentity = props.Contains("auto_created=True") || fieldType.Contains("AutoField") || fieldType.Equals("BigAutoField", StringComparison.OrdinalIgnoreCase);
            var isNullable = props.Contains("null=True");
            var isUnique = props.Contains("unique=True");

            var column = new Column
            {
                Name = colName,
                DataType = MapDjangoTypeToSql(fieldType, props),
                IsNullable = isNullable,
                IsPrimaryKey = isPrimaryKey,
                IsIdentity = isIdentity
            };

            table.Columns.Add(column);

            if (isUnique)
            {
                table.Indexes.Add(new Index
                {
                    Name = $"UQ_{table.Name}_{colName}",
                    Columns = new List<string> { colName },
                    IsUnique = true
                });
            }
        }
    }

    private static string MapDjangoTypeToSql(string fieldType, string props)
    {
        return fieldType switch
        {
            "BigAutoField" => "bigint",
            "AutoField" => "integer",
            "SmallAutoField" => "smallint",
            "IntegerField" => "integer",
            "BigIntegerField" => "bigint",
            "SmallIntegerField" => "smallint",
            "CharField" => ExtractVarChar(props),
            "TextField" => "text",
            "EmailField" => "varchar(254)",
            "BooleanField" => "boolean",
            "DateTimeField" => "timestamp with time zone",
            "DateField" => "date",
            "DecimalField" => "decimal(12,2)",
            "FloatField" => "double precision",
            "UUIDField" => "uuid",
            "JSONField" => "jsonb",
            _ => fieldType.ToLowerInvariant()
        };
    }

    private static string ExtractVarChar(string props)
    {
        var match = Regex.Match(props, @"max_length=(\d+)");
        return match.Success ? $"varchar({match.Groups[1].Value})" : "varchar(255)";
    }
}
