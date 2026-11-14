namespace SchemaFlow.Core.Models;

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
