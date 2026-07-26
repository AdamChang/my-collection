using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public enum SyncStatus
{
    Running,
    Succeeded,
    Failed
}

public sealed class SyncJob
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }
    public required string Provider { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Running;

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }

    /// <summary>失敗時的錯誤訊息，供 UI 顯示並提供重試。</summary>
    public string? Error { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
