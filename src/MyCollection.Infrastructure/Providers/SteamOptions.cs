namespace MyCollection.Infrastructure.Providers;

public sealed class SteamOptions
{
    public const string SectionName = "Steam";

    public string BaseAddress { get; init; } = "https://api.steampowered.com/";
    public int TimeoutSeconds { get; init; } = 10;
}
