using SchemaFlow.Core.Models;

namespace SchemaFlow.Core.Parsers;

/// <summary>
/// MySQL ve MariaDB DDL ve migration komutlarını ayrıştıran somut strateji sınıfıdır.
/// Backtick (``) isimlendirmeleri, AUTO_INCREMENT, ENGINE=InnoDB ve satır içi KEY/INDEX'leri destekler.
/// </summary>
public class MySqlParser : PostgreSqlParser
{
    public override DatabaseDialect SupportedDialect => DatabaseDialect.MySql;
}
