namespace SchemaFlow.Core.Models.Operations;

public record TableColumnDefinition(
    string Name,
    string DataType,
    bool IsNullable = true,
    string? DefaultValue = null,
    bool IsPrimaryKey = false,
    bool IsIdentity = false
);

public record CreateTableOperation : MigrationOperation
{
    public override string OperationName => "CreateTable";
    public List<TableColumnDefinition> Columns { get; init; } = new();
    public List<string> PrimaryKeyColumns { get; init; } = new();
    public override string ToDisplaySummary() => $"CreateTable '{TableName}' ({Columns.Count} sütun)";
}

public record DropTableOperation : MigrationOperation
{
    public override string OperationName => "DropTable";
    public override string ToDisplaySummary() => $"DropTable '{TableName}' (Tüm veriler kalıcı silinir)";
}

public record RenameTableOperation : MigrationOperation
{
    public override string OperationName => "RenameTable";
    public string NewTableName { get; init; } = string.Empty;
    public string? Schema { get; init; }
    public string? NewSchema { get; init; }
    public override string ToDisplaySummary() => $"RenameTable '{TableName}' ➡️ '{NewTableName}'";
}

public record AddColumnOperation : MigrationOperation
{
    public override string OperationName => "AddColumn";
    public string ColumnName { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool IsNullable { get; init; } = true;
    public bool IsNullableKnown { get; init; } = true;
    public bool? NullableState => IsNullableKnown ? IsNullable : null;
    public string? DefaultValue { get; init; }
    public string? DefaultValueSql { get; init; }
    public override string ToDisplaySummary() => $"AddColumn '{TableName}.{ColumnName}' ({DataType}, Nullable: {(IsNullableKnown ? IsNullable.ToString() : "Unknown")})";
}

public record DropColumnOperation : MigrationOperation
{
    public override string OperationName => "DropColumn";
    public string ColumnName { get; init; } = string.Empty;
    public string? DataType { get; init; }
    public override string ToDisplaySummary() => $"DropColumn '{TableName}.{ColumnName}' (Kalıcı sütun silinmesi)";
}

public record RenameColumnOperation : MigrationOperation
{
    public override string OperationName => "RenameColumn";
    public string ColumnName { get; init; } = string.Empty;
    public string NewColumnName { get; init; } = string.Empty;
    public override string ToDisplaySummary() => $"RenameColumn '{TableName}.{ColumnName}' ➡️ '{NewColumnName}'";
}

public record AlterColumnOperation : MigrationOperation
{
    public override string OperationName => "AlterColumn";
    public string ColumnName { get; init; } = string.Empty;
    public string? DataType { get; init; }
    public string? OldDataType { get; init; }
    public bool? IsNullable { get; init; }
    public bool? OldIsNullable { get; init; }
    public string? DefaultValue { get; init; }
    public override string ToDisplaySummary() => $"AlterColumn '{TableName}.{ColumnName}' (Tip: {DataType ?? "Değişmedi"}, Nullable: {IsNullable?.ToString() ?? "Değişmedi"})";
}

public record AddPrimaryKeyOperation : MigrationOperation
{
    public override string OperationName => "AddPrimaryKey";
    public string ConstraintName { get; init; } = string.Empty;
    public List<string> Columns { get; init; } = new();
    public override string ToDisplaySummary() => $"AddPrimaryKey '{ConstraintName}' on '{TableName}' ({string.Join(", ", Columns)})";
}

public record DropPrimaryKeyOperation : MigrationOperation
{
    public override string OperationName => "DropPrimaryKey";
    public string ConstraintName { get; init; } = string.Empty;
    public override string ToDisplaySummary() => $"DropPrimaryKey '{ConstraintName}' on '{TableName}'";
}

public record AddForeignKeyOperation : MigrationOperation
{
    public override string OperationName => "AddForeignKey";
    public string ConstraintName { get; init; } = string.Empty;
    public List<string> SourceColumns { get; init; } = new();
    public string PrincipalTable { get; init; } = string.Empty;
    public List<string> PrincipalColumns { get; init; } = new();
    public string? OnDelete { get; init; }
    public override string ToDisplaySummary() => $"AddForeignKey '{ConstraintName}' '{TableName}'({string.Join(",", SourceColumns)}) ➡️ '{PrincipalTable}'({string.Join(",", PrincipalColumns)})";
}

public record DropForeignKeyOperation : MigrationOperation
{
    public override string OperationName => "DropForeignKey";
    public string ConstraintName { get; init; } = string.Empty;
    public override string ToDisplaySummary() => $"DropForeignKey '{ConstraintName}' from '{TableName}'";
}

public record CreateIndexOperation : MigrationOperation
{
    public override string OperationName => "CreateIndex";
    public string IndexName { get; init; } = string.Empty;
    public List<string> Columns { get; init; } = new();
    public bool IsUnique { get; init; }
    public string? Filter { get; init; }
    public override string ToDisplaySummary() => $"CreateIndex '{(IsUnique ? "UNIQUE " : "")}{IndexName}' on '{TableName}' ({string.Join(", ", Columns)})";
}

public record DropIndexOperation : MigrationOperation
{
    public override string OperationName => "DropIndex";
    public string IndexName { get; init; } = string.Empty;
    public override string ToDisplaySummary() => $"DropIndex '{IndexName}' from '{TableName}'";
}

public record RawSqlOperation : MigrationOperation
{
    public override string OperationName => "Sql";
    public string SqlContent { get; init; } = string.Empty;
    public bool SuppressTransaction { get; init; }
    public override string ToDisplaySummary() => $"Sql (Ham SQL betiği, {SqlContent.Length} karakter)";
}

public record UnknownOperation : MigrationOperation
{
    public override string OperationName => "Unknown";
    public string MethodName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public override string ToDisplaySummary() => $"Unknown: '{MethodName}' ({Reason})";
}
