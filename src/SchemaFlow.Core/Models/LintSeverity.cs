namespace SchemaFlow.Core.Models;

/// <summary>
/// Şema denetiminde tespit edilen bulgunun önem derecesi.
/// </summary>
public enum LintSeverity
{
    /// <summary>
    /// Kritik mimari veya veri bütünlüğü hatası (Örn: Primary Key yok).
    /// </summary>
    Error,

    /// <summary>
    /// Performans veya sözdizimi riski (Örn: Missing FK Index, Reserved SQL Keyword).
    /// </summary>
    Warning,

    /// <summary>
    /// Kod kalitesi veya optimizasyon tavsiyesi.
    /// </summary>
    Info
}
