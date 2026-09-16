using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class RenameCategoryFieldCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ICategoryFieldRenamer> _renamer = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    private sealed class StubProtectedKeys(params string[] keys) : IProtectedFieldKeys
    {
        private readonly HashSet<string> _keys = new(keys, StringComparer.Ordinal);
        public bool IsProtected(string key) => _keys.Contains(key);
    }

    private static Category Custom(params string[] keys) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ObjectId.GenerateNewId(),
        Name = "自訂",
        Fields = keys.Select(k => new CategoryField { Key = k, Label = k, Type = FieldType.Text }).ToList(),
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private RenameCategoryFieldCommandHandler Sut(params string[] protectedKeys) =>
        new(_categories.Object, _renamer.Object, new StubProtectedKeys(protectedKeys), _time);

    private void Seed(Category category) =>
        _categories.Setup(r => r.GetAsync(category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(category);

    // ---- validator ----

    [Theory]
    [InlineData("PurchasePrice")]
    [InlineData("purchase price")]
    [InlineData("")]
    public void Validator_rejects_new_key_that_is_not_camel_case(string newKey)
    {
        var result = new RenameCategoryFieldCommandValidator()
            .Validate(new RenameCategoryFieldCommand(ObjectId.GenerateNewId().ToString(), "price", newKey));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_same_key()
    {
        var result = new RenameCategoryFieldCommandValidator()
            .Validate(new RenameCategoryFieldCommand(ObjectId.GenerateNewId().ToString(), "price", "price"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewKey");
    }

    // ---- handler ----

    [Fact]
    public async Task Renames_field_and_reports_moved_items()
    {
        var category = Custom("price", "brand");
        Seed(category);
        _renamer.Setup(r => r.RenameAsync(category.Id, "price", "purchasePrice", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);

        var result = await Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        result.MovedItemCount.Should().Be(12);
        result.Category.Fields.Select(f => f.Key).Should().Equal("purchasePrice", "brand");
        _renamer.VerifyAll();
    }

    [Fact]
    public async Task Throws_not_found_when_old_key_is_not_declared()
    {
        var category = Custom("brand");
        Seed(category);

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejects_protected_old_key()
    {
        var category = Custom("steamAppId");
        Seed(category);

        var act = () => Sut("steamAppId").Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "steamAppId", "appId"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Key");
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejects_new_key_that_is_already_declared()
    {
        var category = Custom("price", "purchasePrice");
        Seed(category);

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == "NewKey");
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Forbids_system_category()
    {
        var system = Custom("platform");
        system.OwnerId = null;
        Seed(system);

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(system.Id.ToString(), "platform", "store"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Does_not_mutate_category_when_renamer_conflicts()
    {
        var category = Custom("price");
        Seed(category);
        _renamer.Setup(r => r.RenameAsync(It.IsAny<ObjectId>(), "price", "purchasePrice", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("2 item(s) already carry 'purchasePrice'."));

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        category.Fields.Single().Key.Should().Be("price");
    }
}
