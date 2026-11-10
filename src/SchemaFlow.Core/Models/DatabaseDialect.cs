namespace SchemaFlow.Core.Models;

/// <summary>
/// SchemaFlow tarafından desteklenen hedef veritabanı türleri (diyalektleri).
/// </summary>
public enum DatabaseDialect
{
    PostgreSql,
    MySql,
    MsSql,
    Sqlite,
    EfCore,
    Prisma,
    Liquibase,
    Django,
    Rails
}
