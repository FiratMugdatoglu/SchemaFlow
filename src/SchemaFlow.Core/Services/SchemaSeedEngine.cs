using SchemaFlow.Core.Models;
using System.Globalization;
using System.Text;

namespace SchemaFlow.Core.Services;

/// <summary>
/// Şema ilişkilerini (Foreign Key bağımlılıklarını) analiz ederek
/// test ortamları için ilişkisel bütünlüğü bozulmamış sahte (dummy / seed) SQL verisi üretir.
/// </summary>
public class SchemaSeedEngine
{
    /// <summary>
    /// Şemadaki tablolar için belirtilen sayıda satır içeren INSERT SQL komutları üretir.
    /// </summary>
    /// <param name="schema">Ayrıştırılmış veritabanı şeması</param>
    /// <param name="count">Her tablo için üretilecek kayıt sayısı (Varsayılan: 10)</param>
    /// <returns>Çalıştırılabilir SQL script metni</returns>
    public string GenerateSeedSql(DatabaseSchema schema, int count = 10)
    {
        if (schema.Tables.Count == 0)
        {
            return "-- Şemada tohumlanacak tablo bulunamadı.\n";
        }

        var sb = new StringBuilder();
        sb.AppendLine("-- =============================================================================");
        sb.AppendLine($"-- 🚀 SchemaFlow.ai Akıllı Sahte Test Verisi (Seed Data)");
        sb.AppendLine($"-- Motor (Dialect): {schema.Dialect}");
        sb.AppendLine($"-- Toplam Tablo: {schema.Tables.Count} | Tablo Başına Kayıt: {count}");
        sb.AppendLine($"-- Oluşturulma Tarihi: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("-- =============================================================================");
        sb.AppendLine();

        // 1. Tabloları Foreign Key hiyerarşisine göre sırala (Önce ebeveyn tablolar, sonra bağımlı tablolar)
        var sortedTables = TopologicalSortTables(schema.Tables);

        // Her tablonun ürettiği Primary Key ID'lerini hafızada tut (FK ilişkilerini bağlamak için)
        var generatedKeysByTable = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in sortedTables)
        {
            sb.AppendLine($"-- -----------------------------------------------------------------------------");
            sb.AppendLine($"-- Tablo: {table.FullName} ({count} Kayıt)");
            sb.AppendLine($"-- -----------------------------------------------------------------------------");

            var tableKeys = new List<object>();
            generatedKeysByTable[table.Name] = tableKeys;

            // Identity olan sütunlar bazı SQL motorlarında INSERT edilmez veya açıkça belirtilir.
            // Ancak sahte veri üretiminde basitlik ve evrensellik için standart sütunları alıyoruz.
            var insertColumns = table.Columns.ToList();
            if (insertColumns.Count == 0) continue;

            for (int i = 1; i <= count; i++)
            {
                var colNames = new List<string>();
                var colValues = new List<string>();

                foreach (var col in insertColumns)
                {
                    colNames.Add(col.Name);

                    // 1. Eğer bu kolon bir Foreign Key ise, hedef tablonun üretilen ID'sine bağla!
                    var matchingFk = table.ForeignKeys.FirstOrDefault(f => 
                        f.SourceColumns.Any(sc => string.Equals(sc, col.Name, StringComparison.OrdinalIgnoreCase)));

                    if (matchingFk != null && generatedKeysByTable.TryGetValue(matchingFk.TargetTable, out var parentKeys) && parentKeys.Count > 0)
                    {
                        var parentKeyVal = parentKeys[(i - 1) % parentKeys.Count];
                        colValues.Add(parentKeyVal.ToString()!);
                        continue;
                    }

                    // 2. Eğer bu kolon Primary Key ise ID üret ve listeye kaydet
                    if (col.IsPrimaryKey)
                    {
                        var pkVal = GeneratePrimaryKeyValue(col, i);
                        tableKeys.Add(pkVal);
                        colValues.Add(FormatSqlValue(pkVal));
                        continue;
                    }

                    // 3. Normal sütun için semantik veri üret
                    var val = GenerateSmartValue(col, i);
                    colValues.Add(FormatSqlValue(val));
                }

                sb.AppendLine($"INSERT INTO {table.FullName} ({string.Join(", ", colNames)}) VALUES ({string.Join(", ", colValues)});");
            }

            sb.AppendLine();
        }

        sb.AppendLine("-- =============================================================================");
        sb.AppendLine("-- ✅ Seed verisi başarıyla oluşturuldu.");
        sb.AppendLine("-- =============================================================================");

        return sb.ToString();
    }

    private static object GeneratePrimaryKeyValue(Column col, int index)
    {
        var type = col.DataType.ToLowerInvariant();
        if (type.Contains("uuid"))
        {
            return $"00000000-0000-0000-0000-{index:D12}";
        }

        return index;
    }

    private static object GenerateSmartValue(Column col, int index)
    {
        var name = col.Name.ToLowerInvariant();
        var type = col.DataType.ToLowerInvariant();

