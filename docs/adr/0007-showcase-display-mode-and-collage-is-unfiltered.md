# 精選牆展示模式（Display Mode）與分區設計

精選牆（`/showcase` 與公開分享頁共用）在既有 List 網格之外，新增依品類自動判定的展示模式：`List`（維持現況）、`Hero`（大圖＋側欄資訊，公仔模型／珍藏卡預設）、`Stats`（背景大圖＋遊玩數據，數位遊戲預設）。模式來源是 `Category.DefaultDisplayMode`，`Item` 可用 nullable 欄位覆寫。頁面依序顯示 Hero 單件輪播 → Stats 單件輪播 → Collage 拼貼 → 現有 List 網格；沒有符合品項的分區直接隱藏。

刻意讓 **Collage 拼貼區不受 Display Mode 篩選**——它顯示所有 `IsShowcased = true` 的品項封面照，跟 Hero／Stats 分區各自只挑對應模式的品項是兩條獨立邏輯。原因：Collage 定位是「整個精選牆的動態預覽」，若也照 Display Mode 篩選，預設是 `List` 的品項（音樂專輯、電影光碟等）就永遠不會出現在任何新分區裡，等於精選了也白精選。
