# Plan 1：基礎建設 + Auth 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 Clean Architecture 四層 solution、MongoDB 連線與索引機制、JWT 註冊/登入/refresh、全域 ProblemDetails 例外處理，並讓 `dotnet test` 含 Testcontainers 整合測試全綠。

**Architecture:** Domain（實體 + 例外，僅依賴 `MongoDB.Bson`）→ Application（MediatR Command/Handler/Validator + Repository 介面）→ Infrastructure（MongoDB 實作、PBKDF2 雜湊、JWT 簽發）→ Api（Minimal API + `IExceptionHandler`）。授權在 Repository 層強制：所有 filter 由 `ownerId` 起頭。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、MongoDB.Driver 3.x、MediatR 14.x、FluentValidation 12.x、xUnit 2.9.3 + FluentAssertions + Moq + Testcontainers.MongoDb。

**前置條件：** Docker Desktop 必須執行中（Testcontainers 需要）。

---

## 檔案結構

| 檔案 | 職責 |
|---|---|
| `src/MyCollection.Domain/Entities/User.cs` | 使用者實體，含 refresh token 雜湊欄位 |
| `src/MyCollection.Domain/Exceptions/*.cs` | `NotFoundException` `ForbiddenException` `ConflictException` `ProviderException` |
| `src/MyCollection.Application/Common/IUserContext.cs` | 目前登入者 |
| `src/MyCollection.Application/Common/ValidationBehavior.cs` | MediatR pipeline，統一跑 FluentValidation |
| `src/MyCollection.Application/Common/Abstractions.cs` | `IPasswordHasher` `ITokenService` |
| `src/MyCollection.Application/Auth/*.cs` | Register / Login / Refresh 的 Command + Validator + Handler + DTO |
| `src/MyCollection.Application/Auth/IUserRepository.cs` | 使用者資料存取契約 |
| `src/MyCollection.Infrastructure/Mongo/MongoOptions.cs` | `Mongo:*` 設定 |
| `src/MyCollection.Infrastructure/Mongo/MongoConventions.cs` | camelCase / enum-as-string / UTC DateTime 慣例 |
| `src/MyCollection.Infrastructure/Mongo/MongoContext.cs` | Collection 存取點 |
| `src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs` | 索引建立（後續計畫持續擴充此檔） |
| `src/MyCollection.Infrastructure/Mongo/MongoUserRepository.cs` | `IUserRepository` 實作 |
| `src/MyCollection.Infrastructure/Security/Pbkdf2PasswordHasher.cs` | 密碼雜湊 |
| `src/MyCollection.Infrastructure/Security/JwtTokenService.cs` | JWT 簽發 + refresh token 產生/雜湊 |
| `src/MyCollection.Infrastructure/DependencyInjection.cs` | `AddInfrastructure()` |
| `src/MyCollection.Api/GlobalExceptionHandler.cs` | 例外 → RFC 9457 ProblemDetails |
| `src/MyCollection.Api/HttpUserContext.cs` | 從 `ClaimsPrincipal` 取 `ownerId` |
| `src/MyCollection.Api/Endpoints/AuthEndpoints.cs` | `/auth/*` 路由 |
| `src/MyCollection.Api/Program.cs` | 組裝 |
| `tests/MyCollection.Tests/Fixtures/MongoFixture.cs` | Testcontainers MongoDB |
| `tests/MyCollection.Tests/Fixtures/ApiFactory.cs` | `WebApplicationFactory`，覆寫 Mongo 連線 |

---

### Task 1：Solution 骨架與專案參考

**Files:**
- Create: `MyCollection.sln`、四個 `src/*` 專案、`tests/MyCollection.Tests`
- Create: `.gitignore`、`Directory.Build.props`

- [ ] **Step 1: 建立 solution 與專案**

> .NET 10 SDK 的 `dotnet new sln` 產生的是新的 XML 格式 **`MyCollection.slnx`**（非 `.sln`）。後續所有引用 solution 檔的地方（例如 Plan 5 的 Dockerfile）都以 `.slnx` 為準。

在 repo 根目錄執行：

```bash
dotnet new sln -n MyCollection
dotnet new classlib -o src/MyCollection.Domain -f net10.0
dotnet new classlib -o src/MyCollection.Application -f net10.0
dotnet new classlib -o src/MyCollection.Infrastructure -f net10.0
dotnet new web -o src/MyCollection.Api -f net10.0
dotnet new xunit -o tests/MyCollection.Tests -f net10.0
dotnet sln add src/MyCollection.Domain src/MyCollection.Application src/MyCollection.Infrastructure src/MyCollection.Api tests/MyCollection.Tests
rm src/MyCollection.Domain/Class1.cs src/MyCollection.Application/Class1.cs src/MyCollection.Infrastructure/Class1.cs
```

- [ ] **Step 2: 建立專案參考與套件**

```bash
dotnet add src/MyCollection.Application reference src/MyCollection.Domain
dotnet add src/MyCollection.Infrastructure reference src/MyCollection.Application
dotnet add src/MyCollection.Api reference src/MyCollection.Infrastructure
dotnet add tests/MyCollection.Tests reference src/MyCollection.Api

dotnet add src/MyCollection.Domain package MongoDB.Bson
dotnet add src/MyCollection.Application package MediatR
dotnet add src/MyCollection.Application package FluentValidation
dotnet add src/MyCollection.Application package FluentValidation.DependencyInjectionExtensions
dotnet add src/MyCollection.Infrastructure package MongoDB.Driver
dotnet add src/MyCollection.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/MyCollection.Api package Microsoft.AspNetCore.Authentication.JwtBearer

dotnet add tests/MyCollection.Tests package FluentAssertions
dotnet add tests/MyCollection.Tests package Moq
dotnet add tests/MyCollection.Tests package Testcontainers.MongoDb
dotnet add tests/MyCollection.Tests package Microsoft.AspNetCore.Mvc.Testing
```

`MyCollection.Infrastructure` 需要 `Microsoft.Extensions.*`（Options / Logging / DependencyInjection）；classlib 沒有 FrameworkReference，改為在 `src/MyCollection.Infrastructure/MyCollection.Infrastructure.csproj` 的 `<Project>` 內加入：