        // Email
        if (name.Contains("email") || name.Contains("mail"))
        {
            return $"user{index}@example.com";
        }

        // Kullanıcı Adı
        if (name.Contains("username") || name.Contains("user_name"))
        {
            return $"user_{index}";
        }

        // İsim / Soyisim
        if (name.Contains("name") || name.Contains("fullname") || name.Contains("firstname"))
        {
            return $"User Name {index}";
        }

        // Başlık / Konu
        if (name.Contains("title") || name.Contains("subject") || name.Contains("heading"))
        {
            return $"Sample Title #{index}";
        }

        // URL / Avatar / Görsel
        if (name.Contains("avatar") || name.Contains("image") || name.Contains("url") || name.Contains("photo"))
        {
            return $"https://picsum.photos/seed/{index}/300/200";
        }

        // Telefon
        if (name.Contains("phone") || name.Contains("gsm") || name.Contains("tel") || name.Contains("mobile"))
        {
            return $"+90555000{index:D4}";
        }

        // Adres / Şehir / Ülke
        if (name.Contains("city")) return "Istanbul";
        if (name.Contains("country")) return "TR";
        if (name.Contains("address")) return $"Ataturk Caddesi No: {index}, Daire: {index}";

        // Tutar / Fiyat / Bakiye
        if (name.Contains("price") || name.Contains("amount") || name.Contains("total") || name.Contains("cost") || name.Contains("balance"))
        {
            return (index * 24.50m);
        }

        // Sayı / Miktar
        if (name.Contains("quantity") || name.Contains("count") || name.Contains("qty"))
        {
            return (index % 5) + 1;
        }

        // Durum / Rol / Tip
        if (name.Contains("status")) return (index % 2 == 0 ? "active" : "pending");
        if (name.Contains("role")) return (index == 1 ? "admin" : "member");

        // Açıklama / İçerik
        if (name.Contains("content") || name.Contains("description") || name.Contains("desc") || name.Contains("bio") || name.Contains("text") || name.Contains("comment"))
        {
            return $"This is a sample test description for record #{index}. Generated automatically by SchemaFlow.";
        }

        // Slug
        if (name.Contains("slug"))
        {
            return $"sample-record-slug-{index}";
        }

        // Tarih & Zaman
        if (type.Contains("timestamp") || type.Contains("datetime") || type.Contains("date") || name.Contains("date") || name.Contains("at"))
        {
            var day = ((index - 1) % 28) + 1;
            return $"2026-11-{day:D2} 12:00:00";
        }

        // Boolean
        if (type.Contains("bool") || type.Equals("tinyint(1)", StringComparison.OrdinalIgnoreCase) || type.Equals("bit", StringComparison.OrdinalIgnoreCase))
        {
            return (index % 2 == 1);
        }

        // Sayısal Tipler
        if (type.Contains("int") || type.Contains("decimal") || type.Contains("numeric") || type.Contains("float") || type.Contains("double"))
        {
            return index;
        }

        // UUID
        if (type.Contains("uuid"))
        {
            return $"aaaaaaaa-bbbb-cccc-dddd-{index:D12}";
        }

        // Standart Fallback
        return $"Sample Value {index}";
    }

    private static string FormatSqlValue(object val)
    {
        return val switch
        {
            bool b => b ? "TRUE" : "FALSE",
            int or long or short or byte => val.ToString()!,
            decimal d => d.ToString("F2", CultureInfo.InvariantCulture),
            double d => d.ToString("F2", CultureInfo.InvariantCulture),
            float f => f.ToString("F2", CultureInfo.InvariantCulture),
            _ => $"'{val.ToString()!.Replace("'", "''")}'"
        };
    }

    /// <summary>
    /// Tabloları Foreign Key bağımlılıklarına göre topolojik sıralar.
    /// Ebeveyn tablolar bağımlı (çocuk) tablolardan önce listelenir.
    /// </summary>
    private static List<Table> TopologicalSortTables(List<Table> tables)
    {
        var tableMap = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var inDegree = tables.ToDictionary(t => t.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var adjList = tables.ToDictionary(t => t.Name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                if (tableMap.ContainsKey(fk.TargetTable) && !string.Equals(table.Name, fk.TargetTable, StringComparison.OrdinalIgnoreCase))
                {
                    // TargetTable -> Table bağımlılığı var (Target önce gelmeli)
                    adjList[fk.TargetTable].Add(table.Name);
                    inDegree[table.Name]++;
                }
            }
        }

        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var result = new List<Table>();

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (tableMap.TryGetValue(u, out var tbl))
            {
                result.Add(tbl);
            }

            foreach (var v in adjList[u])
            {
                inDegree[v]--;
                if (inDegree[v] == 0)
                {
                    queue.Enqueue(v);
                }
            }
        }

        // Döngüsel bağımlılık varsa geriye kalanları sona ekle
        foreach (var table in tables)
        {
            if (!result.Contains(table))
            {
                result.Add(table);
            }
        }

        return result;
    }
}
