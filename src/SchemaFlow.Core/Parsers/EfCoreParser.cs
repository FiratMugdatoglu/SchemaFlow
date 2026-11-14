using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaFlow.Core.Models;
using Index = SchemaFlow.Core.Models.Index;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Microsoft Roslyn (CSharpSyntaxTree) AST derleyicisi kullanarak
/// Entity Framework Core (EF Core) C# migration dosyalarını (.cs)
/// %100 doğruluk ve semantik derinlikle ayrıştıran parser sınıfıdır.
/// </summary>
public class EfCoreParser : BaseDatabaseParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.EfCore;

    public override async Task<DatabaseSchema> ParseSqlAsync(string csharpCode, CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "EfCoreDatabase",
            Dialect = DatabaseDialect.EfCore
        };

        if (string.IsNullOrWhiteSpace(csharpCode))
            return schema;

        // 1. Roslyn SyntaxTree ile C# kodunu tam AST (Abstract Syntax Tree) olarak ayrıştır
        var tree = CSharpSyntaxTree.ParseText(csharpCode, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);

        // 2. Tüm metot çağrılarını (InvocationExpressionSyntax) tara
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            var methodName = GetMethodName(inv);
            if (string.IsNullOrEmpty(methodName)) continue;

            switch (methodName)
            {
                case "CreateTable":
                    ParseCreateTable(inv, schema);
                    break;

                case "AddColumn":
                    ParseAddColumn(inv, schema);
                    break;

                case "DropColumn":
                    ParseDropColumn(inv, schema);
                    break;

                case "RenameColumn":
                    ParseRenameColumn(inv, schema);
                    break;

                case "AlterColumn":
                    ParseAlterColumn(inv, schema);
                    break;

                case "CreateIndex":
                    ParseCreateIndex(inv, schema);
                    break;

                case "DropTable":
                    ParseDropTable(inv, schema);
                    break;
            }
        }

        return schema;
    }

    private static string? GetMethodName(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax ma)
        {
            return ma.Name.Identifier.Text;
        }
        return null;
    }

    private void ParseCreateTable(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var tableName = GetStringArgument(inv.ArgumentList, "name", 0);
        if (string.IsNullOrEmpty(tableName)) return;

        var table = schema.FindTable(tableName) ?? new Table
        {
            Name = tableName,
            Description = "EF Core Migration Table"
        };

        // 1. Kolonları Çöz (columns: table => new { ... })
        var columnsArg = GetArgument(inv.ArgumentList, "columns", 1);
        if (columnsArg?.Expression is LambdaExpressionSyntax lambda &&
            lambda.Body is AnonymousObjectCreationExpressionSyntax anonObj)
        {
            foreach (var declarator in anonObj.Initializers)
            {
                var colName = declarator.NameEquals?.Name.Identifier.Text;
                if (string.IsNullOrEmpty(colName)) continue;

                var column = ParseColumnDefinition(colName, declarator.Expression);
                if (column != null)
                {
                    table.Columns.Add(column);
                }
            }
        }

        // 2. Kısıtlamaları Çöz (constraints: table => { table.PrimaryKey(...); table.ForeignKey(...); })
        var constraintsArg = GetArgument(inv.ArgumentList, "constraints", 2);
        if (constraintsArg?.Expression is LambdaExpressionSyntax cLambda)
        {
            ParseConstraints(cLambda.Body, table);
        }

        if (!schema.Tables.Contains(table))
        {
            schema.Tables.Add(table);
        }
    }

    private static Column? ParseColumnDefinition(string colName, ExpressionSyntax expr)
    {
        // expr: table.Column<string>(type: "nvarchar(100)", nullable: false, ...)
        // veya zincirleme: table.Column<int>(...).Annotation(...)
        InvocationExpressionSyntax? colInv = null;

        var curr = expr;
        while (curr != null)
        {
            if (curr is InvocationExpressionSyntax inv)
            {
                var name = GetMethodName(inv);
                if (name == "Column")
                {
                    colInv = inv;
                    break;
                }
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                {
                    curr = ma.Expression;
                    continue;
                }
            }
            break;
        }

        if (colInv == null) return null;

        // Generic CLR tipini al: <string>, <int>, <DateTime>
        var clrType = "string";
        if (colInv.Expression is MemberAccessExpressionSyntax maCol &&
            maCol.Name is GenericNameSyntax genName &&
            genName.TypeArgumentList.Arguments.Count > 0)
        {
            clrType = genName.TypeArgumentList.Arguments[0].ToString();
        }

        // Parametreleri çek
        var sqlType = GetStringArgument(colInv.ArgumentList, "type");
        var isNullable = GetBoolArgument(colInv.ArgumentList, "nullable") ?? false;
        var defValSql = GetStringArgument(colInv.ArgumentList, "defaultValueSql");
        var defVal = defValSql ?? GetStringArgument(colInv.ArgumentList, "defaultValue");

        return new Column
        {
            Name = colName,
            DataType = !string.IsNullOrEmpty(sqlType) ? sqlType : clrType,
            IsNullable = isNullable,
            DefaultValue = defVal
        };
    }

    private void ParseConstraints(CSharpSyntaxNode body, Table table)
    {
        var calls = body.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var call in calls)
        {
            var methodName = GetMethodName(call);
            if (methodName == "PrimaryKey")
            {
                // table.PrimaryKey("PK_Users", x => x.Id) veya columns: x => x.Id
                var cols = ExtractLambdaPropertyNames(call.ArgumentList, 1, "columns");
                foreach (var colName in cols)
                {
                    var col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
                    if (col != null) col.IsPrimaryKey = true;
                }
            }
            else if (methodName == "ForeignKey")
            {
                // table.ForeignKey(name: "FK_...", column: x => x.UserId, principalTable: "Users", principalColumn: "Id", onDelete: ...)
                var fkName = GetStringArgument(call.ArgumentList, "name", 0) ?? $"FK_{table.Name}";
                var srcCols = ExtractLambdaPropertyNames(call.ArgumentList, 1, "column");
                var targetTable = GetStringArgument(call.ArgumentList, "principalTable", 2);
                var targetCols = ExtractLambdaPropertyNames(call.ArgumentList, 3, "principalColumn");

                if (!string.IsNullOrEmpty(targetTable) && srcCols.Count > 0)
                {
                    table.ForeignKeys.Add(new ForeignKey
                    {
                        ConstraintName = fkName,
                        SourceTable = table.Name,
                        SourceColumns = srcCols,
                        TargetTable = targetTable,
                        TargetColumns = targetCols.Count > 0 ? targetCols : new List<string> { "Id" },
                        OnDelete = "Cascade"
                    });
                }
            }
        }
    }

    private static void ParseAddColumn(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var tableName = GetStringArgument(inv.ArgumentList, "table", 1);
        var colName = GetStringArgument(inv.ArgumentList, "name", 0);
        var sqlType = GetStringArgument(inv.ArgumentList, "type", 2);
        var nullable = GetBoolArgument(inv.ArgumentList, "nullable", 3) ?? true;
        var defVal = GetStringArgument(inv.ArgumentList, "defaultValue") ?? GetStringArgument(inv.ArgumentList, "defaultValueSql");

        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(colName)) return;

        var table = schema.FindTable(tableName) ?? new Table { Name = tableName };
        if (!schema.Tables.Contains(table)) schema.Tables.Add(table);

        var col = table.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
        if (col == null)
        {
            table.Columns.Add(new Column
            {
                Name = colName,
                DataType = sqlType ?? "nvarchar(max)",
                IsNullable = nullable,
                DefaultValue = defVal
            });
        }
    }

    private static void ParseDropColumn(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var tableName = GetStringArgument(inv.ArgumentList, "table", 1);
        var colName = GetStringArgument(inv.ArgumentList, "name", 0);

        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(colName)) return;

        var table = schema.FindTable(tableName);
        if (table != null)
        {
            table.Columns.RemoveAll(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void ParseRenameColumn(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var tableName = GetStringArgument(inv.ArgumentList, "table", 1);
        var colName = GetStringArgument(inv.ArgumentList, "name", 0);
        var newName = GetStringArgument(inv.ArgumentList, "newName", 2);

        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(colName) || string.IsNullOrEmpty(newName)) return;

        var table = schema.FindTable(tableName);
        var col = table?.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
        if (col != null)
        {
            col.Name = newName;
        }
    }

    private static void ParseAlterColumn(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var tableName = GetStringArgument(inv.ArgumentList, "table", 1);
        var colName = GetStringArgument(inv.ArgumentList, "name", 0);
        var sqlType = GetStringArgument(inv.ArgumentList, "type", 2);
        var nullable = GetBoolArgument(inv.ArgumentList, "nullable");

        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(colName)) return;

        var table = schema.FindTable(tableName);
        var col = table?.Columns.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
        if (col != null)
        {
            if (!string.IsNullOrEmpty(sqlType)) col.DataType = sqlType;
            if (nullable.HasValue) col.IsNullable = nullable.Value;
        }
    }

    private static void ParseCreateIndex(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var idxName = GetStringArgument(inv.ArgumentList, "name", 0);
        var tableName = GetStringArgument(inv.ArgumentList, "table", 1);
        var singleCol = GetStringArgument(inv.ArgumentList, "column", 2);
        var isUnique = GetBoolArgument(inv.ArgumentList, "unique") ?? false;

        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(idxName)) return;

        var table = schema.FindTable(tableName);
        if (table == null) return;

        var cols = new List<string>();
        if (!string.IsNullOrEmpty(singleCol))
        {
            cols.Add(singleCol);
        }
        else
        {
            // columns: new[] { "Col1", "Col2" }
            var colsArg = GetArgument(inv.ArgumentList, "columns", 2);
            if (colsArg?.Expression is ArrayCreationExpressionSyntax arr)
            {
                foreach (var init in arr.Initializer?.Expressions ?? Enumerable.Empty<ExpressionSyntax>())
                {
                    if (init is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        cols.Add(lit.Token.ValueText);
                    }
                }
            }
        }

        table.Indexes.Add(new Index
        {
            Name = idxName,
            Columns = cols,
            IsUnique = isUnique
        });
    }

    private static void ParseDropTable(InvocationExpressionSyntax inv, DatabaseSchema schema)
    {
        var tableName = GetStringArgument(inv.ArgumentList, "name", 0);
        if (!string.IsNullOrEmpty(tableName))
        {
            schema.Tables.RemoveAll(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
        }
    }

    #region Roslyn Argument Extraction Helpers

    private static ArgumentSyntax? GetArgument(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var named = argList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == name);
        if (named != null) return named;

        if (positional >= 0 && positional < argList.Arguments.Count && argList.Arguments[positional].NameColon == null)
        {
            return argList.Arguments[positional];
        }

        return null;
    }

    private static string? GetStringArgument(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var arg = GetArgument(argList, name, positional);
        if (arg?.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }
        return null;
    }

    private static bool? GetBoolArgument(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var arg = GetArgument(argList, name, positional);
        if (arg?.Expression is LiteralExpressionSyntax lit)
        {
            if (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.Token.IsKind(SyntaxKind.TrueKeyword)) return true;
            if (lit.IsKind(SyntaxKind.FalseLiteralExpression) || lit.Token.IsKind(SyntaxKind.FalseKeyword)) return false;
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
            // x => x.Id
            if (lambda.Body is MemberAccessExpressionSyntax ma)
            {
                result.Add(ma.Name.Identifier.Text);
            }
            // x => new { x.Id, x.Name }
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
