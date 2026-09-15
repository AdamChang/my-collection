using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class CategoryCommandTests
{
    private readonly Mock<ICategoryRepository> _repository = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static CategoryFieldDto Field(string key, string type = "Text", string[]? options = null) =>
        new(key, $"{key} label", type, options, false, false, false);

    private static CreateCategoryCommand ValidCommand(params CategoryFieldDto[] fields) =>
        new("公仔", "figure", "Physical", "List", fields.Length == 0 ? [Field("brand")] : fields);

    private sealed class StubProtectedKeys(params string[] keys) : IProtectedFieldKeys
    {
        private readonly HashSet<string> _keys = new(keys, StringComparer.Ordinal);
        public bool IsProtected(string key) => _keys.Contains(key);
    }

    private static Category ExistingCategory(params string[] keys) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ObjectId.GenerateNewId(),
        Name = "自訂",
        Fields = keys.Select(k => new CategoryField { Key = k, Label = k, Type = FieldType.Text }).ToList(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public void Validator_accepts_valid_command()
    {
        new CreateCategoryCommandValidator().Validate(ValidCommand()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_duplicate_field_keys()
    {
        var result = new CreateCategoryCommandValidator()
            .Validate(ValidCommand(Field("brand"), Field("brand")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Brand")]      // 大寫
    [InlineData("my brand")]   // 空白
    [InlineData("brand-name")] // 連字號
    [InlineData("1brand")]     // 數字開頭
    public void Validator_rejects_non_camel_case_field_key(string key)
    {
        new CreateCategoryCommandValidator().Validate(ValidCommand(Field(key))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_unknown_field_type()
    {
        new CreateCategoryCommandValidator().Validate(ValidCommand(Field("brand", "Colour"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_requires_options_for_select_field()
    {
        new CreateCategoryCommandValidator()
            .Validate(ValidCommand(Field("brand", "Select"))).IsValid.Should().BeFalse();

        new CreateCategoryCommandValidator()
            .Validate(ValidCommand(Field("brand", "Select", ["GSC"]))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateHandler_persists_category_with_timestamps()
    {
        Category? saved = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var dto = await new CreateCategoryCommandHandler(_repository.Object, _time)
            .Handle(ValidCommand(Field("brand", "Select", ["GSC", "ALTER"])), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("公仔");
        saved.Kind.Should().Be(CategoryKind.Physical);
        saved.Fields.Should().ContainSingle();
        saved.Fields[0].Type.Should().Be(FieldType.Select);
        saved.Fields[0].Options.Should().BeEquivalentTo("GSC", "ALTER");
        saved.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        dto.Id.Should().Be(saved.Id.ToString());
    }

    [Fact]
    public async Task CreateHandler_persists_default_display_mode()
    {
        Category? saved = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var command = new CreateCategoryCommand("公仔", "figure", "Physical", "Hero", [Field("brand")]);
        var dto = await new CreateCategoryCommandHandler(_repository.Object, _time)
            .Handle(command, CancellationToken.None);

        saved!.DefaultDisplayMode.Should().Be(DisplayMode.Hero);
        dto.DefaultDisplayMode.Should().Be("Hero");
    }

    [Fact]
    public void Validator_rejects_unknown_default_display_mode()
    {
        var command = new CreateCategoryCommand("公仔", "figure", "Physical", "Nope", [Field("brand")]);

        new CreateCategoryCommandValidator().Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateHandler_throws_NotFound_when_missing()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var command = new UpdateCategoryCommand(
            ObjectId.GenerateNewId().ToString(), "公仔", "figure", "Physical", "List", [Field("brand")]);

        var act = () => new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys())
            .Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_rejects_withdrawing_a_protected_field()
    {
        var existing = ExistingCategory("steamAppId", "brand");
        _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var command = new UpdateCategoryCommand(existing.Id.ToString(), "自訂", "box", "Physical", "List", [Field("brand")]);

        var act = () => new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys("steamAppId"))
            .Handle(command, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Fields" && e.ErrorMessage.Contains("steamAppId"));
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_allows_withdrawing_an_unprotected_field()
    {
        var existing = ExistingCategory("steamAppId", "brand");
        _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var command = new UpdateCategoryCommand(existing.Id.ToString(), "自訂", "box", "Physical", "List", [Field("steamAppId")]);

        var dto = await new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys("steamAppId"))
            .Handle(command, CancellationToken.None);

        dto.Fields.Select(f => f.Key).Should().Equal("steamAppId");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_keeps_protected_field_when_it_is_still_declared()
    {
        // 改 Label、改順序都不算撤回
        var existing = ExistingCategory("steamAppId", "brand");
        _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var command = new UpdateCategoryCommand(existing.Id.ToString(), "自訂", "box", "Physical", "List",
            [Field("brand"), new CategoryFieldDto("steamAppId", "改過的名稱", "Number", null, false, false, true)]);

        var dto = await new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys("steamAppId"))
            .Handle(command, CancellationToken.None);

        dto.Fields.Should().Contain(f => f.Key == "steamAppId" && f.Label == "改過的名稱");
    }
}
