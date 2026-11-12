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

        // İstatistik Rozetleri & Sekmeler
        sb.AppendLine("    <div class=\"flex items-center space-x-4 text-xs font-medium text-slate-400\">");
        sb.AppendLine("      <div class=\"flex bg-slate-950 p-1 rounded-lg border border-slate-800 space-x-1\">");
        sb.AppendLine("        <button id=\"tab-btn-diagram\" onclick=\"switchTab('diagram')\" class=\"px-3 py-1 rounded-md bg-cyan-600 text-white font-semibold transition\">📊 ER Diyagramı</button>");
        sb.AppendLine("        <button id=\"tab-btn-dictionary\" onclick=\"switchTab('dictionary')\" class=\"px-3 py-1 rounded-md text-slate-400 hover:text-slate-200 transition\">📋 Veri Sözlüğü (Data Dictionary)</button>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"h-4 w-px bg-slate-800\"></div>");
        sb.AppendLine($"      <div class=\"flex items-center space-x-1.5\"><span class=\"text-slate-200 font-semibold\">{schema.Tables.Count}</span><span>Tablo</span></div>");
        sb.AppendLine($"      <div class=\"flex items-center space-x-1.5\"><span class=\"text-slate-200 font-semibold\">{schema.GetAllForeignKeys().Count()}</span><span>İlişki</span></div>");
        sb.AppendLine("      <div class=\"h-4 w-px bg-slate-800\"></div>");
        sb.AppendLine("      <button id=\"btn-reset-zoom\" onclick=\"resetZoom()\" class=\"px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 transition\">Sıfırla (Reset)</button>");
        sb.AppendLine("      <button onclick=\"copyMermaid()\" class=\"px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 transition\">Kodu Kopyala</button>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </header>");

        // ANA GÖVDE
        sb.AppendLine("  <div class=\"flex-1 flex overflow-hidden\">");

        // Sol Panel: Tablolar Listesi
        sb.AppendLine("    <aside class=\"w-72 border-r border-slate-800 bg-slate-900/40 flex flex-col shrink-0\">");
        sb.AppendLine("      <div class=\"p-3 border-b border-slate-800\">");
        sb.AppendLine("        <input type=\"text\" id=\"search\" placeholder=\"Tablo veya sütun ara...\" onkeyup=\"filterTables()\" class=\"w-full px-3 py-1.5 text-xs rounded-md bg-slate-950 border border-slate-800 text-slate-200 focus:outline-none focus:border-cyan-500 transition\" />");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div id=\"table-list\" class=\"flex-1 overflow-y-auto p-2 space-y-1 custom-scroll\">");
        foreach (var table in schema.Tables)
        {
            var pkCols = string.Join(", ", table.PrimaryKeyColumns.Select(c => c.Name));
            sb.AppendLine($"        <div onclick=\"scrollToTable('{table.Name.ToLowerInvariant()}')\" class=\"table-item p-2 rounded-lg hover:bg-slate-800/60 cursor-pointer transition border border-transparent hover:border-slate-700\" data-name=\"{table.Name.ToLowerInvariant()}\">");
            sb.AppendLine($"          <div class=\"flex items-center justify-between text-xs font-semibold text-slate-200\"><span>{table.Name}</span><span class=\"text-[10px] text-slate-500\">{table.Columns.Count} sütun</span></div>");
            if (!string.IsNullOrEmpty(pkCols))
            {
                sb.AppendLine($"          <div class=\"text-[10px] text-cyan-400/80 mt-0.5 truncate\">PK: {pkCols}</div>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("    </aside>");

        // 1. Görünüm: İnteraktif Tuval (Canvas)
        sb.AppendLine("    <main class=\"flex-1 relative bg-[#090d16] flex items-center justify-center overflow-hidden\" id=\"view-diagram\">");
        sb.AppendLine("      <div id=\"diagram-container\" class=\"w-full h-full flex items-center justify-center p-4\">");
        sb.AppendLine("        <pre class=\"mermaid hidden\" id=\"raw-mermaid\">");
        sb.AppendLine(mermaidCode);
        sb.AppendLine("        </pre>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"absolute bottom-4 right-4 bg-slate-900/90 border border-slate-800 rounded-lg p-2 text-[11px] text-slate-400 flex items-center space-x-2 shadow-xl backdrop-blur\">");
        sb.AppendLine("        <span>💡 <b>İpucu:</b> Yakınlaşmak için fare tekerleğini, gezinmek için sürüklemeyi kullanın.</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </main>");

        // 2. Görünüm: Zengin Veri Sözlüğü (Data Dictionary Tabloları)
        sb.AppendLine("    <main class=\"flex-1 bg-[#0b0f17] overflow-y-auto p-8 custom-scroll hidden\" id=\"view-dictionary\">");
        sb.AppendLine("      <div class=\"max-w-5xl mx-auto space-y-10\">");
        sb.AppendLine("        <div>");
        sb.AppendLine("          <h1 class=\"text-2xl font-bold text-white tracking-tight\">📋 Veritabanı Veri Sözlüğü (Data Dictionary)</h1>");
        sb.AppendLine($"         <p class=\"text-xs text-slate-400 mt-1\">Motor: <span class=\"text-cyan-400\">{schema.Dialect}</span> • Toplam {schema.Tables.Count} Tablo • {schema.GetAllForeignKeys().Count()} Yabancı Anahtar İlişkisi</p>");
        sb.AppendLine("        </div>");

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"        <div id=\"table-card-{table.Name.ToLowerInvariant()}\" class=\"bg-slate-900/70 border border-slate-800 rounded-xl overflow-hidden shadow-xl\">");
            sb.AppendLine("          <div class=\"px-5 py-3.5 bg-slate-800/50 border-b border-slate-800 flex items-center justify-between\">");
            sb.AppendLine($"           <div class=\"flex items-center space-x-2.5\"><span class=\"w-2.5 h-2.5 rounded-full bg-cyan-400\"></span><h2 class=\"font-bold text-base text-white\">{table.FullName}</h2></div>");
            sb.AppendLine($"           <span class=\"text-xs px-2.5 py-0.5 rounded-full bg-slate-800 text-slate-300 border border-slate-700\">{table.Columns.Count} Sütun</span>");
            sb.AppendLine("          </div>");

            if (!string.IsNullOrEmpty(table.Description))
            {
                sb.AppendLine($"         <div class=\"px-5 py-2.5 bg-slate-950/40 text-xs text-slate-400 border-b border-slate-800/60\">{table.Description}</div>");
            }

            // Sütunlar Tablosu
            sb.AppendLine("          <div class=\"overflow-x-auto\">");
            sb.AppendLine("            <table class=\"w-full text-left text-xs text-slate-300\">");
            sb.AppendLine("              <thead class=\"bg-slate-950/70 text-slate-400 uppercase text-[10px] tracking-wider border-b border-slate-800\">");
            sb.AppendLine("                <tr>");
            sb.AppendLine("                  <th class=\"px-5 py-2.5 font-semibold\">Sütun Adı</th>");
            sb.AppendLine("                  <th class=\"px-4 py-2.5 font-semibold\">Veri Tipi</th>");
            sb.AppendLine("                  <th class=\"px-4 py-2.5 font-semibold text-center\">PK</th>");
            sb.AppendLine("                  <th class=\"px-4 py-2.5 font-semibold text-center\">Boş Olabilir?</th>");
            sb.AppendLine("                  <th class=\"px-4 py-2.5 font-semibold\">Varsayılan</th>");
            sb.AppendLine("                </tr>");
            sb.AppendLine("              </thead>");
            sb.AppendLine("              <tbody class=\"divide-y divide-slate-800/60 font-mono\">");

            foreach (var col in table.Columns)
            {
                var pkBadge = col.IsPrimaryKey ? "<span class=\"px-1.5 py-0.5 text-[10px] font-bold rounded bg-amber-500/10 text-amber-400 border border-amber-500/30\">PK</span>" : "<span class=\"text-slate-600\">—</span>";
                var nullBadge = col.IsNullable ? "<span class=\"text-emerald-400\">Evet</span>" : "<span class=\"text-rose-400 font-semibold\">Hayır</span>";
                var defVal = string.IsNullOrEmpty(col.DefaultValue) ? "<span class=\"text-slate-600\">—</span>" : $"<span class=\"text-cyan-300\">{col.DefaultValue}</span>";

                sb.AppendLine("                <tr class=\"hover:bg-slate-800/30 transition\">");
                sb.AppendLine($"                 <td class=\"px-5 py-2.5 font-semibold text-white font-sans flex items-center space-x-1.5\"><span>{col.Name}</span></td>");
                sb.AppendLine($"                 <td class=\"px-4 py-2.5 text-cyan-400\">{col.DataType}</td>");
                sb.AppendLine($"                 <td class=\"px-4 py-2.5 text-center\">{pkBadge}</td>");
                sb.AppendLine($"                 <td class=\"px-4 py-2.5 text-center font-sans\">{nullBadge}</td>");
                sb.AppendLine($"                 <td class=\"px-4 py-2.5\">{defVal}</td>");
                sb.AppendLine("                </tr>");
            }

            sb.AppendLine("              </tbody>");
            sb.AppendLine("            </table>");
            sb.AppendLine("          </div>");

            // Yabancı Anahtarlar (Varsa)
            if (table.ForeignKeys.Count > 0)
            {
                sb.AppendLine("          <div class=\"px-5 py-2.5 bg-slate-950/60 border-t border-slate-800 text-xs\">");
                sb.AppendLine("            <span class=\"font-semibold text-slate-400\">🔗 İlişkiler (Foreign Keys):</span>");
                sb.AppendLine("            <div class=\"mt-1 space-y-1 font-mono text-[11px] text-slate-300\">");
                foreach (var fk in table.ForeignKeys)
                {
                    sb.AppendLine($"             <div class=\"text-cyan-400/90\">• ({string.Join(", ", fk.SourceColumns)}) ➡️ <span class=\"text-white font-semibold\">{fk.TargetTable}</span>({string.Join(", ", fk.TargetColumns)})</div>");
                }
                sb.AppendLine("            </div>");
                sb.AppendLine("          </div>");
            }

            sb.AppendLine("        </div>");
        }

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
        sb.AppendLine("    function switchTab(tab) {");
        sb.AppendLine("      const viewDiagram = document.getElementById('view-diagram');");
        sb.AppendLine("      const viewDict = document.getElementById('view-dictionary');");
        sb.AppendLine("      const btnDiagram = document.getElementById('tab-btn-diagram');");
        sb.AppendLine("      const btnDict = document.getElementById('tab-btn-dictionary');");
        sb.AppendLine("      const resetBtn = document.getElementById('btn-reset-zoom');");
        sb.AppendLine("      if (tab === 'diagram') {");
        sb.AppendLine("        viewDiagram.classList.remove('hidden');");
        sb.AppendLine("        viewDict.classList.add('hidden');");
        sb.AppendLine("        btnDiagram.className = 'px-3 py-1 rounded-md bg-cyan-600 text-white font-semibold transition';");
        sb.AppendLine("        btnDict.className = 'px-3 py-1 rounded-md text-slate-400 hover:text-slate-200 transition';");
        sb.AppendLine("        resetBtn.style.display = 'inline-block';");
        sb.AppendLine("      } else {");
        sb.AppendLine("        viewDiagram.classList.add('hidden');");
        sb.AppendLine("        viewDict.classList.remove('hidden');");
        sb.AppendLine("        btnDict.className = 'px-3 py-1 rounded-md bg-cyan-600 text-white font-semibold transition';");
        sb.AppendLine("        btnDiagram.className = 'px-3 py-1 rounded-md text-slate-400 hover:text-slate-200 transition';");
        sb.AppendLine("        resetBtn.style.display = 'none';");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("    function scrollToTable(tableName) {");
        sb.AppendLine("      switchTab('dictionary');");
        sb.AppendLine("      const target = document.getElementById('table-card-' + tableName);");
        sb.AppendLine("      if (target) {");
        sb.AppendLine("        target.scrollIntoView({ behavior: 'smooth', block: 'start' });");
        sb.AppendLine("        target.classList.add('ring-2', 'ring-cyan-400');");
        sb.AppendLine("        setTimeout(() => target.classList.remove('ring-2', 'ring-cyan-400'), 2000);");
        sb.AppendLine("      }");
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
