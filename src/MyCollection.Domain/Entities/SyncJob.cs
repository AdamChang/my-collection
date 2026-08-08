using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public enum SyncStatus
{
    Running,
    Succeeded,
    Failed
}

public enum SyncJobKind
{
    Sync,
    Enrich
}

public sealed class SyncJob
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }
    public required string Provider { get; set; }
    public SyncJobKind Kind { get; set; } = SyncJobKind.Sync;

    /// <summary>補完作業的明確目標；null 代表依 marker 批次挑選。</summary>
    public List<string>? ItemIds { get; set; }

    public int Limit { get; set; } = 50;

    public SyncStatus Status { get; set; } = SyncStatus.Running;

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }

    /// <summary>正常但未處理的品項數，例如外部來源查無對應。與 Failed 語意不同。</summary>
    public int Skipped { get; set; }

    /// <summary>失敗時的錯誤訊息，供 UI 顯示並提供重試。</summary>
    public string? Error { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>背景執行的實際 claim 次數；同一 delivery 的重入不重複累加。</summary>
    public int Attempt { get; set; }

    /// <summary>避免同一 operation 被並行執行；revision 中止後租約到期即可重試。</summary>
    public DateTime? LeaseUntil { get; set; }
}
