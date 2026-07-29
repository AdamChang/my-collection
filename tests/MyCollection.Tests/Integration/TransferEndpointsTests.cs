using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Sharing;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
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

    private async Task<ShareLinkDto> CreateShareLinkAsync() =>
        (await (await _client.PostAsJsonAsync("/shares", new
        {
            scope = "Showcase", includeCategoryIds = Array.Empty<string>(), includePrice = false, expiresAt = (DateTime?)null
        })).Content.ReadFromJsonAsync<ShareLinkDto>())!;

    private static async Task<ZipArchive> ReadArchiveAsync(HttpResponseMessage response)
    {
        var buffer = new MemoryStream(await response.Content.ReadAsByteArrayAsync());

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static MultipartFormDataContent ArchiveUpload(byte[] zip)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

        return new MultipartFormDataContent { { content, "file", "archive.zip" } };
    }

    private async Task<byte[]> ExportBytesAsync() =>
        await (await _client.GetAsync("/export")).Content.ReadAsByteArrayAsync();

    private async Task<ObjectId> OwnerIdAsync(string email) =>
        (await _factory.Services.GetRequiredService<MongoContext>().Users
            .Find(Builders<User>.Filter.Eq(u => u.Email, email))
            .SingleAsync()).Id;

    /// <summary>直接寫 DB：沒有公開 API 能建立 Source = Steam 的品項。</summary>
    private async Task<string> SeedSteamItemAsync(string categoryId)
    {
        var context = _factory.Services.GetRequiredService<MongoContext>();

        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = await OwnerIdAsync("transfer@example.com"),
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
        var share = await CreateShareLinkAsync();

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
        manifest.ShareLinks.Should().ContainSingle(s => s.Slug == share.Slug);
    }

    [Fact]
    public async Task Export_excludes_other_users_data()
    {
        var category = await CreateCategoryAsync();
        await CreateItemAsync(category.Id);
        await CreateShareLinkAsync();

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
        manifest.ShareLinks.Should().BeEmpty();
    }

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
            // ExportedAt 必須是 Kind=Utc：UtcOnlyDateTimeSerializer 會擋下 default(DateTime)，
            // 那會在測試自己寫檔時就爆掉，根本走不到要驗證的匯入路徑。
            await ArchiveManifestSerializer.WriteAsync(
                entry,
                new ArchiveManifest { SchemaVersion = 99, ExportedAt = DateTime.UtcNow },
                default);
        }

        var response = await _client.PostAsync("/import", ArchiveUpload(tampered.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 資料未被動過
        var items = await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items");
        items!.Total.Should().Be(1);
    }

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

        // 圖片必須重新落在匯入者的 ownerId 底下。若沿用封存檔內的來源 ownerId，
        // 兩個帳號的媒體目錄就會互相踩踏——這正是匯入端不信任封存檔任何 ownerId 的原因。
        var sourceOwnerId = await OwnerIdAsync("transfer@example.com");
        var targetOwnerId = await OwnerIdAsync("target@example.com");
        var image = items.Items[0].Images[0];

        image.Path.Should().StartWith($"{targetOwnerId}/").And.NotContain(sourceOwnerId.ToString());

        // 三個尺寸都真的存在且讀得到
        foreach (var path in new[] { image.Path, image.CardPath, image.ThumbPath })
        {
            (await target.GetAsync($"/media/{path}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Import_replaces_existing_data_rather_than_merging()
    {
        var category = await CreateCategoryAsync();
        var kept = await CreateItemAsync(category.Id, "保留的唱片");

        var exported = await ExportBytesAsync();

        await CreateItemAsync(category.Id, "匯入後應該消失的唱片");
        (await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!.Total.Should().Be(2);

        (await _client.PostAsync("/import", ArchiveUpload(exported))).EnsureSuccessStatusCode();

        var items = (await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!;
        items.Total.Should().Be(1);
        items.Items[0].Name.Should().Be("保留的唱片");

        // 還原自己的備份必須原地復位：id 不變。匯入端只在 id 被「別人」占用時才改號，
        // 若改成無條件改號，Steam 品項對品類的引用與既有分享連結就會全部斷掉。
        items.Items[0].Id.Should().Be(kept.Id);
        items.Items[0].CategoryId.Should().Be(category.Id);
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
        (await source.PostAsJsonAsync("/categories", new
        {
            name = "數位遊戲", icon = "gamepad-2", kind = "Digital", fields = Array.Empty<object>()
        })).EnsureSuccessStatusCode();

        var exported = await (await source.GetAsync("/export")).Content.ReadAsByteArrayAsync();

        var response = await _client.PostAsync("/import", ArchiveUpload(exported));
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Warnings.Should().BeEmpty();

        // 沒有累積出兩個同名的自訂品類。系統品類也叫「數位遊戲」且恆存在，
        // 必須排除掉才問得出這個問題——它不參與 reconcile，永遠不會被刪。
        var categories = (await _client.GetFromJsonAsync<CategoryDto[]>("/categories"))!;
        var imported = categories.Should().ContainSingle(c => c.Name == "數位遊戲" && !c.IsSystem).Subject;
        categories.Should().ContainSingle(c => c.Name == "數位遊戲" && c.IsSystem);

        // Steam 品項還在，且已從本機的孤兒品類改指到匯入進來的那個。
        // 不能比對 sourceCategory.Id：來源帳號在同一個部署上仍持有該 id，
        // 匯入端因此會改號，比對來源 id 會把這個正確行為誤判成失敗。
        var items = (await _client.GetFromJsonAsync<PagedResult<ItemDto>>("/items"))!;
        var steamItem = items.Items.Single(i => i.Id == steamItemId);

        steamItem.CategoryId.Should().Be(imported.Id);
        steamItem.CategoryId.Should().NotBe(localDigital.Id);
    }
}
