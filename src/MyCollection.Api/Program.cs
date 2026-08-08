using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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

// 背景作業在自己的 scope 裡先設定 BackgroundUserContext，其餘情況照舊讀 HTTP 身分。
// 兩者都是 scoped，所以互不干擾。
builder.Services.AddScoped<BackgroundUserContext>();
builder.Services.AddScoped<HttpUserContext>();
builder.Services.AddScoped<IUserContext>(sp =>
    sp.GetRequiredService<BackgroundUserContext>().UserId is { } userId
        ? new FixedUserContext(userId)
        : sp.GetRequiredService<HttpUserContext>());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Web", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<StartupHealthState>();

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

// 匯入端點會上傳整包收藏。MediaEndpoints 的單張圖片 10 MB 上限是自己明確檢查的，
// 不依賴這個全域值，所以放寬它不會削弱該處的防護。
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseCors("Web");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAuthEndpoints();
app.MapCategoryEndpoints();
app.MapItemEndpoints();
app.MapMediaEndpoints();
app.MapShowcaseEndpoints();
app.MapShareEndpoints();
app.MapIngestionEndpoints();
app.MapImageTransferEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/health/startup", (StartupHealthState state) =>
        state.IsReady
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "starting" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();
app.MapGet("/health", () => Results.Redirect("/health/live")).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MongoContext>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    await MongoIndexInitializer.EnsureIndexesAsync(context, CancellationToken.None);
    await SystemCategorySeeder.SeedAsync(context, timeProvider, CancellationToken.None);
}

app.Services.GetRequiredService<StartupHealthState>().MarkReady();

app.Run();

/// <summary>供 WebApplicationFactory 取得進入點組件。</summary>
public partial class Program;

public sealed class StartupHealthState
{
    private int _isReady;

    public bool IsReady => Volatile.Read(ref _isReady) == 1;

    public void MarkReady() => Volatile.Write(ref _isReady, 1);

    public void MarkNotReady() => Volatile.Write(ref _isReady, 0);
}
