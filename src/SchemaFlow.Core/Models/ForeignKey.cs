namespace SchemaFlow.Core.Models;

/// <summary>
/// İki tablo arasındaki yabancı anahtar (Foreign Key) ilişkisini temsil eder.
/// Mermaid ER diyagramlarının ve ilişkisel haritaların temel taşıdır.
/// </summary>
public class ForeignKey
{
    /// <summary>
    /// Kısıtlayıcı (Constraint) adı (örn: fk_orders_user_id)
    /// </summary>
    public string? ConstraintName { get; set; }

    /// <summary>
    /// İlişkiyi tanımlayan kaynak tablonun adı (örn: orders)
    /// </summary>
    public string SourceTable { get; set; } = string.Empty;

    /// <summary>
    /// Kaynak tablodaki sütun(lar) (örn: ["user_id"])
    /// </summary>
    public List<string> SourceColumns { get; set; } = new();

    /// <summary>
    /// Referans verilen hedef tablo adı (örn: users)
    /// </summary>
    public string TargetTable { get; set; } = string.Empty;

    /// <summary>
    /// Hedef tablodaki referans sütun(lar) (örn: ["id"])
    /// </summary>
    public List<string> TargetColumns { get; set; } = new();

    /// <summary>
    /// Kayıt silindiğinde uygulanacak aksiyon (CASCADE, SET NULL, RESTRICT, NO ACTION)
    /// </summary>
    public string? OnDelete { get; set; }

    /// <summary>
    /// Kayıt güncellendiğinde uygulanacak aksiyon (CASCADE, RESTRICT, NO ACTION)
    /// </summary>
    public string? OnUpdate { get; set; }

    public override string ToString()
    {
        var srcCols = string.Join(", ", SourceColumns);
        var tgtCols = string.Join(", ", TargetColumns);
        return $"{SourceTable}({srcCols}) -> {TargetTable}({tgtCols})";
    }
}
