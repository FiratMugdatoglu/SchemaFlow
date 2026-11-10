using SchemaFlow.Core.Models;
using System.Text.RegularExpressions;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// PostgreSQL DDL ve migration komutlarını ayrıştıran (parse eden) somut strateji sınıfıdır.
/// CREATE TABLE, ALTER TABLE (Foreign Key), CREATE INDEX ve COMMENT ON ifadelerini destekler.
/// </summary>
public class PostgreSqlParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.PostgreSql;

    public override Task<DatabaseSchema> ParseSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            Dialect = SupportedDialect
        };

        if (string.IsNullOrWhiteSpace(sql))
            return Task.FromResult(schema);

        // 1. Yorum satırlarını temizle
        var cleanSql = StripComments(sql);

        // 2. Noktalı virgüle göre ifadeleri ayır
        var statements = SplitStatements(cleanSql);

        // 3. Her ifadeyi türüne göre işle
        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmed = statement.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (Regex.IsMatch(trimmed, @"^CREATE\s+TABLE", RegexOptions.IgnoreCase))
            {
                ProcessCreateTable(trimmed, schema);
            }
            else if (Regex.IsMatch(trimmed, @"^ALTER\s+TABLE", RegexOptions.IgnoreCase))
            {
                ProcessAlterTable(trimmed, schema);
            }
            else if (Regex.IsMatch(trimmed, @"^CREATE\s+(UNIQUE\s+)?INDEX", RegexOptions.IgnoreCase))
            {
                ProcessCreateIndex(trimmed, schema);
            }
            else if (Regex.IsMatch(trimmed, @"^COMMENT\s+ON", RegexOptions.IgnoreCase))
            {
                ProcessComment(trimmed, schema);
            }
        }

        return Task.FromResult(schema);
    }

    /// <summary>
    /// CREATE TABLE [IF NOT EXISTS] tabloadı (...) ifadesini ayrıştırır.
    /// </summary>
    private void ProcessCreateTable(string statement, DatabaseSchema schema)
    {
        var match = Regex.Match(
            statement,
            @"^CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([^\s(]+)\s*\((.*)\)[^)]*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!match.Success)
            return;

        var fullTableName = CleanIdentifier(match.Groups[1].Value);
        var tableBody = match.Groups[2].Value;

        var (schemaName, tableName) = ExtractSchemaAndTableName(fullTableName);

        var table = schema.FindTable(tableName);
        if (table == null)
        {
            table = new Table
            {
                Schema = string.IsNullOrEmpty(schemaName) ? "public" : schemaName,
                Name = tableName
            };
            schema.Tables.Add(table);
        }

        // Tablo gövdesindeki kolon ve kısıtlayıcıları (constraints) virgüllere göre güvenle ayır
        var elements = SplitTableElements(tableBody);

        foreach (var element in elements)
        {
            var elemTrim = element.Trim();
            if (string.IsNullOrWhiteSpace(elemTrim))
                continue;

            // Tablo seviyesi kısıtlayıcı kontrolü: CONSTRAINT ..., PRIMARY KEY (...), FOREIGN KEY (...), KEY/INDEX (...)
            if (Regex.IsMatch(elemTrim, @"^(CONSTRAINT\s+\S+\s+)?(PRIMARY\s+KEY|FOREIGN\s+KEY|UNIQUE|KEY\b|INDEX\b)", RegexOptions.IgnoreCase))
            {
                ProcessTableLevelConstraint(elemTrim, table);
            }
            else
            {
                // Normal sütun tanımı: id UUID NOT NULL PRIMARY KEY ...
                ProcessColumnDefinition(elemTrim, table);
            }
        }
    }

    /// <summary>
    /// Tablo içindeki tek bir sütun tanımını ayrıştırır.
    /// Örn: id UUID PRIMARY KEY DEFAULT gen_random_uuid()
    /// </summary>
    private void ProcessColumnDefinition(string columnDef, Table table)
    {
        // İlk kelime sütun adıdır
        var parts = Regex.Split(columnDef.Trim(), @"\s+");
        if (parts.Length < 2)
            return;

        var colName = CleanIdentifier(parts[0]);
        var remaining = columnDef.Substring(parts[0].Length).Trim();

        var column = new Column
        {
            Name = colName,
            IsNullable = true
        };

        // Veri tipini tespit et (örn: VARCHAR(255), NUMERIC(10,2), UUID, BIGINT)
        var typeMatch = Regex.Match(remaining, @"^([A-Za-z0-9_]+(?:\s*\([^)]*\))?)", RegexOptions.IgnoreCase);
        if (typeMatch.Success)
        {
            column.DataType = typeMatch.Groups[1].Value.Trim();
            remaining = remaining.Substring(typeMatch.Length).Trim();
        }
        else
        {
            column.DataType = parts[1];
            remaining = remaining.Substring(parts[1].Length).Trim();
        }

        // SERIAL / BIGSERIAL veya MySQL AUTO_INCREMENT ise otomatik artan ve PK'dır
        if (column.DataType.Equals("SERIAL", StringComparison.OrdinalIgnoreCase) ||
            column.DataType.Equals("BIGSERIAL", StringComparison.OrdinalIgnoreCase) ||
            column.DataType.Equals("SMALLSERIAL", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(remaining, @"\bAUTO_INCREMENT\b", RegexOptions.IgnoreCase))
        {
            column.IsIdentity = true;
            column.IsPrimaryKey = true;
            column.IsNullable = false;
        }

        // PRIMARY KEY kontrolü
        if (Regex.IsMatch(remaining, @"\bPRIMARY\s+KEY\b", RegexOptions.IgnoreCase))
        {
            column.IsPrimaryKey = true;
            column.IsNullable = false;
        }

        // NOT NULL kontrolü
        if (Regex.IsMatch(remaining, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase))
        {
            column.IsNullable = false;
        }

        // DEFAULT değer kontrolü
        var defaultMatch = Regex.Match(remaining, @"\bDEFAULT\s+([^\s,]+(?:\(.*?\))?)", RegexOptions.IgnoreCase);
        if (defaultMatch.Success)
        {
            column.DefaultValue = defaultMatch.Groups[1].Value;
        }

        // MySQL satır içi COMMENT 'açıklama' kontrolü
        var commentMatch = Regex.Match(remaining, @"\bCOMMENT\s+'(.*?)'", RegexOptions.IgnoreCase);
        if (commentMatch.Success)
        {
            column.Description = commentMatch.Groups[1].Value.Replace("''", "'");
        }

        // Satır içi (inline) REFERENCES foreign key kontrolü: REFERENCES users(id) ON DELETE CASCADE
        var refMatch = Regex.Match(remaining, @"\bREFERENCES\s+([^\s(]+)\s*\(([^)]+)\)(?:\s+ON\s+DELETE\s+([A-Za-z\s]+))?", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            var targetTable = CleanIdentifier(refMatch.Groups[1].Value);
            var targetCol = CleanIdentifier(refMatch.Groups[2].Value);
            var onDelete = refMatch.Groups[3].Success ? refMatch.Groups[3].Value.Trim() : null;

            var (_, cleanTargetTable) = ExtractSchemaAndTableName(targetTable);

            table.ForeignKeys.Add(new ForeignKey
            {
                SourceTable = table.Name,
                SourceColumns = new List<string> { colName },
                TargetTable = cleanTargetTable,
                TargetColumns = new List<string> { targetCol },
                OnDelete = onDelete
            });
        }

        table.Columns.Add(column);
    }

    /// <summary>
    /// Tablo seviyesindeki kısıtlayıcıları (CONSTRAINT fk FOREIGN KEY (...) REFERENCES ...) ayrıştırır.
    /// </summary>
    private void ProcessTableLevelConstraint(string constraintDef, Table table)
    {
        // 1. PRIMARY KEY (col1, col2)
        var pkMatch = Regex.Match(constraintDef, @"PRIMARY\s+KEY\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
        if (pkMatch.Success)
        {
            var pkCols = pkMatch.Groups[1].Value.Split(',').Select(c => CleanIdentifier(c.Trim()));
            foreach (var colName in pkCols)
            {
                var col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
                if (col != null)
                {
                    col.IsPrimaryKey = true;
                    col.IsNullable = false;
                }
            }
            return;
        }

        // 2. FOREIGN KEY (col) REFERENCES other_table(other_col)
        var fkMatch = Regex.Match(
            constraintDef,
            @"(?:CONSTRAINT\s+(\S+)\s+)?FOREIGN\s+KEY\s*\(([^)]+)\)\s*REFERENCES\s+([^\s(]+)\s*\(([^)]+)\)(?:\s+ON\s+DELETE\s+([A-Za-z\s]+))?",
            RegexOptions.IgnoreCase
        );

        if (fkMatch.Success)
        {
            var constraintName = fkMatch.Groups[1].Success ? CleanIdentifier(fkMatch.Groups[1].Value) : null;
            var sourceCols = fkMatch.Groups[2].Value.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            var targetTable = CleanIdentifier(fkMatch.Groups[3].Value);
            var targetCols = fkMatch.Groups[4].Value.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            var onDelete = fkMatch.Groups[5].Success ? fkMatch.Groups[5].Value.Trim() : null;

            var (_, cleanTargetTable) = ExtractSchemaAndTableName(targetTable);

            table.ForeignKeys.Add(new ForeignKey
            {
                ConstraintName = constraintName,
                SourceTable = table.Name,
                SourceColumns = sourceCols,
                TargetTable = cleanTargetTable,
                TargetColumns = targetCols,
                OnDelete = onDelete
            });
            return;
        }

        // 3. MySQL KEY / INDEX / UNIQUE KEY (col1, col2)
        var keyMatch = Regex.Match(constraintDef, @"^(?:UNIQUE\s+)?(?:KEY|INDEX)\s+(?:`?([^\s(]+)`?\s+)?\(([^)]+)\)", RegexOptions.IgnoreCase);
        if (keyMatch.Success)
        {
            var isUnique = constraintDef.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase);
            var idxName = keyMatch.Groups[1].Success ? CleanIdentifier(keyMatch.Groups[1].Value) : $"IX_{table.Name}";
            var cols = keyMatch.Groups[2].Value.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            table.Indexes.Add(new Index { Name = idxName, Columns = cols, IsUnique = isUnique });
        }
    }

    /// <summary>
    /// ALTER TABLE tabloadı ADD CONSTRAINT ... ifadesini ayrıştırır.
    /// </summary>
    private void ProcessAlterTable(string statement, DatabaseSchema schema)
    {
        var match = Regex.Match(
            statement,
            @"ALTER\s+TABLE\s+(?:ONLY\s+)?([^\s]+)\s+ADD\s+(?:CONSTRAINT\s+(\S+)\s+)?FOREIGN\s+KEY\s*\(([^)]+)\)\s*REFERENCES\s+([^\s(]+)\s*\(([^)]+)\)(?:\s+ON\s+DELETE\s+([A-Za-z\s]+))?",
            RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            var sourceFullTable = CleanIdentifier(match.Groups[1].Value);
            var constraintName = match.Groups[2].Success ? CleanIdentifier(match.Groups[2].Value) : null;
            var sourceCols = match.Groups[3].Value.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            var targetTable = CleanIdentifier(match.Groups[4].Value);
            var targetCols = match.Groups[5].Value.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            var onDelete = match.Groups[6].Success ? match.Groups[6].Value.Trim() : null;

            var (_, srcTableName) = ExtractSchemaAndTableName(sourceFullTable);
            var (_, tgtTableName) = ExtractSchemaAndTableName(targetTable);

            var table = schema.FindTable(srcTableName);
            if (table != null)
            {
                table.ForeignKeys.Add(new ForeignKey
                {
                    ConstraintName = constraintName,
                    SourceTable = table.Name,
                    SourceColumns = sourceCols,
                    TargetTable = tgtTableName,
                    TargetColumns = targetCols,
                    OnDelete = onDelete
                });
            }
        }
    }

    /// <summary>
    /// CREATE [UNIQUE] INDEX idx_name ON tabloadı (col1, col2) ifadesini ayrıştırır.
    /// </summary>
    private void ProcessCreateIndex(string statement, DatabaseSchema schema)
    {
        var match = Regex.Match(
            statement,
            @"CREATE\s+(UNIQUE\s+)?INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?([^\s]+)\s+ON\s+([^\s(]+)(?:\s+USING\s+\w+)?\s*\(([^)]+)\)(?:\s+WHERE\s+(.+))?",
            RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            var isUnique = match.Groups[1].Success;
            var indexName = CleanIdentifier(match.Groups[2].Value);
            var tableNameFull = CleanIdentifier(match.Groups[3].Value);
            var cols = match.Groups[4].Value.Split(',').Select(c => CleanIdentifier(c.Trim())).ToList();
            var whereClause = match.Groups[5].Success ? match.Groups[5].Value.Trim() : null;

            var (_, tableName) = ExtractSchemaAndTableName(tableNameFull);

            var table = schema.FindTable(tableName);
            if (table != null)
            {
                table.Indexes.Add(new Index
                {
                    Name = indexName,
                    Columns = cols,
                    IsUnique = isUnique,
                    FilterPredicate = whereClause
                });
            }
        }
    }

    /// <summary>
    /// COMMENT ON TABLE / COLUMN ifadelerini dokümantasyon açıklaması olarak işler.
    /// </summary>
    private void ProcessComment(string statement, DatabaseSchema schema)
    {
        // 1. COMMENT ON TABLE tabloadı IS 'açıklama'
        var tableMatch = Regex.Match(statement, @"COMMENT\s+ON\s+TABLE\s+([^\s]+)\s+IS\s+'(.*)'", RegexOptions.IgnoreCase);
        if (tableMatch.Success)
        {
            var (_, tableName) = ExtractSchemaAndTableName(CleanIdentifier(tableMatch.Groups[1].Value));
            var comment = tableMatch.Groups[2].Value.Replace("''", "'");

            var table = schema.FindTable(tableName);
            if (table != null)
            {
                table.Description = comment;
            }
            return;
        }

        // 2. COMMENT ON COLUMN tabloadı.kolonadı IS 'açıklama'
        var colMatch = Regex.Match(statement, @"COMMENT\s+ON\s+COLUMN\s+([^\s]+)\.([^\s]+)\s+IS\s+'(.*)'", RegexOptions.IgnoreCase);
        if (colMatch.Success)
        {
            var (_, tableName) = ExtractSchemaAndTableName(CleanIdentifier(colMatch.Groups[1].Value));
            var colName = CleanIdentifier(colMatch.Groups[2].Value);
            var comment = colMatch.Groups[3].Value.Replace("''", "'");

            var table = schema.FindTable(tableName);
            var column = table?.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
            if (column != null)
            {
                column.Description = comment;
            }
        }
    }

    /// <summary>
    /// Parantez derinliğini koruyarak CREATE TABLE gövdesini virgüllerden böler.
    /// NUMERIC(10, 2) gibi iç virgüllerin yanlış bölünmesini önler.
    /// </summary>
    private static List<string> SplitTableElements(string body)
    {
        var elements = new List<string>();
        var current = new System.Text.StringBuilder();
        var parenDepth = 0;
        var inQuote = false;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (c == '\'')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote)
            {
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth = Math.Max(0, parenDepth - 1);
            }

            if (c == ',' && !inQuote && parenDepth == 0)
            {
                var el = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(el))
                    elements.Add(el);
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        var remaining = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
            elements.Add(remaining);

        return elements;
    }

    /// <summary>
    /// "public.users" gibi şemalı bir adı (Schema: "public", Name: "users") şeklinde parçalar.
    /// </summary>
    private static (string? Schema, string Name) ExtractSchemaAndTableName(string identifier)
    {
        if (identifier.Contains('.'))
        {
            var parts = identifier.Split('.', 2);
            return (parts[0].Trim(), parts[1].Trim());
        }

        return (null, identifier.Trim());
    }
}