```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

**不要**再顯式加 `Microsoft.Extensions.Options` / `.Logging` / `.DependencyInjection` / `.Hosting` 等套件參考——共享框架已經提供，多加會觸發 **NU1510**（套件剪除警告），在 `TreatWarningsAsErrors=true` 下直接讓建置失敗。若後續 Task 遇到 NU1510，正解是移除該顯式 `PackageReference`，不是加 `NoWarn`。

- [ ] **Step 3: 建立 `Directory.Build.props`**

`Directory.Build.props`：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: 建立 `.gitignore`**

```bash
dotnet new gitignore
```

再於 `.gitignore` 末尾追加：

```
# MyCollection
data/
web/node_modules/
web/dist/
appsettings.Local.json
```

- [ ] **Step 5: 驗證建置**

Run: `dotnet build`
Expected: `Build succeeded`，0 Error 0 Warning。

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "chore: 建立 Clean Architecture solution 骨架"
```

---

### Task 2：Domain 例外型別

**Files:**
- Create: `src/MyCollection.Domain/Exceptions/DomainExceptions.cs`
- Test: `tests/MyCollection.Tests/Unit/DomainExceptionTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/DomainExceptionTests.cs`：

```csharp
using FluentAssertions;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class DomainExceptionTests
{
    [Fact]
    public void NotFoundException_carries_resource_and_key()
    {
        var ex = new NotFoundException("Item", "abc123");

        ex.Resource.Should().Be("Item");
        ex.Key.Should().Be("abc123");
        ex.Message.Should().Be("Item 'abc123' was not found.");
    }

    [Fact]
    public void ProviderException_carries_provider_key()
    {
        var ex = new ProviderException("steam", "rate limited");

        ex.ProviderKey.Should().Be("steam");
        ex.Message.Should().Be("rate limited");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter DomainExceptionTests`
Expected: 編譯失敗，`CS0234: 命名空間 'MyCollection' 中沒有類型或命名空間名稱 'Domain'`。

（此時 Domain 專案還沒有任何 `.cs` 檔，命名空間本身不存在，所以是 CS0234 而非 CS0246。往後 Domain 已有檔案時，同類失敗會是 CS0246。）

- [ ] **Step 3: 實作**

`src/MyCollection.Domain/Exceptions/DomainExceptions.cs`：

```csharp
namespace MyCollection.Domain.Exceptions;

/// <summary>找不到資源。對應 HTTP 404。</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
    public object Key { get; } = key;
}

/// <summary>ownerId 不符或無權限。對應 HTTP 403。</summary>
public sealed class ForbiddenException(string message = "Access to the requested resource is denied.")
    : Exception(message);

/// <summary>唯一性衝突（email、share slug）。對應 HTTP 409。</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>外部 Provider 呼叫失敗。對應 HTTP 502。</summary>
public sealed class ProviderException(string providerKey, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string ProviderKey { get; } = providerKey;
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter DomainExceptionTests`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 5: Commit**

```bash
git add src/MyCollection.Domain tests/MyCollection.Tests
git commit -m "feat(domain): 新增領域例外型別"
```

---

### Task 3：User 實體與 Mongo 慣例

**Files:**
- Create: `src/MyCollection.Domain/Entities/User.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoConventions.cs`
- Test: `tests/MyCollection.Tests/Unit/MongoConventionTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/MongoConventionTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Tests.Unit;

public class MongoConventionTests
{
    [Fact]
    public void Serializes_properties_in_camel_case_and_dates_as_utc()
    {
        MongoConventions.Register();

        var user = new User
        {
            Id = ObjectId.GenerateNewId(),
            Email = "a@b.c",
            PasswordHash = "hash",
            DisplayName = "Adam",
            CreatedAt = new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc)
        };

        var doc = user.ToBsonDocument();

        doc.Contains("displayName").Should().BeTrue();
        doc.Contains("DisplayName").Should().BeFalse();
        doc["createdAt"].BsonType.Should().Be(BsonType.DateTime);
        doc["refreshTokenHash"].IsBsonNull.Should().BeTrue();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoConventionTests`
Expected: 編譯失敗，找不到 `User` 與 `MongoConventions`。

- [ ] **Step 3: 實作實體與慣例**

`src/MyCollection.Domain/Entities/User.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public sealed class User
{
    public ObjectId Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>Refresh token 只存雜湊。單一 token 設計：新登入會作廢舊 token。</summary>
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

`src/MyCollection.Infrastructure/Mongo/MongoConventions.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全域 BSON 慣例。必須在任何序列化發生前呼叫一次。
///
/// 註冊在 lock 內完成、旗標最後才設：BsonClassMap 一旦建立就永久快取，
/// 若讓第二個執行緒在註冊完成前提早返回並開始序列化，整個行程都會固定用錯誤的 schema。
/// </summary>
public static class MongoConventions
{
    private static readonly Lock Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            ConventionRegistry.Register(
                "mycollection",
                new ConventionPack
                {
                    new CamelCaseElementNameConvention(),
                    new IgnoreExtraElementsConvention(true),
                    new EnumRepresentationConvention(BsonType.String)
                },
                _ => true);

            BsonSerializer.TryRegisterSerializer(new UtcOnlyDateTimeSerializer());
            BsonSerializer.TryRegisterSerializer(
                new NullableSerializer<DateTime>(new UtcOnlyDateTimeSerializer()));

            _registered = true;
        }
    }
}

