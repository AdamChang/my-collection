# 收藏資料匯入／匯出 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓使用者在 Settings 頁匯出一個含品類、手建品項與圖片的 ZIP 封存檔，帶到另一台獨立部署的機器匯入，以快照取代方式還原收藏。

**Architecture:** 匯出是單趟串流：`GET /export` 直接對 `HttpResponse.Body` 開 `ZipArchive` 寫出 `manifest.json` 與 `media/**`。匯入分兩階段：階段一在 ZIP 暫存檔上完成全部驗證，完全不寫入；階段二先產生自動備份，再執行「刪除 → 寫入 → 圖片重建」。manifest 以 MongoDB Canonical Extended JSON 序列化，確保 `Item.Attributes`（`BsonDocument`）內的 `Decimal128`／`DateTime`／`Int64` 無損 round-trip。

**Tech Stack:** .NET 10 · ASP.NET Core Minimal API · MediatR 14 · FluentValidation 12 · MongoDB Driver 3.10 · `System.IO.Compression.ZipArchive` · ImageSharp · Angular 20.3 signals · xUnit + FluentAssertions + Moq + Testcontainers

**Spec:** `docs/superpowers/specs/2026-07-28-collection-import-export-design.md`

---

## 讀這份計畫前必須知道的三件事

1. **後端路由沒有 `/api` 前綴。** `web/nginx.conf:15` 的 `proxy_pass http://api:8080/` 會剝掉它。後端註冊的是 `/export`、`/import`；Angular 端用 `${API_BASE}/export`（`API_BASE = '/api'`）。spec 第 8 節寫的 `/api/export` 指的是瀏覽器可見的網址。

2. **授權寫在倉儲層。** 每個 MongoDB filter 都以 `_userContext.UserId` 起頭，不在 handler 裡檢查。漏寫的後果是「查無資料」，不是資料外洩。新增的 `MongoTransferRepository` 必須遵守這條。

3. **BSON 慣例已全域註冊。** `MongoConventions.Register()` 提供 camelCase 元素名、enum 存字串、`DateTime` 只收 `Kind=Utc`。測試端由 `tests/MyCollection.Tests/TestBootstrap.cs` 的 `[ModuleInitializer]` 在組件載入時註冊，所以序列化單元測試拿到的慣例與正式環境一致。**任何寫進 BSON 的 `DateTime` 必須是 `DateTimeKind.Utc`，否則 `UtcOnlyDateTimeSerializer` 會直接擲例外。**

---

## 執行分段與斷點

這份計畫刻意分成四段執行，每段結束時專案都處於「可建置、測試全綠、已提交、功能不半殘」的狀態，可以安心關掉 session，下次從下一個 Task 接續。

**採 inline 執行（`superpowers:executing-plans`），不要用 subagent-driven。** 每個 subagent 都是冷啟動、要重新推導專案脈絡；inline 沿用同一份上下文，用量明顯較省。這份計畫的每個任務都已附完整程式碼，不需要 subagent 額外探索。

| 段 | Tasks | 結束時的狀態 | 斷點安全性 |
|---|---|---|---|
| 一 | 1–6 | 匯出功能完整可用；`.webp` 白名單已修補。尚無任何破壞性程式碼。 | 最安全。即使就此停手，成果仍有價值。 |
| 二 | 7–10 | 匯入所需的三個元件與 handler 都寫好，但**沒有端點可以觸發**。 | 安全。純新增程式碼，既有行為零改動。 |
| 三 | 11–12 | 後端完整，往返測試綠燈。 | 安全。**但 11 與 12 之間不可斷。** |
| 四 | 13–15 | 部署設定、前端 UI、手動驗證。 | 安全。13 之後、14 之後皆可停。 |

### 唯一的硬性約束

**Task 11 與 Task 12 必須在同一段內完成。**

Task 11 讓 `POST /import` 上線，那是一個會刪除使用者資料的端點。Task 12 的往返整合測試才是證明它真的正確的東西。停在兩者之間，等於在系統裡留了一個未經驗證的破壞性端點。

若開始 Task 11 時判斷用量可能撐不到 Task 12 結束，**不要開始**——停在 Task 10 之後，那裡是乾淨的。

### 每段結束前的收尾

不論停在哪一段，關掉 session 之前一律確認：

```bash
dotnet build MyCollection.slnx     # Build succeeded，0 Error
dotnet test MyCollection.slnx      # 全數通過
git status                         # working tree clean
```

三項有任何一項不過，就把當前 Task 做完或還原，不要留半成品過夜。

### 下次接續的方式

計畫用 `- [ ]` 追蹤進度，執行時逐項勾成 `- [x]`。新 session 只要說「從 Task N 開始執行 `docs/superpowers/plans/2026-07-28-collection-import-export.md`」即可，不需要重述脈絡。

---

## File Structure

**新增（後端）**

| 檔案 | 責任 |
|---|---|
| `src/MyCollection.Application/Transfer/ArchiveManifest.cs` | 封存檔資料模型與 `CurrentSchemaVersion` 常數 |
| `src/MyCollection.Application/Transfer/ArchiveManifestSerializer.cs` | Canonical Extended JSON 讀寫 |
| `src/MyCollection.Application/Transfer/ITransferRepository.cs` | 匯出／匯入專用的跨 collection 查詢與批次寫入 |
| `src/MyCollection.Application/Transfer/ArchiveWriter.cs` | 匯出核心，寫入任意 `Stream`（匯出端點與自動備份共用） |
| `src/MyCollection.Application/Transfer/ExportCommand.cs` | MediatR 包裝，委派給 `ArchiveWriter` |
| `src/MyCollection.Application/Transfer/ArchiveValidator.cs` | 階段一驗證，回傳 `ValidationFailure` 清單 |
| `src/MyCollection.Application/Transfer/CategoryReconciler.cs` | spec §6.2 第 3 步的判定規則，純函式 |
| `src/MyCollection.Application/Transfer/ImportCommand.cs` | 匯入 handler 與 `ImportResultDto` |
| `src/MyCollection.Application/Common/IBackupStore.cs` | 備份存取抽象，刻意不經 `IFileStorage` |
| `src/MyCollection.Infrastructure/Storage/LocalBackupStore.cs` | 備份的本機實作與保留策略 |
| `src/MyCollection.Infrastructure/Mongo/MongoTransferRepository.cs` | `ITransferRepository` 的 Mongo 實作 |
| `src/MyCollection.Api/Endpoints/TransferEndpoints.cs` | `GET /export`、`POST /import` |

**修改（後端）**

| 檔案 | 變更 |
|---|---|
| `src/MyCollection.Application/Common/IFileStorage.cs` | 加 `DeleteDirectoryAsync` |
| `src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs` | 實作 `DeleteDirectoryAsync` |
| `src/MyCollection.Infrastructure/Storage/StorageOptions.cs` | 加 `BackupRoot` |
| `src/MyCollection.Api/Endpoints/MediaEndpoints.cs` | `/media/{**path}` 加 `.webp` 白名單 |
| `src/MyCollection.Api/Program.cs` | 註冊 `MapTransferEndpoints()` |
| `src/MyCollection.Infrastructure/DependencyInjection.cs` | 註冊 `ITransferRepository`、`IBackupStore`、`ArchiveWriter` |
| `docker-compose.yml` | `Storage__BackupRoot` 與 `./data/backups` volume |
| `web/nginx.conf` | `client_max_body_size` 12m → 2g |

**新增／修改（前端）**

| 檔案 | 責任 |
|---|---|
| `web/src/app/core/api/transfer.service.ts` | 匯出下載與匯入上傳 |
| `web/src/app/features/settings/data-transfer.component.ts` | 匯入／匯出 UI（獨立子元件） |
| `web/src/app/core/models.ts` | 加 `ImportResultDto` |
| `web/src/app/features/settings/settings.component.ts` | 嵌入子元件 |

`settings.component.ts` 目前已 266 行。匯入／匯出 UI 含破壞性確認對話框與結果摘要，塞進去會逼近 400 行且混雜兩種不相干的責任，因此獨立成 `data-transfer.component.ts`，`settings.component.ts` 只負責嵌入。

**測試**

| 檔案 | 類型 |
|---|---|
| `tests/MyCollection.Tests/Unit/ArchiveManifestSerializerTests.cs` | 單元 |
| `tests/MyCollection.Tests/Unit/CategoryReconcilerTests.cs` | 單元 |
| `tests/MyCollection.Tests/Unit/ArchiveValidatorTests.cs` | 單元 |
| `tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs` | 單元（真實檔案系統，temp 目錄） |
| `tests/MyCollection.Tests/Unit/LocalBackupStoreTests.cs` | 單元 |
| `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs` | 整合（Testcontainers 真 MongoDB） |
| `web/src/app/core/api/transfer.service.spec.ts` | 前端單元 |

---

## Task 1: `IFileStorage.DeleteDirectoryAsync`

匯入時要刪掉每個被取代 item 的整個媒體目錄。逐檔刪除只能清掉 DB 有記錄的檔案，孤兒檔會永久殘留。

**Files:**
- Modify: `src/MyCollection.Application/Common/IFileStorage.cs`
- Modify: `src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs`
- Test: `tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MyCollection.Infrastructure.Storage;

namespace MyCollection.Tests.Unit;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mc-storage-{Guid.NewGuid():N}");
    private readonly LocalFileStorage _sut;

    public LocalFileStorageTests() =>
        _sut = new LocalFileStorage(Options.Create(new StorageOptions { LocalRoot = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync(string relativePath) =>
        await _sut.SaveAsync(relativePath, new MemoryStream([1, 2, 3]), CancellationToken.None);

    [Fact]
    public async Task DeleteDirectory_removes_every_file_under_the_prefix()
    {
        await SeedAsync("owner/item/a-full.webp");
        await SeedAsync("owner/item/b-thumb.webp");

        await _sut.DeleteDirectoryAsync("owner/item", CancellationToken.None);

        Directory.Exists(Path.Combine(_root, "owner", "item")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectory_leaves_sibling_directories_untouched()
    {
        await SeedAsync("owner/item-a/x-full.webp");
        await SeedAsync("owner/item-b/y-full.webp");

        await _sut.DeleteDirectoryAsync("owner/item-a", CancellationToken.None);

        File.Exists(Path.Combine(_root, "owner", "item-b", "y-full.webp")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectory_is_silent_when_the_directory_does_not_exist()
    {
        var act = async () => await _sut.DeleteDirectoryAsync("owner/missing", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteDirectory_rejects_paths_that_escape_the_root()
    {
        var act = async () => await _sut.DeleteDirectoryAsync("../../etc", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~LocalFileStorageTests"`
Expected: 編譯失敗，`'LocalFileStorage' 未包含 'DeleteDirectoryAsync' 的定義`

- [ ] **Step 3: 加介面方法**

在 `src/MyCollection.Application/Common/IFileStorage.cs` 的 `DeleteAsync` 之後加入：

```csharp
    /// <summary>
    /// 刪除整個目錄前綴底下的所有檔案。不存在時不擲例外。
    /// 逐檔刪除只能清掉 DB 有記錄的檔案，孤兒檔會殘留，因此需要這個方法。
    /// </summary>
    Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct);
```

- [ ] **Step 4: 實作**

在 `src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs` 的 `DeleteAsync` 之後加入（`Resolve` 已負責邊界檢查，直接沿用）：

```csharp
    public Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct)
    {
        var fullPath = Resolve(relativePrefix);

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }

        return Task.CompletedTask;
    }
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~LocalFileStorageTests"`
Expected: PASS，4 passed

- [ ] **Step 6: 提交**

```bash
git add src/MyCollection.Application/Common/IFileStorage.cs \
        src/MyCollection.Infrastructure/Storage/LocalFileStorage.cs \
        tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs
git commit -m "feat(storage): add DeleteDirectoryAsync to IFileStorage"
```

---

## Task 2: `/media` 副檔名白名單

`MediaEndpoints.cs:43` 的 `GET /media/{**path}` 是 `AllowAnonymous`，目前能讀出 media root 底下任何檔案。備份已規劃寫在別處，但本功能會讓該目錄下的內容變多，補上白名單是廉價的縱深防禦。

