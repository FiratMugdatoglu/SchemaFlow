namespace SchemaFlow.Core.Models;

/// <summary>
/// Bir tabloya ait veritabanı indeksini temsil eder.
/// </summary>
public class Index
{
    /// <summary>
    /// İndeks adı (örn: idx_users_email)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// İndeksin kapsadığı sütun isimleri
    /// </summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>
    /// Benzersiz (Unique) indeks mi?
    /// </summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// Birincil anahtar (Primary Key) indeksi mi?
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Kısmi/Filtrelenmiş indeks ifadesi (örn: WHERE deleted_at IS NULL)
    /// </summary>
    public string? FilterPredicate { get; set; }

    public override string ToString()
    {
        var uniq = IsUnique ? "UNIQUE " : "";
        return $"{uniq}INDEX {Name} ({string.Join(", ", Columns)})";
    }
}
