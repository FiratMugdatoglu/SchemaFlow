using SchemaFlow.Core.Abstractions;
using SchemaFlow.Core.Models;
using System.Text;
using System.Text.Json;

namespace SchemaFlow.Core.Renderers;

/// <summary>
/// DatabaseSchema nesnesini tamamen bağımsız, tarayıcıda offline açılabilen,
/// Pan & Zoom özellikli interaktif bir HTML Stüdyo sayfasına dönüştürür.
/// </summary>
public class HtmlRenderer : ISchemaRenderer
{
    private readonly MermaidRenderer _mermaidRenderer = new();
    private readonly MarkdownRenderer _markdownRenderer = new();

    public string OutputFormat => "html";

    public string Render(DatabaseSchema schema)
    {
        var mermaidCode = _mermaidRenderer.Render(schema);
        var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = false });

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"tr\" class=\"dark\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        sb.AppendLine($"  <title>SchemaFlow Studio - {schema.DatabaseName ?? "Veritabanı Şeması"}</title>");
        sb.AppendLine("  <script src=\"https://cdn.tailwindcss.com\"></script>");
        sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js\"></script>");
        sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/svg-pan-zoom@3.6.1/dist/svg-pan-zoom.min.js\"></script>");
        sb.AppendLine("  <script>");
        sb.AppendLine("    tailwind.config = { darkMode: 'class', theme: { extend: { colors: { brand: '#06b6d4' } } } }");
        sb.AppendLine("  </script>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { background-color: #0b0f17; color: #e2e8f0; font-family: ui-sans-serif, system-ui, sans-serif; }");
        sb.AppendLine("    #diagram-container svg { width: 100% !important; height: 100% !important; min-height: 600px; }");
        sb.AppendLine("    .custom-scroll::-webkit-scrollbar { width: 6px; height: 6px; }");
        sb.AppendLine("    .custom-scroll::-webkit-scrollbar-thumb { background: #334155; border-radius: 4px; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body class=\"h-screen flex flex-col overflow-hidden\">");

        // ÜST ÇUBUK (Top Navigation)
        sb.AppendLine("  <header class=\"h-14 border-b border-slate-800 bg-slate-900/80 backdrop-blur px-6 flex items-center justify-between shrink-0\">");
        sb.AppendLine("    <div class=\"flex items-center space-x-3\">");
        sb.AppendLine("      <div class=\"w-8 h-8 rounded-lg bg-gradient-to-tr from-cyan-500 to-blue-600 flex items-center justify-center font-bold text-white shadow-lg shadow-cyan-500/20\">S</div>");
        sb.AppendLine("      <span class=\"font-bold text-lg tracking-tight text-white\">SchemaFlow<span class=\"text-cyan-400\">.ai</span></span>");
        sb.AppendLine($"     <span class=\"text-xs px-2 py-0.5 rounded-full bg-cyan-950 text-cyan-400 border border-cyan-800/50\">{schema.Dialect}</span>");
        sb.AppendLine("    </div>");

        // İstatistik Rozetleri
        sb.AppendLine("    <div class=\"flex items-center space-x-4 text-xs font-medium text-slate-400\">");
        sb.AppendLine($"      <div class=\"flex items-center space-x-1.5\"><span class=\"text-slate-200 font-semibold\">{schema.Tables.Count}</span><span>Tablo</span></div>");
        sb.AppendLine($"      <div class=\"flex items-center space-x-1.5\"><span class=\"text-slate-200 font-semibold\">{schema.GetAllForeignKeys().Count()}</span><span>İlişki</span></div>");
        sb.AppendLine("      <div class=\"h-4 w-px bg-slate-800\"></div>");
        sb.AppendLine("      <button onclick=\"resetZoom()\" class=\"px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 transition\">Sıfırla (Reset)</button>");
        sb.AppendLine("      <button onclick=\"copyMermaid()\" class=\"px-2.5 py-1 rounded bg-cyan-600 hover:bg-cyan-500 text-white font-semibold transition\">Mermaid Kopyala</button>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </header>");

        // ANA GÖVDE (Sol Kenar Çubuğu + Sağ Tuval)
        sb.AppendLine("  <div class=\"flex-1 flex overflow-hidden\">");

        // Sol Panel: Tablolar Arama ve Listesi
        sb.AppendLine("    <aside class=\"w-72 border-r border-slate-800 bg-slate-900/40 flex flex-col shrink-0\">");
        sb.AppendLine("      <div class=\"p-3 border-b border-slate-800\">");
        sb.AppendLine("        <input type=\"text\" id=\"search\" placeholder=\"Tablo veya sütun ara...\" onkeyup=\"filterTables()\" class=\"w-full px-3 py-1.5 text-xs rounded-md bg-slate-950 border border-slate-800 text-slate-200 focus:outline-none focus:border-cyan-500 transition\" />");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div id=\"table-list\" class=\"flex-1 overflow-y-auto p-2 space-y-1 custom-scroll\">");
        foreach (var table in schema.Tables)
        {
            var pkCols = string.Join(", ", table.PrimaryKeyColumns.Select(c => c.Name));
            sb.AppendLine($"        <div class=\"table-item p-2 rounded-lg hover:bg-slate-800/60 cursor-pointer transition border border-transparent hover:border-slate-700\" data-name=\"{table.Name.ToLowerInvariant()}\">");
            sb.AppendLine($"          <div class=\"flex items-center justify-between text-xs font-semibold text-slate-200\"><span>{table.Name}</span><span class=\"text-[10px] text-slate-500\">{table.Columns.Count} sütun</span></div>");
            if (!string.IsNullOrEmpty(pkCols))
            {
                sb.AppendLine($"          <div class=\"text-[10px] text-cyan-400/80 mt-0.5 truncate\">PK: {pkCols}</div>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("    </aside>");

        // Sağ Panel: İnteraktif Tuval (Canvas)
        sb.AppendLine("    <main class=\"flex-1 relative bg-[#090d16] flex items-center justify-center overflow-hidden\" id=\"canvas-wrapper\">");
        sb.AppendLine("      <div id=\"diagram-container\" class=\"w-full h-full flex items-center justify-center p-4\">");
        sb.AppendLine("        <pre class=\"mermaid hidden\" id=\"raw-mermaid\">");
        sb.AppendLine(mermaidCode);
        sb.AppendLine("        </pre>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"absolute bottom-4 right-4 bg-slate-900/90 border border-slate-800 rounded-lg p-2 text-[11px] text-slate-400 flex items-center space-x-2 shadow-xl backdrop-blur\">");
        sb.AppendLine("        <span>💡 <b>İpucu:</b> Yakınlaşmak için fare tekerleğini, gezinmek için sürüklemeyi kullanın.</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </main>");

        sb.AppendLine("  </div>");

        // JAVASCRIPT BAŞLANGIÇ
        sb.AppendLine("  <script>");
        sb.AppendLine("    mermaid.initialize({ startOnLoad: false, theme: 'dark', er: { useMaxWidth: false } });");
        sb.AppendLine("    let panZoomInstance = null;");
        sb.AppendLine("    window.addEventListener('DOMContentLoaded', async () => {");
        sb.AppendLine("      const container = document.getElementById('diagram-container');");
        sb.AppendLine("      const rawMermaid = document.getElementById('raw-mermaid').innerText;");
        sb.AppendLine("      const { svg } = await mermaid.render('mermaid-svg', rawMermaid);");
        sb.AppendLine("      container.innerHTML = svg;");
        sb.AppendLine("      panZoomInstance = svgPanZoom('#mermaid-svg', { zoomEnabled: true, controlIconsEnabled: false, fit: true, center: true, minZoom: 0.1, maxZoom: 10 });");
        sb.AppendLine("    });");
        sb.AppendLine("    function resetZoom() { if(panZoomInstance) { panZoomInstance.reset(); panZoomInstance.fit(); panZoomInstance.center(); } }");
        sb.AppendLine("    function copyMermaid() {");
        sb.AppendLine("      const raw = document.getElementById('raw-mermaid').innerText;");
        sb.AppendLine("      navigator.clipboard.writeText(raw);");
        sb.AppendLine("      alert('Mermaid diyagram kodu panoya kopyalandı!');");
        sb.AppendLine("    }");
        sb.AppendLine("    function filterTables() {");
        sb.AppendLine("      const query = document.getElementById('search').value.toLowerCase();");
        sb.AppendLine("      document.querySelectorAll('.table-item').forEach(el => {");
        sb.AppendLine("        const name = el.getAttribute('data-name');");
        sb.AppendLine("        el.style.display = name.includes(query) ? 'block' : 'none';");
        sb.AppendLine("      });");
        sb.AppendLine("    }");
        sb.AppendLine("  </script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