**Files:**
- Modify: `src/MyCollection.Api/Endpoints/MediaEndpoints.cs:43-60`
- Test: `tests/MyCollection.Tests/Integration/MediaEndpointsTests.cs`

- [ ] **Step 1: 寫失敗的測試**

在 `tests/MyCollection.Tests/Integration/MediaEndpointsTests.cs` 的類別內加入：

```csharp
    [Fact]
    public async Task Media_endpoint_refuses_paths_that_are_not_webp()
    {
        var response = await _client.GetAsync("/media/anything/secret.zip");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~MediaEndpointsTests.Media_endpoint_refuses"`
Expected: FAIL

此測試在加白名單前也可能因檔案不存在而回 404 而意外通過。若如此，先在 storage root 手動放一個 `.zip` 再測——但更簡單的做法是信任 Step 4 的實作審視：白名單是在開檔前就短路，與檔案是否存在無關。**若 Step 2 意外通過，改用下面這個版本的測試**，它先透過既有的上傳流程產生一個真實檔案，再以偽造副檔名請求同一個路徑：

```csharp
    [Fact]
    public async Task Media_endpoint_refuses_paths_that_are_not_webp()
    {
        var item = await CreateItemAsync();
        var uploaded = await _client.PostAsync($"/items/{item.Id}/images", PngUpload());
        var image = (await uploaded.Content.ReadFromJsonAsync<ItemImageDto>())!;

        // 同一個實體檔案，換成非 .webp 的請求路徑
        var disguised = image.Path.Replace(".webp", ".zip", StringComparison.Ordinal);
        var response = await _client.GetAsync($"/media/{disguised}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
```

- [ ] **Step 3: 實作白名單**

修改 `src/MyCollection.Api/Endpoints/MediaEndpoints.cs`，把 `app.MapGet("/media/{**path}", ...)` 的 lambda 開頭改成：

```csharp
        app.MapGet("/media/{**path}", async (string path, IFileStorage storage, CancellationToken ct) =>
            {
                // 這是匿名端點。限定副檔名，避免它變成 media root 的任意檔案讀取器。
                if (!path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.NotFound();
                }

                Stream? stream;
```

其餘內容不變。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~MediaEndpointsTests"`
Expected: PASS，全部既有測試仍通過（既有測試讀的都是 `.webp`）

- [ ] **Step 5: 提交**

```bash
git add src/MyCollection.Api/Endpoints/MediaEndpoints.cs \
        tests/MyCollection.Tests/Integration/MediaEndpointsTests.cs
git commit -m "fix(api): restrict anonymous media endpoint to webp files"
```

---

## Task 3: 封存檔模型與 Canonical Extended JSON 序列化

`Item.Attributes` 是 `BsonDocument`，內容由使用者自定的 schema 決定，可能含 `Decimal128`、`DateTime`、`Int64`。`System.Text.Json` 會把這些壓成 string／number，來回一趟即失真。

**Files:**
- Create: `src/MyCollection.Application/Transfer/ArchiveManifest.cs`
- Create: `src/MyCollection.Application/Transfer/ArchiveManifestSerializer.cs`
- Test: `tests/MyCollection.Tests/Unit/ArchiveManifestSerializerTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Unit/ArchiveManifestSerializerTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ArchiveManifestSerializerTests
{
    private static ArchiveManifest RoundTrip(ArchiveManifest manifest)
    {
        using var buffer = new MemoryStream();
        ArchiveManifestSerializer.Write(buffer, manifest);
        buffer.Position = 0;

        return ArchiveManifestSerializer.Read(buffer);
    }

    private static ArchiveManifest ManifestWith(BsonDocument attributes)
    {
        var categoryId = ObjectId.GenerateNewId();

        return new ArchiveManifest
        {
            ExportedAt = new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc),
            Categories =
            [
                new ArchiveCategory
                {
                    Id = categoryId,
                    Name = "黑膠唱片",
                    Icon = "disc-3",
                    Kind = CategoryKind.Physical,
                    Fields = [new CategoryField { Key = "label", Label = "廠牌", Type = FieldType.Text }],
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Items =
            [
                new ArchiveItem
                {
                    Id = ObjectId.GenerateNewId(),
                    CategoryId = categoryId,
                    Name = "Kind of Blue",
                    Tags = ["jazz"],
                    Source = ItemSource.Manual,
                    Attributes = attributes,
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        };
    }

    [Fact]
    public void Attributes_preserve_decimal128_across_round_trip()
    {
        var attributes = new BsonDocument { { "price", new BsonDecimal128(1234.56m) } };

        var value = RoundTrip(ManifestWith(attributes)).Items[0].Attributes["price"];

        value.BsonType.Should().Be(BsonType.Decimal128);
        value.AsDecimal.Should().Be(1234.56m);
    }

    [Fact]
    public void Attributes_preserve_int64_and_do_not_collapse_to_int32()
    {
        var attributes = new BsonDocument { { "playtime", new BsonInt64(42L) } };

        RoundTrip(ManifestWith(attributes)).Items[0].Attributes["playtime"]
            .BsonType.Should().Be(BsonType.Int64);
    }

    [Fact]
    public void Attributes_preserve_utc_datetime_across_round_trip()
    {
        var released = new DateTime(1959, 8, 17, 0, 0, 0, DateTimeKind.Utc);
        var attributes = new BsonDocument { { "releaseDate", new BsonDateTime(released) } };

        RoundTrip(ManifestWith(attributes)).Items[0].Attributes["releaseDate"]
            .ToUniversalTime().Should().Be(released);
    }

    [Fact]
    public void Round_trip_preserves_object_ids_and_enums()
    {
        var original = ManifestWith([]);

        var result = RoundTrip(original);

        result.SchemaVersion.Should().Be(ArchiveManifest.CurrentSchemaVersion);
        result.Categories[0].Id.Should().Be(original.Categories[0].Id);
        result.Categories[0].Kind.Should().Be(CategoryKind.Physical);
        result.Categories[0].Fields[0].Type.Should().Be(FieldType.Text);
        result.Items[0].CategoryId.Should().Be(original.Categories[0].Id);
        result.Items[0].Source.Should().Be(ItemSource.Manual);
        result.Items[0].Tags.Should().Equal("jazz");
    }

    [Fact]
    public void Written_json_uses_canonical_extended_json_markers()
    {
        using var buffer = new MemoryStream();
        ArchiveManifestSerializer.Write(buffer, ManifestWith([]));

        var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        json.Should().Contain("$oid").And.Contain("$date");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~ArchiveManifestSerializerTests"`
Expected: 編譯失敗，找不到 `MyCollection.Application.Transfer`

- [ ] **Step 3: 建立封存檔模型**

建立 `src/MyCollection.Application/Transfer/ArchiveManifest.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 封存檔的 manifest。刻意不含 ownerId：它由各機器註冊時各自產生，
/// 帶進封存檔只會誤導，匯入端一律改用當前登入者的 id。
/// </summary>
public sealed class ArchiveManifest
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>zip 內 manifest 的固定檔名。</summary>
    public const string FileName = "manifest.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime ExportedAt { get; set; }

    public List<ArchiveCategory> Categories { get; set; } = [];
    public List<ArchiveItem> Items { get; set; } = [];
    public List<ArchiveShareLink> ShareLinks { get; set; } = [];
}

public sealed class ArchiveCategory
{
    public ObjectId Id { get; set; }
    public required string Name { get; set; }
    public string Icon { get; set; } = "box";
    public CategoryKind Kind { get; set; }
    public List<CategoryField> Fields { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ArchiveImage
{
    public required string Id { get; set; }
    public bool IsPrimary { get; set; }
    public int Order { get; set; }

    /// <summary>zip 內的相對路徑，格式為 media/{itemId}/{imageId}.webp，僅 full 尺寸。</summary>
    public required string File { get; set; }
}

public sealed class ArchiveItem
{
    public ObjectId Id { get; set; }
    public ObjectId CategoryId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsShowcased { get; set; }
    public ItemSource Source { get; set; }
    public Acquisition? Acquisition { get; set; }
    public BsonDocument Attributes { get; set; } = [];
    public List<ArchiveImage> Images { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ArchiveShareLink
{
    public required string Slug { get; set; }
    public ShareScope Scope { get; set; }
    public List<ObjectId> IncludeCategoryIds { get; set; } = [];
    public bool IncludePrice { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>zip 內的媒體路徑組裝，匯出與匯入必須用同一份規則。</summary>
public static class ArchivePaths
{
    public static string Image(ObjectId itemId, string imageId) => $"media/{itemId}/{imageId}.webp";
}
```

- [ ] **Step 4: 建立序列化器**

建立 `src/MyCollection.Application/Transfer/ArchiveManifestSerializer.cs`：

```csharp
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace MyCollection.Application.Transfer;

/// <summary>
/// manifest 一律走 MongoDB 的 Canonical Extended JSON，不用 System.Text.Json。
///
/// ArchiveItem.Attributes 是 BsonDocument，內容由使用者自定的 category schema 決定，
/// 可能含 Decimal128、DateTime、Int64。一般 JSON 會把這些壓成 string 或 number，
/// 來回一趟就失真。Canonical 模式輸出 $oid / $date / $numberDecimal，保證無損。
///
/// 兩種序列化器混用只會讓邊界出錯的機率倍增，所以整份 manifest 統一用這一個。
/// </summary>
public static class ArchiveManifestSerializer
{
    private static readonly JsonWriterSettings WriterSettings = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
        Indent = true
    };

    public static void Write(Stream destination, ArchiveManifest manifest)
    {
        var json = manifest.ToBsonDocument().ToJson(WriterSettings);

        using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(json);
    }

    /// <summary>內容不是合法的 Extended JSON 時擲 <see cref="FormatException"/>。</summary>
    public static ArchiveManifest Read(Stream source)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var json = reader.ReadToEnd();

        return BsonSerializer.Deserialize<ArchiveManifest>(BsonDocument.Parse(json));
    }
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~ArchiveManifestSerializerTests"`
Expected: PASS，5 passed

- [ ] **Step 6: 提交**

```bash
git add src/MyCollection.Application/Transfer/ tests/MyCollection.Tests/Unit/ArchiveManifestSerializerTests.cs
git commit -m "feat(transfer): add archive manifest model and canonical extended json serializer"
```

---

## Task 4: `ITransferRepository` 與 Mongo 實作

**Files:**
- Create: `src/MyCollection.Application/Transfer/ITransferRepository.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoTransferRepository.cs`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs:36`
- Test: `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs`（本任務僅建立檔案骨架，端點測試在 Task 6／11 補齊）

本任務的正確性由 Task 6 與 Task 11 的整合測試覆蓋——這些方法全是薄薄的 Mongo 查詢，單獨為它們寫 mock 測試只會測到 Moq 自己。

- [ ] **Step 1: 定義介面**

建立 `src/MyCollection.Application/Transfer/ITransferRepository.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯出／匯入專用的跨 collection 存取。與一般 Repository 分開，
/// 因為它需要「只取自訂品類」「排除 Steam 來源」這類匯出獨有的條件，
/// 混進 ICategoryRepository/IItemRepository 會汙染日常查詢的語意。
///
/// 所有方法的 filter 一律以 IUserContext.UserId 起頭。
/// </summary>
public interface ITransferRepository
{
    // ---- 匯出 ----

    /// <summary>只取自訂品類（OwnerId == me）。系統品類 OwnerId 為 null，自動排除。</summary>
    Task<IReadOnlyList<Category>> ListOwnCategoriesAsync(CancellationToken ct);

    /// <summary>Source != Steam。OpenGraph 來源視為手建，要匯出。</summary>
    Task<IReadOnlyList<Item>> ListExportableItemsAsync(CancellationToken ct);

    Task<IReadOnlyList<ShareLink>> ListOwnShareLinksAsync(CancellationToken ct);

    // ---- 匯入 ----

