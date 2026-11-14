using SchemaFlow.Core.Models;
using SchemaFlow.Core.Models.Operations;
using SchemaFlow.Core.Parsers;
using SchemaFlow.Core.Services;
using Xunit;

namespace SchemaFlow.Tests;

public class RoslynEfCoreParserAndRiskEngineTests
{
    private readonly RoslynEfCoreParser _parser = new();
    private readonly MigrationRiskEngine _riskEngine = new();

    [Fact]
    public void RoslynParser_ShouldExtractAllOperations_WithScopeAndLineNumbers()
    {
        var code = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestMigrations
{
    public partial class ComplexMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: ""AuditLogs"",
                columns: table => new
                {
                    Id = table.Column<long>(type: ""bigint"", nullable: false),
                    Action = table.Column<string>(type: ""nvarchar(100)"", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(""PK_AuditLogs"", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: ""IX_AuditLogs_Action"",
                table: ""AuditLogs"",
                column: ""Action"",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: ""FK_Orders_Users_UserId"",
                table: ""Orders"",
                column: ""UserId"",
                principalTable: ""Users"",
                principalColumn: ""Id"");

            migrationBuilder.Sql(""CREATE VIEW ActiveUsers AS SELECT * FROM Users WHERE IsActive = 1;"");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: ""FK_Orders_Users_UserId"", table: ""Orders"");
            migrationBuilder.DropIndex(name: ""IX_AuditLogs_Action"", table: ""AuditLogs"");
            migrationBuilder.DropTable(name: ""AuditLogs"");
        }
    }
}
";

        var result = _parser.ParseMigration(code, "20261115_ComplexMigration.cs");

        Assert.Equal("ComplexMigration", result.MigrationClassName);
        Assert.Equal(4, result.UpOperations.Count);
        Assert.Equal(3, result.DownOperations.Count);

        // Up operasyonları
        var createTable = result.UpOperations.OfType<CreateTableOperation>().Single();
        Assert.Equal("AuditLogs", createTable.TableName);
        Assert.Equal(MigrationMethodScope.Up, createTable.Scope);
        Assert.True(createTable.LineNumber > 0);

        var createIndex = result.UpOperations.OfType<CreateIndexOperation>().Single();
        Assert.Equal("IX_AuditLogs_Action", createIndex.IndexName);
        Assert.True(createIndex.IsUnique);

        var addFk = result.UpOperations.OfType<AddForeignKeyOperation>().Single();
        Assert.Equal("Orders", addFk.TableName);
        Assert.Equal("Users", addFk.PrincipalTable);

        var sqlOp = result.UpOperations.OfType<RawSqlOperation>().Single();
        Assert.Contains("ActiveUsers", sqlOp.SqlContent);

        // Down operasyonları
        var dropFk = result.DownOperations.OfType<DropForeignKeyOperation>().Single();
        Assert.Equal(MigrationMethodScope.Down, dropFk.Scope);

        var dropIdx = result.DownOperations.OfType<DropIndexOperation>().Single();
        Assert.Equal(MigrationMethodScope.Down, dropIdx.Scope);

        var dropTable = result.DownOperations.OfType<DropTableOperation>().Single();
        Assert.Equal(MigrationMethodScope.Down, dropTable.Scope);
    }

    [Fact]
    public void RoslynParser_ShouldCaptureUnknownOperations_AndNotDropSilently()
    {
        var codeWithCustomHelper = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestMigrations
{
    public partial class CustomHelperMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CustomDatabaseSpecificExtension(""CustomArg"");
        }
    }
}
";

        var result = _parser.ParseMigration(codeWithCustomHelper);

        Assert.Single(result.UnresolvedOperations);
        var unk = result.UnresolvedOperations.First();
        Assert.Equal("CustomDatabaseSpecificExtension", unk.MethodName);
        Assert.Equal(DetectionType.Unknown, unk.Detection);
        Assert.Equal(AnalysisConfidence.Medium, unk.Confidence);

        // Risk motoru da bunu raporlamalı (MIG012)
        var safetyReport = _riskEngine.Analyze(result.AllOperations);
        Assert.Contains(safetyReport.Issues, i => i.RuleId == "MIG012");
    }

    [Fact]
    public void RoslynParser_ShouldIgnoreExternalCalls_PreventingFalsePositives()
    {
        var codeWithLogger = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestMigrations
{
    public partial class LoggedMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            System.Console.WriteLine(""Starting migration..."");
            migrationBuilder.DropTable(name: ""TemporaryTokens"");
        }
    }
}
";

        var result = _parser.ParseMigration(codeWithLogger);

        // Console.WriteLine kesinlikle EF Core operasyonu olarak yakalanmamalı!
        Assert.Single(result.UpOperations);
        Assert.IsType<DropTableOperation>(result.UpOperations[0]);
        Assert.Empty(result.UnresolvedOperations);
    }

    [Fact]
    public void RiskEngine_WhenExpandContractHasDataBackfillSql_ShouldLowerRiskSeverity()
    {
        // Geliştirici eski sütunu silip yeni sütun eklemiş, AMA arada UPDATE komutu çalıştırmış!
        var codeWithSafeBackfill = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestMigrations
{
    public partial class SafeMigratedRename : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: ""EmailAddress"",
                table: ""Users"",
                type: ""nvarchar(255)"",
                nullable: true);

            migrationBuilder.Sql(""UPDATE Users SET EmailAddress = Email WHERE EmailAddress IS NULL;"");

            migrationBuilder.DropColumn(
                name: ""Email"",
                table: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(codeWithSafeBackfill);
        var safetyReport = _riskEngine.Analyze(parseResult.AllOperations);

        // Arada SQL olduğu için MIG003 Critical (Kayıp) değil, Medium (Doğrulama Bilgisi) olmalıdır!
        var mig003 = safetyReport.Issues.FirstOrDefault(i => i.RuleId == "MIG003");
        Assert.NotNull(mig003);
        Assert.Equal(RiskSeverity.Medium, mig003.Severity);
        Assert.Contains("Veri Taşımalı Sütun Değişimi", mig003.Title);
    }

    [Fact]
    public void RiskEngine_ShouldDetectNullableToNotNullAlterColumn()
    {
        var alterCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestMigrations
{
    public partial class MakeColumnRequired : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: ""PhoneNumber"",
                table: ""Contacts"",
                type: ""nvarchar(20)"",
                nullable: false,
                oldNullable: true);
        }
    }
}
";

        var parseResult = _parser.ParseMigration(alterCode);
        var safetyReport = _riskEngine.Analyze(parseResult.AllOperations);

        var issue = safetyReport.Issues.FirstOrDefault(i => i.RuleId == "MIG005");
        Assert.NotNull(issue);
        Assert.Equal(RiskSeverity.High, issue.Severity);
        Assert.Contains("NOT NULL Yapılıyor", issue.Title);
    }

    [Fact]
    public void RiskEngine_ShouldDetectRenameTable_AsHighRiskAppCompatibilityIssue()
    {
        var renameTableCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestMigrations
{
    public partial class RenameLegacyTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: ""tbl_user_accounts"",
                newName: ""Users"");
        }
    }
}
";

        var parseResult = _parser.ParseMigration(renameTableCode);
        var safetyReport = _riskEngine.Analyze(parseResult.AllOperations);

        var issue = safetyReport.Issues.FirstOrDefault(i => i.RuleId == "MIG007");
        Assert.NotNull(issue);
        Assert.Equal(RiskSeverity.High, issue.Severity);
        Assert.Contains("Tablo Adı Değişimi", issue.Title);
    }
}
