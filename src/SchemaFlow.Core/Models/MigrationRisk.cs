using SchemaFlow.Core.Models.Operations;

namespace SchemaFlow.Core.Models;

/// <summary>
/// Şema veya migration değişikliğinin canlı sisteme etkisinin ciddiyet derecesi.
/// </summary>
public enum RiskSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Info
}

/// <summary>
/// Statik analiz sonucu tespit edilen tekil bir migration güvenlik bulgusu.
/// </summary>
public record MigrationRiskIssue
{
    public string RuleId { get; init; } = string.Empty;
    public RiskSeverity Severity { get; init; } = RiskSeverity.Medium;
    public DetectionType Detection { get; init; } = DetectionType.Confirmed;
    public AnalysisConfidence Confidence { get; init; } = AnalysisConfidence.High;
    public string TableName { get; init; } = string.Empty;
    public string? ColumnName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ImpactAnalysis { get; init; }
    public string? Recommendation { get; init; }
    public int LineNumber { get; init; }
    public string? SourceFile { get; init; }
}

/// <summary>
/// Bir migration dosyasının veya PR kapsamındaki migration'ların kapsamlı güvenlik raporu.
/// </summary>
public class MigrationSafetyReport
{
    public string TargetMigrationName { get; set; } = string.Empty;
    public List<MigrationOperation> Operations { get; set; } = new();
    public List<MigrationRiskIssue> Issues { get; set; } = new();
    public List<ExpandContractPlan> ExpandContractPlans { get; set; } = new();

    public bool HasCriticalOrHighRisk => Issues.Any(i => i.Severity is RiskSeverity.Critical or RiskSeverity.High);
    public int CriticalCount => Issues.Count(i => i.Severity == RiskSeverity.Critical);
    public int HighCount => Issues.Count(i => i.Severity == RiskSeverity.High);
    public int MediumCount => Issues.Count(i => i.Severity == RiskSeverity.Medium);
    public int LowCount => Issues.Count(i => i.Severity is RiskSeverity.Low or RiskSeverity.Info);
}
