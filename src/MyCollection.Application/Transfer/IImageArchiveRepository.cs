using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 圖片封存專用的讀取。與 <see cref="Items.IItemRepository"/> 分開：那裡的查詢一律
/// 分頁且帶篩選條件，這裡要的是「全部掃過去」，混進去只會讓日常查詢多一個危險的入口。
///
/// filter 一律以 IUserContext.UserId 起頭。
/// </summary>
public interface IImageArchiveRepository
{
    /// <summary>
    /// 自己名下、至少有一張圖片的品項，依 id 排序。
    ///
    /// 刻意不分 <see cref="ItemSource"/>：IGDB 封面下載會讓 Steam 同步來的品項
    /// 也帶本地圖檔，排除它們等於讓那些圖永遠同步不到另一台機器。
    /// </summary>
    Task<IReadOnlyList<Item>> ListItemsWithImagesAsync(CancellationToken ct);
}
