using FluentValidation;
using MediatR;
using MyCollection.Application.Auth;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Api.Maintenance;

/// <summary>
/// 邀請只能由維運指令發出，系統內沒有管理者角色（ADR-0013 §一）。
/// 這裡是 CLI 的最外層，等同 HTTP 的 GlobalExceptionHandler，所以在這裡把預期中的例外轉成 exit code。
/// </summary>
public static class InviteMaintenance
{
    public const string Usage = """
        Usage:
          maintenance invite create --email <email> [--days <1-30>]
          maintenance invite revoke --email <email>
          maintenance invite list
        """;

    public static async Task<int> RunAsync(
        string[] args, ISender sender, string? webOrigin, TextWriter output, TextWriter error, CancellationToken ct)
    {
        try
        {
            switch (args)
            {
                case ["invite", "create", ..] when Option(args, "--email") is { } email:
                    var daysText = Option(args, "--days");
                    var days = 7;
                    if (daysText is not null && !int.TryParse(daysText, out days))
                    {
                        break;
                    }

                    var created = await sender.Send(new CreateInvitationCommand(email, days), ct);
                    await output.WriteLineAsync($"Email:   {created.Email}");
                    await output.WriteLineAsync($"Code:    {created.Code}");
                    await output.WriteLineAsync($"Expires: {created.ExpiresAt:u}");
                    await output.WriteLineAsync($"Link:    {webOrigin?.TrimEnd('/')}/register?code={created.Code}");
                    return 0;

                case ["invite", "revoke", ..] when Option(args, "--email") is { } email:
                    var revoked = await sender.Send(new RevokeInvitationCommand(email), ct);
                    await output.WriteLineAsync(revoked == 0
                        ? $"No pending invitation for {email}."
                        : $"Revoked {revoked} pending invitation(s) for {email}.");
                    return 0;

                case ["invite", "list"]:
                    foreach (var i in await sender.Send(new ListInvitationsQuery(), ct))
                    {
                        await output.WriteLineAsync(
                            $"{i.Status,-8} {i.Email,-40} {i.Code}  expires {i.ExpiresAt:u}  used {i.UsedAt:u}");
                    }

                    return 0;
            }
        }
        catch (ValidationException ex)
        {
            await error.WriteLineAsync(string.Join(Environment.NewLine, ex.Errors.Select(e => e.ErrorMessage)));
            return 1;
        }
        catch (ConflictException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 1;
        }

        await error.WriteLineAsync(Usage);
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
