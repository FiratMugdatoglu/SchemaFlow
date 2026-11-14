using SchemaFlow.Core.Models;
using SchemaFlow.Core.Services;
using Xunit;

namespace SchemaFlow.Tests;

public class SchemaDiffEngineTests
{
    private readonly SchemaDiffEngine _engine = new();

    [Fact]
    public void Compare_ShouldDetectDroppedColumn_AsBreakingChange()
    {
        var baseSchema = new DatabaseSchema();
        var baseTable = new Table { Name = "Users" };
        baseTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        baseTable.Columns.Add(new Column { Name = "Email", DataType = "varchar(100)" });
        baseSchema.Tables.Add(baseTable);

        var targetSchema = new DatabaseSchema();
        var targetTable = new Table { Name = "Users" };
        targetTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        // Email dropped!
        targetSchema.Tables.Add(targetTable);

        var result = _engine.Compare(baseSchema, targetSchema);

        Assert.True(result.HasBreakingChanges);
        Assert.Equal(1, result.BreakingCount);
        Assert.Contains(result.Changes, c => c.Type == ChangeType.ColumnDropped && c.ColumnName == "Email");
    }

    [Fact]
    public void Compare_ShouldDetectExpandContractPattern_WhenColumnDroppedAndAddedInSameTable()
    {
        var baseSchema = new DatabaseSchema();
        var baseTable = new Table { Name = "Users" };
        baseTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        baseTable.Columns.Add(new Column { Name = "Email", DataType = "varchar(100)" });
        baseSchema.Tables.Add(baseTable);

        var targetSchema = new DatabaseSchema();
        var targetTable = new Table { Name = "Users" };
        targetTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        targetTable.Columns.Add(new Column { Name = "EmailAddress", DataType = "varchar(150)", IsNullable = true });
        targetSchema.Tables.Add(targetTable);

        var result = _engine.Compare(baseSchema, targetSchema);

        Assert.True(result.HasBreakingChanges);
        Assert.Single(result.ExpandContractPlans);

        var plan = result.ExpandContractPlans.First();
        Assert.Equal("Users", plan.TableName);
        Assert.Equal("Email", plan.DroppedColumn);
        Assert.Equal("EmailAddress", plan.AddedColumn);

        var dropChange = result.Changes.First(c => c.Type == ChangeType.ColumnDropped);
        Assert.NotNull(dropChange.Recommendation);
        Assert.Contains("Expand & Contract", dropChange.Recommendation);
    }

    [Fact]
    public void Compare_ShouldDetectNotNullColumnWithoutDefault_AsBreakingChange()
    {
        var baseSchema = new DatabaseSchema();
        var baseTable = new Table { Name = "Customers" };
        baseTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        baseSchema.Tables.Add(baseTable);

        var targetSchema = new DatabaseSchema();
        var targetTable = new Table { Name = "Customers" };
        targetTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        targetTable.Columns.Add(new Column { Name = "TaxNumber", DataType = "varchar(20)", IsNullable = false, DefaultValue = null });
        targetSchema.Tables.Add(targetTable);

        var result = _engine.Compare(baseSchema, targetSchema);

        Assert.True(result.HasBreakingChanges);
        var change = result.Changes.First(c => c.ColumnName == "TaxNumber");
        Assert.True(change.IsBreaking);
    }

    [Fact]
    public void Compare_ShouldTreatNullableColumnAddition_AsSafeChange()
    {
        var baseSchema = new DatabaseSchema();
        var baseTable = new Table { Name = "Products" };
        baseTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        baseSchema.Tables.Add(baseTable);

        var targetSchema = new DatabaseSchema();
        var targetTable = new Table { Name = "Products" };
        targetTable.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        targetTable.Columns.Add(new Column { Name = "Description", DataType = "text", IsNullable = true });
        targetSchema.Tables.Add(targetTable);

        var result = _engine.Compare(baseSchema, targetSchema);

        Assert.False(result.HasBreakingChanges);
        Assert.Equal(1, result.SafeCount);
    }
}
