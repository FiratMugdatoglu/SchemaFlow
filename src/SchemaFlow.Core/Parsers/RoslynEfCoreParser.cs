using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaFlow.Core.Models;
using SchemaFlow.Core.Models.Operations;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Microsoft Roslyn CSharpSyntaxTree derleyicisini kullanarak EF Core Migration sınıflarını
/// tam semantik ve sözdizimsel derinlikle normalize edilmiş MigrationOperation nesnelerine ayrıştırır.
/// </summary>
public class RoslynEfCoreParser
{
    public class ParseResult
    {
        public string MigrationClassName { get; set; } = string.Empty;
        public List<MigrationOperation> UpOperations { get; set; } = new();
        public List<MigrationOperation> DownOperations { get; set; } = new();
        public List<UnknownOperation> UnresolvedOperations { get; set; } = new();

        public IEnumerable<MigrationOperation> AllOperations => UpOperations.Concat(DownOperations).Concat(UnresolvedOperations);
    }

    public ParseResult ParseMigration(string sourceCode, string? sourceFilePath = null)
    {
        var result = new ParseResult();
        if (string.IsNullOrWhiteSpace(sourceCode))
            return result;

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = syntaxTree.GetRoot();

        // 1. Migration sınıfını bul
        var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        result.MigrationClassName = classDeclaration?.Identifier.Text ?? Path.GetFileNameWithoutExtension(sourceFilePath) ?? "UnknownMigration";

        // 2. Up ve Down metotlarını ayrı ayrı analiz et
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var upMethod = methods.FirstOrDefault(m => m.Identifier.Text == "Up");
        var downMethod = methods.FirstOrDefault(m => m.Identifier.Text == "Down");

        if (upMethod != null)
        {
            var upOps = ParseMethodOperations(upMethod, MigrationMethodScope.Up, sourceFilePath);
            result.UpOperations.AddRange(upOps.Where(o => o is not UnknownOperation));
            result.UnresolvedOperations.AddRange(upOps.OfType<UnknownOperation>());
        }

        if (downMethod != null)
        {
            var downOps = ParseMethodOperations(downMethod, MigrationMethodScope.Down, sourceFilePath);
            result.DownOperations.AddRange(downOps.Where(o => o is not UnknownOperation));
            result.UnresolvedOperations.AddRange(downOps.OfType<UnknownOperation>());
        }

        // Eğer Up ve Down bulunamadıysa (örneğin sadece migration scripti veya gövdesiz kod)
        if (upMethod == null && downMethod == null)
        {
            var genericOps = ParseMethodOperations(root, MigrationMethodScope.Up, sourceFilePath);
            result.UpOperations.AddRange(genericOps.Where(o => o is not UnknownOperation));
            result.UnresolvedOperations.AddRange(genericOps.OfType<UnknownOperation>());
        }

        return result;
    }

    private static List<MigrationOperation> ParseMethodOperations(SyntaxNode methodNode, MigrationMethodScope scope, string? sourceFilePath)
    {
        var operations = new List<MigrationOperation>();
        var invocations = methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            // Yalnızca doğrudan migrationBuilder çağrılarını veya bilinen işlemleri ele al
            if (!IsMigrationBuilderCall(inv, out var receiverName, out var methodName))
                continue;

            var line = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var rawText = inv.ToString();

            try
            {
                var op = ParseSpecificOperation(methodName, inv, scope, line, sourceFilePath, rawText);
                if (op != null)
                {
                    operations.Add(op);
                }
            }
            catch (Exception ex)
            {
                operations.Add(new UnknownOperation
                {
                    MethodName = methodName,
                    TableName = string.Empty,
                    Reason = $"Ayrıştırma hatası: {ex.Message}",
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = DetectionType.Unknown,
                    Confidence = AnalysisConfidence.Low
                });
            }
        }