    /// <summary>Source == Steam。匯入時保留，用於判定孤兒品類。</summary>
    Task<IReadOnlyList<Item>> ListSteamItemsAsync(CancellationToken ct);

    Task DeleteNonSteamItemsAsync(CancellationToken ct);

    Task DeleteOwnShareLinksAsync(CancellationToken ct);

    Task DeleteCategoriesAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct);

    /// <summary>把指定 item 的 CategoryId 改指到 targetCategoryId。</summary>
    Task RepointItemsAsync(IReadOnlyList<ObjectId> itemIds, ObjectId targetCategoryId, CancellationToken ct);

    Task InsertCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct);

    Task InsertItemsAsync(IReadOnlyList<Item> items, CancellationToken ct);

    Task InsertShareLinksAsync(IReadOnlyList<ShareLink> links, CancellationToken ct);

    /// <summary>slug 全域唯一，此查詢刻意不套 ownerId 過濾。</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
}
```

- [ ] **Step 2: Mongo 實作**

建立 `src/MyCollection.Infrastructure/Mongo/MongoTransferRepository.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoTransferRepository(MongoContext context, IUserContext userContext) : ITransferRepository
{
    private ObjectId Owner => userContext.UserId;

    private FilterDefinition<Item> OwnItems =>
        Builders<Item>.Filter.Eq(x => x.OwnerId, Owner);

    public async Task<IReadOnlyList<Category>> ListOwnCategoriesAsync(CancellationToken ct) =>
        await context.Categories
            .Find(Builders<Category>.Filter.Eq(x => x.OwnerId, Owner))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Item>> ListExportableItemsAsync(CancellationToken ct) =>
        await context.Items
            .Find(OwnItems & Builders<Item>.Filter.Ne(x => x.Source, ItemSource.Steam))
            .SortBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ShareLink>> ListOwnShareLinksAsync(CancellationToken ct) =>
        await context.ShareLinks
            .Find(Builders<ShareLink>.Filter.Eq(x => x.OwnerId, Owner))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Item>> ListSteamItemsAsync(CancellationToken ct) =>
        await context.Items
            .Find(OwnItems & Builders<Item>.Filter.Eq(x => x.Source, ItemSource.Steam))
            .ToListAsync(ct);

    public async Task DeleteNonSteamItemsAsync(CancellationToken ct) =>
        await context.Items.DeleteManyAsync(
            OwnItems & Builders<Item>.Filter.Ne(x => x.Source, ItemSource.Steam), ct);

    public async Task DeleteOwnShareLinksAsync(CancellationToken ct) =>
        await context.ShareLinks.DeleteManyAsync(
            Builders<ShareLink>.Filter.Eq(x => x.OwnerId, Owner), ct);

    public async Task DeleteCategoriesAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await context.Categories.DeleteManyAsync(
            Builders<Category>.Filter.Eq(x => x.OwnerId, Owner)
            & Builders<Category>.Filter.In(x => x.Id, ids), ct);
    }

    public async Task RepointItemsAsync(
        IReadOnlyList<ObjectId> itemIds, ObjectId targetCategoryId, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        await context.Items.UpdateManyAsync(
            OwnItems & Builders<Item>.Filter.In(x => x.Id, itemIds),
            Builders<Item>.Update.Set(x => x.CategoryId, targetCategoryId),
            cancellationToken: ct);
    }

    public async Task InsertCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct)
    {
        if (categories.Count == 0)
        {
            return;
        }

        await context.Categories.InsertManyAsync(categories, cancellationToken: ct);
    }

    public async Task InsertItemsAsync(IReadOnlyList<Item> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return;
        }

        await context.Items.InsertManyAsync(items, cancellationToken: ct);
    }

    public async Task InsertShareLinksAsync(IReadOnlyList<ShareLink> links, CancellationToken ct)
    {
        if (links.Count == 0)
        {
            return;
        }

        await context.ShareLinks.InsertManyAsync(links, cancellationToken: ct);
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        await context.ShareLinks
            .Find(Builders<ShareLink>.Filter.Eq(x => x.Slug, slug))
            .AnyAsync(ct);
}
```

- [ ] **Step 3: 註冊 DI**

在 `src/MyCollection.Infrastructure/DependencyInjection.cs` 的 `services.AddScoped<IShareLinkRepository, MongoShareLinkRepository>();` 之後加入：

```csharp
        services.AddScoped<ITransferRepository, MongoTransferRepository>();
```

並在檔案 using 區加入 `using MyCollection.Application.Transfer;`（若尚未存在）。

- [ ] **Step 4: 確認建置通過**

Run: `dotnet build MyCollection.slnx`
Expected: Build succeeded，0 Error

- [ ] **Step 5: 提交**

```bash
git add src/MyCollection.Application/Transfer/ITransferRepository.cs \
        src/MyCollection.Infrastructure/Mongo/MongoTransferRepository.cs \
        src/MyCollection.Infrastructure/DependencyInjection.cs
