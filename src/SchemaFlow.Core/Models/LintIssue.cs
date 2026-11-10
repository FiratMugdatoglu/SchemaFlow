namespace SchemaFlow.Core.Models;

/// <summary>
/// Şema analizi sırasında tespit edilen tekil bir linter kural ihlalini temsil eder.
/// </summary>
public class LintIssue
{
    /// <summary>
    /// Kural kodu (örn: SF001, SF002)
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Önem derecesi (Error, Warning, Info)
    /// </summary>
    public LintSeverity Severity { get; set; }

    /// <summary>
    /// Sorunun ait olduğu tablo adı
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Varsa ilgili sütun adı
    /// </summary>
    public string? ColumnName { get; set; }

    /// <summary>
    /// Hata/uyarı açıklaması
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Çözüm önerisi veya düzeltme tavsiyesi
    /// </summary>
    public string Recommendation { get; set; } = string.Empty;

    public override string ToString()
    {
        var target = string.IsNullOrEmpty(ColumnName) ? TableName : $"{TableName}.{ColumnName}";
        return $"[{Severity.ToString().ToUpperInvariant()}] {RuleId} ({target}): {Message}";
    }
}
