using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
app.MapCategoryEndpoints();
app.MapItemEndpoints();
app.MapMediaEndpoints();
app.MapShowcaseEndpoints();
app.MapShareEndpoints();
app.MapIngestionEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MongoContext>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    await MongoIndexInitializer.EnsureIndexesAsync(context, CancellationToken.None);
    await SystemCategorySeeder.SeedAsync(context, timeProvider, CancellationToken.None);
}

app.Run();

/// <summary>供 WebApplicationFactory 取得進入點組件。</summary>
public partial class Program;
