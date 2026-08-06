# 精選牆展示模式（Display Mode）與分區設計

精選牆（`/showcase` 與公開分享頁共用）在既有 List 網格之外，新增依品類自動判定的展示模式：`List`（維持現況）、`Hero`（大圖＋側欄資訊，公仔模型／珍藏卡預設）、`Stats`（背景大圖＋遊玩數據，數位遊戲預設）。模式來源是 `Category.DefaultDisplayMode`，`Item` 可用 nullable 欄位覆寫。頁面依序顯示 Hero 單件輪播 → Stats 單件輪播 → Collage 拼貼 → 現有 List 網格；沒有符合品項的分區直接隱藏。

刻意讓 **Collage 拼貼區不受 Display Mode 篩選**——它顯示所有 `IsShowcased = true` 的品項封面照，跟 Hero／Stats 分區各自只挑對應模式的品項是兩條獨立邏輯。原因：Collage 定位是「整個精選牆的動態預覽」，若也照 Display Mode 篩選，預設是 `List` 的品項（音樂專輯、電影光碟等）就永遠不會出現在任何新分區裡，等於精選了也白精選。

## 後續

[ADR-0009](0009-showcase-tabs-are-filters-not-layout-pickers.md)（2026-08-06）改變了這些分區的**呈現方式**：不再依序疊在同一頁，而是四個使用者可切換的頁籤（拼貼牆／焦點展品／遊戲成就／列表）。

本 ADR 的**語意完全不變**——頁籤是篩選器而非版型選擇器，`Category.DefaultDisplayMode` 與 `Item.DisplayMode` 仍然決定品項屬於哪一群，Collage 頁籤也仍然不受展示模式篩選。