/// <summary>
/// 只接受 Kind = Utc 的 DateTime。
///
/// 預設的 DateTimeSerializer(DateTimeKind.Utc) 只保證讀出來是 UTC，寫入時會呼叫
/// ToUniversalTime() —— .NET 把 Unspecified 當成本地時間，於是 UTC+8 的機器會把
/// 03:00 靜默存成前一天 19:00。購入日期差一天、refresh token 提早失效，都不會拋錯，
/// 只會在某天變成「時間怪怪的」。寧可在寫入當下就爆。
/// </summary>
public sealed class UtcOnlyDateTimeSerializer : SerializerBase<DateTime>
{
    // MongoDB.Bson.Serialization.Serializers.DateTimeSerializer 是 sealed，無法繼承，
    // 改用組合：實際的序列化/反序列化邏輯委派給它，只在寫入前插入 Kind 檢查。
    private static readonly DateTimeSerializer Inner = new(DateTimeKind.Utc);

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"DateTime must have Kind=Utc before it is persisted, but was {value.Kind}. " +
                "Normalise the value at the API boundary (treat naive input as UTC).");
        }

        Inner.Serialize(context, args, value);
    }

    public override DateTime Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        => Inner.Deserialize(context, args);
}
```

**寫測試時注意**：`BsonClassMapSerializer<T>` 會把成員序列化過程中拋出的例外包一層 `BsonSerializationException`（附上類別/屬性名稱做診斷）。斷言要驗根因：

```csharp
        act.Should().Throw<Exception>()
            .Which.GetBaseException().Should().BeOfType<InvalidOperationException>();
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter MongoConventionTests`
Expected: `Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: 新增 User 實體與 MongoDB 序列化慣例"
```

---

### Task 4：MongoContext 與索引初始化

**Files:**
- Create: `src/MyCollection.Infrastructure/Mongo/MongoOptions.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoContext.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs`
- Create: `tests/MyCollection.Tests/Fixtures/MongoFixture.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoIndexTests.cs`

- [ ] **Step 1: 寫失敗測試（含 fixture）**

`tests/MyCollection.Tests/Fixtures/MongoFixture.cs`：

```csharp
using Microsoft.Extensions.Options;
using MyCollection.Infrastructure.Mongo;
using Testcontainers.MongoDb;

namespace MyCollection.Tests.Fixtures;

public sealed class MongoFixture : IAsyncLifetime
{
    // Testcontainers.MongoDb 4.13.0 起，無參數建構子已標記 obsolete（CS0618），
    // 在 TreatWarningsAsErrors=true 下會直接讓建置失敗。image tag 走建構子參數。
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public string DatabaseName => "mycollection_test";

