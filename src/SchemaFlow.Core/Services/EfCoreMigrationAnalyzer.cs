using SchemaFlow.Core.Models;
using SchemaFlow.Core.Models.Operations;
using SchemaFlow.Core.Parsers;

namespace SchemaFlow.Core.Services;

/// <summary>
/// EF Core Migration analiz sonuç modeli.
/// </summary>
public class MigrationAnalysisResult
{
    public string MigrationName { get; set; } = string.Empty;
    public List<MigrationOperation> Operations { get; set; } = new();
    public List<LintIssue> Issues { get; set; } = new();
    public List<ExpandContractPlan> ExpandContractPlans { get; set; } = new();
    public MigrationSafetyReport? SafetyReport { get; set; }

    public bool HasDestructiveChanges => Issues.Any(i => i.Severity == LintSeverity.Error);
    public int DestructiveCount => Issues.Count(i => i.Severity == LintSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == LintSeverity.Warning);
}

/// <summary>
/// Roslyn tabanlı RoslynEfCoreParser ve MigrationRiskEngine motorlarını orkestre eden,
/// geriye dönük uyumluluğu koruyarak CLI ve servislere migration güvenlik analizi sunan sınıftır.
/// </summary>
public class EfCoreMigrationAnalyzer
{
    private readonly RoslynEfCoreParser _parser = new();
    private readonly MigrationRiskEngine _riskEngine = new();

    public MigrationAnalysisResult AnalyzeMigrationCode(string csharpCode, string? sourceFilePath = null)
    {
        var parseResult = _parser.ParseMigration(csharpCode, sourceFilePath);
        var safetyReport = _riskEngine.Analyze(parseResult.AllOperations, parseResult.MigrationClassName);

        var result = new MigrationAnalysisResult
        {
            MigrationName = parseResult.MigrationClassName,
            ExpandContractPlans = safetyReport.ExpandContractPlans,
            SafetyReport = safetyReport,
            Operations = parseResult.UpOperations.Concat(parseResult.UnresolvedOperations).ToList()
        };

        // Risk motorundan gelen bulguları LintIssue formatına eşle
        foreach (var issue in safetyReport.Issues)
        {
            result.Issues.Add(new LintIssue
            {
                RuleId = issue.RuleId,
                Severity = issue.Severity switch
                {
                    RiskSeverity.Critical => LintSeverity.Error,
                    RiskSeverity.High => LintSeverity.Error,
                    RiskSeverity.Medium => LintSeverity.Warning,
                    _ => LintSeverity.Info
                },
                TableName = issue.TableName,
                ColumnName = issue.ColumnName,
                Message = $"{issue.Title}: {issue.Description}",
                Recommendation = issue.Recommendation ?? issue.ImpactAnalysis ?? string.Empty
            });
        }

        return result;
    }
}
