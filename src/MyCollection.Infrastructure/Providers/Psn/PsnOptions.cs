namespace MyCollection.Infrastructure.Providers.Psn;

public sealed class PsnOptions
{
    public const string SectionName = "Psn";

    public string OAuthBaseAddress { get; init; } = "https://ca.account.sony.com/api/authz/v3/oauth/";

    public string TrophyBaseAddress { get; init; } = "https://m.np.playstation.com/api/trophy/v1/";

    public int TimeoutSeconds { get; init; } = 10;

    public int TrophyTitlePageSize { get; init; } = 800;
}
