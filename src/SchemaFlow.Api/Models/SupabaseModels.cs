namespace SchemaFlow.Api.Models;

/// <summary>
/// Supabase 'profiles' tablosunun C# karşılığıdır.
/// </summary>
public class ProfileModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string SubscriptionTier { get; set; } = "free"; // 'free', 'pro', 'team'
    public string SubscriptionStatus { get; set; } = "active";
    public string? LemonSqueezyCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Supabase 'projects' tablosunun C# karşılığıdır.
/// </summary>
public class ProjectModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Dialect { get; set; } = "postgres";
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Supabase 'schema_versions' tablosunun C# karşılığıdır.
/// </summary>
public class SchemaVersionModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public string? CommitHash { get; set; }
    public string? CommitMessage { get; set; }
    public string SchemaJson { get; set; } = string.Empty;
    public string MermaidCode { get; set; } = string.Empty;
    public string MarkdownDocs { get; set; } = string.Empty;
    public int TotalTables { get; set; }
    public int TotalRelations { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
