using SchemaFlow.Core.Models;
using SchemaFlow.Core.Models.Operations;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Services;
using Xunit;

namespace SchemaFlow.Tests;

/// <summary>
/// SchemaFlow v1.0.6 Production Reliability Remediation & Regression Test Suite.
/// Bu test süiti; parser ve risk motorunda düzeltilen güvenlik açıklarını, 
/// sipariş sıralamalarını, SQL backfill analizlerini ve Roslyn doğruluğunu garanti altına alır.
/// </summary>
public class ProductionReliabilityAuditTests
{
    private readonly RoslynEfCoreParser _parser = new();
    private readonly MigrationRiskEngine _riskEngine = new();

    #region Remediated Audit Tests (Doğrulanmış ve Düzeltilmiş Güvenlik Davranışları)

    [Fact]
    public void Remediated_IncompleteOrFakeUpdateSql_RetainsCriticalRisk_NoFalseNegative()
    {
        // Geliştirici veya script bir SQL eklemiş ama bu bir veri taşıma (UPDATE) değil, bir SELECT veya yorum!
        var codeWithPseudoSql = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class PseudoSqlMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");

            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            // DİKKAT: Bu bir UPDATE değil, SELECT sorgusu! Veri kopyalamıyor!
            migrationBuilder.Sql(""SELECT Email, EmailAddress FROM Users;"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(codeWithPseudoSql);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);

        // DÜZELTİLDİ: SELECT veya yorum içeren SQL'ler riski asla düşürmez. Risk CRITICAL olarak korunur!
        Assert.Equal(RiskSeverity.Critical, mig003.Severity);
        Assert.Equal(DetectionType.Inferred, mig003.Detection);
    }

    [Fact]
    public void Remediated_UnrelatedDropAndAddColumns_AreNotFalselyPaired()
    {
        // 2 farklı sütun siliniyor (Bio, AvatarUrl), 1 alakasız sütun ekleniyor (ReferralCode)
        var codeWithMultipleColumns = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class UnrelatedColumnsMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: ""Bio"", table: ""Users"");
            migrationBuilder.DropColumn(name: ""AvatarUrl"", table: ""Users"");

            migrationBuilder.AddColumn<string>(
                name: ""ReferralCode"",
                table: ""Users"",
                type: ""nvarchar(50)"",
                nullable: true);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(codeWithMultipleColumns);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // DÜZELTİLDİ: Açgözlü (greedy) ilk sütunu eşleme kaldırıldı.
        // Bio ve AvatarUrl, ReferralCode ile eşleşmez. Dolayısıyla ExpandContract planı üretilmez.
        Assert.Empty(report.ExpandContractPlans);

        // Her iki silinen sütun da bağımsız DropColumn (MIG002 High Risk) olarak raporlanır!
        var mig002Issues = report.Issues.Where(i => i.RuleId == "MIG002").ToList();
        Assert.Equal(2, mig002Issues.Count);
        Assert.Contains(mig002Issues, i => i.ColumnName == "Bio");
        Assert.Contains(mig002Issues, i => i.ColumnName == "AvatarUrl");
    }