        return operations;
    }

    private static bool IsMigrationBuilderCall(InvocationExpressionSyntax inv, out string receiverName, out string methodName)
    {
        receiverName = string.Empty;
        methodName = string.Empty;

        if (inv.Expression is MemberAccessExpressionSyntax ma)
        {
            methodName = ma.Name.Identifier.Text;
            receiverName = ma.Expression.ToString();

            // migrationBuilder, mb veya zincirleme çağrılar
            if (receiverName.Contains("migrationBuilder", StringComparison.OrdinalIgnoreCase) ||
                receiverName.Equals("mb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Standart EF Core metot isimleri
            var efMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CreateTable", "DropTable", "RenameTable",
                "AddColumn", "DropColumn", "RenameColumn", "AlterColumn",
                "CreateIndex", "DropIndex",
                "AddForeignKey", "DropForeignKey",
                "AddPrimaryKey", "DropPrimaryKey",
                "Sql"
            };

            if (efMethods.Contains(methodName))
            {
                return true;
            }
        }

        return false;
    }

    private static MigrationOperation? ParseSpecificOperation(
        string methodName,
        InvocationExpressionSyntax inv,
        MigrationMethodScope scope,
        int line,
        string? sourceFilePath,
        string rawText)
    {
        var args = inv.ArgumentList;

        switch (methodName)
        {
            case "CreateTable":
            {
                var tableName = GetStringConstant(args, "name", 0) ?? "UnknownTable";
                var columns = new List<TableColumnDefinition>();
                var pkCols = new List<string>();

                // columns: table => new { ... }
                var columnsArg = GetArgument(args, "columns", 1);
                if (columnsArg?.Expression is LambdaExpressionSyntax lambda &&
                    lambda.Body is AnonymousObjectCreationExpressionSyntax anon)
                {
                    foreach (var declarator in anon.Initializers)
                    {
                        var colName = declarator.NameEquals?.Name.Identifier.Text;
                        if (string.IsNullOrEmpty(colName)) continue;

                        var colDef = ParseColumnDefinition(colName, declarator.Expression);
                        if (colDef != null) columns.Add(colDef);
                    }
                }

                // constraints: table => { table.PrimaryKey(...); }
                var constraintsArg = GetArgument(args, "constraints", 2);
                if (constraintsArg?.Expression is LambdaExpressionSyntax cLambda)
                {
                    var cInvocations = cLambda.Body.DescendantNodes().OfType<InvocationExpressionSyntax>();
                    foreach (var cInv in cInvocations)
                    {
                        if (cInv.Expression is MemberAccessExpressionSyntax cMa && cMa.Name.Identifier.Text == "PrimaryKey")
                        {
                            var extractedPks = ExtractLambdaPropertyNames(cInv.ArgumentList, 1, "columns");
                            pkCols.AddRange(extractedPks);
                        }
                    }
                }

                // PK bayrağını sütunlara yansıt
                for (var i = 0; i < columns.Count; i++)
                {
                    if (pkCols.Contains(columns[i].Name, StringComparer.OrdinalIgnoreCase))
                    {
                        columns[i] = columns[i] with { IsPrimaryKey = true };
                    }
                }

                return new CreateTableOperation
                {
                    TableName = tableName,
                    Columns = columns,
                    PrimaryKeyColumns = pkCols,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "DropTable":
            {
                var tableName = GetStringConstant(args, "name", 0) ?? "UnknownTable";
                return new DropTableOperation
                {
                    TableName = tableName,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "RenameTable":
            {
                var name = GetStringConstant(args, "name", 0) ?? "UnknownTable";
                var newName = GetStringConstant(args, "newName", 1) ?? "UnknownNewName";
                var schema = GetStringConstant(args, "schema");
                var newSchema = GetStringConstant(args, "newSchema");

                return new RenameTableOperation
                {
                    TableName = name,
                    NewTableName = newName,
                    Schema = schema,
                    NewSchema = newSchema,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "AddColumn":
            {
                var colName = GetStringConstant(args, "name", 0) ?? "UnknownColumn";
                var tableName = GetStringConstant(args, "table", 1) ?? "UnknownTable";
                var type = GetStringConstant(args, "type", 2) ?? ExtractGenericTypeName(inv) ?? "object";
                var nullable = GetBoolConstant(args, "nullable", 3) ?? true;
                var defVal = GetStringConstant(args, "defaultValue");
                var defValSql = GetStringConstant(args, "defaultValueSql");

                return new AddColumnOperation
                {
                    TableName = tableName,
                    ColumnName = colName,
                    DataType = type,
                    IsNullable = nullable,
                    DefaultValue = defVal,
                    DefaultValueSql = defValSql,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "DropColumn":
            {
                var colName = GetStringConstant(args, "name", 0) ?? "UnknownColumn";
                var tableName = GetStringConstant(args, "table", 1) ?? "UnknownTable";

                return new DropColumnOperation
                {
                    TableName = tableName,
                    ColumnName = colName,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "RenameColumn":
            {
                var colName = GetStringConstant(args, "name", 0) ?? "UnknownColumn";
                var tableName = GetStringConstant(args, "table", 1) ?? "UnknownTable";
                var newName = GetStringConstant(args, "newName", 2) ?? "UnknownNewName";

                return new RenameColumnOperation
                {
                    TableName = tableName,
                    ColumnName = colName,
                    NewColumnName = newName,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "AlterColumn":
            {
                var colName = GetStringConstant(args, "name", 0) ?? "UnknownColumn";
                var tableName = GetStringConstant(args, "table", 1) ?? "UnknownTable";
                var type = GetStringConstant(args, "type", 2) ?? ExtractGenericTypeName(inv);
                var nullable = GetBoolConstant(args, "nullable");
                var defVal = GetStringConstant(args, "defaultValue") ?? GetStringConstant(args, "defaultValueSql");
                var oldType = GetStringConstant(args, "oldClrType") ?? GetStringConstant(args, "oldType");
                var oldNullable = GetBoolConstant(args, "oldNullable");

                return new AlterColumnOperation
                {
                    TableName = tableName,
                    ColumnName = colName,
                    DataType = type,
                    IsNullable = nullable,
                    OldDataType = oldType,
                    OldIsNullable = oldNullable,
                    DefaultValue = defVal,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "AddPrimaryKey":
            {
                var name = GetStringConstant(args, "name", 0) ?? "PK_Unknown";
                var table = GetStringConstant(args, "table", 1) ?? "UnknownTable";
                var cols = ExtractLambdaPropertyNames(args, 2, "columns");

                return new AddPrimaryKeyOperation
                {
                    ConstraintName = name,
                    TableName = table,
                    Columns = cols,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "DropPrimaryKey":
            {
                var name = GetStringConstant(args, "name", 0) ?? "PK_Unknown";
                var table = GetStringConstant(args, "table", 1) ?? "UnknownTable";

                return new DropPrimaryKeyOperation
                {
                    ConstraintName = name,
                    TableName = table,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "AddForeignKey":
            {
                var name = GetStringConstant(args, "name", 0) ?? "FK_Unknown";
                var table = GetStringConstant(args, "table", 1) ?? "UnknownTable";
                var srcCols = ExtractLambdaPropertyNames(args, 2, "columns");
                var targetTable = GetStringConstant(args, "principalTable", 3) ?? "UnknownTarget";
                var targetCols = ExtractLambdaPropertyNames(args, 4, "principalColumns");
                var onDelete = GetStringConstant(args, "onDelete");

                return new AddForeignKeyOperation
                {
                    ConstraintName = name,
                    TableName = table,
                    SourceColumns = srcCols,
                    PrincipalTable = targetTable,
                    PrincipalColumns = targetCols,
                    OnDelete = onDelete,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "DropForeignKey":
            {
                var name = GetStringConstant(args, "name", 0) ?? "FK_Unknown";
                var table = GetStringConstant(args, "table", 1) ?? "UnknownTable";

                return new DropForeignKeyOperation
                {
                    ConstraintName = name,
                    TableName = table,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "CreateIndex":
            {
                var name = GetStringConstant(args, "name", 0) ?? "IX_Unknown";
                var table = GetStringConstant(args, "table", 1) ?? "UnknownTable";
                var cols = ExtractLambdaPropertyNames(args, 2, "columns");
                var unique = GetBoolConstant(args, "unique") ?? false;
                var filter = GetStringConstant(args, "filter");

                return new CreateIndexOperation
                {
                    IndexName = name,
                    TableName = table,
                    Columns = cols,
                    IsUnique = unique,
                    Filter = filter,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "DropIndex":
            {
                var name = GetStringConstant(args, "name", 0) ?? "IX_Unknown";
                var table = GetStringConstant(args, "table", 1) ?? "UnknownTable";

                return new DropIndexOperation
                {
                    IndexName = name,
                    TableName = table,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            case "Sql":
            {
                var sql = GetStringConstant(args, "sql", 0) ?? rawText;
                var suppress = GetBoolConstant(args, "suppressTransaction") ?? false;

                return new RawSqlOperation
                {
                    SqlContent = sql,
                    SuppressTransaction = suppress,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText
                };
            }

            default:
            {
                return new UnknownOperation
                {
                    MethodName = methodName,
                    TableName = GetStringConstant(args, "table", 1) ?? string.Empty,
                    Reason = $"Desteklenmeyen veya özel migration çağrısı: '{methodName}'",
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = DetectionType.Unknown,
                    Confidence = AnalysisConfidence.Medium
                };
            }
        }
    }

    private static TableColumnDefinition? ParseColumnDefinition(string colName, ExpressionSyntax expr)
    {
        InvocationExpressionSyntax? colInv = null;
        var curr = expr;
        while (curr != null)
        {
            if (curr is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Column")
                {
                    colInv = inv;
                    break;
                }
                if (inv.Expression is MemberAccessExpressionSyntax parentMa)
                {
                    curr = parentMa.Expression;
                    continue;
                }
            }
            break;
        }

        if (colInv == null) return null;

        var clrType = ExtractGenericTypeName(colInv) ?? "string";
        var sqlType = GetStringConstant(colInv.ArgumentList, "type");
        var isNullable = GetBoolConstant(colInv.ArgumentList, "nullable") ?? false;
        var defValSql = GetStringConstant(colInv.ArgumentList, "defaultValueSql");
        var defVal = defValSql ?? GetStringConstant(colInv.ArgumentList, "defaultValue");

        return new TableColumnDefinition(
            Name: colName,
            DataType: !string.IsNullOrEmpty(sqlType) ? sqlType : clrType,
            IsNullable: isNullable,
            DefaultValue: defVal
        );
    }

    #region Argument & Syntax Helpers

    private static ArgumentSyntax? GetArgument(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var named = argList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == name);
        if (named != null) return named;

        if (positional >= 0 && positional < argList.Arguments.Count)
        {
            var candidate = argList.Arguments[positional];
            if (candidate.NameColon == null) return candidate;
        }

        return null;
    }

    private static string? GetStringConstant(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var arg = GetArgument(argList, name, positional);
        if (arg?.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        // Sabit ifade veya nameof(x)
        if (arg?.Expression is InvocationExpressionSyntax nameofInv &&
            nameofInv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof")
        {
            return nameofInv.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString();
        }

        return null;
    }

    private static bool? GetBoolConstant(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var arg = GetArgument(argList, name, positional);
        if (arg?.Expression is LiteralExpressionSyntax lit)
        {
            if (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.Token.IsKind(SyntaxKind.TrueKeyword)) return true;
            if (lit.IsKind(SyntaxKind.FalseLiteralExpression) || lit.Token.IsKind(SyntaxKind.FalseKeyword)) return false;
        }
        return null;
    }

    private static string? ExtractGenericTypeName(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax ma &&
            ma.Name is GenericNameSyntax genName &&
            genName.TypeArgumentList.Arguments.Count > 0)
        {
            return genName.TypeArgumentList.Arguments[0].ToString();
        }
        return null;
    }

    private static List<string> ExtractLambdaPropertyNames(ArgumentListSyntax argList, int positional, string? name = null)
    {
        var result = new List<string>();
        ArgumentSyntax? arg = null;
        if (!string.IsNullOrEmpty(name))
        {
            arg = argList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == name);
        }
        if (arg == null && positional >= 0 && positional < argList.Arguments.Count)
        {
            arg = argList.Arguments[positional];
        }
        if (arg == null)
        {
            arg = argList.Arguments.FirstOrDefault(a => a.Expression is LambdaExpressionSyntax);
        }

        if (arg?.Expression is LambdaExpressionSyntax lambda)
        {
            if (lambda.Body is MemberAccessExpressionSyntax ma)
            {
                result.Add(ma.Name.Identifier.Text);
            }
            else if (lambda.Body is AnonymousObjectCreationExpressionSyntax anon)
            {
                foreach (var init in anon.Initializers)
                {
                    if (init.Expression is MemberAccessExpressionSyntax m)
                    {
                        result.Add(m.Name.Identifier.Text);
                    }
                    else if (init.NameEquals != null)
                    {
                        result.Add(init.NameEquals.Name.Identifier.Text);
                    }
                }
            }
        }
        else if (arg?.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            result.Add(lit.Token.ValueText);
        }

        return result;
    }

    #endregion
}
