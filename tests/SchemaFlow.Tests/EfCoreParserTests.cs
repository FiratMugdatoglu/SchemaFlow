using SchemaFlow.Core.Models;
using SchemaFlow.Core.Parsers;
using Xunit;

namespace SchemaFlow.Tests;

public class EfCoreParserTests
{
    private readonly EfCoreParser _parser = new();

    [Fact]
    public async Task ParseSqlAsync_ShouldExtractCreateTable_WithAllColumnsAndKeys()
    {
        var migrationCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DemoApp.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: ""Users"",
                columns: table => new
                {
                    Id = table.Column<int>(type: ""integer"", nullable: false)
                        .Annotation(""Npgsql:ValueGenerationStrategy"", 1),
                    Username = table.Column<string>(type: ""character varying(50)"", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: ""text"", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: ""timestamp with time zone"", nullable: false, defaultValueSql: ""CURRENT_TIMESTAMP"")
                },
                constraints: table =>
                {
                    table.PrimaryKey(""PK_Users"", x => x.Id);
                });
        }
    }
}
";

        var schema = await _parser.ParseSqlAsync(migrationCode);

        Assert.Single(schema.Tables);
        var table = schema.Tables.First();
        Assert.Equal("Users", table.Name);
        Assert.Equal(4, table.Columns.Count);

        var idCol = table.Columns.First(c => c.Name == "Id");
        Assert.Equal("integer", idCol.DataType);
        Assert.False(idCol.IsNullable);
        Assert.True(idCol.IsPrimaryKey);

        var usernameCol = table.Columns.First(c => c.Name == "Username");
        Assert.Equal("character varying(50)", usernameCol.DataType);
        Assert.False(usernameCol.IsNullable);

        var emailCol = table.Columns.First(c => c.Name == "Email");
        Assert.True(emailCol.IsNullable);

        var createdAtCol = table.Columns.First(c => c.Name == "CreatedAt");
        Assert.Equal("CURRENT_TIMESTAMP", createdAtCol.DefaultValue);
    }

    [Fact]
    public async Task ParseSqlAsync_ShouldHandleForeignKeys()
    {
        var migrationCode = @"
using Microsoft.EntityFrameworkCore.Migrations;

namespace DemoApp.Migrations
{
    public partial class AddOrders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: ""Orders"",
                columns: table => new
                {
                    Id = table.Column<int>(type: ""int"", nullable: false),
                    UserId = table.Column<int>(type: ""int"", nullable: false),
                    TotalAmount = table.Column<decimal>(type: ""decimal(18,2)"", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(""PK_Orders"", x => x.Id);
                    table.ForeignKey(
                        name: ""FK_Orders_Users_UserId"",
                        column: x => x.UserId,
                        principalTable: ""Users"",
                        principalColumn: ""Id"",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
";

        var schema = await _parser.ParseSqlAsync(migrationCode);

        var orderTable = schema.Tables.First(t => t.Name == "Orders");
        Assert.Single(orderTable.ForeignKeys);
        var fk = orderTable.ForeignKeys.First();
        Assert.Equal("Users", fk.TargetTable);
        Assert.Contains("UserId", fk.SourceColumns);
    }
}
