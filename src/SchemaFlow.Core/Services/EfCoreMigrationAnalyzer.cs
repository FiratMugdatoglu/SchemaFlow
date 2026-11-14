using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaFlow.Core.Models;

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

    public bool HasDestructiveChanges => Issues.Any(i => i.Severity == LintSeverity.Error);
    public int DestructiveCount => Issues.Count(i => i.Severity == LintSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == LintSeverity.Warning);
}

/// <summary>
/// Roslyn AST (Abstract Syntax Tree) motoru ile EF Core C# migration dosyalarını derinlemesine analiz eden,
/// Expand & Contract anti-pattern'larını ve veri kaybı risklerini tespit eden analiz sınıfıdır.
/// </summary>
public class EfCoreMigrationAnalyzer
{
    public MigrationAnalysisResult AnalyzeMigrationCode(string csharpCode)
    {
        var result = new MigrationAnalysisResult();

        if (string.IsNullOrWhiteSpace(csharpCode))
            return result;

        var tree = CSharpSyntaxTree.ParseText(csharpCode);
        var root = tree.GetRoot();

        // 1. Migration sınıf adını belirle
        var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        result.MigrationName = classDeclaration?.Identifier.Text ?? "UnknownMigration";

        // 2. Up() metodunu bul (Down metodu rollback içindir, asıl canlı riski Up()'tadır)
        var upMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "Up");

        SyntaxNode targetNode = upMethod != null ? (SyntaxNode)upMethod : root;

