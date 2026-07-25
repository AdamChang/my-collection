using System.Globalization;
using FluentValidation.Results;
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

public interface IAttributeValidator
{
    /// <summary>依品類 schema 檢查 attributes，回傳全部失敗（不短路）。</summary>
    IReadOnlyList<ValidationFailure> Validate(Category category, BsonDocument attributes);
}

public sealed class AttributeValidator : IAttributeValidator
{
    public IReadOnlyList<ValidationFailure> Validate(Category category, BsonDocument attributes)
    {
        var failures = new List<ValidationFailure>();
        var definedKeys = category.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var element in attributes.Elements)
        {
            if (!definedKeys.Contains(element.Name))
            {
                failures.Add(new ValidationFailure(
                    $"attributes.{element.Name}",
                    $"'{element.Name}' is not defined in category '{category.Name}'."));
            }
        }

        foreach (var field in category.Fields)
        {
            var property = $"attributes.{field.Key}";
            var present = attributes.TryGetValue(field.Key, out var value) && !value.IsBsonNull;

            if (!present)
            {
                if (field.Required)
                {
                    failures.Add(new ValidationFailure(property, $"'{field.Label}' is required."));
                }

                continue;
            }

            var error = CheckType(field, value!);
            if (error is not null)
            {
                failures.Add(new ValidationFailure(property, error));
            }
        }

        return failures;
    }

    private static string? CheckType(CategoryField field, BsonValue value) => field.Type switch
    {
        FieldType.Text => value.IsString ? null : $"'{field.Label}' must be text.",

        FieldType.Number => value.IsNumeric ? null : $"'{field.Label}' must be a number.",

        FieldType.Bool => value.IsBoolean ? null : $"'{field.Label}' must be true or false.",

        FieldType.Date => IsDate(value) ? null : $"'{field.Label}' must be an ISO-8601 date.",

        FieldType.Url => IsAbsoluteUrl(value) ? null : $"'{field.Label}' must be an absolute http(s) URL.",

        FieldType.Select => !value.IsString
            ? $"'{field.Label}' must be text."
            : field.Options is not null && field.Options.Contains(value.AsString, StringComparer.Ordinal)
                ? null
                : $"'{field.Label}' must be one of: {string.Join(", ", field.Options ?? [])}.",

        _ => $"'{field.Label}' has an unsupported field type."
    };

    /// <summary>
    /// 只接受 ISO-8601。用 TryParseExact 而非 TryParse：後者過於寬鬆，
    /// InvariantCulture 下 "15 January" 會被解析成 2015-01-01，型別驗證形同虛設。
    /// </summary>
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
    ];

    private static bool IsDate(BsonValue value) =>
        value.IsValidDateTime
        || (value.IsString && DateTime.TryParseExact(
            value.AsString,
            IsoFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out _));

    private static bool IsAbsoluteUrl(BsonValue value) =>
        value.IsString
        && Uri.TryCreate(value.AsString, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
