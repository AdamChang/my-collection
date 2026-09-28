using FluentAssertions;
using MyCollection.Application.Auth;

namespace MyCollection.Tests.Unit;

public class InvitationCodeTests
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void Generate_returns_20_crockford_characters()
    {
        var code = InvitationCode.Generate();

        code.Should().HaveLength(InvitationCode.Length);
        code.Should().Match(c => c.All(Crockford.Contains));
    }

    [Fact]
    public void Generate_does_not_repeat()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => InvitationCode.Generate());

        codes.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("abcde-fghjk-mnpqr-stvwx", "ABCDEFGHJKMNPQRSTVWX")]
    [InlineData("  ABCDE FGHJK\tMNPQR-STVWX ", "ABCDEFGHJKMNPQRSTVWX")]
    [InlineData("iIlL-oO", "111100")]
    [InlineData("", "")]
    public void Normalise_uppercases_strips_separators_and_maps_ambiguous_characters(string input, string expected)
    {
        InvitationCode.Normalise(input).Should().Be(expected);
    }

    [Fact]
    public void Format_groups_by_five()
    {
        InvitationCode.Format("ABCDEFGHJKMNPQRSTVWX").Should().Be("ABCDE-FGHJK-MNPQR-STVWX");
    }

    [Fact]
    public void Normalise_reverses_Format()
    {
        var code = InvitationCode.Generate();

        InvitationCode.Normalise(InvitationCode.Format(code)).Should().Be(code);
    }
}
