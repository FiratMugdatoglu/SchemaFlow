using SchemaFlow.Core.Models;

namespace SchemaFlow.Api.Contracts;

/// <summary>
/// SQL ayrıştırma isteği için model.
/// </summary>
public record ParseRequest(string Sql, string? Dialect = "postgres");

/// <summary>
/// SQL ayrıştırma cevabı modeli.
/// </summary>
public record ParseResponse(
    bool Success,
    string Dialect,
    int TotalTables,
    int TotalRelations,
    DatabaseSchema Schema,
    long ElapsedMilliseconds,
    string? ErrorMessage = null
);

/// <summary>
/// Diyagram / Doküman üretme isteği modeli.
/// </summary>
public record RenderRequest(
    string Sql, 
    string? Format = "mermaid", 
    string? Dialect = "postgres"
);

/// <summary>
/// Diyagram / Doküman üretme cevabı modeli.
/// </summary>
public record RenderResponse(
    bool Success,
    string Format,
    string Content,
    long ElapsedMilliseconds,
    string? ErrorMessage = null
);
