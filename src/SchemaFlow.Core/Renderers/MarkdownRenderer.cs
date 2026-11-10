using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using System.Text;

namespace SchemaFlow.Core.Renderers;

/// <summary>
/// DatabaseSchema nesnesini GitHub Wiki, README.md ve dokümantasyon sayfaları için
/// zengin, tablolu ve içinde Mermaid diyagramı gömülü Markdown dokümanına dönüştürür.
/// </summary>
public class MarkdownRenderer : ISchemaRenderer
{
    private readonly MermaidRenderer _mermaidRenderer = new();

    public string OutputFormat => "markdown";

    public string Render(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        // 1. Doküman Başlığı ve Bilgi Özeti
        sb.AppendLine("# 🗄️ Veritabanı Şema Dokümantasyonu");
        sb.AppendLine();
        sb.AppendLine($"> **Motor (Dialect):** `{schema.Dialect}`  ");
        sb.AppendLine($"> **Toplam Tablo Sayısı:** `{schema.Tables.Count}`  ");
        sb.AppendLine($"> **Oluşturulma Tarihi:** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`  ");
        sb.AppendLine($"> *Bu dokümantasyon [SchemaFlow.ai](https://github.com) tarafından otomatik üretilmiştir.*");
        sb.AppendLine();

        // 2. İçindekiler (Table of Contents)
        sb.AppendLine("## 📑 İçindekiler");
        sb.AppendLine("- [📊 İlişkisel ER Diyagramı](#-ilişkisel-er-diyagramı)");
        sb.AppendLine("- [📋 Tablolar](#-tablolar)");
        foreach (var table in schema.Tables)
        {
            var anchor = table.Name.ToLowerInvariant().Replace(" ", "-").Replace(".", "");
            sb.AppendLine($"  - [{table.FullName}](#{anchor})");
        }
        sb.AppendLine();

        // 3. Görsel ER Diyagramı (Mermaid Embed)
        sb.AppendLine("## 📊 İlişkisel ER Diyagramı");
        sb.AppendLine("```mermaid");
        sb.AppendLine(_mermaidRenderer.Render(schema));
        sb.AppendLine("```");
        sb.AppendLine();

        // 4. Tablo Detayları ve Veri Sözlüğü (Data Dictionary)
        sb.AppendLine("## 📋 Tablolar");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"### `{table.FullName}`");
            if (!string.IsNullOrWhiteSpace(table.Description))
            {
                sb.AppendLine($"*{table.Description}*");
                sb.AppendLine();
            }

            // Sütunlar Tablosu
            sb.AppendLine("#### Sütunlar");
            sb.AppendLine("| Adı | Tipi | PK | Nullable | Varsayılan | Açıklama |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :--- | :--- |");

            foreach (var col in table.Columns)
            {
                var pk = col.IsPrimaryKey ? "✅" : "—";
                var nullable = col.IsNullable ? "Evet" : "**Hayır**";
                var defVal = string.IsNullOrWhiteSpace(col.DefaultValue) ? "—" : $"`{col.DefaultValue}`";
                var desc = string.IsNullOrWhiteSpace(col.Description) ? "—" : col.Description;

                sb.AppendLine($"| **{col.Name}** | `{col.DataType}` | {pk} | {nullable} | {defVal} | {desc} |");
            }
            sb.AppendLine();

            // İlişkiler Tablosu (Varsa)
            if (table.ForeignKeys.Any())
            {
                sb.AppendLine("#### İlişkiler (Foreign Keys)");
                sb.AppendLine("| Kısıt Adı | Kolon | Hedef Tablo | Hedef Kolon | On Delete |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

                foreach (var fk in table.ForeignKeys)
                {
                    var constraintName = fk.ConstraintName ?? "—";
                    var srcCols = string.Join(", ", fk.SourceColumns);
                    var tgtCols = string.Join(", ", fk.TargetColumns);
                    var onDelete = fk.OnDelete ?? "NO ACTION";

                    sb.AppendLine($"| `{constraintName}` | `{srcCols}` | [{fk.TargetTable}](#{fk.TargetTable.ToLowerInvariant()}) | `{tgtCols}` | `{onDelete}` |");
                }
                sb.AppendLine();
            }

            // İndeksler Tablosu (Varsa)
            if (table.Indexes.Any())
            {
                sb.AppendLine("#### İndeksler");
                sb.AppendLine("| İndeks Adı | Kolonlar | Tekil (Unique)? | Filtre |");
                sb.AppendLine("| :--- | :--- | :---: | :--- |");

                foreach (var idx in table.Indexes)
                {
                    var uniq = idx.IsUnique ? "✅" : "—";
                    var filter = string.IsNullOrWhiteSpace(idx.FilterPredicate) ? "—" : $"`{idx.FilterPredicate}`";
                    sb.AppendLine($"| `{idx.Name}` | `{string.Join(", ", idx.Columns)}` | {uniq} | {filter} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