    [Fact]
    public void Remediated_AlterColumn_WithoutOldNullableScaffolded_DetectsInferredRisk()
    {
        // Geliştirici elle yazmış veya EF Core oldNullable eklememiş:
        // Sütun nullable: false yapılıyor.
        var codeAlterWithoutOldNullable = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class ManualAlterColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: ""TaxNumber"",
                table: ""Companies"",
                type: ""nvarchar(20)"",
                nullable: false);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(codeAlterWithoutOldNullable);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // DÜZELTİLDİ: oldNullable bilinmese dahi potansiyel daralma Inferred olarak raporlanır!
        var mig005 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG005");
        Assert.NotNull(mig005);
        Assert.Equal(RiskSeverity.High, mig005.Severity);
        Assert.Equal(DetectionType.Inferred, mig005.Detection);
        Assert.Equal(AnalysisConfidence.Medium, mig005.Confidence);
    }

    [Fact]
    public void Remediated_AlterColumn_SafeLengthExpansion_IsNotFlaggedAsHighRisk()
    {
        // varchar(50) -> varchar(255) güvenli bir genişlemedir, veri kaybı riski YOKTUR.
        var codeSafeExpansion = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class SafeExpansionMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: ""Title"",
                table: ""Posts"",
                type: ""varchar(255)"",
                oldType: ""varchar(50)"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(codeSafeExpansion);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // DÜZELTİLDİ: Güvenli genişlemeler MIG011 High Risk olarak damgalanmaz!
        var mig011 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG011");
        Assert.Null(mig011);

        // Bunun yerine bilgilendirme amacıyla MIG011_WIDENING Info seviyesinde üretilir.
        var widening = report.Issues.FirstOrDefault(i => i.RuleId == "MIG011_WIDENING");
        Assert.NotNull(widening);
        Assert.Equal(RiskSeverity.Info, widening.Severity);
    }

    [Fact]
    public void Remediated_DynamicOrVariableExpressions_ResolvesClassConstants()
    {
        // Tablo adı sınıf seviyesindeki bir const değişkenden geliyor
        var codeWithConstant = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class DynamicMigration : Migration
    {
        private const string UsersTableName = ""Users"";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: ""OldColumn"",
                table: UsersTableName);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(codeWithConstant);

        // DÜZELTİLDİ: Sınıf seviyesindeki const ifadeler Roslyn üzerinden çözümlenir!
        var dropOp = parseResult.UpOperations.OfType<DropColumnOperation>().Single();
        Assert.Equal("Users", dropOp.TableName);
        Assert.Equal("OldColumn", dropOp.ColumnName);
        Assert.Equal(DetectionType.Confirmed, dropOp.Detection);
    }

    #endregion

    #region Regression Tests (İşlem Sırası, SQL Backfill, Alakasız Çağrılar ve Tip Değişimleri)

    [Fact]
    public void Regression_ExecutionOrder_DropBeforeSql_TriggersCriticalOrderViolation()
    {
        // AddColumn -> DropColumn -> Sql (UPDATE)
        // Kolon silindikten SONRA SQL çalıştırmak canlıda 'Column not found' hatasıyla patlar!
        var invalidOrderCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class InvalidOrderMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");

            // ÇALIŞMA SIRASI HATASI: Email kolonu çoktan silindi!
            migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email;"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(invalidOrderCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var orderIssue = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003_ORDER");
        Assert.NotNull(orderIssue);
        Assert.Equal(RiskSeverity.Critical, orderIssue.Severity);
        Assert.Equal(DetectionType.Confirmed, orderIssue.Detection);
        Assert.Contains("Hatalı Operasyon Sırası", orderIssue.Title);
        Assert.Contains("DropColumn'dan", orderIssue.Description);
    }

    [Fact]
    public void Regression_ExecutionOrder_SqlBeforeAdd_TriggersCriticalOrderViolation()
    {
        // Sql (UPDATE) -> AddColumn -> DropColumn
        // Kolon eklenmeden ÖNCE SQL çalıştırmak 'Column EmailAddress not found' hatası verir!
        var invalidOrderCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class SqlBeforeAddMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ÇALIŞMA SIRASI HATASI: EmailAddress henüz eklenmedi!
            migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email;"");

            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(invalidOrderCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var orderIssue = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003_ORDER");
        Assert.NotNull(orderIssue);
        Assert.Equal(RiskSeverity.Critical, orderIssue.Severity);
        Assert.Contains("AddColumn'dan", orderIssue.Description);
        Assert.Contains("önce", orderIssue.Description);
    }

    [Fact]
    public void Regression_ExecutionOrder_CorrectAddSqlDrop_ClassifiedAsVerifiedCandidateWithHighRisk()
    {
        // AddColumn -> UPDATE -> DropColumn
        // Doğru sıra! Ancak statik analiz veri taşmasının garantisi olamayacağı için High Risk ve doğrulama ister.
        var validOrderCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class ValidOrderMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email;"");

            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(validOrderCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        Assert.Equal(RiskSeverity.High, mig003.Severity);
        Assert.Equal(DetectionType.Inferred, mig003.Detection);
        Assert.Contains("Doğrulama Gerekli", mig003.Title);
    }

    [Fact]
    public void Regression_PartialWhereClause_DoesNotTreatAsCompleteBackfill()
    {
        // UPDATE Users SET EmailAddress = Email WHERE Id = 1; (Yalnızca 1 satırı güncelliyor, tabloyu değil!)
        var partialUpdateCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class PartialBackfillMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email WHERE Id = 1;"");

            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(partialUpdateCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        // Kısmi filtre tüm satırları taşımadığı için risk Critical olarak kalır!
        Assert.Equal(RiskSeverity.Critical, mig003.Severity);
        Assert.Contains("Potansiyel Veri Kaybı", mig003.Title);
        Assert.Contains("Koşullu WHERE filtresi", mig003.Description);
    }

    [Fact]
    public void Regression_UpdateOnDifferentTable_DoesNotMitigateRisk()
    {
        // Users tablosundaki kolonlar değişiyor ama SQL Orders tablosunu güncelliyor!
        var wrongTableCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class WrongTableMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            migrationBuilder.Sql(""UPDATE Orders SET Total = 100;"");

            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(wrongTableCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        Assert.Equal(RiskSeverity.Critical, mig003.Severity);
    }

    [Fact]
    public void Regression_ForeignServiceMethodCalls_AreIgnoredByParser()
    {
        // customService.DropTable("temp") gibi EF migrationBuilder'a ait olmayan metodlar ayrıştırılmamalıdır
        var foreignCallCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class ForeignCallsMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            customService.DropTable(""temp"");
            logger.LogInformation(""AddColumn"");
            Console.WriteLine(""CreateTable"");

            migrationBuilder.DropTable(name: ""RealTable"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(foreignCallCode);
        Assert.Single(parseResult.UpOperations);
        var realOp = parseResult.UpOperations.OfType<DropTableOperation>().Single();
        Assert.Equal("RealTable", realOp.TableName);
    }

    [Fact]
    public void Regression_NameofExpression_ResolvesTerminalIdentifier()
    {
        var nameofCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class NameofMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: nameof(Entities.User.EmailAddress),
                table: nameof(Entities.User),
                type: ""nvarchar(255)"",
                nullable: true);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(nameofCode);
        var addOp = parseResult.UpOperations.OfType<AddColumnOperation>().Single();
        Assert.Equal("EmailAddress", addOp.ColumnName);
        Assert.Equal("User", addOp.TableName);
    }

    [Fact]
    public void Regression_StringConcatenation_ResolvesConcatenatedValue()
    {
        var concatCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class ConcatMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: ""tbl_"" + ""users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(concatCode);
        var dropOp = parseResult.UpOperations.OfType<DropTableOperation>().Single();
        Assert.Equal("tbl_users", dropOp.TableName);
    }

    [Fact]
    public void Regression_AlterColumn_NarrowingType_FlagsHighRisk()
    {
        // varchar(255) -> varchar(50) veri kırpılması (truncation) riski taşır!
        var narrowingCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class NarrowingMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: ""Title"",
                table: ""Posts"",
                type: ""varchar(50)"",
                oldType: ""varchar(255)"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(narrowingCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var mig011 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG011");
        Assert.NotNull(mig011);
        Assert.Equal(RiskSeverity.High, mig011.Severity);
        Assert.Equal(DetectionType.Confirmed, mig011.Detection);
        Assert.Contains("Tip Daraltma", mig011.Title);
    }

    [Fact]
    public void Regression_AddColumn_UnresolvedNullableExpression_FlagsUnknownNullability()
    {
        // nullable argümanı verilmiş fakat dinamik bir değişken olduğu için statik çözümlenememiş
        var dynamicNullableCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class DynamicNullableMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: ""DynamicCol"",
                table: ""Users"",
                type: ""nvarchar(100)"",
                nullable: RuntimeConfig.IsNullable);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(dynamicNullableCode);
        var addOp = parseResult.UpOperations.OfType<AddColumnOperation>().Single();
        Assert.False(addOp.IsNullableKnown);
        Assert.Null(addOp.NullableState);
        Assert.Equal(DetectionType.Unknown, addOp.Detection);
        Assert.Equal(AnalysisConfidence.Low, addOp.Confidence);

        var report = _riskEngine.Analyze(parseResult.AllOperations);
        var unknownIssue = report.Issues.FirstOrDefault(i => i.RuleId == "MIG004_UNKNOWN");
        Assert.NotNull(unknownIssue);
        Assert.Equal(RiskSeverity.Medium, unknownIssue.Severity);
        Assert.Equal(DetectionType.Unknown, unknownIssue.Detection);
    }

    [Fact]
    public void Verification_UpdateWithTableAlias_RecognizedAsBackfillCandidate()
    {
        // UPDATE u SET u.EmailAddress = u.Email FROM Users u
        var aliasUpdateCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class AliasUpdateMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: ""EmailAddress"", table: ""Users"", type: ""nvarchar(255)"", nullable: true);
            migrationBuilder.Sql(""UPDATE u SET u.EmailAddress = u.Email FROM Users u;"");
            migrationBuilder.DropColumn(name: ""Email"", table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(aliasUpdateCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        // Aliaslı UPDATE adayı başarıyla tanındı ve High severity + doğrulama uyarısı aldı
        Assert.Equal(RiskSeverity.High, mig003.Severity);
        Assert.Contains("Doğrulama Gerekli", mig003.Title);
    }

    [Fact]
    public void Verification_MultipleOperationsOnSameLine_OrderViolationDetectedByIndex()
    {
        // Aynı satırda DropColumn ve Sql çağrılıyor (Drop, Sql'den önce)
        var sameLineCode = @"using Microsoft.EntityFrameworkCore.Migrations;
namespace AuditMigrations {
    public partial class SameLineMigration : Migration {
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<string>(name: ""EmailAddress"", table: ""Users"", type: ""nvarchar(255)"", nullable: true); migrationBuilder.DropColumn(name: ""Email"", table: ""Users""); migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email;"");
        }
    }
}";

        var parseResult = _parser.ParseMigration(sameLineCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // Satır numaraları aynı olsa bile AST sırası ile Sql'in Drop'tan sonra olduğu tespit edilir!
        var orderIssue = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003_ORDER");
        Assert.NotNull(orderIssue);
        Assert.Equal(RiskSeverity.Critical, orderIssue.Severity);
    }

    [Fact]
    public void Verification_DownMethod_AnalyzedIndependently()
    {
        var migrationCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class RollbackMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: ""Orders"", columns: table => new { });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback sırasında tablo silinmesi
            migrationBuilder.DropTable(name: ""Orders"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(migrationCode);

        // 1. Up metodunda DropTable yoktur
        var upReport = _riskEngine.Analyze(parseResult.UpOperations);
        Assert.DoesNotContain(upReport.Issues, i => i.RuleId == "MIG001");

        // 2. Down metodu bağımsız analiz edildiğinde DropTable riski (MIG001 Critical) tespit edilir
        var downReport = _riskEngine.Analyze(parseResult.DownOperations);
        var mig001 = downReport.Issues.FirstOrDefault(i => i.RuleId == "MIG001");
        Assert.NotNull(mig001);
        Assert.Equal(RiskSeverity.Critical, mig001.Severity);
    }

    [Fact]
    public void Verification_ShortColumnNames_NotFalselyPairedAsRename()
    {
        // 'Tax' ve 'Fax' 3 harfli farklı kelimelerdir (Levenshtein mesafesi 1 olmasına rağmen eşleştirilmemelidir)
        var shortColCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class ShortColumnsMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: ""Tax"", table: ""Companies"");
            migrationBuilder.AddColumn<string>(name: ""Fax"", table: ""Companies"", type: ""varchar(20)"", nullable: true);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(shortColCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // 'Tax' ile 'Fax' kısa kelime koruması nedeniyle eşleştirilmez; bağımsız DropColumn raporlanır!
        Assert.Empty(report.ExpandContractPlans);
        var dropIssue = report.Issues.FirstOrDefault(i => i.RuleId == "MIG002" && i.ColumnName == "Tax");
        Assert.NotNull(dropIssue);
        Assert.Equal(RiskSeverity.High, dropIssue.Severity);
    }

    [Fact]
    public void Verification_MultiStatementSql_TruncateNotHiddenByBackfill()
    {
        // UPDATE Users SET EmailAddress = Email; TRUNCATE TABLE AuditLogs;
        var multiSqlCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class MultiSqlMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: ""EmailAddress"", table: ""Users"", type: ""nvarchar(255)"", nullable: true);
            migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email; TRUNCATE TABLE AuditLogs;"");
            migrationBuilder.DropColumn(name: ""Email"", table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(multiSqlCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // 1. TRUNCATE komutu bağımsız olarak yakalanmalıdır (MIG009_SQL_TRUNCATE Critical)
        var truncateIssue = report.Issues.FirstOrDefault(i => i.RuleId == "MIG009_SQL_TRUNCATE");
        Assert.NotNull(truncateIssue);
        Assert.Equal(RiskSeverity.Critical, truncateIssue.Severity);
        Assert.Equal("AuditLogs", truncateIssue.TableName);

        // 2. Yan etki olarak yıkıcı komut barındırdığı için MIG003 riski düşürülmez (Critical kalır)
        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        Assert.Equal(RiskSeverity.Critical, mig003.Severity);
        Assert.Contains("Yıkıcı Yan Etki", mig003.Title);
    }

    [Fact]
    public void Verification_SqlLexer_CommentsInsideStringLiteral_NotTreatedAsComments()
    {
        // String literal içinde geçen '--' ifadesi yorum sayılarak temizlenmemelidir
        var literalCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public partial class LiteralCommentMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: ""EmailAddress"", table: ""Users"", type: ""nvarchar(255)"", nullable: true);
            migrationBuilder.Sql(""UPDATE Users SET Description = '-- this is not a comment', EmailAddress = Email;"");
            migrationBuilder.DropColumn(name: ""Email"", table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(literalCode);
        var report = _riskEngine.Analyze(parseResult.AllOperations);

        // SQL lexer string literal içindeki '--' ifadesini yorum sanmaz, atama başarıyla tanınır
        var mig003 = report.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        Assert.Equal(RiskSeverity.High, mig003.Severity);
        Assert.Contains("Doğrulama Gerekli", mig003.Title);
    }

    [Fact]
    public void Verification_SemanticAndSyntacticReceiverCheck_RejectsCustomMigrationBuilder()
    {
        // var migrationBuilder = new CustomService(); migrationBuilder.DropTable("Users");
        var customBuilderCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditMigrations
{
    public class CustomMigrationBuilder
    {
        public void DropTable(string name) { }
    }

    public partial class FakeBuilderMigration : Migration
    {
        protected override void Up(MigrationBuilder mb)
        {
            var migrationBuilder = new CustomMigrationBuilder();
            migrationBuilder.DropTable(""FakeTable"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(customBuilderCode);

        // Custom nesne üzerindeki DropTable çağrısı EF Core MigrationBuilder sanılmamalıdır!
        Assert.Empty(parseResult.UpOperations);
    }

    #endregion
}
