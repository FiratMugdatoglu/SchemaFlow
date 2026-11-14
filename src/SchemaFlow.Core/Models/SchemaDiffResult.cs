namespace SchemaFlow.Core.Models;

/// <summary>
/// İki şema arasındaki tekil bir değişikliği ve bu değişikliğin canlı sistemi bozma (Breaking) riskini temsil eder.
/// </summary>
public class SchemaChange
{
    public ChangeType Type { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsBreaking { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Recommendation { get; set; }
}

/// <summary>
/// Şema değişiklik türleri
/// </summary>
public enum ChangeType
{
    TableAdded,
    TableDropped,
    ColumnAdded,
    ColumnDropped,
    ColumnTypeChanged,
    ColumnNullabilityChanged,
    PrimaryKeyChanged,
    ForeignKeyAdded,
    ForeignKeyDropped
}

/// <summary>
/// İki şema karşılaştırıldığında üretilen toplam analiz ve rapor nesnesidir.
/// </summary>
public class SchemaDiffResult
{
    public List<SchemaChange> Changes { get; set; } = new();

    /// <summary>
    /// Sıfır kesinti (Zero-Downtime) Expand & Contract geçiş stratejisi önerileri
    /// </summary>
    public List<ExpandContractPlan> ExpandContractPlans { get; set; } = new();

    public bool HasBreakingChanges => Changes.Any(c => c.IsBreaking);

    public int TotalChanges => Changes.Count;

    public int BreakingCount => Changes.Count(c => c.IsBreaking);

    public int SafeCount => Changes.Count(c => !c.IsBreaking);
}
