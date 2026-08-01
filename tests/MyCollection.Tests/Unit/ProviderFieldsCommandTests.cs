using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ProviderFieldsCommandTests
{
    private static readonly ObjectId CategoryId = ObjectId.Parse("00000000000000000000000a");

    private readonly Mock<ISearchProvider> _provider = new();
    private readonly Mock<ICategoryRepository> _categories = new();

    public ProviderFieldsCommandTests()
    {
        _provider.SetupGet(p => p.Key).Returns(ProviderKeys.Igdb);
        _provider.SetupGet(p => p.RequiredFields).Returns(
        [
            new CategoryField { Key = "igdbId", Label = "IGDB ID", Type = FieldType.Number },
            new CategoryField { Key = "developer", Label = "開發商", Type = FieldType.Text }
        ]);
    }

    private ProviderRegistry Registry() => new([_provider.Object]);

    private void SetupCategory(params CategoryField[] fields) =>
        _categories.Setup(c => c.GetAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category
            {
                Id = CategoryId,
                OwnerId = ObjectId.GenerateNewId(),
                Name = "Switch 卡帶",
                Fields = fields.ToList()
            });

    [Fact]
    public async Task Reports_every_field_the_category_lacks()
    {
        SetupCategory(new CategoryField { Key = "developer", Label = "我改過的標籤", Type = FieldType.Text });

        var result = await new MissingProviderFieldsQueryHandler(Registry(), _categories.Object)
            .Handle(new MissingProviderFieldsQuery(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        result.Select(f => f.Key).Should().BeEquivalentTo("igdbId");
    }

    [Fact]
    public async Task Reports_nothing_when_the_category_already_declares_everything()
    {
        SetupCategory(
            new CategoryField { Key = "igdbId", Label = "IGDB ID", Type = FieldType.Number },
            new CategoryField { Key = "developer", Label = "開發商", Type = FieldType.Text });

        var result = await new MissingProviderFieldsQueryHandler(Registry(), _categories.Object)
            .Handle(new MissingProviderFieldsQuery(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_category_throws_NotFoundException()
    {
        _categories.Setup(c => c.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var act = () => new MissingProviderFieldsQueryHandler(Registry(), _categories.Object)
            .Handle(new MissingProviderFieldsQuery(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Appends_only_the_missing_fields_and_keeps_user_edited_labels()
    {
        SetupCategory(new CategoryField { Key = "developer", Label = "我改過的標籤", Type = FieldType.Text });

        Category? saved = null;
        _categories.Setup(c => c.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        await new EnsureProviderFieldsCommandHandler(Registry(), _categories.Object)
            .Handle(new EnsureProviderFieldsCommand(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        saved!.Fields.Select(f => f.Key).Should().BeEquivalentTo("developer", "igdbId");
        saved.Fields.Single(f => f.Key == "developer").Label.Should().Be("我改過的標籤");
    }

    [Fact]
    public async Task System_categories_are_rejected_by_the_repository_guard()
    {
        SetupCategory();
        _categories.Setup(c => c.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("System categories cannot be modified."));

        var act = () => new EnsureProviderFieldsCommandHandler(Registry(), _categories.Object)
            .Handle(new EnsureProviderFieldsCommand(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
