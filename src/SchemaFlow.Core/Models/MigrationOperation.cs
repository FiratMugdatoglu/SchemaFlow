namespace SchemaFlow.Core.Models;

/// <summary>
/// EF Core Migration operasyon türleri
/// </summary>
public enum MigrationOperationKind
{
    CreateTable,
    DropTable,
    AddColumn,
    DropColumn,
    RenameColumn,
    AlterColumn,
    AddForeignKey,
    DropForeignKey,
    CreateIndex,
    DropIndex,
    SqlRaw,
    Other
}

/// <summary>
/// C# Roslyn AST tarafından EF Core migration dosyasından çıkarılan operasyon modeli.
/// </summary>
public class MigrationOperation
{
    public MigrationOperationKind Kind { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string? NewColumnName { get; set; }
    public string? DataType { get; set; }
    public bool? IsNullable { get; set; }
    public string? DefaultValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsDestructive { get; set; }
    public int LineNumber { get; set; }
}

/// <summary>
/// Sıfır kesinti (Zero-Downtime) Expand & Contract geçiş planı.
/// Kolon veya tablo silme/yeniden adlandırma işlemlerinde veri kaybını önler.
/// </summary>
public class ExpandContractPlan
{
    public string TableName { get; set; } = string.Empty;
    public string DroppedColumn { get; set; } = string.Empty;
    public string AddedColumn { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> PhaseSteps { get; set; } = new();
    public string? SuggestedSqlBackfill { get; set; }
    public string? SuggestedEfCoreCode { get; set; }
}
