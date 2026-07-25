namespace MyCollection.Application.Common;

/// <summary>
/// API 邊界的 DateTime 歸一化。
///
/// 資料層的 UtcOnlyDateTimeSerializer 會拒絕任何 Kind != Utc 的值（避免 UTC+8 機器把
/// 03:00 靜默存成前一天 19:00）。但 System.Text.Json 反序列化沒帶 'Z' 的字串會得到
/// Unspecified，前端只要少寫一個 Z 就會讓請求 500。這裡在進入 Handler 前先歸一化：
/// 沒有時區資訊的輸入一律視為 UTC，帶時區的則換算成 UTC。
/// </summary>
public static class UtcDate
{
    public static DateTime Normalise(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? Normalise(DateTime? value) => value is null ? null : Normalise(value.Value);
}
