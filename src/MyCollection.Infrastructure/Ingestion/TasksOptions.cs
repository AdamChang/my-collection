namespace MyCollection.Infrastructure.Ingestion;

public sealed class TasksOptions
{
    public const string SectionName = "Tasks";

    public string Provider { get; init; } = "InProcess";
    public string ProjectId { get; init; } = string.Empty;
    public string Location { get; init; } = "asia-east1";
    public string Queue { get; init; } = "ingestion";
    public string HandlerUrl { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string ServiceAccountEmail { get; init; } = string.Empty;
}
