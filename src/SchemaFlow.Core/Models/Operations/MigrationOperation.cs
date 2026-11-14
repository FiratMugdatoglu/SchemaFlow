namespace SchemaFlow.Core.Models.Operations;

/// <summary>
/// Statik analizin bulguyu nasıl elde ettiğini belirten tespit türü.
/// Kesin tespit ile olasılıksal çıkarımı birbirinden ayırır.
/// </summary>
public enum DetectionType
{
    /// <summary>
    /// Kodda doğrudan ve net olarak görülen operasyon (örn: migrationBuilder.DropColumn).
    /// </summary>
    Confirmed,

    /// <summary>
    /// Birden fazla operasyonun veya şema farkının birleşmesinden çıkarılan olasılık (örn: Expand & Contract rename tahmini).
    /// </summary>
    Inferred,

    /// <summary>
    /// Statik analiz sırasında çözümlenemeyen dinamik değişken veya yardımcı metot ifadesi.
    /// </summary>
    Unknown
}

/// <summary>
/// Statik analiz güven derecesi.
/// </summary>
public enum AnalysisConfidence
{
    High,
    Medium,
    Low
}

/// <summary>
/// Operasyonun migration içindeki etki alanı.
/// </summary>
public enum MigrationMethodScope
{
    Up,
    Down,
    Unknown
}

/// <summary>
/// Tüm normalize edilmiş EF Core migration operasyonlarının temel sınıfıdır.
/// </summary>
public abstract record MigrationOperation
{
    public abstract string OperationName { get; }
    public string TableName { get; init; } = string.Empty;
    public string? SourceFile { get; init; }
    public int LineNumber { get; init; }
    public MigrationMethodScope Scope { get; init; } = MigrationMethodScope.Up;
    public DetectionType Detection { get; init; } = DetectionType.Confirmed;
    public AnalysisConfidence Confidence { get; init; } = AnalysisConfidence.High;
    public string? RawSyntax { get; init; }
    public string? Notes { get; init; }

    public bool IsDestructive => this is DropTableOperation or DropColumnOperation or DropPrimaryKeyOperation;
    public string Kind => OperationName;
    public string Description => ToDisplaySummary();

    public abstract string ToDisplaySummary();
}
