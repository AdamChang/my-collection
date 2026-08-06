# 存放位置永不公開，評分比照價格由使用者選擇是否公開

`Item` 新增 `StorageLocation`（如「A櫃-第2層」）與 `Rating`（1–10）兩個全域欄位，供精選牆 Hero 模式顯示。公開分享頁的 `PublicItemDto` 是刻意獨立於內部 `ItemDto` 的白名單投影（見程式註解：新欄位不會自動外流），這兩個新欄位預設都不會出現在公開分享頁，除非明確加進去。

決定 `StorageLocation` **永遠不進白名單**，不比照 `ShareLink.IncludePrice` 開一個 `IncludeStorageLocation` 開關——存放位置是能直接拿來定位實體收藏的地理情報，風險比金額更高，不該留一個「使用者可能手滑打開」的選項。`Rating` 則比照 `Price` 模式，新增 `ShareLink.IncludeRating`：評分只是評價性質內容，公開與否沒有安全疑慮，交給使用者自己決定即可。
