using SchemaFlow.Core.Models;
using SchemaFlow.Core.Services;
using Xunit;

namespace SchemaFlow.Tests;

public class EfCoreMigrationAnalyzerTests
{
    private readonly EfCoreMigrationAnalyzer _analyzer = new();

    [Fact]
    public void AnalyzeMigrationCode_ShouldDetectExpandContractAntiPattern_WhenColumnDroppedAndAddedInSameTable()
    {
        // Klasik EF Core tuzağı: C# sınıfında 'Email' propu 'EmailAddress' yapıldığında
        // EF Core DropColumn + AddColumn üretir ve üretimdeki tüm kullanıcı mailleri silinir!
        var dangerousMigration = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace DemoApp.Migrations
{
    public partial class RenameEmailProperty : Migration
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
        }
    }
}
";

        var result = _analyzer.AnalyzeMigrationCode(dangerousMigration);

        Assert.Equal(2, result.Operations.Count);
        Assert.True(result.HasDestructiveChanges);

        // Expand & Contract planı üretilmeli!
        Assert.Single(result.ExpandContractPlans);
        var plan = result.ExpandContractPlans.First();
        Assert.Equal("Users", plan.TableName);
        Assert.Equal("Email", plan.DroppedColumn);
        Assert.Equal("EmailAddress", plan.AddedColumn);

        // İlgili EFM002 kural ihlali olmalı
        var issue = result.Issues.FirstOrDefault(i => i.RuleId == "EFM002");
        Assert.NotNull(issue);
        Assert.Equal(LintSeverity.Error, issue.Severity);
        Assert.Contains("Expand & Contract", issue.Message);
    }

    [Fact]
    public void AnalyzeMigrationCode_ShouldDetectUnsafeNotNullColumnAdded()
    {
        var unsafeMigration = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace DemoApp.Migrations
{
    public partial class AddRequiredAge : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: ""Age"",
                table: ""Users"",
                type: ""int"",
                nullable: false);
        }
    }
}
";

        var result = _analyzer.AnalyzeMigrationCode(unsafeMigration);

        var issue = result.Issues.FirstOrDefault(i => i.RuleId == "EFM003");
        Assert.NotNull(issue);
        Assert.Equal(LintSeverity.Error, issue.Severity);
        Assert.Contains("NOT NULL", issue.Message);
    }

    [Fact]
    public void AnalyzeMigrationCode_ShouldDetectDropTable()
    {
        var dropTableMigration = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace DemoApp.Migrations
{
    public partial class DropLegacyTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: ""OldLogs"");
        }
    }
}
";

        var result = _analyzer.AnalyzeMigrationCode(dropTableMigration);

        var op = result.Operations.FirstOrDefault(o => o.Kind == MigrationOperationKind.DropTable);
        Assert.NotNull(op);
        Assert.Equal("OldLogs", op.TableName);
        Assert.True(op.IsDestructive);

        var issue = result.Issues.FirstOrDefault(i => i.RuleId == "EFM004");
        Assert.NotNull(issue);
        Assert.Equal(LintSeverity.Error, issue.Severity);
    }

    [Fact]
    public void AnalyzeMigrationCode_ShouldIdentifySafeRenameColumn()
    {
        var renameMigration = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace DemoApp.Migrations
{
    public partial class SafeRename : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: ""OldTitle"",
                table: ""Articles"",
                newName: ""Title"");
        }
    }
}
";

        var result = _analyzer.AnalyzeMigrationCode(renameMigration);

        var op = result.Operations.FirstOrDefault(o => o.Kind == MigrationOperationKind.RenameColumn);
        Assert.NotNull(op);
        Assert.Equal("Articles", op.TableName);
        Assert.Equal("OldTitle", op.ColumnName);
        Assert.Equal("Title", op.NewColumnName);

        var issue = result.Issues.FirstOrDefault(i => i.RuleId == "EFM006");
        Assert.NotNull(issue);
        Assert.Equal(LintSeverity.Warning, issue.Severity);
    }
}
