namespace SchemaFlow.Core.Models;

/// <summary>
/// Parsed edilen tüm veritabanı şemasını temsil eden çatı nesnedir.
/// Veritabanı bağımsız (agnostic) bir AST (Abstract Syntax Tree) gibi davranır.
/// </summary>
public class DatabaseSchema
{
    /// <summary>
    /// Şemanın ait olduğu veritabanı diyalekti
    /// </summary>
    public DatabaseDialect Dialect { get; set; }

    /// <summary>
    /// Şema / Veritabanı adı (opsiyonel)
    /// </summary>
    public string? DatabaseName { get; set; }

    /// <summary>
    /// Şemadaki tüm tabloların listesi
    /// </summary>
    public List<Table> Tables { get; set; } = new();

    /// <summary>
    /// İsme göre tablo arama yardımcısı (büyük/küçük harf duyarsız)
    /// </summary>
    public Table? FindTable(string tableName)
    {
        return Tables.FirstOrDefault(t => 
            string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.FullName, tableName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Şemadaki tüm yabancı anahtarları düz bir liste olarak döner
    /// </summary>
    public IEnumerable<ForeignKey> GetAllForeignKeys()
    {
        return Tables.SelectMany(t => t.ForeignKeys);
    }
}