    public MongoContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = ConnectionString,
            Database = DatabaseName
        }));

        await MongoIndexInitializer.EnsureIndexesAsync(Context, CancellationToken.None);
    }

    /// <summary>每個測試開頭呼叫，清空所有 collection 但保留索引。</summary>
    public async Task ResetAsync()
    {
        await Context.Users.DeleteManyAsync(FilterDefinition<Domain.Entities.User>.Empty);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(MongoCollection.Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongo";
}
```

（`ResetAsync` 會在後續計畫隨新增 collection 擴充；`FilterDefinition` 需 `using MongoDB.Driver;`。）

`tests/MyCollection.Tests/Integration/MongoIndexTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoIndexTests(MongoFixture fixture)
{
    [Fact]
    public async Task Users_collection_has_unique_email_index()
    {
        var cursor = await fixture.Context.Users.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        var emailIndex = indexes.SingleOrDefault(i => i["name"] == "ux_users_email");

        emailIndex.Should().NotBeNull();
        emailIndex!["key"].Should().Be(new BsonDocument("email", 1));
        emailIndex["unique"].AsBoolean.Should().BeTrue();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoIndexTests`
Expected: 編譯失敗，找不到 `MongoContext` / `MongoOptions` / `MongoIndexInitializer`。

- [ ] **Step 3: 實作**

`src/MyCollection.Infrastructure/Mongo/MongoOptions.cs`：

```csharp
namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; init; } = "mongodb://localhost:27017";
    public string Database { get; init; } = "mycollection";
}
```

`src/MyCollection.Infrastructure/Mongo/MongoContext.cs`：

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoContext
{
    public MongoContext(IOptions<MongoOptions> options)
    {
        MongoConventions.Register();

        var client = new MongoClient(options.Value.ConnectionString);
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
}
```

`src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs`：

```csharp
using MongoDB.Driver;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 啟動時建立所有索引。CreateMany 具冪等性：同名同定義的索引會被忽略。
/// </summary>
public static class MongoIndexInitializer
{
    public static async Task EnsureIndexesAsync(MongoContext context, CancellationToken ct)
    {
        await context.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(x => x.Email),
                new CreateIndexOptions { Name = "ux_users_email", Unique = true }),
            cancellationToken: ct);

        await context.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(x => x.RefreshTokenHash),
                new CreateIndexOptions { Name = "ix_users_refreshTokenHash", Sparse = true }),
            cancellationToken: ct);
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter MongoIndexTests`
Expected: `Passed: 1`（首次會拉 `mongo:8.0` image，可能需要數分鐘）

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(infra): 新增 MongoContext、索引初始化與 Testcontainers fixture"
```

---

### Task 5：PBKDF2 密碼雜湊

**Files:**
- Create: `src/MyCollection.Application/Common/IPasswordHasher.cs`
- Create: `src/MyCollection.Infrastructure/Security/Pbkdf2PasswordHasher.cs`
- Test: `tests/MyCollection.Tests/Unit/Pbkdf2PasswordHasherTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/Pbkdf2PasswordHasherTests.cs`：

```csharp
using FluentAssertions;
using MyCollection.Infrastructure.Security;

namespace MyCollection.Tests.Unit;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new();

    [Fact]
    public void Hash_produces_different_output_for_same_password()
    {
        var a = _sut.Hash("P@ssw0rd!");
        var b = _sut.Hash("P@ssw0rd!");

        a.Should().NotBe(b, "每次雜湊都應使用新的 salt");
    }

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = _sut.Hash("P@ssw0rd!");

        _sut.Verify(hash, "P@ssw0rd!").Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _sut.Hash("P@ssw0rd!");

        _sut.Verify(hash, "wrong").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("bcrypt.1.2.3")]
    public void Verify_returns_false_for_malformed_hash(string hash)
    {
        _sut.Verify(hash, "P@ssw0rd!").Should().BeFalse();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter Pbkdf2PasswordHasherTests`
Expected: 編譯失敗，找不到 `Pbkdf2PasswordHasher`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Common/IPasswordHasher.cs`：

```csharp
namespace MyCollection.Application.Common;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}
```

`src/MyCollection.Infrastructure/Security/Pbkdf2PasswordHasher.cs`：

```csharp
using System.Security.Cryptography;
using MyCollection.Application.Common;

namespace MyCollection.Infrastructure.Security;

/// <summary>
/// 格式：pbkdf2.{iterations}.{base64 salt}.{base64 key}
/// 迭代次數存在字串內，未來調高時舊雜湊仍可驗證。
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return $"pbkdf2.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string hash, string password)
    {
        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter Pbkdf2PasswordHasherTests`
Expected: `Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(security): 新增 PBKDF2 密碼雜湊"
```

---

### Task 6：IUserRepository 與 MongoDB 實作

**Files:**
- Create: `src/MyCollection.Application/Auth/IUserRepository.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoUserRepository.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoUserRepositoryTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoUserRepositoryTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoUserRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private MongoUserRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoUserRepository(fixture.Context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(string email = "adam@example.com") => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = email,
        PasswordHash = "hash",
        DisplayName = "Adam",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Insert_then_GetByEmail_roundtrips()
    {
        var user = NewUser();

        await _sut.InsertAsync(user, CancellationToken.None);
        var found = await _sut.GetByEmailAsync("adam@example.com", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.DisplayName.Should().Be("Adam");
    }

    [Fact]
    public async Task GetByEmail_is_case_insensitive_via_normalised_storage()
    {
        await _sut.InsertAsync(NewUser("Adam@Example.COM"), CancellationToken.None);

        var found = await _sut.GetByEmailAsync("adam@example.com", CancellationToken.None);

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task Insert_duplicate_email_throws_ConflictException()
    {
        await _sut.InsertAsync(NewUser(), CancellationToken.None);

        var act = () => _sut.InsertAsync(NewUser(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SetRefreshToken_then_GetByRefreshTokenHash_roundtrips()
    {
        var user = NewUser();
        await _sut.InsertAsync(user, CancellationToken.None);
        var expiry = DateTime.UtcNow.AddDays(7);

        await _sut.SetRefreshTokenAsync(user.Id, "token-hash", expiry, CancellationToken.None);
        var found = await _sut.GetByRefreshTokenHashAsync("token-hash", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.RefreshTokenExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetByRefreshTokenHash_returns_null_after_token_cleared()
    {
        var user = NewUser();
        await _sut.InsertAsync(user, CancellationToken.None);
        await _sut.SetRefreshTokenAsync(user.Id, "token-hash", DateTime.UtcNow.AddDays(7), CancellationToken.None);

        await _sut.SetRefreshTokenAsync(user.Id, null, null, CancellationToken.None);

        var found = await _sut.GetByRefreshTokenHashAsync("token-hash", CancellationToken.None);
        found.Should().BeNull();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoUserRepositoryTests`
Expected: 編譯失敗，找不到 `MongoUserRepository`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Auth/IUserRepository.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(ObjectId id, CancellationToken ct);

    /// <summary>email 以小寫正規化後比對。</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    Task<User?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct);

    /// <summary>email 重複時擲出 <see cref="Domain.Exceptions.ConflictException"/>。</summary>
    Task InsertAsync(User user, CancellationToken ct);

    Task SetRefreshTokenAsync(ObjectId id, string? refreshTokenHash, DateTime? expiresAt, CancellationToken ct);
}
```

`src/MyCollection.Infrastructure/Mongo/MongoUserRepository.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoUserRepository(MongoContext context) : IUserRepository
{
    private IMongoCollection<User> Users => context.Users;

    public Task<User?> GetByIdAsync(ObjectId id, CancellationToken ct) =>
        Users.Find(Builders<User>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        Users.Find(Builders<User>.Filter.Eq(x => x.Email, Normalise(email))).FirstOrDefaultAsync(ct)!;

    public Task<User?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct) =>
        Users.Find(Builders<User>.Filter.Eq(x => x.RefreshTokenHash, refreshTokenHash)).FirstOrDefaultAsync(ct)!;

    public async Task InsertAsync(User user, CancellationToken ct)
    {
        user.Email = Normalise(user.Email);

        try
        {
            await Users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"Email '{user.Email}' is already registered.");
        }
    }

    public Task SetRefreshTokenAsync(ObjectId id, string? refreshTokenHash, DateTime? expiresAt, CancellationToken ct) =>
        Users.UpdateOneAsync(
            Builders<User>.Filter.Eq(x => x.Id, id),
            Builders<User>.Update
                .Set(x => x.RefreshTokenHash, refreshTokenHash)
                .Set(x => x.RefreshTokenExpiresAt, expiresAt),
            cancellationToken: ct);

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter MongoUserRepositoryTests`
Expected: `Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(auth): 新增 IUserRepository 與 MongoDB 實作"
```

---

### Task 7：JWT Token 服務

**Files:**
- Create: `src/MyCollection.Application/Common/ITokenService.cs`
- Create: `src/MyCollection.Infrastructure/Security/JwtOptions.cs`
- Create: `src/MyCollection.Infrastructure/Security/JwtTokenService.cs`
- Test: `tests/MyCollection.Tests/Unit/JwtTokenServiceTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/JwtTokenServiceTests.cs`：

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Security;

namespace MyCollection.Tests.Unit;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "mycollection",
        Audience = "mycollection-web",
        Key = "this-is-a-test-signing-key-with-at-least-32-bytes",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private JwtTokenService CreateSut() => new(Microsoft.Extensions.Options.Options.Create(Options), _time);

    private static User NewUser() => new()
    {
        Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
        Email = "adam@example.com",
        PasswordHash = "hash",
        DisplayName = "Adam",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void CreateAccessToken_embeds_sub_email_and_expiry()
    {
        var token = CreateSut().CreateAccessToken(NewUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value
            .Should().Be("507f1f77bcf86cd799439011");
        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value
            .Should().Be("adam@example.com");
        jwt.Issuer.Should().Be("mycollection");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("mycollection-web");
        jwt.ValidTo.Should().BeCloseTo(new DateTime(2026, 7, 25, 3, 30, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CreateRefreshToken_is_random_each_call()
    {
        var sut = CreateSut();

        sut.CreateRefreshToken().Should().NotBe(sut.CreateRefreshToken());
    }

    [Fact]
    public void HashRefreshToken_is_deterministic()
    {
        var sut = CreateSut();
        var token = sut.CreateRefreshToken();

        sut.HashRefreshToken(token).Should().Be(sut.HashRefreshToken(token));
        sut.HashRefreshToken(token).Should().NotBe(token);
    }
}
```

需要 `Microsoft.Extensions.TimeProvider.Testing` 套件：

```bash
dotnet add tests/MyCollection.Tests package Microsoft.Extensions.TimeProvider.Testing
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter JwtTokenServiceTests`
Expected: 編譯失敗，找不到 `JwtTokenService` / `JwtOptions`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Common/ITokenService.cs`：

```csharp
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Common;

public interface ITokenService
{
    string CreateAccessToken(User user);

