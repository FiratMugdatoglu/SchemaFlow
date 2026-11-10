using SchemaFlow.Core.Models;

namespace SchemaFlow.Core.Services;

/// <summary>
/// Veritabanı şemasındaki mimari eksiklikleri, performans darboğazlarını
/// ve anti-pattern'ları denetleyen kural motorudur.
/// </summary>
public class SchemaLinterEngine
{
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "ORDER", "GROUP", "USER", "TABLE", "INDEX", 
        "KEY", "LIMIT", "OFFSET", "CHECK", "COLUMN", "JOIN", "HAVING", "UPDATE", 
        "DELETE", "INSERT", "DEFAULT", "PRIMARY", "FOREIGN", "DESC", "ASC", "ALL"
    };

    /// <summary>
    /// Verilen veritabanı şemasını tüm linter kurallarına göre analiz eder ve bulguları listeler.
    /// </summary>
    public List<LintIssue> Analyze(DatabaseSchema schema)
    {
        var issues = new List<LintIssue>();

        foreach (var table in schema.Tables)
        {
            // 1. Kural: SF001 - Eksik Primary Key Denetimi (Error)
            CheckMissingPrimaryKey(table, issues);

            // 2. Kural: SF002 - İndekssiz Foreign Key Denetimi (Warning - Performans)
            CheckMissingForeignKeyIndexes(table, issues);

            // 3. Kural: SF003 - Rezerve SQL Kelimeleri Denetimi (Warning - Syntax)
            CheckReservedKeywords(table, issues);

            // 4. Kural: SF004 - Nullable Primary Key Denetimi (Error)
            CheckNullablePrimaryKeys(table, issues);

            // 5. Kural: SF005 - Aşırı Büyük / Sınırsız İndeks Tipleri (Info)
            CheckUnboundedIndexedColumns(table, issues);
        }

        return issues.OrderBy(i => i.Severity).ThenBy(i => i.TableName).ToList();
    }

    private static void CheckMissingPrimaryKey(Table table, List<LintIssue> issues)
    {
        if (!table.PrimaryKeyColumns.Any())
        {
            issues.Add(new LintIssue
            {
                RuleId = "SF001",
                Severity = LintSeverity.Error,
                TableName = table.Name,
                Message = $"'{table.Name}' tablosunda tanımlı bir Birincil Anahtar (Primary Key) bulunamadı.",
                Recommendation = "Tablodaki her satırın tekil kimliğini garanti altına almak için en az bir Primary Key (örn: id) sütunu tanımlayın."
            });
        }
    }

    private static void CheckMissingForeignKeyIndexes(Table table, List<LintIssue> issues)
    {
        foreach (var fk in table.ForeignKeys)
        {
            foreach (var col in fk.SourceColumns)
            {
                // Kolon ya Primary Key olmalı ya da tablonun bir Index'inde ilk kolon olarak yer almalı
                var isPk = table.PrimaryKeyColumns.Any(pk => string.Equals(pk.Name, col, StringComparison.OrdinalIgnoreCase));
                var isIndexed = table.Indexes.Any(idx => idx.Columns.Any(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase)));

                if (!isPk && !isIndexed)
                {
                    issues.Add(new LintIssue
                    {
                        RuleId = "SF002",
                        Severity = LintSeverity.Warning,
                        TableName = table.Name,
                        ColumnName = col,
                        Message = $"'{table.Name}.{col}' yabancı anahtarı (FK) üzerinde indeks (Index) tanımlanmamış.",
                        Recommendation = $"Büyük tablolarda JOIN ve CASCADE silme performansını korumak için 'CREATE INDEX idx_{table.Name}_{col} ON {table.Name}({col});' ekleyin."
                    });
                }
            }
        }
    }

    private static void CheckReservedKeywords(Table table, List<LintIssue> issues)
    {
        if (ReservedKeywords.Contains(table.Name))
        {
            issues.Add(new LintIssue
            {
                RuleId = "SF003",
                Severity = LintSeverity.Warning,
                TableName = table.Name,
                Message = $"'{table.Name}' tablo adı SQL rezerve anahtar kelimesi ile çakışıyor.",
                Recommendation = $"Sorgu yazarken sözdizimi hatalarından kaçınmak için çoğul veya açıklayıcı bir isim kullanın (örn: '{table.Name}s' veya 'app_{table.Name}')."
            });
        }

        foreach (var col in table.Columns)
        {
            if (ReservedKeywords.Contains(col.Name))
            {
                issues.Add(new LintIssue
                {
                    RuleId = "SF003",
                    Severity = LintSeverity.Warning,
                    TableName = table.Name,
                    ColumnName = col.Name,
                    Message = $"'{table.Name}.{col.Name}' sütun adı SQL rezerve anahtar kelimesidir.",
                    Recommendation = $"Sorgularda tırnak zorunluluğunu ve ORM hatalarını önlemek için sütun adını netleştirin (örn: '{col.Name}_date', '{col.Name}_status')."
                });
            }
        }
    }

    private static void CheckNullablePrimaryKeys(Table table, List<LintIssue> issues)
    {
        foreach (var pk in table.PrimaryKeyColumns)
        {
            if (pk.IsNullable)
            {
                issues.Add(new LintIssue
                {
                    RuleId = "SF004",
                    Severity = LintSeverity.Error,
                    TableName = table.Name,
                    ColumnName = pk.Name,
                    Message = $"'{table.Name}.{pk.Name}' birincil anahtar sütunu NULL değer alabilir (Nullable) olarak işaretlenmiş.",
                    Recommendation = "Primary Key sütunları hiçbir koşulda NULL olamaz. Sütunu 'NOT NULL' olarak tanımlayın."
                });
            }
        }
    }

    private static void CheckUnboundedIndexedColumns(Table table, List<LintIssue> issues)
    {
        foreach (var col in table.Columns)
        {
            var isIndexed = table.Indexes.Any(i => i.Columns.Contains(col.Name));
            var isText = col.DataType.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
                         col.DataType.Contains("MAX", StringComparison.OrdinalIgnoreCase);

            if (isIndexed && isText)
            {
                issues.Add(new LintIssue
                {
                    RuleId = "SF005",
                    Severity = LintSeverity.Info,
                    TableName = table.Name,
                    ColumnName = col.Name,
                    Message = $"'{table.Name}.{col.Name}' sütununda sınırsız metin tipi ('{col.DataType}') üzerinde indeks tanımlanmış.",
                    Recommendation = "İndeks boyutunu ve bellek kullanımını optimize etmek için sütun tipini sınırlandırılmış bir VARCHAR(n) veya prefix index olarak kullanın."
                });
            }
        }
    }
}