git commit -m "feat(transfer): add transfer repository for export and import queries"
```

---

## Task 5: `ArchiveWriter` — 匯出核心

抽成獨立類別而非直接寫在 MediatR handler 裡，因為 Task 10 的自動備份要用同一支邏輯寫進備份檔，而不是走 HTTP。

**Files:**
- Create: `src/MyCollection.Application/Transfer/ArchiveWriter.cs`
- Create: `src/MyCollection.Application/Transfer/ArchiveMapper.cs`
- Test: `tests/MyCollection.Tests/Unit/ArchiveWriterTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Unit/ArchiveWriterTests.cs`：

```csharp
using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ArchiveWriterTests
{
    private readonly Mock<ITransferRepository> _repository = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();
    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();
    private static readonly ObjectId ItemId = ObjectId.GenerateNewId();

    public ArchiveWriterTests()
    {
        _repository.Setup(r => r.ListOwnCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Category
            {
                Id = CategoryId,
                OwnerId = OwnerId,
                Name = "黑膠唱片",
                Kind = CategoryKind.Physical,
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]);

        _repository.Setup(r => r.ListExportableItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Item
            {
                Id = ItemId,
                OwnerId = OwnerId,
                CategoryId = CategoryId,
                Name = "Kind of Blue",
                Images =
                [
                    new ItemImage
                    {
                        Id = "img1",
                        Path = $"{OwnerId}/{ItemId}/img1-full.webp",
                        CardPath = $"{OwnerId}/{ItemId}/img1-card.webp",
                        ThumbPath = $"{OwnerId}/{ItemId}/img1-thumb.webp",
                        IsPrimary = true,
                        Order = 0
                    }
                ],
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]);

        _repository.Setup(r => r.ListOwnShareLinksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([9, 9, 9]));
    }

    private ArchiveWriter CreateSut() => new(_repository.Object, _storage.Object, _time);

    private async Task<ZipArchive> WriteAsync()
    {
        var buffer = new MemoryStream();
        await CreateSut().WriteAsync(buffer, CancellationToken.None);
        buffer.Position = 0;

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    [Fact]
    public async Task Writes_manifest_and_only_the_full_size_image()
    {
        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            ArchiveManifest.FileName,
            ArchivePaths.Image(ItemId, "img1"));
    }

    [Fact]
    public async Task Reads_the_full_size_path_from_storage_not_card_or_thumb()
    {
        using var archive = await WriteAsync();

        _storage.Verify(
            s => s.OpenReadAsync($"{OwnerId}/{ItemId}/img1-full.webp", It.IsAny<CancellationToken>()),
            Times.Once);
        _storage.Verify(
            s => s.OpenReadAsync(It.Is<string>(p => p.Contains("-card") || p.Contains("-thumb")),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Manifest_omits_owner_id_and_uses_archive_relative_image_paths()
    {
        using var archive = await WriteAsync();

        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var json = System.Text.Encoding.UTF8.GetString(copy.ToArray());
        json.Should().NotContain(OwnerId.ToString());

        copy.Position = 0;
        var manifest = ArchiveManifestSerializer.Read(copy);

        manifest.SchemaVersion.Should().Be(ArchiveManifest.CurrentSchemaVersion);
        manifest.ExportedAt.Should().Be(new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc));
        manifest.Items[0].Images[0].File.Should().Be(ArchivePaths.Image(ItemId, "img1"));
        manifest.Items[0].Images[0].IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_image_file_is_still_listed_in_manifest_and_simply_absent_from_the_zip()
    {
        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        using var archive = await WriteAsync();

        archive.Entries.Select(e => e.FullName).Should().Equal(ArchiveManifest.FileName);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~ArchiveWriterTests"`
Expected: 編譯失敗，找不到 `ArchiveWriter`

- [ ] **Step 3: 實作**

建立 `src/MyCollection.Application/Transfer/ArchiveWriter.cs`：

```csharp
using System.IO.Compression;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯出核心。寫入任意 Stream，因此匯出端點（HttpResponse.Body）與
/// 匯入前的自動備份（備份檔）可以共用同一份邏輯。
///
/// 單趟串流，不落暫存檔也不整包進記憶體，所以耗用與收藏規模無關。
/// </summary>
public sealed class ArchiveWriter(
    ITransferRepository repository,
    IFileStorage storage,
    TimeProvider timeProvider)
{
    public async Task WriteAsync(Stream destination, CancellationToken ct)
    {
        var categories = await repository.ListOwnCategoriesAsync(ct);
        var items = await repository.ListExportableItemsAsync(ct);
        var shareLinks = await repository.ListOwnShareLinksAsync(ct);

        var manifest = new ArchiveManifest
        {
            ExportedAt = timeProvider.GetUtcNow().UtcDateTime,
            Categories = [.. categories.Select(ArchiveMapper.ToArchive)],
            Items = [.. items.Select(ArchiveMapper.ToArchive)],
            ShareLinks = [.. shareLinks.Select(ArchiveMapper.ToArchive)]
        };

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await using (var manifestEntry = archive.CreateEntry(ArchiveManifest.FileName).Open())
        {
            ArchiveManifestSerializer.Write(manifestEntry, manifest);
        }

        foreach (var item in items)
        {
            foreach (var image in item.Images)
            {
                // 檔案遺失不由匯出端處理：manifest 照 DB 寫，zip 內少一個 entry，
                // 由匯入端偵測並降級為 warning。這讓匯出維持單趟串流，
                // 不必為了預檢而把每個檔案開兩次。
                await using var source = await storage.OpenReadAsync(image.Path, ct);
                if (source is null)
                {
                    continue;
                }

                await using var entry = archive.CreateEntry(ArchivePaths.Image(item.Id, image.Id)).Open();
                await source.CopyToAsync(entry, ct);
            }
        }
    }

}
```

- [ ] **Step 4: 建立 `ArchiveMapper`**

Domain 與封存檔型別的雙向對應集中在這一個檔案。正向只有 `ArchiveWriter` 用，反向 Task 8 的 validator 與 Task 10 的匯入 handler 都要用——把同一份欄位對應手寫三遍，在一個必須跨版本讀得懂的磁碟格式上是實打實的漂移風險。

建立 `src/MyCollection.Application/Transfer/ArchiveMapper.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// Domain 實體與封存檔型別之間的唯一對應點。
///
/// 兩個方向放在一起是刻意的：欄位對應寫錯的後果是資料悄悄遺失，
/// 而不是編譯失敗。放在同一個檔案裡，加欄位時漏掉另一邊會立刻看得出來。
/// </summary>
public static class ArchiveMapper
{
    // ---- Domain → 封存檔 ----

    public static ArchiveCategory ToArchive(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Icon = category.Icon,
        Kind = category.Kind,
        Fields = [.. category.Fields.Select(ToArchive)],
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt
    };

    public static ArchiveCategoryField ToArchive(CategoryField field) => new()
    {
        Key = field.Key,
        Label = field.Label,
        Type = field.Type,
        Options = field.Options,
        Required = field.Required,
        Searchable = field.Searchable,
        ShowOnCard = field.ShowOnCard
    };

    public static ArchiveAcquisition? ToArchive(Acquisition? acquisition) => acquisition is null
        ? null
        : new ArchiveAcquisition
        {
            AcquiredAt = acquisition.AcquiredAt,
            Vendor = acquisition.Vendor,
            Price = acquisition.Price is null
                ? null
                : new ArchiveMoney { Amount = acquisition.Price.Amount, Currency = acquisition.Price.Currency }
        };

    public static ArchiveItem ToArchive(Item item) => new()
    {
        Id = item.Id,
        CategoryId = item.CategoryId,
        Name = item.Name,
        Description = item.Description,
        Tags = item.Tags,
        IsShowcased = item.IsShowcased,
        Source = item.Source,
        Acquisition = ToArchive(item.Acquisition),
        Attributes = item.Attributes,
        Images =
        [
            .. item.Images.Select(image => new ArchiveImage
            {
                Id = image.Id,
                IsPrimary = image.IsPrimary,
                Order = image.Order,
                File = ArchivePaths.Image(item.Id, image.Id)
            })
        ],
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };

    public static ArchiveShareLink ToArchive(ShareLink link) => new()
    {
        Slug = link.Slug,
        Scope = link.Scope,
        IncludeCategoryIds = link.IncludeCategoryIds,
        IncludePrice = link.IncludePrice,
        ExpiresAt = link.ExpiresAt,
        CreatedAt = link.CreatedAt
    };

    // ---- 封存檔 → Domain ----

    /// <param name="ownerId">
    /// 封存檔不帶 ownerId，一律由呼叫端指定。驗證階段只需要 schema，可傳 null。
    /// </param>
    public static Category ToDomain(ArchiveCategory source, ObjectId? ownerId) => new()
    {
        Id = source.Id,
        OwnerId = ownerId,
        Name = source.Name,
        Icon = source.Icon,
        Kind = source.Kind,
        Fields = [.. source.Fields.Select(ToDomain)],
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    public static CategoryField ToDomain(ArchiveCategoryField source) => new()
    {
        Key = source.Key,
        Label = source.Label,
        Type = source.Type,
        Options = source.Options,
        Required = source.Required,
        Searchable = source.Searchable,
        ShowOnCard = source.ShowOnCard
    };

    public static Acquisition? ToDomain(ArchiveAcquisition? source) => source is null
        ? null
        : new Acquisition
        {
            AcquiredAt = source.AcquiredAt,
            Vendor = source.Vendor,
            Price = source.Price is null ? null : new Money(source.Price.Amount, source.Price.Currency)
        };
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~ArchiveWriterTests"`
Expected: PASS，4 passed

- [ ] **Step 6: 提交**

```bash
git add src/MyCollection.Application/Transfer/ArchiveWriter.cs \
        src/MyCollection.Application/Transfer/ArchiveMapper.cs \
        tests/MyCollection.Tests/Unit/ArchiveWriterTests.cs
git commit -m "feat(transfer): add archive writer and domain/archive mapper"
```

---

## Task 6: `GET /export` 端點

**Files:**
- Create: `src/MyCollection.Application/Transfer/ExportCommand.cs`
- Create: `src/MyCollection.Api/Endpoints/TransferEndpoints.cs`
- Modify: `src/MyCollection.Api/Program.cs:75`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs`：

```csharp
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Application.Transfer;
using MyCollection.Tests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class TransferEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "transfer@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static MultipartFormDataContent PngUpload()
    {
        using var image = new Image<Rgba32>(800, 600);
        var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        return new MultipartFormDataContent { { content, "file", "test.png" } };
    }

    private async Task<CategoryDto> CreateCategoryAsync(string name = "黑膠唱片") =>
        (await (await _client.PostAsJsonAsync("/categories", new
        {
            name,
            icon = "disc-3",
            kind = "Physical",
            fields = new[]
            {
                new { key = "label", label = "廠牌", type = "Text", required = false, searchable = true, showOnCard = true }
            }
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

    private async Task<ItemDto> CreateItemAsync(string categoryId, string name = "Kind of Blue") =>
        (await (await _client.PostAsJsonAsync("/items", new
        {
            categoryId,
            name,
            description = (string?)null,
            tags = new[] { "jazz" },
            isShowcased = true,
            attributes = new { label = "Columbia" }
        })).Content.ReadFromJsonAsync<ItemDto>())!;

    private static async Task<ZipArchive> ReadArchiveAsync(HttpResponseMessage response)
    {
        var buffer = new MemoryStream(await response.Content.ReadAsByteArrayAsync());

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    [Fact]
    public async Task Export_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Export_returns_a_zip_containing_manifest_and_images()
    {
        var category = await CreateCategoryAsync();
        var item = await CreateItemAsync(category.Id);
        (await _client.PostAsync($"/items/{item.Id}/images", PngUpload())).EnsureSuccessStatusCode();

        var response = await _client.GetAsync("/export");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".zip");

        using var archive = await ReadArchiveAsync(response);

        archive.GetEntry(ArchiveManifest.FileName).Should().NotBeNull();
        archive.Entries.Should().Contain(e => e.FullName.StartsWith("media/") && e.FullName.EndsWith(".webp"));

        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var manifest = ArchiveManifestSerializer.Read(copy);
        manifest.Categories.Should().ContainSingle(c => c.Name == "黑膠唱片");
        manifest.Items.Should().ContainSingle(i => i.Name == "Kind of Blue");
        manifest.Items[0].Images.Should().ContainSingle();
        manifest.Items[0].Attributes["label"].AsString.Should().Be("Columbia");
    }

    [Fact]
    public async Task Export_excludes_other_users_data()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id);

        using var stranger = await AuthenticatedClient.CreateAsync(_factory, "stranger@example.com");
        var response = await stranger.GetAsync("/export");
        response.EnsureSuccessStatusCode();

        using var archive = await ReadArchiveAsync(response);
        await using var manifestStream = archive.GetEntry(ArchiveManifest.FileName)!.Open();
        using var copy = new MemoryStream();
        await manifestStream.CopyToAsync(copy);
        copy.Position = 0;

        var manifest = ArchiveManifestSerializer.Read(copy);
        manifest.Categories.Should().BeEmpty();
        manifest.Items.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~TransferEndpointsTests"`
Expected: 編譯失敗或 404

- [ ] **Step 3: 建立 MediatR command**

建立 `src/MyCollection.Application/Transfer/ExportCommand.cs`：

```csharp
using MediatR;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 直接寫進呼叫端提供的 Stream。端點傳 HttpResponse.Body，
/// 因此整個匯出過程不落暫存檔、不整包進記憶體。
/// </summary>
public record ExportArchiveCommand(Stream Destination) : IRequest;

public sealed class ExportArchiveCommandHandler(ArchiveWriter writer) : IRequestHandler<ExportArchiveCommand>
{
    public Task Handle(ExportArchiveCommand request, CancellationToken cancellationToken) =>
        writer.WriteAsync(request.Destination, cancellationToken);
}
```

- [ ] **Step 4: 建立端點**

建立 `src/MyCollection.Api/Endpoints/TransferEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Transfer;

namespace MyCollection.Api.Endpoints;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").WithTags("Transfer").RequireAuthorization();

        group.MapGet("/export", async (HttpContext http, ISender sender, TimeProvider time, CancellationToken ct) =>
        {
            var fileName = $"mycollection-{time.GetUtcNow():yyyyMMdd-HHmmss}.zip";

            http.Response.ContentType = "application/zip";
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

            // 串流開始後就無法再改 status code，中途失敗只能斷線。
            await sender.Send(new ExportArchiveCommand(http.Response.Body), ct);
        });

        return app;
    }
}
```

- [ ] **Step 5: 註冊端點與 DI**

在 `src/MyCollection.Api/Program.cs:75` 的 `app.MapShareEndpoints();` 之後加入：

```csharp
app.MapTransferEndpoints();
```

在 `src/MyCollection.Infrastructure/DependencyInjection.cs` 的 `services.AddScoped<ITransferRepository, MongoTransferRepository>();` 之後加入：

```csharp
        services.AddScoped<ArchiveWriter>();
```

`ArchiveWriter` 依賴 scoped 的 `ITransferRepository`，所以必須是 scoped，不能是 singleton。

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~TransferEndpointsTests"`
Expected: PASS，3 passed

- [ ] **Step 7: 提交**

```bash
git add src/MyCollection.Application/Transfer/ExportCommand.cs \
        src/MyCollection.Api/Endpoints/TransferEndpoints.cs \
        src/MyCollection.Api/Program.cs \
        src/MyCollection.Infrastructure/DependencyInjection.cs \
        tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs
git commit -m "feat(api): add streaming collection export endpoint"
```

---

## Task 7: `CategoryReconciler`

spec §6.2 第 3 步的判定規則。抽成純函式，因為它是整個匯入流程裡唯一有分支複雜度的部分，混在 handler 裡就只能靠整合測試碰運氣覆蓋。

**Files:**
- Create: `src/MyCollection.Application/Transfer/CategoryReconciler.cs`
- Test: `tests/MyCollection.Tests/Unit/CategoryReconcilerTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Unit/CategoryReconcilerTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class CategoryReconcilerTests
{
    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();

    private static Category Local(ObjectId id, string name) => new()
    {
        Id = id,
        OwnerId = OwnerId,
        Name = name,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ArchiveCategory Archived(ObjectId id, string name) => new()
    {
        Id = id,
        Name = name,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Item SteamItem(ObjectId categoryId) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = OwnerId,
        CategoryId = categoryId,
        Name = "Half-Life",
        Source = ItemSource.Steam,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Category_present_in_archive_is_deleted_so_step_four_can_rewrite_it()
    {
        var id = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan([Local(id, "黑膠唱片")], [Archived(id, "黑膠唱片")], []);

        plan.Delete.Should().Equal(id);
        plan.Repoints.Should().BeEmpty();
        plan.KeptOrphanNames.Should().BeEmpty();
    }

    [Fact]
    public void Category_absent_from_archive_and_unreferenced_is_deleted()
    {
        var id = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan([Local(id, "公仔")], [], []);

        plan.Delete.Should().Equal(id);
    }

    [Fact]
    public void Orphan_category_with_a_same_named_archive_entry_is_repointed_then_deleted()
    {
        var localId = ObjectId.GenerateNewId();
        var archiveId = ObjectId.GenerateNewId();
        var steamItem = SteamItem(localId);

        var plan = CategoryReconciler.Plan(
            [Local(localId, "數位遊戲")],
            [Archived(archiveId, "數位遊戲")],
            [steamItem]);

        plan.Repoints.Should().ContainSingle();
        plan.Repoints[0].TargetCategoryId.Should().Be(archiveId);
        plan.Repoints[0].ItemIds.Should().Equal(steamItem.Id);
        plan.Delete.Should().Equal(localId);
        plan.KeptOrphanNames.Should().BeEmpty();
    }

    [Fact]
    public void Orphan_category_without_a_same_named_archive_entry_is_kept_and_reported()
    {
        var localId = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan(
            [Local(localId, "數位遊戲")],
            [Archived(ObjectId.GenerateNewId(), "黑膠唱片")],
            [SteamItem(localId)]);

        plan.Delete.Should().BeEmpty();
        plan.Repoints.Should().BeEmpty();
        plan.KeptOrphanNames.Should().Equal("數位遊戲");
    }

    [Fact]
    public void Archive_membership_wins_over_the_orphan_rule()
    {
        // 同一個 id 既在封存檔中、又被 Steam item 引用：
        // 第 4 步會以封存檔版本重新寫入同一個 id，Steam item 的引用因此仍然有效，
        // 不需要 repoint。
        var id = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan([Local(id, "數位遊戲")], [Archived(id, "數位遊戲")], [SteamItem(id)]);

        plan.Delete.Should().Equal(id);
        plan.Repoints.Should().BeEmpty();
        plan.KeptOrphanNames.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~CategoryReconcilerTests"`
Expected: 編譯失敗，找不到 `CategoryReconciler`

- [ ] **Step 3: 實作**

建立 `src/MyCollection.Application/Transfer/CategoryReconciler.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

public sealed record CategoryRepoint(IReadOnlyList<ObjectId> ItemIds, ObjectId TargetCategoryId);

public sealed record CategoryPlan(
    IReadOnlyList<ObjectId> Delete,
    IReadOnlyList<CategoryRepoint> Repoints,
    IReadOnlyList<string> KeptOrphanNames);

/// <summary>
/// 決定匯入時本機自訂品類的去留（spec §6.2 第 3 步）。純函式，不碰 IO。
///
/// 「同名改指」不是裝飾：兩台機器各自跑 Steam 同步時，
/// SyncCommand.EnsureDigitalCategoryAsync 會各自建立一個 id 不同的自訂「數位遊戲」品類。
/// 沒有這步，每來回匯入一次就多累積一個同名品類。名稱是唯一可用的錨點——ObjectId 天生對不上。
/// </summary>
public static class CategoryReconciler
{
    public static CategoryPlan Plan(
        IReadOnlyList<Category> localOwnCategories,
        IReadOnlyList<ArchiveCategory> archiveCategories,
        IReadOnlyList<Item> steamItems)
    {
        var archiveIds = archiveCategories.Select(c => c.Id).ToHashSet();
        var archiveByName = archiveCategories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var itemsByCategory = steamItems
            .GroupBy(i => i.CategoryId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ObjectId>)[.. g.Select(i => i.Id)]);

        var delete = new List<ObjectId>();
        var repoints = new List<CategoryRepoint>();
        var keptOrphanNames = new List<string>();

        foreach (var local in localOwnCategories)
        {
            // 在封存檔中 → 刪掉，第 4 步會以同一個 id 重新寫入封存檔版本。
            // 即使有 Steam item 引用它也無妨，引用的 id 不變。
            if (archiveIds.Contains(local.Id))
            {
                delete.Add(local.Id);
                continue;
            }

            if (!itemsByCategory.TryGetValue(local.Id, out var referencingItems))
            {
                delete.Add(local.Id);
                continue;
            }

            if (archiveByName.TryGetValue(local.Name, out var target))
            {
                repoints.Add(new CategoryRepoint(referencingItems, target));
                delete.Add(local.Id);
                continue;
            }

            keptOrphanNames.Add(local.Name);
        }

        return new CategoryPlan(delete, repoints, keptOrphanNames);
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~CategoryReconcilerTests"`
Expected: PASS，5 passed

- [ ] **Step 5: 提交**

```bash
git add src/MyCollection.Application/Transfer/CategoryReconciler.cs \
        tests/MyCollection.Tests/Unit/CategoryReconcilerTests.cs
git commit -m "feat(transfer): add category reconciler for import cleanup rules"
```

---

## Task 8: `ArchiveValidator`

階段一驗證。失敗時 handler 擲 `FluentValidation.ValidationException`，既有的 `GlobalExceptionHandler` 會轉成 400 加 `errors` 字典。

**Files:**
- Create: `src/MyCollection.Application/Transfer/ArchiveValidator.cs`
- Test: `tests/MyCollection.Tests/Unit/ArchiveValidatorTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Unit/ArchiveValidatorTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Items;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ArchiveValidatorTests
{
    private static readonly ObjectId SystemCategoryId = ObjectId.Parse("000000000000000000000002");

    private readonly ArchiveValidator _sut = new(new AttributeValidator());

    private static ArchiveCategory Category(ObjectId id, string name = "黑膠唱片") => new()
    {
        Id = id,
        Name = name,
        Fields = [new ArchiveCategoryField { Key = "label", Label = "廠牌", Type = FieldType.Text }],
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ArchiveItem Item(ObjectId categoryId, BsonDocument? attributes = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        CategoryId = categoryId,
        Name = "Kind of Blue",
        Attributes = attributes ?? [],
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Category SystemCategory() => new()
    {
        Id = SystemCategoryId,
        OwnerId = null,
        Name = "數位遊戲",
        Kind = CategoryKind.Digital,
        Fields = [],
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Valid_manifest_produces_no_failures()
    {
        var categoryId = ObjectId.GenerateNewId();
        var manifest = new ArchiveManifest
        {
            Categories = [Category(categoryId)],
            Items = [Item(categoryId, new BsonDocument { { "label", "Columbia" } })]
        };

        _sut.Validate(manifest, [SystemCategory()]).Should().BeEmpty();
    }

    [Fact]
    public void Item_pointing_at_a_system_category_is_accepted()
    {
        var manifest = new ArchiveManifest { Items = [Item(SystemCategoryId)] };

        _sut.Validate(manifest, [SystemCategory()]).Should().BeEmpty();
    }

    [Fact]
    public void Item_pointing_at_an_unknown_category_is_rejected()
    {
        var manifest = new ArchiveManifest { Items = [Item(ObjectId.GenerateNewId())] };

        _sut.Validate(manifest, [SystemCategory()])
            .Should().ContainSingle().Which.ErrorMessage.Should().Contain("category");
    }

    [Fact]
    public void Attributes_that_break_the_category_schema_are_rejected()
    {
        var categoryId = ObjectId.GenerateNewId();
        var manifest = new ArchiveManifest
        {
            Categories = [Category(categoryId)],
            Items = [Item(categoryId, new BsonDocument { { "label", 42 } })]
        };

        _sut.Validate(manifest, [SystemCategory()])
            .Should().ContainSingle().Which.PropertyName.Should().Contain("label");
    }

    [Fact]
    public void Blank_names_are_rejected_for_both_categories_and_items()
    {
        var categoryId = ObjectId.GenerateNewId();
        var manifest = new ArchiveManifest
        {
            Categories = [Category(categoryId, name: "  ")],
            Items = [new ArchiveItem
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = categoryId,
                Name = "",
                CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }]
        };

        _sut.Validate(manifest, [SystemCategory()]).Should().HaveCount(2);
    }

    [Fact]
    public void All_failures_are_reported_not_just_the_first()
    {
        var manifest = new ArchiveManifest
        {
            Items = [Item(ObjectId.GenerateNewId()), Item(ObjectId.GenerateNewId())]
        };

        _sut.Validate(manifest, [SystemCategory()]).Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~ArchiveValidatorTests"`
Expected: 編譯失敗，找不到 `ArchiveValidator`

- [ ] **Step 3: 實作**

建立 `src/MyCollection.Application/Transfer/ArchiveValidator.cs`：

```csharp
using FluentValidation.Results;
using MongoDB.Bson;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯入階段一。回傳全部失敗（不短路），讓使用者一次看完要修什麼。
/// 這一步跑完之前不得寫入任何資料。
/// </summary>
public sealed class ArchiveValidator(IAttributeValidator attributeValidator)
{
    /// <param name="systemCategories">
    /// 系統品類（OwnerId == null）。它們的 id 是跨機器固定的常數，
    /// 引用它們的 item 不需要在封存檔中帶著品類定義。
    /// </param>
    public IReadOnlyList<ValidationFailure> Validate(
        ArchiveManifest manifest,
        IReadOnlyList<Category> systemCategories)
    {
        // schemaVersion 不在這裡檢查：ArchiveManifestSerializer.Read 會在反序列化之前
        // 就擋掉版本不符的封存檔並擲 InvalidArchiveException。放在這裡只會是永遠不成立的死碼。
        var failures = new List<ValidationFailure>();

        for (var i = 0; i < manifest.Categories.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(manifest.Categories[i].Name))
            {
                failures.Add(new ValidationFailure($"categories[{i}].name", "Category name must not be blank."));
            }
        }

        var schemaById = new Dictionary<ObjectId, Category>();

        foreach (var category in systemCategories)
        {
            schemaById[category.Id] = category;
        }

        foreach (var category in manifest.Categories)
        {
            // 驗證只需要 schema（Fields），OwnerId 無關緊要。
            schemaById[category.Id] = ArchiveMapper.ToDomain(category, ownerId: null);
        }

        for (var i = 0; i < manifest.Items.Count; i++)
        {
            var item = manifest.Items[i];

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                failures.Add(new ValidationFailure($"items[{i}].name", "Item name must not be blank."));
            }

            if (!schemaById.TryGetValue(item.CategoryId, out var category))
            {
                failures.Add(new ValidationFailure(
                    $"items[{i}].categoryId",
                    $"Item '{item.Name}' points at category '{item.CategoryId}', " +
                    "which is neither in the archive nor a system category."));

                continue;
            }

            foreach (var failure in attributeValidator.Validate(category, item.Attributes))
            {
                failures.Add(new ValidationFailure(
                    $"items[{i}].{failure.PropertyName}", failure.ErrorMessage));
            }
        }

        return failures;
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~ArchiveValidatorTests"`
Expected: PASS，6 passed

`schemaVersion` 沒有對應的測試，因為它不歸這裡管——`ArchiveManifestSerializer.Read` 會在反序列化之前就擋掉版本不符並擲 `InvalidArchiveException`，該行為已由 `ArchiveManifestSerializerTests` 覆蓋。

- [ ] **Step 5: 提交**

```bash
git add src/MyCollection.Application/Transfer/ArchiveValidator.cs \
        tests/MyCollection.Tests/Unit/ArchiveValidatorTests.cs
git commit -m "feat(transfer): add archive validator for import stage one"
```

---

## Task 9: 備份存放區

備份**不得**經過 `IFileStorage`。`GET /media/{**path}` 是 `AllowAnonymous`，備份寫在 media root 底下等於把整份收藏資料庫掛在匿名端點上——`ownerId` 從公開分享頁的圖片 URL 就看得到。

**Files:**
- Create: `src/MyCollection.Application/Common/IBackupStore.cs`
- Create: `src/MyCollection.Infrastructure/Storage/LocalBackupStore.cs`
- Modify: `src/MyCollection.Infrastructure/Storage/StorageOptions.cs`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyCollection.Tests/Unit/LocalBackupStoreTests.cs`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/MyCollection.Tests/Unit/LocalBackupStoreTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MyCollection.Infrastructure.Storage;

namespace MyCollection.Tests.Unit;

public class LocalBackupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mc-backup-{Guid.NewGuid():N}");
    private readonly LocalBackupStore _sut;
    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();

    public LocalBackupStoreTests() =>
        _sut = new LocalBackupStore(Options.Create(new StorageOptions { BackupRoot = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task WriteAsync(string fileName)
    {
        await using var stream = await _sut.CreateAsync(OwnerId, fileName, CancellationToken.None);
        await stream.WriteAsync(new byte[] { 1, 2, 3 });
    }

    private string[] Files() =>
        Directory.Exists(Path.Combine(_root, OwnerId.ToString()))
            ? [.. Directory.GetFiles(Path.Combine(_root, OwnerId.ToString())).Select(Path.GetFileName)!]
            : [];

    [Fact]
    public async Task Create_writes_the_file_under_the_owner_folder()
    {
        await WriteAsync("pre-import-20260728-030000.zip");

        Files().Should().Equal("pre-import-20260728-030000.zip");
    }

    [Fact]
    public async Task Prune_keeps_only_the_newest_files_for_that_owner()
    {
        await WriteAsync("pre-import-20260701-000000.zip");
        await WriteAsync("pre-import-20260702-000000.zip");
        await WriteAsync("pre-import-20260703-000000.zip");
        await WriteAsync("pre-import-20260704-000000.zip");

        await _sut.PruneAsync(OwnerId, keep: 3, CancellationToken.None);

        Files().Should().BeEquivalentTo(
            "pre-import-20260702-000000.zip",
            "pre-import-20260703-000000.zip",
            "pre-import-20260704-000000.zip");
    }

    [Fact]
    public async Task Prune_does_not_touch_another_owners_backups()
    {
        var other = ObjectId.GenerateNewId();
        await using (var stream = await _sut.CreateAsync(other, "pre-import-20260101-000000.zip", CancellationToken.None))
        {
            await stream.WriteAsync(new byte[] { 1 });
        }

        await WriteAsync("pre-import-20260704-000000.zip");
        await _sut.PruneAsync(OwnerId, keep: 1, CancellationToken.None);

        Directory.GetFiles(Path.Combine(_root, other.ToString())).Should().ContainSingle();
    }

    [Fact]
    public async Task Prune_is_silent_when_the_owner_has_no_backups()
    {
        var act = async () => await _sut.PruneAsync(ObjectId.GenerateNewId(), keep: 3, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~LocalBackupStoreTests"`
Expected: 編譯失敗，找不到 `LocalBackupStore`

- [ ] **Step 3: 定義介面**

建立 `src/MyCollection.Application/Common/IBackupStore.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// 匯入前自動備份的存放區。刻意與 <see cref="IFileStorage"/> 分開：
/// media root 由 AllowAnonymous 的 GET /media/{**path} 對外提供，
/// 備份放在那裡等於把整份收藏資料庫掛在匿名端點上。
///
/// 不提供下載端點——開放就得重做一次授權設計，而使用者本人已在該台機器前。
/// 檔案位於 host 的 {BackupRoot}/{ownerId}/，直接取檔即可。
/// </summary>
public interface IBackupStore
{
    /// <summary>建立備份檔並回傳可寫入的 stream。呼叫端負責 Dispose。</summary>
    Task<Stream> CreateAsync(ObjectId ownerId, string fileName, CancellationToken ct);

    /// <summary>只保留該 ownerId 最新的 <paramref name="keep"/> 份，其餘刪除。</summary>
    Task PruneAsync(ObjectId ownerId, int keep, CancellationToken ct);
}
```

- [ ] **Step 4: 加 `BackupRoot` 設定**

修改 `src/MyCollection.Infrastructure/Storage/StorageOptions.cs`，在 `LocalRoot` 之後加入：

```csharp
    /// <summary>
    /// 匯入前自動備份的根目錄。必須位於 LocalRoot 之外：
    /// LocalRoot 由匿名的 /media 端點對外提供。
    /// </summary>
    public string BackupRoot { get; init; } = "data/backups";
```

- [ ] **Step 5: 實作**

建立 `src/MyCollection.Infrastructure/Storage/LocalBackupStore.cs`：

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Storage;

public sealed class LocalBackupStore : IBackupStore
{
    private readonly string _root;

    public LocalBackupStore(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.BackupRoot);
        Directory.CreateDirectory(_root);
    }

    public Task<Stream> CreateAsync(ObjectId ownerId, string fileName, CancellationToken ct)
    {
        var directory = OwnerDirectory(ownerId);
        Directory.CreateDirectory(directory);

        // fileName 由呼叫端以時間戳組成，不含使用者輸入；仍取 GetFileName 剝掉任何目錄成分。
        var path = Path.Combine(directory, Path.GetFileName(fileName));

        return Task.FromResult<Stream>(File.Create(path));
    }

    public Task PruneAsync(ObjectId ownerId, int keep, CancellationToken ct)
    {
        var directory = OwnerDirectory(ownerId);

        if (!Directory.Exists(directory))
        {
            return Task.CompletedTask;
        }

        // 依檔名排序而非寫入時間：檔名含時間戳，且不受檔案系統時間精度或搬移影響。
        var stale = Directory.GetFiles(directory)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep);

        foreach (var file in stale)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private string OwnerDirectory(ObjectId ownerId) => Path.Combine(_root, ownerId.ToString());
}
```

- [ ] **Step 6: 註冊 DI**

在 `src/MyCollection.Infrastructure/DependencyInjection.cs` 的 `services.AddSingleton<IFileStorage, LocalFileStorage>();` 之後加入：

```csharp
        services.AddSingleton<IBackupStore, LocalBackupStore>();
```

- [ ] **Step 7: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~LocalBackupStoreTests"`
Expected: PASS，4 passed

- [ ] **Step 8: 提交**

```bash
git add src/MyCollection.Application/Common/IBackupStore.cs \
        src/MyCollection.Infrastructure/Storage/LocalBackupStore.cs \
        src/MyCollection.Infrastructure/Storage/StorageOptions.cs \
        src/MyCollection.Infrastructure/DependencyInjection.cs \
        tests/MyCollection.Tests/Unit/LocalBackupStoreTests.cs
git commit -m "feat(transfer): add backup store outside the anonymous media root"
```

---

## Task 10: 匯入 handler

**Files:**
- Create: `src/MyCollection.Application/Transfer/ImportCommand.cs`
- Modify: `src/MyCollection.Api/GlobalExceptionHandler.cs`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Test: 由 Task 11 的整合測試覆蓋（handler 依賴 ZIP、儲存、Mongo 三者的真實互動，mock 出來的測試只會測到 mock 本身）

- [ ] **Step 1: 實作 handler**

建立 `src/MyCollection.Application/Transfer/ImportCommand.cs`：

```csharp
using System.IO.Compression;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Media;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <param name="Archive">必須可 seek——ZipArchive 需要隨機存取 central directory。</param>
public record ImportArchiveCommand(Stream Archive) : IRequest<ImportResultDto>;

public sealed record ImportResultDto(
    int Categories,
    int Items,
    int Images,
    IReadOnlyList<string> Warnings);

public sealed class ImportArchiveCommandHandler(
    ITransferRepository repository,
    ICategoryRepository categories,
    ArchiveValidator validator,
    ArchiveWriter archiveWriter,
    IBackupStore backups,
    IFileStorage storage,
    IImageProcessor imageProcessor,
    IUserContext userContext,
    TimeProvider timeProvider) : IRequestHandler<ImportArchiveCommand, ImportResultDto>
{
    private const int BackupsToKeep = 3;

    public async Task<ImportResultDto> Handle(ImportArchiveCommand request, CancellationToken ct)
    {
        using var archive = OpenArchive(request.Archive);
        var manifest = ReadManifest(archive);

        // ---- 階段一：驗證。這一段結束前不寫入任何東西。 ----
        var systemCategories = (await categories.ListAsync(ct)).Where(c => c.OwnerId is null).ToList();
        var failures = validator.Validate(manifest, systemCategories);

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // ---- 備份。沒有 transaction，這是階段二失敗後唯一的復原手段。 ----
        var ownerId = userContext.UserId;
        var now = timeProvider.GetUtcNow();

        await using (var backup = await backups.CreateAsync(
                         ownerId, $"pre-import-{now:yyyyMMdd-HHmmss}.zip", ct))
        {
            await archiveWriter.WriteAsync(backup, ct);
        }

        await backups.PruneAsync(ownerId, BackupsToKeep, ct);

        // ---- 階段二：套用。 ----
        var warnings = new List<string>();

        var replaced = await repository.ListExportableItemsAsync(ct);
        await repository.DeleteNonSteamItemsAsync(ct);

        foreach (var item in replaced)
        {
            await storage.DeleteDirectoryAsync($"{ownerId}/{item.Id}", ct);
        }

        await repository.DeleteOwnShareLinksAsync(ct);

        var steamItems = await repository.ListSteamItemsAsync(ct);
        var localCategories = await repository.ListOwnCategoriesAsync(ct);
        var plan = CategoryReconciler.Plan(localCategories, manifest.Categories, steamItems);

        // repoint 必須在 delete 之前：否則 Steam item 會在中間狀態指向已不存在的品類。
        foreach (var repoint in plan.Repoints)
        {
            await repository.RepointItemsAsync(repoint.ItemIds, repoint.TargetCategoryId, ct);
        }

        await repository.DeleteCategoriesAsync(plan.Delete, ct);

        warnings.AddRange(plan.KeptOrphanNames.Select(name =>
            $"品類「{name}」因仍有 Steam 品項掛在上面而保留，未被封存檔取代。"));

        await repository.InsertCategoriesAsync(
            [.. manifest.Categories.Select(c => ArchiveMapper.ToDomain(c, ownerId))], ct);

        var (items, imageCount, imageWarnings) = await BuildItemsAsync(archive, manifest, ownerId, ct);
        warnings.AddRange(imageWarnings);

        await repository.InsertItemsAsync(items, ct);

        var shareLinks = await BuildShareLinksAsync(manifest, ownerId, warnings, ct);
        await repository.InsertShareLinksAsync(shareLinks, ct);

        return new ImportResultDto(manifest.Categories.Count, items.Count, imageCount, warnings);
    }

    /// <summary>
    /// manifest 的大小上限。ArchiveManifestSerializer.Read 的 doc comment 說明了原因：
    /// MongoDB 的 JsonReader 對巢狀深度沒有上限，極深巢狀的 JSON 會觸發無法攔截的
    /// StackOverflowException 直接終止行程，只能在讀取前用大小把它擋掉。
    /// 個人收藏的 manifest 是幾 MB 等級，64 MB 留了非常寬裕的餘裕。
    /// </summary>
    private const long MaxManifestBytes = 64L * 1024 * 1024;

    private static ZipArchive OpenArchive(Stream source)
    {
        try
        {
            return new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidArchiveException("上傳的檔案不是合法的 ZIP 封存檔。", exception);
        }
    }

    private static ArchiveManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ArchiveManifest.FileName)
                    ?? throw new InvalidArchiveException($"封存檔內缺少 {ArchiveManifest.FileName}。");

        if (entry.Length > MaxManifestBytes)
        {
            throw new InvalidArchiveException(
                $"{ArchiveManifest.FileName} 超過 {MaxManifestBytes / 1024 / 1024} MB 上限。");
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        // Read 內部已經把 MongoDB.Bson 會丟的各種例外統一成 InvalidArchiveException，
        // 也已經檢查過 schemaVersion，這裡不需要再包一層。
        return ArchiveManifestSerializer.Read(buffer);
    }

    private async Task<(List<Item> Items, int ImageCount, List<string> Warnings)> BuildItemsAsync(
        ZipArchive archive, ArchiveManifest manifest, ObjectId ownerId, CancellationToken ct)
    {
        var items = new List<Item>(manifest.Items.Count);
        var warnings = new List<string>();
        var imageCount = 0;

        foreach (var source in manifest.Items)
        {
            var item = new Item
            {
                Id = source.Id,
                OwnerId = ownerId,
                CategoryId = source.CategoryId,
                Name = source.Name,
                Description = source.Description,
                Tags = source.Tags,
                IsShowcased = source.IsShowcased,
                Source = source.Source,
                Acquisition = ArchiveMapper.ToDomain(source.Acquisition),
                Attributes = source.Attributes,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            };

            foreach (var image in source.Images.OrderBy(i => i.Order))
            {
                var entry = archive.GetEntry(image.File);

                if (entry is null)
                {
                    warnings.Add($"品項「{source.Name}」的圖片 {image.File} 不在封存檔內，已略過。");
                    continue;
                }

                await using var content = entry.Open();
                var processed = await imageProcessor.ProcessAsync(content, ct);

                item.Images.Add(new ItemImage
                {
                    Id = image.Id,
                    Path = await SaveAsync(MediaPaths.Full(item, image.Id), processed.Full, ct),
                    CardPath = await SaveAsync(MediaPaths.Card(item, image.Id), processed.Card, ct),
                    ThumbPath = await SaveAsync(MediaPaths.Thumb(item, image.Id), processed.Thumb, ct),
                    IsPrimary = image.IsPrimary,
                    Order = image.Order
                });

                imageCount++;
            }

            // 主圖可能是那張缺檔的圖，補一張回來，避免品項變成沒有主圖。
            if (item.Images.Count > 0 && item.Images.TrueForAll(i => !i.IsPrimary))
            {
                item.Images[0].IsPrimary = true;
            }

            items.Add(item);
        }

        return (items, imageCount, warnings);
    }

    private async Task<string> SaveAsync(string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);

        return await storage.SaveAsync(path, stream, ct);
    }

    private async Task<List<ShareLink>> BuildShareLinksAsync(
        ArchiveManifest manifest, ObjectId ownerId, List<string> warnings, CancellationToken ct)
    {
        var links = new List<ShareLink>(manifest.ShareLinks.Count);

        foreach (var source in manifest.ShareLinks)
        {
            var slug = source.Slug;

            if (await repository.SlugExistsAsync(slug, ct))
            {
                slug = ObjectId.GenerateNewId().ToString();
                warnings.Add($"分享連結 {source.Slug} 已被占用，改用 {slug}。");
            }

            links.Add(new ShareLink
            {
                Id = ObjectId.GenerateNewId(),
                OwnerId = ownerId,
                Slug = slug,
                Scope = source.Scope,
                IncludeCategoryIds = source.IncludeCategoryIds,
                IncludePrice = source.IncludePrice,
                ExpiresAt = source.ExpiresAt,
                CreatedAt = source.CreatedAt
            });
        }

        return links;
    }
}
```

- [ ] **Step 2: 讓 `InvalidArchiveException` 對應到 400**

`GlobalExceptionHandler` 是唯一的錯誤轉換點。沒有這一條，壞掉的封存檔會變成 500，而它其實是使用者可修正的輸入問題。

在 `src/MyCollection.Api/GlobalExceptionHandler.cs` 的 `Map` switch 中，緊接在 `InvalidImageException` 那一條之後加入：

```csharp
            InvalidArchiveException a => (StatusCodes.Status400BadRequest, "Invalid archive.", a.Message, null),
```

並在檔案 using 區加入 `using MyCollection.Application.Transfer;`。

- [ ] **Step 3: 註冊 DI**

在 `src/MyCollection.Infrastructure/DependencyInjection.cs` 的 `services.AddScoped<ArchiveWriter>();` 之後加入：

```csharp
        services.AddScoped<ArchiveValidator>();
```

- [ ] **Step 4: 確認建置通過**

Run: `dotnet build MyCollection.slnx`
Expected: Build succeeded，0 Error

- [ ] **Step 5: 提交**

```bash
git add src/MyCollection.Application/Transfer/ImportCommand.cs \
        src/MyCollection.Api/GlobalExceptionHandler.cs \
        src/MyCollection.Infrastructure/DependencyInjection.cs
git commit -m "feat(transfer): add import handler with validation, backup and snapshot replace"
```

---

## Task 11: `POST /import` 端點

**Files:**
- Modify: `src/MyCollection.Api/Endpoints/TransferEndpoints.cs`
- Test: `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs`

- [ ] **Step 1: 寫失敗的測試**

在 `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs` 的類別內加入（沿用該檔案已有的 helper）：

```csharp
    private static MultipartFormDataContent ArchiveUpload(byte[] zip)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

        return new MultipartFormDataContent { { content, "file", "archive.zip" } };
    }

    private async Task<byte[]> ExportBytesAsync() =>
        await (await _client.GetAsync("/export")).Content.ReadAsByteArrayAsync();

    [Fact]
    public async Task Import_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsync("/import", ArchiveUpload([1, 2, 3]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Import_rejects_a_file_that_is_not_a_zip()
    {
        var response = await _client.PostAsync("/import", ArchiveUpload([1, 2, 3, 4]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_rejects_an_unknown_schema_version_without_touching_data()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id);

        var tampered = new MemoryStream();
        using (var archive = new ZipArchive(tampered, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = archive.CreateEntry(ArchiveManifest.FileName).Open();
            ArchiveManifestSerializer.Write(entry, new ArchiveManifest { SchemaVersion = 99 });
        }

        var response = await _client.PostAsync("/import", ArchiveUpload(tampered.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 資料未被動過
        var items = await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items");
        items!.Total.Should().Be(1);
    }
```

需要在檔案頂端補上 `using MyCollection.Application.Common;`（`PagedResult<T>`）。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~TransferEndpointsTests.Import"`
Expected: FAIL，404

- [ ] **Step 3: 加端點**

修改 `src/MyCollection.Api/Endpoints/TransferEndpoints.cs`，在 `MapGet("/export", ...)` 之後、`return app;` 之前加入：

```csharp
        group.MapPost("/import", async (IFormFile file, ISender sender, CancellationToken ct) =>
            {
                if (file.Length == 0)
                {
                    return Results.BadRequest(new { title = "The archive must not be empty." });
                }

                // ZipArchive 需要隨機存取 central directory，而 multipart stream 不可 seek，
                // 所以先落一份暫存檔。無論成敗都要刪掉。
                var tempPath = Path.GetTempFileName();

                try
                {
                    await using (var temp = File.Create(tempPath))
                    {
                        await file.CopyToAsync(temp, ct);
                    }

                    await using var archive = File.OpenRead(tempPath);
                    var result = await sender.Send(new ImportArchiveCommand(archive), ct);

                    return Results.Ok(result);
                }
                finally
                {
                    File.Delete(tempPath);
                }
            })
            .DisableAntiforgery()
            .WithMetadata(new UnlimitedRequestBody());
```

並在同一個檔案的類別外（namespace 內）加入：

```csharp
/// <summary>
/// 解除 Kestrel 對匯入端點的 request body 大小限制。
/// minimal API 沒有 DisableRequestSizeLimit() 擴充方法（那是 MVC 的 attribute），
/// 端點層級要靠這個 metadata 介面。
/// </summary>
internal sealed class UnlimitedRequestBody : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => null;
}
```

- [ ] **Step 4: 放寬 multipart form 長度上限**

解除 Kestrel 的 body 限制還不夠：`IFormFile` 綁定會經過 form 讀取，`FormOptions.MultipartBodyLengthLimit` 預設 128 MB，超過就擲例外。

在 `src/MyCollection.Api/Program.cs` 的服務註冊區（`var app = builder.Build();` 之前）加入：

```csharp
// 匯入端點會上傳整包收藏。MediaEndpoints 的單張圖片 10 MB 上限是自己明確檢查的，
// 不依賴這個全域值，所以放寬它不會削弱該處的防護。
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
});
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~TransferEndpointsTests"`
Expected: PASS，6 passed

- [ ] **Step 6: 提交**

```bash
git add src/MyCollection.Api/Endpoints/TransferEndpoints.cs \
        src/MyCollection.Api/Program.cs \
        tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs
git commit -m "feat(api): add collection import endpoint"
```

---

## Task 12: 完整往返整合測試

驗證 spec §11 列出的整合情境。這是整個功能唯一能證明「兩地搬移真的可行」的測試。

**Files:**
- Modify: `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs`

- [ ] **Step 1: 寫測試**

在 `tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs` 的類別內加入：

```csharp
    [Fact]
    public async Task Round_trip_restores_categories_items_and_images_for_a_different_owner()
    {
        var category = await CreateCategoryAsync();
        var item = await CreateItemAsync(category.Id);
        (await _client.PostAsync($"/items/{item.Id}/images", PngUpload())).EnsureSuccessStatusCode();

        var exported = await ExportBytesAsync();

        // 換一個使用者匯入，模擬另一台機器上 ownerId 不同的帳號
        using var target = await AuthenticatedClient.CreateAsync(_factory, "target@example.com");
        var response = await target.PostAsync("/import", ArchiveUpload(exported));
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Categories.Should().Be(1);
        result.Items.Should().Be(1);
        result.Images.Should().Be(1);
        result.Warnings.Should().BeEmpty();

        var items = (await target.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!;
        items.Total.Should().Be(1);
        items.Items[0].Name.Should().Be("Kind of Blue");
        items.Items[0].Tags.Should().Equal("jazz");
        items.Items[0].IsShowcased.Should().BeTrue();
        items.Items[0].Images.Should().ContainSingle();

        // 圖片路徑已改用匯入者的 ownerId，且三個尺寸都讀得到
        var image = items.Items[0].Images[0];
        image.Path.Should().NotContain(item.Id.ToString() + "/../");

        foreach (var path in new[] { image.Path, image.CardPath, image.ThumbPath })
        {
            (await target.GetAsync($"/media/{path}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Import_replaces_existing_data_rather_than_merging()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id, "保留的唱片");

        var exported = await ExportBytesAsync();

        await CreateItemAsync(category.Id, "匯入後應該消失的唱片");
        (await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!.Total.Should().Be(2);

        (await _client.PostAsync("/import", ArchiveUpload(exported))).EnsureSuccessStatusCode();

        var items = (await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!;
        items.Total.Should().Be(1);
        items.Items[0].Name.Should().Be("保留的唱片");
    }

    [Fact]
    public async Task Import_downgrades_a_missing_image_to_a_warning()
    {
        var category = await CreateCategoryAsync();
        var item = await CreateItemAsync(category.Id);
        (await _client.PostAsync($"/items/{item.Id}/images", PngUpload())).EnsureSuccessStatusCode();

        var exported = await ExportBytesAsync();

        // 把 media entry 抽掉，manifest 保持不變
        var stripped = new MemoryStream();
        using (var original = new ZipArchive(new MemoryStream(exported), ZipArchiveMode.Read))
        using (var rebuilt = new ZipArchive(stripped, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in original.Entries.Where(e => e.FullName == ArchiveManifest.FileName))
            {
                await using var source = entry.Open();
                await using var destination = rebuilt.CreateEntry(entry.FullName).Open();
                await source.CopyToAsync(destination);
            }
        }

        var response = await _client.PostAsync("/import", ArchiveUpload(stripped.ToArray()));
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Items.Should().Be(1);
        result.Images.Should().Be(0);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("不在封存檔內");
    }

    [Fact]
    public async Task Import_keeps_steam_items_and_repoints_their_orphan_category_by_name()
    {
        // 本機有一個自訂「數位遊戲」品類，上面掛著一個 Steam 品項。
        // 封存檔裡有另一個 id 不同、但同名的「數位遊戲」品類。
        var localDigital = await CreateCategoryAsync("數位遊戲");
        var steamItemId = await SeedSteamItemAsync(localDigital.Id);

        using var source = await AuthenticatedClient.CreateAsync(_factory, "source@example.com");
        var sourceCategory = (await (await source.PostAsJsonAsync("/categories", new
        {
            name = "數位遊戲", icon = "gamepad-2", kind = "Digital", fields = Array.Empty<object>()
        })).Content.ReadFromJsonAsync<CategoryDto>())!;

        var exported = await (await source.GetAsync("/export")).Content.ReadAsByteArrayAsync();

        var response = await _client.PostAsync("/import", ArchiveUpload(exported));
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Warnings.Should().BeEmpty();

        // Steam 品項還在，且已改指到封存檔版本的品類
        var items = (await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!;
        var steamItem = items.Items.Single(i => i.Id == steamItemId);
        steamItem.CategoryId.Should().Be(sourceCategory.Id);

        // 沒有累積出兩個同名品類
        var categories = (await _client.GetFromJsonAsync<CategoryDto[]>("/categories"))!;
        categories.Count(c => c.Name == "數位遊戲").Should().Be(1);
    }
```

`SeedSteamItemAsync` 需要直接寫進 MongoDB，因為沒有建立 Steam 來源品項的公開 API。在同一個類別內加入：

```csharp
    /// <summary>直接寫 DB：沒有公開 API 能建立 Source = Steam 的品項。</summary>
    private async Task<string> SeedSteamItemAsync(string categoryId)
    {
        var context = _factory.Services.GetRequiredService<MongoContext>();
        var ownerId = (await context.Users
            .Find(Builders<User>.Filter.Eq(u => u.Email, "transfer@example.com"))
            .SingleAsync()).Id;

        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ownerId,
            CategoryId = ObjectId.Parse(categoryId),
            Name = "Half-Life",
            Source = ItemSource.Steam,
            ExternalRef = new ExternalRef
            {
                Provider = "steam",
                ExternalId = "70",
                LastSyncedAt = DateTime.UtcNow
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await context.Items.InsertOneAsync(item);

        return item.Id.ToString();
    }
```

需要在檔案頂端補上：

```csharp
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
```

- [ ] **Step 2: 執行測試**

Run: `dotnet test tests/MyCollection.Tests --filter "FullyQualifiedName~TransferEndpointsTests"`
Expected: PASS，10 passed

若 `Import_keeps_steam_items_and_repoints_their_orphan_category_by_name` 失敗，先確認 `CategoryReconciler.Plan` 的 repoint 是否在 delete 之前送出——順序顛倒會讓 Steam item 短暫指向不存在的品類，而 `RepointItemsAsync` 的 filter 仍會成功但語意錯誤。

- [ ] **Step 3: 執行完整測試套件**

Run: `dotnet test MyCollection.slnx`
Expected: PASS，無 regression

- [ ] **Step 4: 提交**

```bash
git add tests/MyCollection.Tests/Integration/TransferEndpointsTests.cs
git commit -m "test(transfer): cover full export-import round trip and steam item retention"
```

---

## Task 13: 部署設定

**Files:**
- Modify: `web/nginx.conf:8`
- Modify: `docker-compose.yml`

- [ ] **Step 1: 放寬 nginx 上傳限制**

修改 `web/nginx.conf:8`：

```nginx
    # 匯入端點會上傳整包收藏（含圖片），12m 遠遠不夠。
    # 這是 server 層級設定，同時涵蓋 /api/import。
    client_max_body_size 2g;
```

- [ ] **Step 2: 加備份 volume**

修改 `docker-compose.yml` 的 `api` 服務。在 `Storage__LocalRoot: /app/data/media` 之後加入：

```yaml
      Storage__BackupRoot: /app/data/backups
```

並把 `volumes` 區塊改成：

```yaml
    volumes:
      - ./data/media:/app/data/media
      - ./data/backups:/app/data/backups
```

- [ ] **Step 3: 驗證容器啟動**

Run: `docker compose config`
Expected: 輸出的 YAML 含 `Storage__BackupRoot` 與兩個 volume 掛載，無錯誤

- [ ] **Step 4: 提交**

```bash
git add web/nginx.conf docker-compose.yml
git commit -m "chore(deploy): raise upload limit and mount backup volume"
```

---

## Task 14: 前端匯入／匯出 UI

**Files:**
- Create: `web/src/app/core/api/transfer.service.ts`
- Create: `web/src/app/core/api/transfer.service.spec.ts`
- Create: `web/src/app/features/settings/data-transfer.component.ts`
- Modify: `web/src/app/core/models.ts`
- Modify: `web/src/app/features/settings/settings.component.ts`

- [ ] **Step 1: 寫失敗的 service 測試**

建立 `web/src/app/core/api/transfer.service.spec.ts`：

```typescript
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { TransferService } from './transfer.service';

describe('TransferService', () => {
  let service: TransferService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(TransferService);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('requests the export as a blob', () => {
    service.export().subscribe();

    const request = controller.expectOne('/api/export');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['zip']));
  });

  it('posts the archive as multipart form data named file', async () => {
    const archive = new File(['zip'], 'archive.zip', { type: 'application/zip' });
    const result = firstValueFrom(service.import(archive));

    const request = controller.expectOne('/api/import');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    expect((request.request.body as FormData).get('file')).toBe(archive);

    request.flush({ categories: 1, items: 2, images: 3, warnings: [] });

    expect((await result).items).toBe(2);
  });
});
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: FAIL，找不到 `./transfer.service`

- [ ] **Step 3: 加 DTO**

在 `web/src/app/core/models.ts` 檔案末尾加入：

```typescript
export interface ImportResultDto {
  categories: number;
  items: number;
  images: number;
  warnings: string[];
}
```

- [ ] **Step 4: 建立 service**

建立 `web/src/app/core/api/transfer.service.ts`：

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { ImportResultDto } from '../models';

@Injectable({ providedIn: 'root' })
export class TransferService {
  private readonly http = inject(HttpClient);

  export(): Observable<Blob> {
    return this.http.get(`${API_BASE}/export`, { responseType: 'blob' });
  }

  import(archive: File): Observable<ImportResultDto> {
    const body = new FormData();
    body.append('file', archive);

    return this.http.post<ImportResultDto>(`${API_BASE}/import`, body);
  }
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: PASS

- [ ] **Step 6: 建立 UI 子元件**

建立 `web/src/app/features/settings/data-transfer.component.ts`。獨立成子元件是因為 `settings.component.ts` 已 266 行，而匯入／匯出含確認對話框與結果摘要，混進去會變成兩種不相干責任的雜燴。

```typescript
import { Component, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { TransferService } from '../../core/api/transfer.service';
import { NotificationService } from '../../core/notification.service';
import { ImportResultDto } from '../../core/models';

@Component({
  selector: 'app-data-transfer',
  template: `
    <section class="settings__panel mc-panel" data-settings-panel>
      <div class="mc-eyebrow">DATA TRANSFER</div>
      <h2>匯出／匯入收藏</h2>

      <p class="hint">
        匯出會產生一個含品類、手建品項與圖片的 ZIP。Steam 同步來的品項不在其中，
        另一台機器重跑一次同步即可取得。
      </p>

      <button type="button" (click)="exportArchive()" [disabled]="busy()">
        {{ exporting() ? '匯出中…' : '匯出封存檔' }}
      </button>

      <hr />

      <label class="file-picker">
        選擇封存檔
        <input type="file" accept=".zip" (change)="pick($event)" [disabled]="busy()" />
      </label>

      @if (selected(); as file) {
        <p>已選擇：<code>{{ file.name }}</code></p>
        <button type="button" (click)="confirming.set(true)" [disabled]="busy()">匯入…</button>
      }

      @if (confirming()) {
        <div class="mc-panel danger" role="alertdialog" aria-labelledby="import-warning">
          <h3 id="import-warning">這會覆蓋這台機器上的收藏</h3>
          <ul>
            <li>刪除所有手建品項與其圖片（Steam 同步來的品項會保留）</li>
            <li>刪除所有自訂品類與公開分享連結</li>
            <li>以封存檔的內容重新寫入</li>
          </ul>
          <p>
            系統會在動手前自動備份到伺服器的 <code>data/backups</code>。
            但匯入過程無法回滾，中途失敗會留下不完整的資料，需要用備份還原。
          </p>
          <button type="button" (click)="importArchive()" [disabled]="busy()">
            {{ importing() ? '匯入中…' : '確定覆蓋' }}
          </button>
          <button type="button" (click)="confirming.set(false)" [disabled]="busy()">取消</button>
        </div>
      }

      @if (result(); as summary) {
        <div class="mc-panel">
          <h3>匯入完成</h3>
          <p>品類 {{ summary.categories }} 個、品項 {{ summary.items }} 筆、圖片 {{ summary.images }} 張。</p>
          @if (summary.warnings.length) {
            <ul>
              @for (warning of summary.warnings; track warning) {
                <li>{{ warning }}</li>
              }
            </ul>
          }
        </div>
      }
    </section>
  `,
})
export class DataTransferComponent {
  private readonly transfer = inject(TransferService);
  private readonly notifications = inject(NotificationService);

  protected readonly exporting = signal(false);
  protected readonly importing = signal(false);
  protected readonly confirming = signal(false);
  protected readonly selected = signal<File | null>(null);
  protected readonly result = signal<ImportResultDto | null>(null);

  protected busy(): boolean {
    return this.exporting() || this.importing();
  }

  protected pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selected.set(input.files?.[0] ?? null);
    this.result.set(null);
  }

  protected exportArchive(): void {
    this.exporting.set(true);

    this.transfer
      .export()
      .pipe(finalize(() => this.exporting.set(false)))
      .subscribe((blob) => this.download(blob));
  }

  protected importArchive(): void {
    const file = this.selected();
    if (!file) {
      return;
    }

    this.importing.set(true);

    this.transfer
      .import(file)
      .pipe(
        finalize(() => {
          this.importing.set(false);
          this.confirming.set(false);
        }),
      )
      .subscribe((summary) => {
        this.result.set(summary);
        this.selected.set(null);
        this.notifications.success('匯入完成');
      });
  }

  private download(blob: Blob): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');

    anchor.href = url;
    anchor.download = `mycollection-${new Date().toISOString().slice(0, 10)}.zip`;
    anchor.click();

    URL.revokeObjectURL(url);
  }
}
```

- [ ] **Step 7: 嵌入 settings 頁**

修改 `web/src/app/features/settings/settings.component.ts`：

在 import 區加入：

```typescript
import { DataTransferComponent } from './data-transfer.component';
```

在 `@Component` 的 `imports` 陣列加入 `DataTransferComponent`：

```typescript
  imports: [FormsModule, DatePipe, DataTransferComponent],
```

在 template 的最後一個 `</section>` 之後加入：

```html
    <app-data-transfer />
```

- [ ] **Step 8: 建置與測試**

Run: `cd web && npm run build && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 建置成功，測試全數通過

- [ ] **Step 9: 提交**

```bash
git add web/src/app/core/api/transfer.service.ts \
        web/src/app/core/api/transfer.service.spec.ts \
        web/src/app/core/models.ts \
        web/src/app/features/settings/data-transfer.component.ts \
        web/src/app/features/settings/settings.component.ts
git commit -m "feat(web): add collection import and export to settings"
```

---

## Task 15: 端到端手動驗證

自動化測試不會發現 nginx 上傳限制、真實瀏覽器下載、Docker volume 掛載這三件事出錯。

**Files:** 無

- [ ] **Step 1: 啟動完整堆疊**

Run: `docker compose up --build -d`
Expected: 三個容器都是 healthy／running

- [ ] **Step 2: 建立測試資料**

在 `http://localhost:8080` 註冊帳號，建立一個自訂品類、兩筆品項，其中一筆上傳至少一張圖片，並建立一個公開分享連結。

- [ ] **Step 3: 匯出**

Settings → 匯出封存檔。確認瀏覽器下載到 `mycollection-*.zip`，且用解壓工具打得開，內含 `manifest.json` 與 `media/**/*.webp`。

- [ ] **Step 4: 匯入到另一個帳號**

登出，註冊第二個帳號，Settings → 選擇剛才的 zip → 匯入 → 確定覆蓋。

Expected: 顯示匯入摘要，品類／品項／圖片數量正確；回到收藏頁能看到品項且圖片正常顯示（不是破圖）。

- [ ] **Step 5: 確認備份產生**

Run: `ls -R data/backups`
Expected: 出現 `{ownerId}/pre-import-*.zip`

- [ ] **Step 6: 確認備份讀不到**

Run: `curl -i "http://localhost:8080/api/media/<ownerId>/pre-import-<timestamp>.zip"`
Expected: `HTTP/1.1 404 Not Found`（副檔名白名單擋下）

- [ ] **Step 7: 確認大檔可上傳**

若步驟 2 的封存檔小於 12 MB，補上傳幾張大圖讓它超過 12 MB，重跑匯出與匯入。
Expected: 不出現 `413 Request Entity Too Large`

若仍出現 413，依序排查：nginx 是否已重建（`docker compose up --build`）、`UnlimitedRequestBody` metadata 是否掛在端點上、`FormOptions.MultipartBodyLengthLimit` 是否生效。三者都確認後仍失敗，改在 handler 內以 feature 直接解除：

```csharp
var feature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
if (feature is { IsReadOnly: false })
{
    feature.MaxRequestBodySize = null;
}
```

放在 `MapPost("/import", ...)` lambda 的第一行，並把 lambda 的第一個參數改成 `HttpContext http`。

- [ ] **Step 8: 收尾**

Run: `docker compose down`

---

## 自我檢查對照表

| Spec 章節 | 對應 Task |
|---|---|
| §3.1 同步串流 | 5, 6 |
| §3.2 Canonical Extended JSON | 3 |
| §3.3 系統品類 id 不需重新對應 | 8（validator 接受系統品類 id） |
| §4 封存檔格式 | 3, 5 |
| §5 匯出 | 4, 5, 6 |
| §6.1 階段一驗證 | 8, 10, 11 |
| §6.2 階段二套用 | 7, 10 |
| §6.3 同名改指 | 7, 12 |
| §6.4 媒體刪除邊界 | 1, 10 |
| §6.5 slug 衝突 | 10 |
| §6.6 原子性限制 | 10（備份）、14（UI 揭露） |
| §6.7 回應摘要 | 10, 14 |
| §7 匯入前自動備份 | 9, 10 |
| §7.1 備份不經 IFileStorage | 9 |
| §7.3 `.webp` 白名單 | 2 |
| §8 端點與設定變更 | 6, 11, 13 |
| §9 前端 | 14 |
| §10 錯誤處理 | 8, 10, 11 |
| §11 測試 | 1, 3, 5, 7, 8, 9, 11, 12 |
