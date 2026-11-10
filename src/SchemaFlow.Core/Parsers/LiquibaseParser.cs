using SchemaFlow.Core.Models;
using System.Xml.Linq;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Java dünyasında (Spring Boot, Quarkus vb.) ve kurumsal bankacılık sistemlerinde
/// standart olan Liquibase XML veritabanı migration dosyalarını (changelog.xml)
/// doğrudan okuyup veritabanı şemasına (AST) dönüştüren ayrıştırıcıdır.
/// </summary>
public class LiquibaseParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.Liquibase;

    public override async Task<DatabaseSchema> ParseFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "LiquibaseDatabase",
            Dialect = DatabaseDialect.Liquibase
        };

        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path, cancellationToken);
                var fileSchema = await ParseSqlAsync(content, cancellationToken);
                schema.Tables.AddRange(fileSchema.Tables);
            }
        }

        return schema;
    }

    public override Task<DatabaseSchema> ParseSqlAsync(string xmlContent, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "LiquibaseDatabase",
            Dialect = DatabaseDialect.Liquibase
        };

        if (string.IsNullOrWhiteSpace(xmlContent))
            return Task.FromResult(schema);

        try
        {
            var cleanXml = xmlContent.Trim();
            while (cleanXml.EndsWith(';') || cleanXml.EndsWith('\n') || cleanXml.EndsWith('\r') || cleanXml.EndsWith(' '))
            {
                cleanXml = cleanXml.TrimEnd(';', '\r', '\n', ' ');
            }

            var doc = XDocument.Parse(cleanXml);

            // 1. <createTable> öğelerini bul
            var createTables = doc.Descendants().Where(e => e.Name.LocalName == "createTable");
            foreach (var tableElem in createTables)
            {
                var tableName = tableElem.Attribute("tableName")?.Value;
                if (string.IsNullOrWhiteSpace(tableName))
                    continue;

                var table = new Table
                {
                    Name = tableName,
                    Description = tableElem.Attribute("remarks")?.Value ?? "Liquibase Table"
                };

                // Kolonları Çöz: <column>
                var columns = tableElem.Elements().Where(e => e.Name.LocalName == "column");
                foreach (var colElem in columns)
                {
                    var colName = colElem.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(colName))
                        continue;

                    var dataType = colElem.Attribute("type")?.Value ?? "varchar(255)";
                    var autoIncrement = colElem.Attribute("autoIncrement")?.Value;
                    var isIdentity = string.Equals(autoIncrement, "true", StringComparison.OrdinalIgnoreCase);
                    var defaultValue = colElem.Attribute("defaultValue")?.Value ?? colElem.Attribute("defaultValueComputed")?.Value;
                    var remarks = colElem.Attribute("remarks")?.Value;

                    // <constraints> alt öğesi
                    var constraintsElem = colElem.Elements().FirstOrDefault(e => e.Name.LocalName == "constraints");
                    var isPrimaryKey = false;
                    var isNullable = true;
                    var isUnique = false;

                    if (constraintsElem != null)
                    {
                        var pkAttr = constraintsElem.Attribute("primaryKey")?.Value;
                        isPrimaryKey = string.Equals(pkAttr, "true", StringComparison.OrdinalIgnoreCase);

                        var nullableAttr = constraintsElem.Attribute("nullable")?.Value;
                        if (!string.IsNullOrWhiteSpace(nullableAttr))
                        {
                            isNullable = string.Equals(nullableAttr, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (isPrimaryKey)
                        {
                            isNullable = false;
                        }

                        var uniqueAttr = constraintsElem.Attribute("unique")?.Value;
                        isUnique = string.Equals(uniqueAttr, "true", StringComparison.OrdinalIgnoreCase);

                        // Satır içi references="users(id)"
                        var referencesAttr = constraintsElem.Attribute("references")?.Value;
                        if (!string.IsNullOrWhiteSpace(referencesAttr))
                        {
                            ParseInlineReferences(table, colName, referencesAttr);
                        }
                    }

                    if (isIdentity)
                        isPrimaryKey = true;

                    var column = new Column
                    {
                        Name = colName,
                        DataType = dataType,
                        IsNullable = isNullable,
                        IsPrimaryKey = isPrimaryKey,
                        IsIdentity = isIdentity,
                        DefaultValue = defaultValue,
                        Description = remarks
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

                schema.Tables.Add(table);
            }

            // 2. <addForeignKeyConstraint> öğelerini bul
            var fkConstraints = doc.Descendants().Where(e => e.Name.LocalName == "addForeignKeyConstraint");
            foreach (var fkElem in fkConstraints)
            {
                var baseTableName = fkElem.Attribute("baseTableName")?.Value;
                var baseColumnNames = fkElem.Attribute("baseColumnNames")?.Value;
                var referencedTableName = fkElem.Attribute("referencedTableName")?.Value;
                var referencedColumnNames = fkElem.Attribute("referencedColumnNames")?.Value;
                var constraintName = fkElem.Attribute("constraintName")?.Value;
                var onDelete = fkElem.Attribute("onDelete")?.Value ?? "NO ACTION";

                if (string.IsNullOrWhiteSpace(baseTableName) || string.IsNullOrWhiteSpace(referencedTableName))
                    continue;

                var table = schema.FindTable(baseTableName);
                if (table == null)
                    continue;

                var srcCols = (baseColumnNames ?? "").Split(',').Select(c => c.Trim()).ToList();
                var targetCols = (referencedColumnNames ?? "").Split(',').Select(c => c.Trim()).ToList();

                table.ForeignKeys.Add(new ForeignKey
                {
                    ConstraintName = constraintName ?? $"FK_{baseTableName}_{referencedTableName}",
                    SourceTable = baseTableName,
                    SourceColumns = srcCols,
                    TargetTable = referencedTableName,
                    TargetColumns = targetCols,
                    OnDelete = onDelete
                });
            }

            // 3. <createIndex> öğelerini bul
            var createIndexes = doc.Descendants().Where(e => e.Name.LocalName == "createIndex");
            foreach (var idxElem in createIndexes)
            {
                var tableName = idxElem.Attribute("tableName")?.Value;
                var indexName = idxElem.Attribute("indexName")?.Value;
                var isUnique = string.Equals(idxElem.Attribute("unique")?.Value, "true", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(indexName))
                    continue;

                var table = schema.FindTable(tableName);
                if (table == null)
                    continue;

                var cols = idxElem.Elements()
                    .Where(e => e.Name.LocalName == "column")
                    .Select(e => e.Attribute("name")?.Value ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                table.Indexes.Add(new Index
                {
                    Name = indexName,
                    Columns = cols,
                    IsUnique = isUnique
                });
            }
        }
        catch
        {
            // XML parse hatası olursa mevcut şemayı dön
        }

        return Task.FromResult(schema);
    }

    private static void ParseInlineReferences(Table table, string colName, string references)
    {
        // Örnek: references="users(id)"
        var openParen = references.IndexOf('(');
        var closeParen = references.IndexOf(')');

        if (openParen > 0 && closeParen > openParen)
        {
            var targetTable = references.Substring(0, openParen).Trim();
            var targetCol = references.Substring(openParen + 1, closeParen - openParen - 1).Trim();

            table.ForeignKeys.Add(new ForeignKey
            {
                ConstraintName = $"FK_{table.Name}_{targetTable}_{colName}",
                SourceTable = table.Name,
                SourceColumns = new List<string> { colName },
                TargetTable = targetTable,
                TargetColumns = new List<string> { targetCol }
            });
        }
    }
}
