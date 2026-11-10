namespace SchemaFlow.Core.Models;

/// <summary>
/// Bir veritabanı tablosunu; sütunları, kısıtlayıcıları, ilişkileri ve indeksleri ile temsil eder.
/// </summary>
public class Table
{
    /// <summary>
    /// Şema adı (örn: PostgreSQL için "public", MSSQL için "dbo")
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// Tablo adı (örn: "users", "orders")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tablo açıklaması (örn: COMMENT ON TABLE)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tablonun sütunları
    /// </summary>
    public List<Column> Columns { get; set; } = new();

    /// <summary>
    /// Bu tablodan çıkan yabancı anahtar (Foreign Key) ilişkileri
    /// </summary>
    public List<ForeignKey> ForeignKeys { get; set; } = new();

    /// <summary>
    /// Tabloya tanımlanmış indeksler
    /// </summary>
    public List<Index> Indexes { get; set; } = new();

    /// <summary>
    /// Birincil anahtar oluşturan sütunların listesi
    /// </summary>
    public IEnumerable<Column> PrimaryKeyColumns => Columns.Where(c => c.IsPrimaryKey);

    /// <summary>
    /// Şema ve tablo adının tam gösterimi (örn: "public.users" veya "users")
    /// </summary>
    public string FullName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";

    public override string ToString() => FullName;
}
