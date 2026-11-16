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

        // Roslyn CSharpCompilation ve SemanticModel oluştur (Sembol analizi ve tip doğrulaması için)
        var compilation = CSharpCompilation.Create(
            "SchemaFlow.SemanticAnalysis",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        // 2. Up ve Down metotlarını ayrı ayrı analiz et
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var upMethod = methods.FirstOrDefault(m => m.Identifier.Text == "Up");
        var downMethod = methods.FirstOrDefault(m => m.Identifier.Text == "Down");

        if (upMethod != null)
        {
            var upOps = ParseMethodOperations(upMethod, MigrationMethodScope.Up, sourceFilePath, classDeclaration, semanticModel);
            result.UpOperations.AddRange(upOps.Where(o => o is not UnknownOperation));
            result.UnresolvedOperations.AddRange(upOps.OfType<UnknownOperation>());
        }

        if (downMethod != null)
        {
            var downOps = ParseMethodOperations(downMethod, MigrationMethodScope.Down, sourceFilePath, classDeclaration, semanticModel);
            result.DownOperations.AddRange(downOps.Where(o => o is not UnknownOperation));
            result.UnresolvedOperations.AddRange(downOps.OfType<UnknownOperation>());
        }

        // Eğer Up ve Down bulunamadıysa (örneğin sadece migration scripti veya gövdesiz kod)
        if (upMethod == null && downMethod == null)
        {
            var genericOps = ParseMethodOperations(root, MigrationMethodScope.Up, sourceFilePath, classDeclaration, semanticModel);
            result.UpOperations.AddRange(genericOps.Where(o => o is not UnknownOperation));
            result.UnresolvedOperations.AddRange(genericOps.OfType<UnknownOperation>());
        }

        return result;
    }

    private static List<MigrationOperation> ParseMethodOperations(
        SyntaxNode methodNode, 
        MigrationMethodScope scope, 
        string? sourceFilePath, 
        ClassDeclarationSyntax? classDecl,
        SemanticModel? semanticModel)
    {
        var operations = new List<MigrationOperation>();
        var invocations = methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>();

        // MigrationBuilder parametre isimlerini tespit et
        var mbNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "migrationBuilder", "mb" };
        if (methodNode is MethodDeclarationSyntax methodDecl)
        {
            foreach (var param in methodDecl.ParameterList.Parameters)
            {
                var typeStr = param.Type?.ToString();
                if (typeStr != null && typeStr.Contains("MigrationBuilder", StringComparison.OrdinalIgnoreCase))
                {
                    mbNames.Add(param.Identifier.Text);
                }
                else if (methodDecl.ParameterList.Parameters.Count == 1 &&
                         (methodDecl.Identifier.Text == "Up" || methodDecl.Identifier.Text == "Down"))
                {
                    mbNames.Add(param.Identifier.Text);
                }
            }
        }

        foreach (var inv in invocations)
        {
            // Yalnızca doğrudan migrationBuilder çağrılarını ele al, alakasız metotları yoksay
            if (!IsMigrationBuilderCall(inv, mbNames, semanticModel, methodNode, out var receiverName, out var methodName))
                continue;

            var line = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var rawText = inv.ToString();

            try
            {
                var op = ParseSpecificOperation(methodName, inv, scope, line, sourceFilePath, rawText, classDecl);
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

    private static bool IsMigrationBuilderCall(
        InvocationExpressionSyntax inv, 
        HashSet<string> mbNames, 
        SemanticModel? semanticModel,
        SyntaxNode methodScope,
        out string receiverName, 
        out string methodName)
    {
        receiverName = string.Empty;
        methodName = string.Empty;

        if (inv.Expression is MemberAccessExpressionSyntax ma)
        {
            methodName = ma.Name.Identifier.Text;
            var receiverExpr = ma.Expression;

            // Parantez ve cast ifadelerini aç
            while (receiverExpr is ParenthesizedExpressionSyntax paren)
                receiverExpr = paren.Expression;
            while (receiverExpr is CastExpressionSyntax cast)
                receiverExpr = cast.Expression;

            receiverName = receiverExpr.ToString();

            // 1. Semantic Model Denetimi (Sembol Çözümleme)
            if (semanticModel != null)
            {
                var typeInfo = semanticModel.GetTypeInfo(receiverExpr);
                if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                {
                    var typeName = typeInfo.Type.Name;
                    var fullType = typeInfo.Type.ToDisplayString();
                    if (!typeName.Equals("MigrationBuilder", StringComparison.Ordinal) &&
                        !fullType.Equals("Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder", StringComparison.Ordinal))
                    {
                        // Sembol kesin olarak çözüldü ve gerçek MigrationBuilder değil (örn: CustomMigrationBuilder, Service)!
                        return false;
                    }
                }
            }

            // 2. Sentaktik Yerel Değişken Tipi Denetimi (SemanticModel sembolü derleme referansı eksikliğinden IErrorTypeSymbol olsa bile)
            if (receiverExpr is IdentifierNameSyntax id)
            {
                var localDecl = methodScope.DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault(v => v.Identifier.Text == id.Identifier.Text);

                if (localDecl != null)
                {
                    var varDeclParent = localDecl.Parent as VariableDeclarationSyntax;
                    var explicitType = varDeclParent?.Type?.ToString();
                    if (!string.IsNullOrEmpty(explicitType) &&
                        !explicitType.Equals("var", StringComparison.OrdinalIgnoreCase) &&
                        !explicitType.Equals("MigrationBuilder", StringComparison.Ordinal) &&
                        !explicitType.EndsWith(".MigrationBuilder", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (localDecl.Initializer?.Value is ObjectCreationExpressionSyntax objCreation)
                    {
                        var createdType = objCreation.Type.ToString();
                        if (!createdType.Equals("MigrationBuilder", StringComparison.Ordinal) &&
                            !createdType.EndsWith(".MigrationBuilder", StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }
                }

                if (mbNames.Contains(id.Identifier.Text) ||
                    id.Identifier.Text.Equals("migrationBuilder", StringComparison.OrdinalIgnoreCase) ||
                    id.Identifier.Text.Equals("mb", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // this.migrationBuilder erişimi
            if (receiverExpr is MemberAccessExpressionSyntax thisMa && thisMa.Expression is ThisExpressionSyntax)
            {
                if (mbNames.Contains(thisMa.Name.Identifier.Text) ||
                    thisMa.Name.Identifier.Text.Contains("migrationBuilder", StringComparison.OrdinalIgnoreCase) ||
                    thisMa.Name.Identifier.Text.Equals("mb", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
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
        string rawText,
        ClassDeclarationSyntax? classDecl)
    {
        var args = inv.ArgumentList;

        switch (methodName)
        {
            case "CreateTable":
            {
                var tableName = GetStringConstant(args, "name", 0, classDecl);
                var isUnresolved = tableName == null;
                var finalTableName = tableName ?? "UnknownTable";

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

                        var colDef = ParseColumnDefinition(colName, declarator.Expression, classDecl);
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
                            var extractedPks = ExtractLambdaPropertyNames(cInv.ArgumentList, 1, "columns", classDecl);
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
                    TableName = finalTableName,
                    Columns = columns,
                    PrimaryKeyColumns = pkCols,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "DropTable":
            {
                var tableName = GetStringConstant(args, "name", 0, classDecl);
                var isUnresolved = tableName == null;

                return new DropTableOperation
                {
                    TableName = tableName ?? "UnknownTable",
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "RenameTable":
            {
                var name = GetStringConstant(args, "name", 0, classDecl);
                var newName = GetStringConstant(args, "newName", 1, classDecl);
                var schema = GetStringConstant(args, "schema", classDecl: classDecl);
                var newSchema = GetStringConstant(args, "newSchema", classDecl: classDecl);
                var isUnresolved = name == null || newName == null;

                return new RenameTableOperation
                {
                    TableName = name ?? "UnknownTable",
                    NewTableName = newName ?? "UnknownNewName",
                    Schema = schema,
                    NewSchema = newSchema,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "AddColumn":
            {
                var colName = GetStringConstant(args, "name", 0, classDecl);
                var tableName = GetStringConstant(args, "table", 1, classDecl);
                var type = GetStringConstant(args, "type", 2, classDecl) ?? ExtractGenericTypeName(inv) ?? "object";

                var nullableArg = GetArgument(args, "nullable", 3);
                bool? nullable = null;
                var isUnresolved = colName == null || tableName == null;

                if (nullableArg != null)
                {
                    nullable = GetBoolConstant(args, "nullable", 3);
                    if (nullable == null)
                    {
                        // Argüman verilmiş fakat değeri statik olarak çözümlenememiş (dinamik/değişken)
                        isUnresolved = true;
                    }
                }
                else
                {
                    // Belirtilmemişse EF Core varsayılanı nullable: false'tur (generic tip nullable değilse)
                    if (type.EndsWith("?"))
                        nullable = true;
                    else
                        nullable = false;
                }

                var defVal = GetStringConstant(args, "defaultValue", classDecl: classDecl);
                var defValSql = GetStringConstant(args, "defaultValueSql", classDecl: classDecl);

                return new AddColumnOperation
                {
                    TableName = tableName ?? "UnknownTable",
                    ColumnName = colName ?? "UnknownColumn",
                    DataType = type,
                    IsNullable = nullable ?? true,
                    IsNullableKnown = nullable != null,
                    DefaultValue = defVal,
                    DefaultValueSql = defValSql,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "DropColumn":
            {
                var colName = GetStringConstant(args, "name", 0, classDecl);
                var tableName = GetStringConstant(args, "table", 1, classDecl);
                var isUnresolved = colName == null || tableName == null;

                return new DropColumnOperation
                {
                    TableName = tableName ?? "UnknownTable",
                    ColumnName = colName ?? "UnknownColumn",
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "RenameColumn":
            {
                var colName = GetStringConstant(args, "name", 0, classDecl);
                var tableName = GetStringConstant(args, "table", 1, classDecl);
                var newName = GetStringConstant(args, "newName", 2, classDecl);
                var isUnresolved = colName == null || tableName == null || newName == null;

                return new RenameColumnOperation
                {
                    TableName = tableName ?? "UnknownTable",
                    ColumnName = colName ?? "UnknownColumn",
                    NewColumnName = newName ?? "UnknownNewName",
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "AlterColumn":
            {
                var colName = GetStringConstant(args, "name", 0, classDecl);
                var tableName = GetStringConstant(args, "table", 1, classDecl);
                var type = GetStringConstant(args, "type", 2, classDecl) ?? ExtractGenericTypeName(inv);
                var nullable = GetBoolConstant(args, "nullable");
                var defVal = GetStringConstant(args, "defaultValue", classDecl: classDecl) ?? GetStringConstant(args, "defaultValueSql", classDecl: classDecl);
                var oldType = GetStringConstant(args, "oldClrType", classDecl: classDecl) ?? GetStringConstant(args, "oldType", classDecl: classDecl);
                var oldNullable = GetBoolConstant(args, "oldNullable");
                var isUnresolved = colName == null || tableName == null;

                return new AlterColumnOperation
                {
                    TableName = tableName ?? "UnknownTable",
                    ColumnName = colName ?? "UnknownColumn",
                    DataType = type,
                    IsNullable = nullable,
                    OldDataType = oldType,
                    OldIsNullable = oldNullable,
                    DefaultValue = defVal,
                    Scope = scope,
                    LineNumber = line,
                    SourceFile = sourceFilePath,
                    RawSyntax = rawText,
                    Detection = isUnresolved ? DetectionType.Unknown : DetectionType.Confirmed,
                    Confidence = isUnresolved ? AnalysisConfidence.Low : AnalysisConfidence.High
                };
            }

            case "AddPrimaryKey":
            {
                var name = GetStringConstant(args, "name", 0, classDecl) ?? "PK_Unknown";
                var table = GetStringConstant(args, "table", 1, classDecl) ?? "UnknownTable";
                var cols = ExtractLambdaPropertyNames(args, 2, "columns", classDecl);

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
                var name = GetStringConstant(args, "name", 0, classDecl) ?? "PK_Unknown";
                var table = GetStringConstant(args, "table", 1, classDecl) ?? "UnknownTable";

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
                var name = GetStringConstant(args, "name", 0, classDecl) ?? "FK_Unknown";
                var table = GetStringConstant(args, "table", 1, classDecl) ?? "UnknownTable";
                var srcCols = ExtractLambdaPropertyNames(args, 2, "columns", classDecl);
                var targetTable = GetStringConstant(args, "principalTable", 3, classDecl) ?? "UnknownTarget";
                var targetCols = ExtractLambdaPropertyNames(args, 4, "principalColumns", classDecl);
                var onDelete = GetStringConstant(args, "onDelete", classDecl: classDecl);

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
                var name = GetStringConstant(args, "name", 0, classDecl) ?? "FK_Unknown";
                var table = GetStringConstant(args, "table", 1, classDecl) ?? "UnknownTable";

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
                var name = GetStringConstant(args, "name", 0, classDecl) ?? "IX_Unknown";
                var table = GetStringConstant(args, "table", 1, classDecl) ?? "UnknownTable";
                var cols = ExtractLambdaPropertyNames(args, 2, "columns", classDecl);
                var unique = GetBoolConstant(args, "unique") ?? false;
                var filter = GetStringConstant(args, "filter", classDecl: classDecl);

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
                var name = GetStringConstant(args, "name", 0, classDecl) ?? "IX_Unknown";
                var table = GetStringConstant(args, "table", 1, classDecl) ?? "UnknownTable";

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
                var sql = GetStringConstant(args, "sql", 0, classDecl) ?? rawText;
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
                    TableName = GetStringConstant(args, "table", 1, classDecl) ?? string.Empty,
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

    private static TableColumnDefinition? ParseColumnDefinition(string colName, ExpressionSyntax expr, ClassDeclarationSyntax? classDecl)
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
        var sqlType = GetStringConstant(colInv.ArgumentList, "type", classDecl: classDecl);
        var isNullable = GetBoolConstant(colInv.ArgumentList, "nullable") ?? false;
        var defValSql = GetStringConstant(colInv.ArgumentList, "defaultValueSql", classDecl: classDecl);
        var defVal = defValSql ?? GetStringConstant(colInv.ArgumentList, "defaultValue", classDecl: classDecl);

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

    private static string? GetStringConstant(
        ArgumentListSyntax argList, 
        string name, 
        int positional = -1, 
        ClassDeclarationSyntax? classDecl = null)
    {
        var arg = GetArgument(argList, name, positional);
        if (arg == null) return null;

        return ResolveStringExpression(arg.Expression, classDecl);
    }

    private static string? ResolveStringExpression(ExpressionSyntax? expr, ClassDeclarationSyntax? enclosingClass)
    {
        if (expr == null) return null;

        while (expr is ParenthesizedExpressionSyntax paren)
            expr = paren.Expression;

        // 1. Literal string
        if (expr is LiteralExpressionSyntax lit)
        {
            if (lit.IsKind(SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
            if (lit.IsKind(SyntaxKind.NullLiteralExpression))
                return null;
        }

        // 2. nameof(...)
        if (expr is InvocationExpressionSyntax nameofInv &&
            nameofInv.Expression is IdentifierNameSyntax id && 
            id.Identifier.Text == "nameof")
        {
            var targetExpr = nameofInv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            return ResolveNameOf(targetExpr);
        }

        // 3. String concatenation (+)
        if (expr is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.AddExpression))
        {
            var left = ResolveStringExpression(bin.Left, enclosingClass);
            var right = ResolveStringExpression(bin.Right, enclosingClass);
            if (left != null && right != null)
            {
                return left + right;
            }
            return null;
        }

        // 4. Interpolated string ($"...")
        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    sb.Append(text.TextToken.ValueText);
                }
                else if (content is InterpolationSyntax interp)
                {
                    var val = ResolveStringExpression(interp.Expression, enclosingClass);
                    if (val == null) return null;
                    sb.Append(val);
                }
            }
            return sb.ToString();
        }

        // 5. IdentifierName (const alan veya yerel const)
        if (expr is IdentifierNameSyntax ident)
        {
            var constName = ident.Identifier.Text;

            // Enclosing class const fields
            var classDecl = enclosingClass ?? expr.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl != null)
            {
                var field = classDecl.DescendantNodes()
                    .OfType<FieldDeclarationSyntax>()
                    .Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)))
                    .SelectMany(f => f.Declaration.Variables)
                    .FirstOrDefault(v => v.Identifier.Text == constName);

                if (field?.Initializer != null)
                {
                    return ResolveStringExpression(field.Initializer.Value, classDecl);
                }
            }

            // Enclosing method local consts
            var methodDecl = expr.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDecl != null)
            {
                var localConst = methodDecl.DescendantNodes()
                    .OfType<LocalDeclarationStatementSyntax>()
                    .Where(l => l.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)))
                    .SelectMany(l => l.Declaration.Variables)
                    .FirstOrDefault(v => v.Identifier.Text == constName);

                if (localConst?.Initializer != null)
                {
                    return ResolveStringExpression(localConst.Initializer.Value, classDecl);
                }
            }
        }

        return null;
    }

    private static string? ResolveNameOf(ExpressionSyntax? expr)
    {
        if (expr == null) return null;
        while (expr is ParenthesizedExpressionSyntax paren) expr = paren.Expression;

        if (expr is MemberAccessExpressionSyntax ma)
        {
            return ma.Name.Identifier.Text;
        }
        if (expr is IdentifierNameSyntax id)
        {
            return id.Identifier.Text;
        }
        if (expr is GenericNameSyntax gn)
        {
            return gn.Identifier.Text;
        }
        if (expr is QualifiedNameSyntax qn)
        {
            return qn.Right.Identifier.Text;
        }
        if (expr is MemberBindingExpressionSyntax mb)
        {
            return mb.Name.Identifier.Text;
        }

        return expr.ToString();
    }

    private static bool? GetBoolConstant(ArgumentListSyntax argList, string name, int positional = -1)
    {
        var arg = GetArgument(argList, name, positional);
        if (arg == null) return null;

        var expr = arg.Expression;
        while (expr is ParenthesizedExpressionSyntax paren) expr = paren.Expression;

        if (expr is LiteralExpressionSyntax lit)
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

    private static List<string> ExtractLambdaPropertyNames(
        ArgumentListSyntax argList, 
        int positional, 
        string? name = null,
        ClassDeclarationSyntax? classDecl = null)
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
        else if (arg?.Expression is ArrayCreationExpressionSyntax arrayCreation && arrayCreation.Initializer != null)
        {
            foreach (var expr in arrayCreation.Initializer.Expressions)
            {
                var s = ResolveStringExpression(expr, classDecl);
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
        }
        else if (arg?.Expression is ImplicitArrayCreationExpressionSyntax implicitArray && implicitArray.Initializer != null)
        {
            foreach (var expr in implicitArray.Initializer.Expressions)
            {
                var s = ResolveStringExpression(expr, classDecl);
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
        }

        return result;
    }

    #endregion
}
