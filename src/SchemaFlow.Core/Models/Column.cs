namespace SchemaFlow.Core.Models;

/// <summary>
/// Bir veritabanı tablosundaki tekil bir sütunu temsil eden domain modelidir.
/// </summary>
public class Column
{
    /// <summary>
    /// Sütunun adı (örn: id, email, created_at)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Sütunun veri tipi (örn: UUID, VARCHAR(255), BIGINT, TIMESTAMP)
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Sütunun NULL değer alıp alamayacağı (nullable)
    /// </summary>
    public bool IsNullable { get; set; } = true;

    /// <summary>
    /// Sütunun birincil anahtar (Primary Key) olup olmadığı
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Otomatik artan / kimlik sütunu mu? (SERIAL, IDENTITY, AUTO_INCREMENT)
    /// </summary>
    public bool IsIdentity { get; set; }

    /// <summary>
    /// Varsayılan değer ifadesi (örn: gen_random_uuid(), CURRENT_TIMESTAMP, 'active')
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Sütun açıklaması veya dokümantasyon yorumu
    /// </summary>
    public string? Description { get; set; }

    public override string ToString()
    {
        var pk = IsPrimaryKey ? " [PK]" : "";
        var nullable = IsNullable ? " NULL" : " NOT NULL";
        return $"{Name}: {DataType}{nullable}{pk}";
    }
}