    /// <summary>回傳明文 refresh token，只交給用戶端，資料庫僅存其雜湊。</summary>
    string CreateRefreshToken();

    string HashRefreshToken(string refreshToken);

    TimeSpan AccessTokenLifetime { get; }

    TimeSpan RefreshTokenLifetime { get; }
}
```

`src/MyCollection.Infrastructure/Security/JwtOptions.cs`：

```csharp
namespace MyCollection.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "mycollection";
    public string Audience { get; init; } = "mycollection-web";

    /// <summary>HMAC-SHA256 簽章金鑰，至少 32 bytes。正式環境以環境變數提供。</summary>
    public string Key { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
}
```

`src/MyCollection.Infrastructure/Security/JwtTokenService.cs`：

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_options.AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public string CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("name", user.DisplayName)
            ],
            notBefore: now,
            expires: now.Add(AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
```

`JwtSecurityTokenHandler` 預設會把 `sub` 映射成 `ClaimTypes.NameIdentifier`；讀取端在 Task 12 以 `MapInboundClaims = false` 關閉此行為。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter JwtTokenServiceTests`
Expected: `Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(auth): 新增 JWT token 服務"
```

---

### Task 8：MediatR 驗證 pipeline

**Files:**
- Create: `src/MyCollection.Application/Common/ValidationBehavior.cs`
- Test: `tests/MyCollection.Tests/Unit/ValidationBehaviorTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ValidationBehaviorTests.cs`：

```csharp
using FluentAssertions;
using FluentValidation;
using MediatR;
using MyCollection.Application.Common;

namespace MyCollection.Tests.Unit;

