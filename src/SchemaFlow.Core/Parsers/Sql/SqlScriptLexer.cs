using System.Text;
using System.Text.RegularExpressions;

namespace SchemaFlow.Core.Parsers.Sql;

public enum SqlCommandKind
{
    Update,
    Delete,
    Truncate,
    Drop,
    Alter,
    Insert,
    Select,
    Unknown
}

public class SqlStatementInfo
{
    public string RawSql { get; init; } = string.Empty;
    public string CleanSql { get; init; } = string.Empty;
    public SqlCommandKind CommandKind { get; init; } = SqlCommandKind.Unknown;
    public string? TargetTable { get; init; }
    public bool IsDestructive { get; init; }
    public string? DestructiveReason { get; init; }
    public bool HasWhereClause { get; init; }
    public string? WhereCondition { get; init; }

    public bool HasAssignment(string targetCol, string sourceCol)
    {
        if (CommandKind != SqlCommandKind.Update)
            return false;

        var colAddPattern = $@"(?:[\[""`]?\w+[\[""`]?\.)?[\[""`]?{Regex.Escape(targetCol)}[\]""`]?";
        var colDropPattern = $@"(?:[\[""`]?\w+[\[""`]?\.)?[\[""`]?{Regex.Escape(sourceCol)}[\]""`]?";
        var assignPattern = $@"{colAddPattern}\s*=\s*{colDropPattern}";

        return Regex.IsMatch(CleanSql, assignPattern, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// SQL betiklerini string literal'ları ('...'), escape karakterlerini ('') ve
/// yorum bloklarını (-- ve /* */) koruyarak güvenli biçimde ifadelere (statements) ayıran durum makinesi (Lexer).
/// </summary>
public static class SqlScriptLexer
{
    private enum LexerState
    {
        Normal,
        InSingleQuoteString,
        InBracketIdentifier,
        InDoubleQuoteIdentifier,
        InBacktickIdentifier,
        InSingleLineComment,
        InMultiLineComment
    }

    /// <summary>
    /// Çoklu veya tekli SQL komut bloğunu ifadelere ayırır ve yorumları temizlenmiş hallerini sınıflandırır.
    /// </summary>
    public static List<SqlStatementInfo> SplitAndAnalyzeStatements(string? sqlText)
    {
        var results = new List<SqlStatementInfo>();
        if (string.IsNullOrWhiteSpace(sqlText))
            return results;

        var statements = SplitStatements(sqlText);

        foreach (var rawStatement in statements)
        {
            var trimmed = rawStatement.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var clean = StripComments(trimmed);
            if (string.IsNullOrWhiteSpace(clean))
            {
                // Yalnızca yorum satırlarından oluşan bir ifade
                results.Add(new SqlStatementInfo
                {
                    RawSql = trimmed,
                    CleanSql = string.Empty,
                    CommandKind = SqlCommandKind.Select, // Pasif / zararsız yorum
                    IsDestructive = false
                });
                continue;
            }

            var statementInfo = ClassifyStatement(trimmed, clean);
            results.Add(statementInfo);
        }

        return results;
    }

    /// <summary>
    /// SQL metnini noktalı virgüllere (;) göre ifadelere böler.
    /// String sabitleri ('...'), identifier'lar ([...]) ve yorumlar içindeki noktalı virgülleri yok sayar.
    /// </summary>
    public static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var currentStatement = new StringBuilder();
        var state = LexerState.Normal;
        var length = sql.Length;

        for (var i = 0; i < length; i++)
        {
            var c = sql[i];
            var next = i + 1 < length ? sql[i + 1] : '\0';

            switch (state)
            {
                case LexerState.Normal:
                    if (c == '-' && next == '-')
                    {
                        state = LexerState.InSingleLineComment;
                        currentStatement.Append(c);
                        currentStatement.Append(next);
                        i++; // skip next
                    }
                    else if (c == '/' && next == '*')
                    {
                        state = LexerState.InMultiLineComment;
                        currentStatement.Append(c);
                        currentStatement.Append(next);
                        i++; // skip next
                    }
                    else if (c == '\'')
                    {
                        state = LexerState.InSingleQuoteString;
                        currentStatement.Append(c);
                    }
                    else if (c == '[')
                    {
                        state = LexerState.InBracketIdentifier;
                        currentStatement.Append(c);
                    }
                    else if (c == '"')
                    {
                        state = LexerState.InDoubleQuoteIdentifier;
                        currentStatement.Append(c);
                    }
                    else if (c == '`')
                    {
                        state = LexerState.InBacktickIdentifier;
                        currentStatement.Append(c);
                    }
                    else if (c == ';')
                    {
                        // İfade sonu
                        var stmt = currentStatement.ToString().Trim();
                        if (!string.IsNullOrEmpty(stmt))
                        {
                            statements.Add(stmt);
                        }
                        currentStatement.Clear();
                    }
                    else
                    {
                        currentStatement.Append(c);
                    }
                    break;

                case LexerState.InSingleQuoteString:
                    currentStatement.Append(c);
                    if (c == '\'')
                    {
                        if (next == '\'')
                        {
                            // '' Kaçış karakteri (Escaped quote)
                            currentStatement.Append(next);
                            i++;
                        }
                        else
                        {
                            state = LexerState.Normal;
                        }
                    }
                    break;

                case LexerState.InBracketIdentifier:
                    currentStatement.Append(c);
                    if (c == ']')
                    {
                        state = LexerState.Normal;
                    }
                    break;

                case LexerState.InDoubleQuoteIdentifier:
                    currentStatement.Append(c);
                    if (c == '"')
                    {
                        state = LexerState.Normal;
                    }
                    break;

                case LexerState.InBacktickIdentifier:
                    currentStatement.Append(c);
                    if (c == '`')
                    {
                        state = LexerState.Normal;
                    }
                    break;

                case LexerState.InSingleLineComment:
                    currentStatement.Append(c);
                    if (c == '\n' || c == '\r')
                    {
                        state = LexerState.Normal;
                    }
                    break;

                case LexerState.InMultiLineComment:
                    currentStatement.Append(c);
                    if (c == '*' && next == '/')
                    {
                        currentStatement.Append(next);
                        i++;
                        state = LexerState.Normal;
                    }
                    break;
            }
        }

        var remaining = currentStatement.ToString().Trim();
        if (!string.IsNullOrEmpty(remaining))
        {
            statements.Add(remaining);
        }

        return statements;
    }

    /// <summary>
    /// String literal'ları koruyarak yalnızca gerçek SQL yorumlarını (-- ve /* */) temizler.
    /// </summary>
    public static string StripComments(string sql)
    {
        var sb = new StringBuilder();
        var state = LexerState.Normal;
        var length = sql.Length;

        for (var i = 0; i < length; i++)
        {
            var c = sql[i];
            var next = i + 1 < length ? sql[i + 1] : '\0';

            switch (state)
            {
                case LexerState.Normal:
                    if (c == '-' && next == '-')
                    {
                        state = LexerState.InSingleLineComment;
                        i++; // skip next
                    }
                    else if (c == '/' && next == '*')
                    {
                        state = LexerState.InMultiLineComment;
                        i++; // skip next
                    }
                    else if (c == '\'')
                    {
                        state = LexerState.InSingleQuoteString;
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;

                case LexerState.InSingleQuoteString:
                    sb.Append(c);
                    if (c == '\'')
                    {
                        if (next == '\'')
                        {
                            sb.Append(next);
                            i++;
                        }
                        else
                        {
                            state = LexerState.Normal;
                        }
                    }
                    break;

                case LexerState.InSingleLineComment:
                    if (c == '\n' || c == '\r')
                    {
                        sb.Append(c);
                        state = LexerState.Normal;
                    }
                    break;

                case LexerState.InMultiLineComment:
                    if (c == '*' && next == '/')
                    {
                        i++;
                        state = LexerState.Normal;
                    }
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    private static SqlStatementInfo ClassifyStatement(string raw, string clean)
    {
        var firstWordMatch = Regex.Match(clean, @"^\s*([A-Za-z]+)", RegexOptions.Singleline);
        var firstWord = firstWordMatch.Success ? firstWordMatch.Groups[1].Value.ToUpperInvariant() : string.Empty;

        var commandKind = SqlCommandKind.Unknown;
        string? targetTable = null;
        var isDestructive = false;
        string? destructiveReason = null;
        var hasWhere = false;
        string? whereClause = null;

        var whereMatch = Regex.Match(clean, @"WHERE\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (whereMatch.Success)
        {
            hasWhere = true;
            whereClause = whereMatch.Groups[1].Value.Trim();
        }

        switch (firstWord)
        {
            case "TRUNCATE":
            {
                commandKind = SqlCommandKind.Truncate;
                isDestructive = true;
                targetTable = ExtractTargetTable(clean, @"TRUNCATE\s+TABLE\s+([^\s;,\(\)]+)");
                destructiveReason = $"'{targetTable ?? "Bilinmeyen"}' tablosundaki tüm satırları geri dönüşsüz sıfırlayan TRUNCATE TABLE işlemi.";
                break;
            }

            case "DROP":
            {
                commandKind = SqlCommandKind.Drop;
                isDestructive = true;
                if (Regex.IsMatch(clean, @"DROP\s+TABLE", RegexOptions.IgnoreCase))
                {
                    targetTable = ExtractTargetTable(clean, @"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?([^\s;,\(\)]+)");
                    destructiveReason = $"'{targetTable ?? "Bilinmeyen"}' tablosunu tamamen kaldıran DROP TABLE işlemi.";
                }
                else if (Regex.IsMatch(clean, @"DROP\s+DATABASE", RegexOptions.IgnoreCase))
                {
                    destructiveReason = "Veritabanını tamamen silen DROP DATABASE işlemi!";
                }
                else
                {
                    destructiveReason = "Veritabanı nesnesini silen DROP işlemi.";
                }
                break;
            }

            case "DELETE":
            {
                commandKind = SqlCommandKind.Delete;
                targetTable = ExtractTargetTable(clean, @"DELETE\s+(?:FROM\s+)?([^\s;,\(\)]+)");
                if (!hasWhere)
                {
                    isDestructive = true;
                    destructiveReason = $"'{targetTable ?? "Bilinmeyen"}' tablosundaki tüm kayıtları WHERE koşulu olmadan silen DELETE işlemi!";
                }
                else
                {
                    isDestructive = true;
                    destructiveReason = $"'{targetTable ?? "Bilinmeyen"}' tablosunda koşullu veri silme (DELETE) işlemi.";
                }
                break;
            }

            case "ALTER":
            {
                commandKind = SqlCommandKind.Alter;
                targetTable = ExtractTargetTable(clean, @"ALTER\s+TABLE\s+([^\s;,\(\)]+)");
                if (Regex.IsMatch(clean, @"DROP\s+COLUMN", RegexOptions.IgnoreCase))
                {
                    isDestructive = true;
                    destructiveReason = $"'{targetTable ?? "Bilinmeyen"}' tablosunda kolon silen ALTER TABLE ... DROP COLUMN işlemi.";
                }
                break;
            }

            case "UPDATE":
            {
                commandKind = SqlCommandKind.Update;
                targetTable = ExtractTargetTable(clean, @"UPDATE\s+([^\s;,\(\)]+)");
                break;
            }

            case "SELECT":
            {
                commandKind = SqlCommandKind.Select;
                break;
            }

            case "INSERT":
            {
                commandKind = SqlCommandKind.Insert;
                targetTable = ExtractTargetTable(clean, @"INSERT\s+INTO\s+([^\s;,\(\)]+)");
                break;
            }

            default:
            {
                commandKind = SqlCommandKind.Unknown;
                break;
            }
        }

        return new SqlStatementInfo
        {
            RawSql = raw,
            CleanSql = clean,
            CommandKind = commandKind,
            TargetTable = targetTable,
            IsDestructive = isDestructive,
            DestructiveReason = destructiveReason,
            HasWhereClause = hasWhere,
            WhereCondition = whereClause
        };
    }

    private static string? ExtractTargetTable(string sql, string pattern)
    {
        var match = Regex.Match(sql, pattern, RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var raw = match.Groups[1].Value.Trim();
        // Remove schema qualifier if present: dbo.Users -> Users, [dbo].[Users] -> Users
        var parts = raw.Split('.');
        var tablePart = parts[^1].Trim();
        return tablePart.Trim('[', ']', '"', '`');
    }
}
