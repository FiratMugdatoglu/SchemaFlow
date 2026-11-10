using SchemaFlow.Core.Models;

namespace SchemaFlow.Core.Abstractions;

/// <summary>
/// Ayrıştırılmış DatabaseSchema nesnesini farklı görsel veya doküman formatlarına
/// (Mermaid.js, Markdown, HTML, JSON) dönüştüren üreteçlerin temel arayüzüdür.
/// </summary>
public interface ISchemaRenderer
{
    /// <summary>
    /// Bu üretecin çıktı formatı adı (örn: "mermaid", "markdown", "html")
    /// </summary>
    string OutputFormat { get; }

    /// <summary>
    /// Verilen veritabanı şemasını ilgili biçimde metin olarak üretir.
    /// </summary>
    /// <param name="schema">Ayrıştırılmış ortak şema nesnesi</param>
    /// <returns>Biçimlendirilmiş çıktı metni</returns>
    string Render(DatabaseSchema schema);
}