        // 3. Up() içerisindeki tüm metot çağrılarını tara ve operasyonları modelle
        var invocations = targetNode.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            var op = ExtractOperation(inv);
            if (op != null)
            {
                result.Operations.Add(op);
            }
        }

        // 4. Kural Denetimlerini ve Expand & Contract Analizini Çalıştır
        EvaluateRules(result);

        return result;
    }

    private static MigrationOperation? ExtractOperation(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax ma)
            return null;

        var methodName = ma.Name.Identifier.Text;
        var lineSpan = inv.GetLocation().GetLineSpan();
        var lineNumber = lineSpan.StartLinePosition.Line + 1;

        switch (methodName)
        {
            case "DropColumn":
            {
                var name = GetStringArg(inv.ArgumentList, "name", 0) ?? "UnknownColumn";
                var table = GetStringArg(inv.ArgumentList, "table", 1) ?? "UnknownTable";
                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.DropColumn,
                    TableName = table,
                    ColumnName = name,
                    IsDestructive = true,
                    LineNumber = lineNumber,
                    Description = $"'{table}.{name}' sütununu kalıcı olarak siliyor."
                };
            }

            case "AddColumn":
            {
                var name = GetStringArg(inv.ArgumentList, "name", 0) ?? "UnknownColumn";
                var table = GetStringArg(inv.ArgumentList, "table", 1) ?? "UnknownTable";
                var type = GetStringArg(inv.ArgumentList, "type", 2);
                var nullable = GetBoolArg(inv.ArgumentList, "nullable", 3) ?? true;
                var defVal = GetStringArg(inv.ArgumentList, "defaultValue") ?? GetStringArg(inv.ArgumentList, "defaultValueSql");

                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.AddColumn,
                    TableName = table,
                    ColumnName = name,
                    DataType = type,
                    IsNullable = nullable,
                    DefaultValue = defVal,
                    IsDestructive = false,
                    LineNumber = lineNumber,
                    Description = $"'{table}' tablosuna '{name}' ({type ?? "object"}, Nullable: {nullable}) sütunu ekliyor."
                };
            }

            case "RenameColumn":
            {
                var name = GetStringArg(inv.ArgumentList, "name", 0) ?? "UnknownColumn";
                var table = GetStringArg(inv.ArgumentList, "table", 1) ?? "UnknownTable";
                var newName = GetStringArg(inv.ArgumentList, "newName", 2) ?? "UnknownNewName";

                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.RenameColumn,
                    TableName = table,
                    ColumnName = name,
                    NewColumnName = newName,
                    IsDestructive = false,
                    LineNumber = lineNumber,
                    Description = $"'{table}.{name}' sütununun adını '{newName}' olarak değiştiriyor."
                };
            }

            case "AlterColumn":
            {
                var name = GetStringArg(inv.ArgumentList, "name", 0) ?? "UnknownColumn";
                var table = GetStringArg(inv.ArgumentList, "table", 1) ?? "UnknownTable";
                var type = GetStringArg(inv.ArgumentList, "type", 2);
                var nullable = GetBoolArg(inv.ArgumentList, "nullable");

                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.AlterColumn,
                    TableName = table,
                    ColumnName = name,
                    DataType = type,
                    IsNullable = nullable,
                    IsDestructive = false,
                    LineNumber = lineNumber,
                    Description = $"'{table}.{name}' sütun tipini/kısıtlamasını güncelliyor."
                };
            }

            case "DropTable":
            {
                var name = GetStringArg(inv.ArgumentList, "name", 0) ?? "UnknownTable";
                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.DropTable,
                    TableName = name,
                    IsDestructive = true,
                    LineNumber = lineNumber,
                    Description = $"'{name}' tablosunu ve tüm verilerini tamamen siliyor!"
                };
            }

            case "CreateTable":
            {
                var name = GetStringArg(inv.ArgumentList, "name", 0) ?? "UnknownTable";
                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.CreateTable,
                    TableName = name,
                    IsDestructive = false,
                    LineNumber = lineNumber,
                    Description = $"'{name}' adında yeni bir tablo oluşturuyor."
                };
            }

            case "Sql":
            {
                var sqlText = GetStringArg(inv.ArgumentList, "sql", 0) ?? "Raw SQL Script";
                return new MigrationOperation
                {
                    Kind = MigrationOperationKind.SqlRaw,
                    TableName = string.Empty,
                    Description = $"Doğrudan ham SQL kodu çalıştırıyor: {sqlText}",
                    IsDestructive = false,
                    LineNumber = lineNumber
                };
            }

            default:
                return null;
        }
    }

    private static void EvaluateRules(MigrationAnalysisResult result)
    {
        // Tablo bazlı gruplandır
        var tableGroups = result.Operations
            .Where(o => !string.IsNullOrEmpty(o.TableName))
            .GroupBy(o => o.TableName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in tableGroups)
        {
            var tableName = group.Key;
            var droppedCols = group.Where(o => o.Kind == MigrationOperationKind.DropColumn).ToList();
            var addedCols = group.Where(o => o.Kind == MigrationOperationKind.AddColumn).ToList();

            // 1. KURAL: EFM002 - Expand & Contract Anti-Pattern (Aynı tabloda DropColumn ve AddColumn birlikte yapılmış)
            // C# modelinde kolon adı değiştirildiğinde EF Core DropColumn + AddColumn üretir ve canlıda VERİ KAYBINA yol açar!
            if (droppedCols.Count > 0 && addedCols.Count > 0)
            {
                foreach (var dropped in droppedCols)
                {
                    // Eşleşen veya olası yedek sütun
                    var correspondingAdded = addedCols.FirstOrDefault();
                    var addedColName = correspondingAdded?.ColumnName ?? "YeniSutun";

                    var plan = new ExpandContractPlan
                    {
                        TableName = tableName,
                        DroppedColumn = dropped.ColumnName ?? "Unknown",
                        AddedColumn = addedColName,
                        Reason = $"'{tableName}' tablosunda '{dropped.ColumnName}' sütunu silinirken aynı anda '{addedColName}' sütunu eklenmiş. C# sınıfında bir property yeniden adlandırıldığında EF Core varsayılan olarak DropColumn + AddColumn üretir! Bu durum canlı ortamda mevcut tüm verilerin GERİ DÖNÜŞSÜZ SİLİNMESİNE yol açar.",
                        PhaseSteps = new List<string>
                        {
                            $"Aşama 1 (Expand): '{addedColName}' sütununu nullable veya varsayılan değerle sisteme ekleyin.",
                            $"Aşama 2 (Backfill): Mevcut verileri eski sütundan yeni sütuna kopyalayın (UPDATE [{tableName}] SET [{addedColName}] = [{dropped.ColumnName}]).",
                            $"Aşama 3 (Migrate): Uygulama kodunu yeni sütunu kullanacak şekilde canlıya deploy edin.",
                            $"Aşama 4 (Contract): Bir sonraki sürümde eski '{dropped.ColumnName}' sütununu güvenle Drop edin.",
                            $"Hızlı Alternatif: Eğer kesintili bakım penceresi varsa ve veriyi taşımak istiyorsanız 'migrationBuilder.RenameColumn(name: \"{dropped.ColumnName}\", newName: \"{addedColName}\", table: \"{tableName}\");' kullanın."
                        },
                        SuggestedSqlBackfill = $"UPDATE [{tableName}] SET [{addedColName}] = [{dropped.ColumnName}] WHERE [{addedColName}] IS NULL;",
                        SuggestedEfCoreCode = $"migrationBuilder.RenameColumn(name: \"{dropped.ColumnName}\", newName: \"{addedColName}\", table: \"{tableName}\");"
                    };

                    result.ExpandContractPlans.Add(plan);

                    result.Issues.Add(new LintIssue
                    {
                        RuleId = "EFM002",
                        Severity = LintSeverity.Error,
                        TableName = tableName,
                        ColumnName = dropped.ColumnName,
                        Message = $"🔴 KRİTİK VERİ KAYBI RİSKİ (Expand & Contract İhlali): '{tableName}.{dropped.ColumnName}' silinirken '{addedColName}' ekleniyor. Mevcut kayıtlar kalıcı olarak silinecektir!",
                        Recommendation = $"Veri kaybını önlemek için Expand & Contract desenini uygulayın veya RenameColumn metodunu kullanın."
                    });
                }
            }
            else if (droppedCols.Count > 0)
            {
                // Tekil DropColumn (EFM001)
                foreach (var dropped in droppedCols)
                {
                    result.Issues.Add(new LintIssue
                    {
                        RuleId = "EFM001",
                        Severity = LintSeverity.Error,
                        TableName = tableName,
                        ColumnName = dropped.ColumnName,
                        Message = $"'{tableName}.{dropped.ColumnName}' sütunu DropColumn ile siliniyor. Bu işlem geri alınamaz ve üretimdeki verileri siler.",
                        Recommendation = "Canlı sistemde çalışan eski uygulama sürümleri varsa bu sütun okunmaya devam edebilir. Önce Contract aşamasını doğrulayın."
                    });
                }
            }

            // 2. KURAL: EFM003 - Unsafe NOT NULL Column Added (Mevcut tablolarda patlar)
            foreach (var added in addedCols)
            {
                if (added.IsNullable == false && string.IsNullOrEmpty(added.DefaultValue))
                {
                    result.Issues.Add(new LintIssue
                    {
                        RuleId = "EFM003",
                        Severity = LintSeverity.Error,
                        TableName = tableName,
                        ColumnName = added.ColumnName,
                        Message = $"'{tableName}' tablosuna varsayılan değeri (defaultValue) olmayan 'NOT NULL {added.ColumnName}' sütunu ekleniyor.",
                        Recommendation = "Mevcut satırlar varsa migration canlıda hata vererek çöker. Sütunu nullable yapın ya da defaultValue / defaultValueSql tanımlayın."
                    });
                }
            }

            // 3. KURAL: EFM004 - DropTable
            var droppedTables = group.Where(o => o.Kind == MigrationOperationKind.DropTable).ToList();
            foreach (var dt in droppedTables)
            {
                result.Issues.Add(new LintIssue
                {
                    RuleId = "EFM004",
                    Severity = LintSeverity.Error,
                    TableName = tableName,
                    Message = $"'{tableName}' tablosu DropTable ile tamamen kaldırılıyor! Tüm veriler silinecektir.",
                    Recommendation = "Tablonun yedeklendiğinden ve canlı uygulamanın bu tabloya bağımlılığı kalmadığından emin olun."
                });
            }

            // 4. KURAL: EFM006 - RenameColumn Uyarısı
            var renamedCols = group.Where(o => o.Kind == MigrationOperationKind.RenameColumn).ToList();
            foreach (var rc in renamedCols)
            {
                result.Issues.Add(new LintIssue
                {
                    RuleId = "EFM006",
                    Severity = LintSeverity.Warning,
                    TableName = tableName,
                    ColumnName = rc.ColumnName,
                    Message = $"'{tableName}.{rc.ColumnName}' sütunu '{rc.NewColumnName}' olarak yeniden adlandırılıyor.",
                    Recommendation = "Canlıda sıfır kesintili (zero-downtime) çalışan eski uygulama podları eski sütun adını sorgulamaya devam edebilir."
                });
            }
        }

        // 5. KURAL: EFM005 - Raw SQL Invocations
        var rawSqlOps = result.Operations.Where(o => o.Kind == MigrationOperationKind.SqlRaw).ToList();
        foreach (var sqlOp in rawSqlOps)
        {
            result.Issues.Add(new LintIssue
            {
                RuleId = "EFM005",
                Severity = LintSeverity.Warning,
                TableName = "RawSQL",
                Message = $"Doğrudan SQL çalıştırılıyor (Satır {sqlOp.LineNumber}).",
                Recommendation = "Çalıştırılan SQL komutunun idempotent ve veri güvenliği açısından güvenli olduğunu manuel inceleyin."
            });
        }
    }

    private static string? GetStringArg(ArgumentListSyntax list, string paramName, int fallbackIndex = -1)
    {
        var arg = list.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == paramName);
        if (arg == null && fallbackIndex >= 0 && fallbackIndex < list.Arguments.Count)
        {
            var candidate = list.Arguments[fallbackIndex];
            if (candidate.NameColon == null) arg = candidate;
        }

        if (arg?.Expression is LiteralExpressionSyntax lit)
        {
            return lit.Token.ValueText;
        }

        if (arg?.Expression is InvocationExpressionSyntax)
        {
            return arg.Expression.ToString();
        }

        return null;
    }

    private static bool? GetBoolArg(ArgumentListSyntax list, string paramName, int fallbackIndex = -1)
    {
        var arg = list.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == paramName);
        if (arg == null && fallbackIndex >= 0 && fallbackIndex < list.Arguments.Count)
        {
            var candidate = list.Arguments[fallbackIndex];
            if (candidate.NameColon == null) arg = candidate;
        }

        if (arg?.Expression is LiteralExpressionSyntax lit)
        {
            if (lit.IsKind(SyntaxKind.TrueLiteralExpression)) return true;
            if (lit.IsKind(SyntaxKind.FalseLiteralExpression)) return false;
        }
        return null;
    }
}
