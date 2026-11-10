using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using System.Text;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// Tüm veritabanı ayrıştırıcılarının (PostgreSQL, MySQL, MSSQL) ortaklaşa kullanacağı temel sınıftır.
/// SQL ifadelerini temizleme, yorumları ayıklama ve dosyaları okuma gibi ortak mantıkları barındırır.
/// </summary>
public abstract class BaseDatabaseParser : IDatabaseParser
{
    public abstract DatabaseDialect SupportedDialect { get; }

    public abstract Task<DatabaseSchema> ParseSqlAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen dosya yollarındaki SQL betiklerini sırayla okuyup tek bir metin halinde birleştirerek parse eder.
    /// </summary>
    public virtual async Task<DatabaseSchema> ParseFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var combinedSql = new StringBuilder();

        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path, cancellationToken);
                combinedSql.AppendLine(content);
                combinedSql.AppendLine(";"); // Dosya aralarına güvenli ayırıcı koyuyoruz
            }
        }

        return await ParseSqlAsync(combinedSql.ToString(), cancellationToken);
    }

    /// <summary>
    /// SQL metnindeki tek satırlı (-- ...) ve çok satırlı (/* ... */) yorumları temizler.
    /// Böylece parser yorum satırlarını yanlışlıkla SQL komutu sanmaz.
    /// </summary>
    protected static string StripComments(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        var sb = new StringBuilder();
        var i = 0;
        var inSingleQuote = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            // String literal içindeysek ('metin') yorum işaretlerini string sanmamak için atla
            if (!inSingleLineComment && !inMultiLineComment)
            {
                if (c == '\'')
                {
                    inSingleQuote = !inSingleQuote;
                    sb.Append(c);
                    i++;
                    continue;
                }
            }

            if (!inSingleQuote)
            {
                // Tek satırlı yorum başlangıcı: --
                if (!inMultiLineComment && !inSingleLineComment && c == '-' && next == '-')
                {
                    inSingleLineComment = true;
                    i += 2;
                    continue;
                }

                // Tek satırlı yorum bitişi: Yeni satır
                if (inSingleLineComment && (c == '\r' || c == '\n'))
                {
                    inSingleLineComment = false;
                    sb.Append(c);
                    i++;
                    continue;
                }

                // Çok satırlı yorum başlangıcı: /*
                if (!inSingleLineComment && !inMultiLineComment && c == '/' && next == '*')
                {
                    inMultiLineComment = true;
                    i += 2;
                    continue;
                }

                // Çok satırlı yorum bitişi: */
                if (inMultiLineComment && c == '*' && next == '/')
                {
                    inMultiLineComment = false;
                    i += 2;
                    continue;
                }
            }

            if (!inSingleLineComment && !inMultiLineComment)
            {
                sb.Append(c);
            }

            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Temizlenmiş SQL metnini noktalı virgüllere göre güvenli ifadelere (statements) böler.
    /// Parantez içlerindeki veya tırnak içlerindeki noktalı virgülleri bölmez.
    /// </summary>
    protected static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        if (string.IsNullOrWhiteSpace(sql))
            return statements;

        var current = new StringBuilder();
        var inSingleQuote = false;
        var parenDepth = 0;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            if (c == '\'')
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (!inSingleQuote)
            {
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth = Math.Max(0, parenDepth - 1);
            }

            if (c == ';' && !inSingleQuote && parenDepth == 0)
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                {
                    statements.Add(stmt);
                }
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        var remaining = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            statements.Add(remaining);
        }

        return statements;
    }

    /// <summary>
    /// Tablo veya sütun isimlerindeki tırnakları (", `, []) temizler.
    /// Örn: "users" -> users, [email] -> email
    /// </summary>
    protected static string CleanIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return string.Empty;

        return identifier.Trim().Trim('"', '\'', '`', '[', ']');
    }
}
