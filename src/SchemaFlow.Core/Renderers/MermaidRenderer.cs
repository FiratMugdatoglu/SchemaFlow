using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using System.Text;

namespace SchemaFlow.Core.Renderers;

/// <summary>
/// DatabaseSchema nesnesini GitHub ve Markdown dostu interaktif Mermaid.js 'erDiagram' koduna dönüştürür.
/// </summary>
public class MermaidRenderer : ISchemaRenderer
{
    public string OutputFormat => "mermaid";

    public string Render(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        // 1. İlişkileri (Relationships) Yaz
        // Mermaid formatı: parent_table ||--o{ child_table : "label"
        var allForeignKeys = schema.GetAllForeignKeys().ToList();

        if (allForeignKeys.Any())
        {
            foreach (var fk in allForeignKeys)
            {
                var parentTable = CleanMermaidEntityName(fk.TargetTable);
                var childTable = CleanMermaidEntityName(fk.SourceTable);
                var label = string.IsNullOrWhiteSpace(fk.ConstraintName)
                    ? string.Join("_", fk.SourceColumns)
                    : fk.ConstraintName;

                // 1'e Çok ilişki gösterimi (Parent ||--o{ Child)
                sb.AppendLine($"    {parentTable} ||--o{{ {childTable} : \"{label}\"");
            }
            sb.AppendLine();
        }

        // 2. Tabloları ve Sütunlarını Yaz
        foreach (var table in schema.Tables)
        {
            var entityName = CleanMermaidEntityName(table.Name);
            sb.AppendLine($"    {entityName} {{");

            foreach (var col in table.Columns)
            {
                var sanitizedType = SanitizeMermaidType(col.DataType);
                var keyAttribute = ResolveKeyAttribute(table, col);
                var comment = string.IsNullOrWhiteSpace(col.Description) 
                    ? string.Empty 
                    : $" \"{col.Description.Replace("\"", "'")}\"";

                if (!string.IsNullOrEmpty(keyAttribute))
                {
                    sb.AppendLine($"        {sanitizedType} {col.Name} {keyAttribute}{comment}");
                }
                else
                {
                    sb.AppendLine($"        {sanitizedType} {col.Name}{comment}");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Sütunun anahtar etiketini belirler (PK, FK veya PK,FK)
    /// </summary>
    private static string ResolveKeyAttribute(Table table, Column col)
    {
        var isPk = col.IsPrimaryKey;
        var isFk = table.ForeignKeys.Any(fk => fk.SourceColumns.Contains(col.Name, StringComparer.OrdinalIgnoreCase));

        if (isPk && isFk) return "PK,FK";
        if (isPk) return "PK";
        if (isFk) return "FK";

        return string.Empty;
    }

    /// <summary>
    /// Mermaid veri tiplerinde boşluk ve özel karakterleri temizler.
    /// Örn: "numeric(10, 2)" -> "numeric(10_2)", "timestamp with time zone" -> "timestamptz"
    /// </summary>
    private static string SanitizeMermaidType(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
            return "text";

        var sanitized = dataType.Trim();

        // Yaygın uzun tipleri kısalt
        if (sanitized.Contains("with time zone", StringComparison.OrdinalIgnoreCase))
            return "timestamptz";

        if (sanitized.Contains("without time zone", StringComparison.OrdinalIgnoreCase))
            return "timestamp";

        // Parantez içi boşluk ve virgülleri Mermaid syntax'ını bozmayacak şekle getir
        sanitized = sanitized.Replace(", ", "_").Replace(",", "_").Replace(" ", "_");

        return sanitized;
    }

    /// <summary>
    /// Mermaid tablo isimlerinde tire veya özel karakter varsa temizler.
    /// </summary>
    private static string CleanMermaidEntityName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return "unknown";

        return tableName.Replace("-", "_").Replace(".", "_").Trim();
    }
}
