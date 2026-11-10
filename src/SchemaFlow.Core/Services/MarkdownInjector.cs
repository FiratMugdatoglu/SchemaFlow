namespace SchemaFlow.Core.Services;

/// <summary>
/// Var olan bir Markdown dosyasına (örn: README.md) projenin diğer kısımlarına zarar vermeden
/// akıllıca şema dokümantasyonunu enjekte eden servis sınıfıdır.
/// </summary>
public class MarkdownInjector
{
    public const string BeginMarker = "<!-- BEGIN_SCHEMAFLOW -->";
    public const string EndMarker = "<!-- END_SCHEMAFLOW -->";

    /// <summary>
    /// Verilen hedef metin içine yeni içeriği enjekte eder.
    /// Etiketler varsa sadece aralarını günceller; yoksa içeriğin sonuna etiketlerle birlikte ekler.
    /// </summary>
    /// <param name="existingContent">Var olan dosya içeriği (örn: mevcut README.md metni)</param>
    /// <param name="newContent">Enjekte edilecek yeni Mermaid/Markdown şema dokümanı</param>
    /// <returns>Güncellenmiş yeni dosya içeriği</returns>
    public string Inject(string existingContent, string newContent)
    {
        var wrappedContent = $"{BeginMarker}\n{newContent.Trim()}\n{EndMarker}";

        if (string.IsNullOrWhiteSpace(existingContent))
        {
            return wrappedContent;
        }

        var beginIndex = existingContent.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
        var endIndex = existingContent.IndexOf(EndMarker, StringComparison.OrdinalIgnoreCase);

        // 1. Durum: Her iki etiket de dosyada zaten mevcut -> Sadece aralarını güncelle!
        if (beginIndex != -1 && endIndex != -1 && endIndex > beginIndex)
        {
            var before = existingContent.Substring(0, beginIndex).TrimEnd();
            var after = existingContent.Substring(endIndex + EndMarker.Length).TrimStart();

            var beforePart = string.IsNullOrEmpty(before) ? "" : before + "\n\n";
            var afterPart = string.IsNullOrEmpty(after) ? "" : "\n\n" + after;

            return $"{beforePart}{wrappedContent}{afterPart}\n";
        }

        // 2. Durum: Etiketler yoksa -> Dosyanın en sonuna ekle
        return $"{existingContent.TrimEnd()}\n\n{wrappedContent}\n";
    }

    /// <summary>
    /// Bir dosyanın içinde SchemaFlow enjeksiyon etiketlerinin bulunup bulunmadığını kontrol eder.
    /// </summary>
    public bool HasMarkers(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var begin = content.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
        var end = content.IndexOf(EndMarker, StringComparison.OrdinalIgnoreCase);

        return begin != -1 && end != -1 && end > begin;
    }
}
