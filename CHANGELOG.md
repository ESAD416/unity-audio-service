# 變更紀錄

## Unreleased — 第二階段效能與資料準備（2026-09-29）

- 每幀只更新使用中的聲源，音量增益改變才寫入 native property；保留 Master／分類／舊分支淡出的既有行為。
- 無並發限制時省略 active 掃描；快取 key 改用 tuple，命中時直接交付獨立 lease。修正失敗回呼立即重試同 ID 的邊界。
- 新增 `PrepareClip`，成功回呼表示 Unity 的音訊資料已 Loaded；群組、Provider 切換及 Shutdown 的取消通知恰好一次。新增 `Diagnostics.PreparingClips`；Preload 原語意不變。
- 同一 Editor 的暖快取播放配置事件由每次 18 降為 10；256 聲源停止後 1,000 次 Tick 由 9.5355 ms 降為 0.0339 ms。僅為局部量測，完整方法及限制見改善計畫 §5.5。
- 118 項不同 PlayMode 行為測試、1 項量測及 2 項 Reload 情境通過；測試／工具僅保留本機。資料準備仍有解碼與記憶體成本，應在載入階段使用；Player／packed content 驗證未執行。

## Unreleased — 第二階段回呼補強（2026-09-29）

- 修正 BGM 取消回呼內再次播放導致 handle 卡在 Loading；完成事件改於內部狀態變更完成後依序派送，保留即時終態與晚訂閱行為。
- 修正 Addressables 共用載入在第一個等待者釋放時提早卸載；整批結果交付期間保護 native operation，隔離個別回呼例外。
- 修正預載取消／服務關閉時使用者回呼拋錯導致後續通知及清理中斷。群組取消先固定舊請求，回呼新增需求不受影響。Provider 切換先公布新世代；Shutdown 清理期間拒絕重新初始化。
- 設定同步旗標以 finally 還原，避免訂閱者例外永久抑制儲存。
- 本機 PlayMode 106／106 通過（含原 92 項及新增 14 項），Reload 生命週期驗收見改善計畫 §5.5；未推送或發布。

## Unreleased — 第二階段可重用核心（2026-09-29）

- 舊 `void Play*` 簽名保留，轉交共用播放核心；新增 `AudioHandle`／`PlayOptions`，可獨立停止、暫停、恢復及區分完成、停止、取消、失敗與拒絕。已結束 handle 不影響重用聲源，晚訂閱立即通知結果。
- 新增 `AudioCatalog` 與分類內的邏輯 ID／別名／不同 Provider key 映射。Fallback 可選 PreferAvailable 或 PrimaryThenBackup；改變政策會刷新待播放與快取世代。
- 新增素材 lease、共用載入、群組預載及安全釋放；Addressables 合併實際相同位置的 address／GUID。Provider 更換／銷毀或 ReleaseClip 不會提前釋放已取得的有效 lease；外部 clip 不由服務卸載。
- 新增可重用聲源、全域／單音效並發限制及拒絕／替換最舊政策；Loading 預約也納入上限。預設 0 不限數量，請依場景設定。
- 新增 FadeBus、獨立遊戲／背景／個別暫停、靜音、Mixer 驗證與無 Mixer 增益備援，以及播放／池／快取診斷。
- 新增 Initialize／Ready／Shutdown 與 Provider 註冊所有權，處理重複 Prefab、additive 場景、停用／重啟及無 Domain Reload 重入。設定 Reset 使用 handler 實際 key，重載記憶體並廣播。

### 行為與遷移

- 非循環 SFX 現在使用獨立聲源，支援單次淡入。舊 FadeChannel 的循環／非循環選擇僅作用於舊 API 分支；跨新舊實例的分類淡出使用 FadeBus。
- 新建 Addressables provider 預設按需載入，不再自動遍歷全 catalog；已有序列化預載旗標仍保留，亦可明確 Preload 指定群組。
- `saveImmediately=true` 現改為延後合併 Save，預設閒置 0.25 秒後儲存；需要即刻落盤時呼叫 Flush。停用與背景切換也會 Flush。
- Resources 舊 TryGetClip 仍可同步載入；TryGetCachedClip 純查快取。AllowAsyncLoad=false 不等待既有載入，Addressables 只取已駐留素材並取得自身 lease。
- 原 Provider 介面保留。外部 Provider 如需服務可追蹤的持有保證，可採 `IAudioClipLeaseProvider`／`IAudioSynchronousClipProvider`；舊 coroutine 介面由 adapter 保持相容。詳見 README。

### 驗證

Unity 6000.6.3f1／Addressables 2.11.2：92 項 PlayMode（含原有 39 項）全部通過；兩種關閉 Reload 的 Play Mode 重入情境通過。原場景實際輸出、分別停止／暫停／恢復、分類淡出及兩輪 24 聲音池重用通過。測試與工具僅保留本機。Player、content build、其他版本／平台、長時間效能與套件化仍待第三階段；完整紀錄見改善計畫 §5.4。

## Unreleased — 第一階段播放可靠性（2026-09-29）

- 修正舊非同步請求覆蓋新播放、停止後重啟、Provider 更換後誤播。保留 SFX one-shot 並發及只停止循環分支的行為。
- 統一聲源轉換管理，修正淡出後 SFX 重播音量、循環換片被舊淡出停止，以及 BGM 轉場與一般 Fade 競爭。
- 修正 Master 淡出僅影響 BGM：現在涵蓋全部五個聲源，與來源增益及播放包絡相乘，且不修改持久化音量。
- Addressables 使用單一操作紀錄負責完成與釋放；新增可選 `IResultAudioClipProvider`，內建 Addressables／Fallback 直接回報原載入結果，避免失效 handle 或舊等待者誤讀新快取。
- 新增 `Controller.Audio` Runtime assembly，保留腳本 GUID。現階段仍需 Addressables；可選 adapter 的套件化尚未完成。

### 行為說明

- 非停止 Fade 的 Master／來源增益持續到下一個明確調整；停止或新播放只重設播放包絡。
- 聲源 Fade 取代該來源舊待播放請求及轉換。Master 非停止 Fade 只調整全域增益；Master 停止取消當時全部分支請求。
- 停用 Controller 或其 GameObject 會停止聲音並取消舊流程，重新啟用後須重新播放。
- 非循環 SFX 的 `fadeInSeconds` 仍被忽略；獨立單次淡入留待第二階段。

### 驗證

Unity 6000.6.3f1／Addressables 2.11.2：39 項本機 PlayMode 測試全部通過；原範例場景及 Editor 非零音訊輸出檢查通過。測試與輔助工具只保留本機，不隨 Git 交付。Addressables content build、Player 與其他版本／平台尚未驗證。