public class ValidationBehaviorTests
{
    public record Ping(string Message) : IRequest<string>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator() => RuleFor(x => x.Message).NotEmpty().WithMessage("Message is required");
    }

    // MediatR 14 的 RequestHandlerDelegate<T> 帶 CancellationToken 參數
    private static Task<string> Next(CancellationToken _) => Task.FromResult("pong");

    [Fact]
    public async Task Passes_through_when_no_validators_registered()
    {
        var sut = new ValidationBehavior<Ping, string>([]);

        var result = await sut.Handle(new Ping(""), Next, CancellationToken.None);

        result.Should().Be("pong");
    }

    [Fact]
    public async Task Passes_through_when_valid()
    {
        var sut = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await sut.Handle(new Ping("hi"), Next, CancellationToken.None);

        result.Should().Be("pong");
    }

    [Fact]
    public async Task Throws_ValidationException_when_invalid()
    {
        var sut = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var act = () => sut.Handle(new Ping(""), Next, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Message is required");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ValidationBehaviorTests`
Expected: 編譯失敗，找不到 `ValidationBehavior`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Common/ValidationBehavior.cs`：

```csharp
using FluentValidation;
using MediatR;

namespace MyCollection.Application.Common;

/// <summary>
/// 所有 MediatR 請求進 Handler 前統一跑 FluentValidation，失敗即擲出。
/// Handler 內因此不需要寫任何防禦性檢查。
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators.ToArray();
        if (applicable.Length == 0)
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(applicable.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToArray();

        if (failures.Length > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter ValidationBehaviorTests`
Expected: `Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(app): 新增 MediatR 驗證 pipeline behavior"
```

---

### Task 9：Register Command

**Files:**
- Create: `src/MyCollection.Application/Auth/AuthDtos.cs`
- Create: `src/MyCollection.Application/Auth/RegisterCommand.cs`
- Test: `tests/MyCollection.Tests/Unit/RegisterCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/RegisterCommandTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class RegisterCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    public RegisterCommandTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokens.Setup(t => t.CreateRefreshToken()).Returns("refresh-token");
        _tokens.Setup(t => t.HashRefreshToken("refresh-token")).Returns("refresh-hash");
        _tokens.SetupGet(t => t.AccessTokenLifetime).Returns(TimeSpan.FromMinutes(30));
        _tokens.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(14));
    }

    private RegisterCommandHandler CreateSut() =>
        new(_users.Object, _hasher.Object, _tokens.Object, _time);

    [Theory]
    [InlineData("", "P@ssw0rd!", "Adam")]
    [InlineData("not-an-email", "P@ssw0rd!", "Adam")]
    [InlineData("a@b.c", "short", "Adam")]
    [InlineData("a@b.c", "P@ssw0rd!", "")]
    public void Validator_rejects_invalid_input(string email, string password, string displayName)
    {
        var result = new RegisterCommandValidator().Validate(new RegisterCommand(email, password, displayName));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_input()
    {
        var result = new RegisterCommandValidator()
            .Validate(new RegisterCommand("adam@example.com", "P@ssw0rd!", "Adam"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_stores_hashed_password_and_refresh_token_hash()
    {
        User? inserted = null;
        _users.Setup(r => r.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => inserted = u)
            .Returns(Task.CompletedTask);

        var response = await CreateSut().Handle(
            new RegisterCommand("adam@example.com", "P@ssw0rd!", "Adam"), CancellationToken.None);

        inserted.Should().NotBeNull();
        inserted!.PasswordHash.Should().Be("hashed");
        inserted.PasswordHash.Should().NotContain("P@ssw0rd!");
        inserted.RefreshTokenHash.Should().Be("refresh-hash");
        inserted.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
        response.ExpiresAt.Should().Be(new DateTime(2026, 7, 25, 3, 30, 0, DateTimeKind.Utc));
        response.User.Email.Should().Be("adam@example.com");
        response.User.Id.Should().Be(inserted.Id.ToString());
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter RegisterCommandTests`
Expected: 編譯失敗，找不到 `RegisterCommand` 等型別。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Auth/AuthDtos.cs`：

```csharp
namespace MyCollection.Application.Auth;

public record UserDto(string Id, string Email, string DisplayName);

public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);
```

`src/MyCollection.Application/Auth/RegisterCommand.cs`：

```csharp
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public record RegisterCommand(string Email, string Password, string DisplayName) : IRequest<AuthResponse>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
    }
}

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<RegisterCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshToken = tokenService.CreateRefreshToken();

        var user = new User
        {
            Id = ObjectId.GenerateNewId(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
            RefreshTokenHash = tokenService.HashRefreshToken(refreshToken),
            RefreshTokenExpiresAt = now.Add(tokenService.RefreshTokenLifetime),
            CreatedAt = now
        };

        // email 重複時 Repository 擲 ConflictException → 409
        await users.InsertAsync(user, cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            refreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter RegisterCommandTests`
Expected: `Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(auth): 新增註冊 command"
```

---

### Task 10：Login 與 Refresh Command

**Files:**
- Create: `src/MyCollection.Application/Auth/LoginCommand.cs`
- Create: `src/MyCollection.Application/Auth/RefreshCommand.cs`
- Test: `tests/MyCollection.Tests/Unit/LoginCommandTests.cs`
- Test: `tests/MyCollection.Tests/Unit/RefreshCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/LoginCommandTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class LoginCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly User ExistingUser = new()
    {
        Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
        Email = "adam@example.com",
        PasswordHash = "stored-hash",
        DisplayName = "Adam",
        CreatedAt = DateTime.UtcNow
    };

    public LoginCommandTests()
    {
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokens.Setup(t => t.CreateRefreshToken()).Returns("refresh-token");
        _tokens.Setup(t => t.HashRefreshToken("refresh-token")).Returns("refresh-hash");
        _tokens.SetupGet(t => t.AccessTokenLifetime).Returns(TimeSpan.FromMinutes(30));
        _tokens.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(14));
    }

    private LoginCommandHandler CreateSut() => new(_users.Object, _hasher.Object, _tokens.Object, _time);

    [Fact]
    public async Task Rotates_refresh_token_on_success()
    {
        _users.Setup(r => r.GetByEmailAsync("adam@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingUser);
        _hasher.Setup(h => h.Verify("stored-hash", "P@ssw0rd!")).Returns(true);

        var response = await CreateSut().Handle(
            new LoginCommand("adam@example.com", "P@ssw0rd!"), CancellationToken.None);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
        _users.Verify(r => r.SetRefreshTokenAsync(
            ExistingUser.Id,
            "refresh-hash",
            new DateTime(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_email_throws_ForbiddenException()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => CreateSut().Handle(new LoginCommand("nobody@example.com", "x"), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Wrong_password_throws_same_message_as_unknown_email()
    {
        _users.Setup(r => r.GetByEmailAsync("adam@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingUser);
        _hasher.Setup(h => h.Verify("stored-hash", "wrong")).Returns(false);

        var act = () => CreateSut().Handle(new LoginCommand("adam@example.com", "wrong"), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Be("Invalid email or password.");
    }
}
```

`tests/MyCollection.Tests/Unit/RefreshCommandTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class RefreshCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    public RefreshCommandTests()
    {
        _tokens.Setup(t => t.HashRefreshToken("old-token")).Returns("old-hash");
        _tokens.Setup(t => t.CreateRefreshToken()).Returns("new-token");
        _tokens.Setup(t => t.HashRefreshToken("new-token")).Returns("new-hash");
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokens.SetupGet(t => t.AccessTokenLifetime).Returns(TimeSpan.FromMinutes(30));
        _tokens.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(14));
    }

    private static User UserWithToken(DateTime? expiresAt) => new()
    {
        Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
        Email = "adam@example.com",
        PasswordHash = "hash",
        DisplayName = "Adam",
        RefreshTokenHash = "old-hash",
        RefreshTokenExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    private RefreshCommandHandler CreateSut() => new(_users.Object, _tokens.Object, _time);

    [Fact]
    public async Task Issues_new_pair_and_invalidates_old_token()
    {
        _users.Setup(r => r.GetByRefreshTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserWithToken(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));

        var response = await CreateSut().Handle(new RefreshCommand("old-token"), CancellationToken.None);

        response.RefreshToken.Should().Be("new-token");
        _users.Verify(r => r.SetRefreshTokenAsync(
            It.IsAny<ObjectId>(), "new-hash", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_token_throws_ForbiddenException()
    {
        _users.Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => CreateSut().Handle(new RefreshCommand("old-token"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Expired_token_throws_and_is_cleared()
    {
        var user = UserWithToken(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        _users.Setup(r => r.GetByRefreshTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var act = () => CreateSut().Handle(new RefreshCommand("old-token"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _users.Verify(r => r.SetRefreshTokenAsync(user.Id, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter "LoginCommandTests|RefreshCommandTests"`
Expected: 編譯失敗，找不到 `LoginCommand` / `RefreshCommand`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Auth/LoginCommand.cs`：

```csharp
using FluentValidation;
using MediatR;
using MyCollection.Application.Common;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Auth;

public record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<LoginCommand, AuthResponse>
{
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);

        // 帳號不存在與密碼錯誤回傳相同訊息，避免帳號列舉
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new ForbiddenException(InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshToken = tokenService.CreateRefreshToken();

        await users.SetRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(refreshToken),
            now.Add(tokenService.RefreshTokenLifetime),
            cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            refreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
```

`src/MyCollection.Application/Auth/RefreshCommand.cs`：

```csharp
using FluentValidation;
using MediatR;
using MyCollection.Application.Common;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Auth;

public record RefreshCommand(string RefreshToken) : IRequest<AuthResponse>;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshCommandHandler(
    IUserRepository users,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<RefreshCommand, AuthResponse>
{
    private const string InvalidToken = "Invalid or expired refresh token.";

    public async Task<AuthResponse> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var user = await users.GetByRefreshTokenHashAsync(hash, cancellationToken);

        if (user is null)
        {
            throw new ForbiddenException(InvalidToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt <= now)
        {
            await users.SetRefreshTokenAsync(user.Id, null, null, cancellationToken);
            throw new ForbiddenException(InvalidToken);
        }

        var newRefreshToken = tokenService.CreateRefreshToken();
        await users.SetRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(newRefreshToken),
            now.Add(tokenService.RefreshTokenLifetime),
            cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            newRefreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter "LoginCommandTests|RefreshCommandTests"`
Expected: `Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(auth): 新增登入與 refresh token 輪替"
```

---

### Task 11：全域例外處理器

**Files:**
- Create: `src/MyCollection.Api/GlobalExceptionHandler.cs`
- Test: `tests/MyCollection.Tests/Unit/GlobalExceptionHandlerTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/GlobalExceptionHandlerTests.cs`：

```csharp
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MyCollection.Api;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<IProblemDetailsService> _problemDetails = new();

    private ProblemDetailsContext? Captured { get; set; }

    private GlobalExceptionHandler CreateSut()
    {
        _problemDetails
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => Captured = c)
            .ReturnsAsync(true);

        return new GlobalExceptionHandler(_problemDetails.Object, NullLogger<GlobalExceptionHandler>.Instance);
    }

    private async Task<int> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        await CreateSut().TryHandleAsync(context, exception, CancellationToken.None);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task NotFoundException_maps_to_404() =>
        (await HandleAsync(new NotFoundException("Item", "x"))).Should().Be(404);

    [Fact]
    public async Task ForbiddenException_maps_to_403() =>
        (await HandleAsync(new ForbiddenException())).Should().Be(403);

    [Fact]
    public async Task ConflictException_maps_to_409() =>
        (await HandleAsync(new ConflictException("dup"))).Should().Be(409);

    [Fact]
    public async Task ProviderException_maps_to_502() =>
        (await HandleAsync(new ProviderException("steam", "boom"))).Should().Be(502);

    [Fact]
    public async Task Unknown_exception_maps_to_500_without_leaking_details()
    {
        var status = await HandleAsync(new InvalidOperationException("internal detail"));

        status.Should().Be(500);
        Captured!.ProblemDetails.Detail.Should().NotContain("internal detail");
    }

    [Fact]
    public async Task ValidationException_maps_to_400_with_errors_extension()
    {
        var failures = new[]
        {
            new ValidationFailure("Email", "Email is required"),
            new ValidationFailure("Email", "Email is invalid"),
            new ValidationFailure("Password", "Password too short")
        };

        var status = await HandleAsync(new ValidationException(failures));

        status.Should().Be(400);
        Captured!.ProblemDetails.Extensions.Should().ContainKey("errors");
        var errors = (IDictionary<string, string[]>)Captured.ProblemDetails.Extensions["errors"]!;
        errors["Email"].Should().BeEquivalentTo("Email is required", "Email is invalid");
        errors["Password"].Should().BeEquivalentTo("Password too short");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter GlobalExceptionHandlerTests`
Expected: 編譯失敗，找不到 `GlobalExceptionHandler`。

- [ ] **Step 3: 實作**

`src/MyCollection.Api/GlobalExceptionHandler.cs`：

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Api;

/// <summary>
/// 唯一的錯誤轉換點：所有層都不寫 try-catch，例外一律冒泡到這裡轉成 RFC 9457 ProblemDetails。
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail, errors) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status}: {Title}", status, title);
        }

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int Status, string Title, string? Detail, IDictionary<string, string[]>? Errors) Map(Exception exception) =>
        exception switch
        {
            ValidationException v => (
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                null,
                v.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),

            NotFoundException n => (StatusCodes.Status404NotFound, "Resource not found.", n.Message, null),

            ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden.", f.Message, null),

            ConflictException c => (StatusCodes.Status409Conflict, "Conflict.", c.Message, null),

            ProviderException p => (
                StatusCodes.Status502BadGateway,
                $"Provider '{p.ProviderKey}' failed.",
                p.Message,
                null),

            // 未知例外絕不外洩內部訊息或堆疊
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null, null)
        };
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter GlobalExceptionHandlerTests`
Expected: `Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(api): 新增全域 ProblemDetails 例外處理器"
```

---

### Task 12：HttpUserContext

**Files:**
- Create: `src/MyCollection.Application/Common/IUserContext.cs`
- Create: `src/MyCollection.Api/HttpUserContext.cs`
- Test: `tests/MyCollection.Tests/Unit/HttpUserContextTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/HttpUserContextTests.cs`：

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MyCollection.Api;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class HttpUserContextTests
{
    private static HttpUserContext CreateSut(ClaimsPrincipal? principal)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal ?? new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return new HttpUserContext(accessor);
    }

    private static ClaimsPrincipal Authenticated(string sub) =>
        new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, sub)], "Bearer"));

    [Fact]
    public void Resolves_UserId_from_sub_claim()
    {
        var sut = CreateSut(Authenticated("507f1f77bcf86cd799439011"));

        sut.IsAuthenticated.Should().BeTrue();
        sut.UserId.Should().Be(ObjectId.Parse("507f1f77bcf86cd799439011"));
    }

    [Fact]
    public void Anonymous_request_is_not_authenticated()
    {
        CreateSut(null).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Accessing_UserId_when_anonymous_throws_ForbiddenException()
    {
        var sut = CreateSut(null);

        var act = () => sut.UserId;

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Malformed_sub_claim_throws_ForbiddenException()
    {
        var sut = CreateSut(Authenticated("not-an-objectid"));

        var act = () => sut.UserId;

        act.Should().Throw<ForbiddenException>();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter HttpUserContextTests`
Expected: 編譯失敗，找不到 `HttpUserContext`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Common/IUserContext.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// 目前登入者。Repository 層的所有 filter 都必須以 <see cref="UserId"/> 起頭，
/// 這比在 Handler 逐一檢查可靠：忘記過濾的後果是查不到資料，而不是洩漏資料。
/// </summary>
public interface IUserContext
{
    /// <summary>未通過驗證時擲出 <see cref="Domain.Exceptions.ForbiddenException"/>。</summary>
    ObjectId UserId { get; }

    bool IsAuthenticated { get; }
}
```

`src/MyCollection.Api/HttpUserContext.cs`：

```csharp
using System.IdentityModel.Tokens.Jwt;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Api;

public sealed class HttpUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public ObjectId UserId
    {
        get
        {
            var sub = httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(sub) || !ObjectId.TryParse(sub, out var id))
            {
                throw new ForbiddenException("Request is not associated with an authenticated user.");
            }

            return id;
        }
    }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter HttpUserContextTests`
Expected: `Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(api): 新增 HttpUserContext"
```

---

### Task 13：DI 組裝與 Program.cs

**Files:**
- Create: `src/MyCollection.Application/DependencyInjection.cs`
- Create: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Create: `src/MyCollection.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/MyCollection.Api/Program.cs`（整檔覆寫）
- Modify: `src/MyCollection.Api/appsettings.json`（整檔覆寫）
- Create: `src/MyCollection.Api/appsettings.Development.json`

- [ ] **Step 1: 寫 Application 與 Infrastructure 的 DI 擴充**

`src/MyCollection.Application/DependencyInjection.cs`：

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Common;

namespace MyCollection.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
```

`src/MyCollection.Infrastructure/DependencyInjection.cs`：

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Security;

namespace MyCollection.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MongoContext>();

        services.AddScoped<IUserRepository, MongoUserRepository>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }
}
```

- [ ] **Step 2: 寫 Auth endpoints**

`src/MyCollection.Api/Endpoints/AuthEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Auth;

namespace MyCollection.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        group.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        group.MapPost("/refresh", async (RefreshCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        app.MapGet("/auth/me", (Application.Common.IUserContext userContext) =>
                Results.Ok(new { userId = userContext.UserId.ToString() }))
            .RequireAuthorization()
            .WithTags("Auth");

        return app;
    }
}
```

- [ ] **Step 3: 寫 Program.cs 與設定檔**

`src/MyCollection.Api/Program.cs`（整檔取代範本內容）：

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyCollection.Api;
using MyCollection.Api.Endpoints;
using MyCollection.Application;
using MyCollection.Application.Common;
using MyCollection.Infrastructure;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// 必須早於任何 BSON 序列化：BsonClassMap 一旦建立就永久快取，
// 若在慣例註冊前先序列化過，整個行程都會固定用 PascalCase 欄位名，
// 而 Repository 產生的 filter 是 camelCase —— 查不到資料，授權模型也跟著失效。
MongoConventions.Register();

// MediatR 14 未設定授權金鑰時會在啟動記一則 warning。本專案為個人非營利用途，靜音即可。
builder.Logging.AddFilter("LuckyPennySoftware.MediatR.License", LogLevel.None);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 關閉 sub → ClaimTypes.NameIdentifier 的預設映射，HttpUserContext 才讀得到原始 sub
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing Jwt configuration section.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAuthEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MongoContext>();
    await MongoIndexInitializer.EnsureIndexesAsync(context, CancellationToken.None);
}

app.Run();

/// <summary>供 WebApplicationFactory 取得進入點組件。</summary>
public partial class Program;
```

`src/MyCollection.Api/appsettings.json`：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Mongo": {
    "ConnectionString": "mongodb://localhost:27017",
    "Database": "mycollection"
  },
  "Jwt": {
    "Issuer": "mycollection",
    "Audience": "mycollection-web",
    "Key": "",
    "AccessTokenMinutes": 30,
    "RefreshTokenDays": 14
  }
}
```

`src/MyCollection.Api/appsettings.Development.json`：

```json
{
  "Jwt": {
    "Key": "dev-only-signing-key-do-not-use-in-production-32b"
  }
}
```

正式環境以環境變數 `Jwt__Key` 覆寫。

- [ ] **Step 4: 驗證啟動**

Run: `dotnet build`
Expected: `Build succeeded`，0 Error 0 Warning。

- [ ] **Step 5: Commit**

```bash
git add src
git commit -m "feat(api): 組裝 DI、JWT 認證與 auth 端點"
```

---

### Task 14：端到端 Auth 整合測試

**Files:**
- Create: `tests/MyCollection.Tests/Fixtures/ApiFactory.cs`
- Test: `tests/MyCollection.Tests/Integration/AuthEndpointsTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Fixtures/ApiFactory.cs`：

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MyCollection.Tests.Fixtures;

public sealed class ApiFactory(MongoFixture mongo) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = mongo.DatabaseName,
            ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes!!",
            ["Jwt:Issuer"] = "mycollection",
            ["Jwt:Audience"] = "mycollection-web"
        }));

        return base.CreateHost(builder);
    }
}
```

`tests/MyCollection.Tests/Integration/AuthEndpointsTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Auth;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class AuthEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static object RegisterPayload(string email = "adam@example.com") =>
        new { email, password = "P@ssw0rd!", displayName = "Adam" };

    [Fact]
    public async Task Register_returns_tokens_and_user()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth!.AccessToken.Should().NotBeNullOrEmpty();
        auth.RefreshToken.Should().NotBeNullOrEmpty();
        auth.User.Email.Should().Be("adam@example.com");
    }

    [Fact]
    public async Task Register_with_invalid_payload_returns_400_with_errors()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/register", new { email = "nope", password = "x", displayName = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem.Should().ContainKey("errors");
    }

    [Fact]
    public async Task Register_with_duplicate_email_returns_409()
    {
        await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        var response = await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_403()
    {
        await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        var response = await _client.PostAsJsonAsync(
            "/auth/login", new { email = "adam@example.com", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Access_token_authorises_protected_endpoint()
    {
        var registered = await (await _client.PostAsJsonAsync("/auth/register", RegisterPayload()))
            .Content.ReadFromJsonAsync<AuthResponse>();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.AccessToken);
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["userId"].Should().Be(registered.User.Id);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_old_token_stops_working()
    {
        var registered = await (await _client.PostAsJsonAsync("/auth/register", RegisterPayload()))
            .Content.ReadFromJsonAsync<AuthResponse>();

        var refreshed = await _client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = registered!.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuseOld = await _client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = registered.RefreshToken });
        reuseOld.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter AuthEndpointsTests`
Expected: 編譯失敗，找不到 `ApiFactory`。

- [ ] **Step 3: 補上缺口**

若編譯通過但測試紅燈，常見原因與修法：

1. `Program` 不可見 → 確認 `Program.cs` 結尾有 `public partial class Program;`。
2. `/auth/me` 回 500 而非 401 → 確認 `app.UseAuthentication()` 在 `app.UseAuthorization()` 之前。
3. `userId` 對不上 → 確認 `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()` 與 `options.MapInboundClaims = false` 兩者都有。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter AuthEndpointsTests`
Expected: `Passed: 7`

- [ ] **Step 5: 跑全部測試**

Run: `dotnet test`
Expected: `Failed: 0`，總計約 43 筆通過。

- [ ] **Step 6: Commit**

```bash
git add tests
git commit -m "test: 新增 auth 端到端整合測試"
```

---

## 驗收

- [ ] `dotnet build` 0 Error 0 Warning
- [ ] `dotnet test` 全綠，含 Testcontainers 整合測試
- [ ] `dotnet run --project src/MyCollection.Api` 後 `GET /health` 回 `{"status":"ok"}`（需本機 MongoDB 或先改 `Mongo:ConnectionString`）
- [ ] `POST /auth/register` → `POST /auth/login` → 帶 access token 打 `GET /auth/me` 成功

**下一步：** `docs/superpowers/plans/2026-07-25-02-schema-catalog.md`
