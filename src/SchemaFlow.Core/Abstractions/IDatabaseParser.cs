using SchemaFlow.Core.Models;

namespace SchemaFlow.Core.Abstractions;

/// <summary>
/// Strategy Pattern'in temel arayüzüdür.
/// Farklı veritabanı diyalektleri (PostgreSQL, MySQL, MSSQL vb.) bu arayüzü implemente ederek
/// kendi SQL/DDL sözdizimlerini ortak DatabaseSchema modeline dönüştürür.
/// </summary>
public interface IDatabaseParser
{
    /// <summary>
    /// Bu parser implementasyonunun desteklediği veritabanı diyalekti.
    /// </summary>
    DatabaseDialect SupportedDialect { get; }

    /// <summary>
    /// Verilen ham SQL betiğini (DDL / Migration script) parse ederek ortak şema modelini üretir.
    /// </summary>
    /// <param name="sql">SQL DDL içeriği</param>
    /// <param name="cancellationToken">İptal belirteci</param>
    /// <returns>Ayrıştırılmış standart DatabaseSchema nesnesi</returns>
    Task<DatabaseSchema> ParseSqlAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen dosya yollarındaki migration/SQL dosyalarını sırasıyla okuyup birleştirerek şema modelini üretir.
    /// </summary>
    /// <param name="filePaths">Ayrıştırılacak SQL dosya yolları</param>
    /// <param name="cancellationToken">İptal belirteci</param>
    /// <returns>Ayrıştırılmış standart DatabaseSchema nesnesi</returns>
    Task<DatabaseSchema> ParseFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
