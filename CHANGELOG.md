# 變更紀錄

## Unreleased — BGM 載入、播放配置與批次控制（2026-09-30）

- 兩首範例 BGM 改用 Streaming＋背景載入；保留品質／取樣率，SFX 與 Voice 設定不變。新增選用的 Audio Imports 檢查／Default profile，以及可取消的準備／播放 DebugMenu 範例。
- 內部播放設定與淡變使用值資料，省去直接 clip 的空 lease 與駐留播放的載入回呼；公開 PlayOptions、handle、lease、晚訂閱及 Provider 釋放契約維持。
- handle 直接定位播放資料，結束即清空；批次停止重用可重入快照並直接操作已找到的播放，維持 StealOldest 與回呼順序。
- 同機 Editor、256 次駐留播放：配置 163,840 → 108,544 bytes；批次停止 0.2413 → 0.1652 ms，暖機控制測點無配置。持續負載配置約降 48%，仍觀察到 GC 同時發生的 3.39 ms 尖峰，不宣稱零 GC 或全面幀率改善。BGM 單一 clip 記憶體約 24.4 MB → 0.20 MB；不代表完整音訊記憶體。
- PlayMode 219／219、Editor／Reload 18／18 通過；macOS Player 選定測試於無圖形與 Metal 圖形模式各 101／101 通過，含實際 packed content、HTTP catalog 更新及 BGM 設定比較。證據、量測方法與界線見改善計畫 §5.13；測試、原生量測工具與資料僅保留本機。

## Unreleased — R11～R15 可靠性修正、單音效索引與閒置縮池（2026-09-30）

- 先提交播放終態、名額與聲源清理，再釋放 Provider lease 及派送通知，修正釋放重入造成超額接納或 Shutdown 後遺留 Loading。釋放例外會記錄且不妨礙其他清理與一次完成通知。
- 外部 `AudioListener.pause` 納入暫停原因，修正 one-shot 提前 Completed；保留 UI 忽略全域暫停、個別／背景暫停與播放淡變凍結的規則。
- 處理 `OnAudioConfigurationChanged`：Playing／Paused 結束為 Failed，Loading 為 Cancelled，原因為 `Audio system configuration changed`；取消舊準備與預載、使服務快取／群組保留失效，重套 Mixer 與設定。保留服務暫停／靜音／音量，不自動續播 BGM；需由呼叫端重新播放或重建自有素材。
- 新增可選 `IAudioClipProviderRefresh`。`RefreshClipProvider()` 會清除內建 Addressables 定位／別名快取並隔離世代，Fallback 向主備來源轉送；已取得的舊 lease 持續有效，晚到結果不回填新快取。執行期 catalog 更新完成後須呼叫此 API；AssetBundle 更新配置仍由專案負責。
- 一般單音效並發判斷改用按需啟用的分類＋ID 計數索引，包含 Loading／Paused，空場後停止維護；替換順序與 R10 拒絕規則保留。
- 新增 `TrimIdleSources(minimumCapacity = 0, maxToRemove = 32)`，明確限量銷毀閒置動態聲源；最低容量指使用中＋閒置總數，排除五個相容聲源，不自動縮池。`Diagnostics.CreatedSources` 表示目前持有數，縮池後會下降。
- 本機 Editor、256 聲源、1,000 次同音效拒絕：ID 路徑 3.3534 → 0.2580 ms；外部 clip 路徑 7.8391 → 0.5488 ms。暖池不限量播放約 0.41 ms，無新增配置事件；方法、首次索引成本、縮池／重建取捨及最終驗收見改善計畫 §5.12。

- 最終完整 PlayMode 205／205（含六項量測）、EditMode／Reload 14／14、macOS Player 選定測試 78／78 通過，原七個失敗案例均轉為通過。Player 使用無圖形模式，驗證真實 packed clip 及 HTTP catalog／AssetBundle 更新；實體裝置切換與其他平台仍未驗證。

## Unreleased — 狀態去重、聲源預熱與拒絕路徑優化（2026-09-30）

- 相同遊戲／背景暫停及同通道靜音直接返回，省略重複快照與聲源巡訪；新播放、延遲載入及池重用仍套用當下狀態。
- 新增 `PrewarmSources(targetCount)`，在主執行緒同步補足動態聲源總容量並回傳新建數量；包含使用中與閒置、排除五個相容聲源。預設不預熱，不改播放上限、不載入素材、不縮池；動態聲源於 Shutdown 清理，詳細契約見 README。
- 完整判斷並發限制後才建立播放資料與必要載入回呼；一般全域限制使用現有計數。沒有完成事件訂閱者時省略通知閉包，BGM 省去額外設定快照。保留 R10 原子拒絕、獨立 handle、設定快照、回呼與替換規則。
- 同機 Editor 的 256 聲源測點：1,000 次全域拒絕由 8.3599 → 0.4486 ms、9,000 → 5,000 次配置事件；重複暫停的 1,000 次配置事件降為 0。最終版本預熱 256 聲源約 0.9560 ms，預熱後首次播放約 0.6305 ms，未預熱首次建立並播放約 1.7089 ms；不包含素材載入／解碼，不代表裝置效能保證，完整方法見改善計畫 §5.10。
- 新增 20 項本機行為回歸與 1 項量測；完整 PlayMode 170／170、本專案 EditMode／Reload 14／14 通過。Player／packed content 尚未驗證。

## Unreleased — R10 並發拒絕修正（2026-09-30）

- 並發限制改為先完整判斷、再停止候選聲音。修正動態調低全域上限後，新請求被拒絕卻先停止舊聲音的問題；拒絕亦保留既有 Loading、BGM 轉場及分支淡出。
- 保留單音效替換釋出全域名額、一次替換多個、最舊順序、Loading 預約、相容分支與完成回呼重入行為。調整上限本身不會立即停止聲音；候選快照僅在接受且需替換時配置。
- 新增 14 項本機回歸，修正前捕捉 6 項失敗；修正後 PlayMode 149／149（145 項行為、4 項量測／觀察）、EditMode／Reload 14／14 通過。原重現情境的既有播放數由錯誤的 3 → 2 修正為維持 3；詳細證據見改善計畫 §5.9。Player／packed content 尚未驗證。

## Unreleased — 音量去重與 Catalog 索引（2026-09-30）

- 音量值相同時省略音訊套用；改變時只寫該 Mixer 參數，聲源備援增益改變才刷新聲源。保留暫時音量轉儲存、設定事件、Reset、初始化與 SetMixer 的完整套用。
- PlayerPrefs 通道 key／預設值改為直接選取，避免每次音量變更建立 key 陣列；延後儲存與自訂 key 行為不變。
- Catalog 改用分類＋ID／別名索引，保留大小寫與先出現者優先的解析契約。RefreshClipProvider 在取消回呼前重建索引；初始化、Editor 修改與反序列化亦處理索引失效。直接修改既有 entry／alias 後仍須明確刷新，詳見 README。
- 新增 GetValidationIssues 與獨立 Editor assembly 的 Catalog Inspector，提示 key 衝突、空值、來源 key 缺失及無效設定；不載入素材或替 Provider／Player 驗證資產存在。
- 同一 Editor 的前後量測：1,000 筆目錄最後別名查詢 10,000 次，423.0434 → 0.6876 ms；256 聲源峰值停止後，含 Mixer 與設定同步的 1,000 次音量變更，36.8321 → 0.1967 ms。索引重建及管理記憶體成本見改善計畫 §5.7，不代表整體遊戲或目標裝置加速幅度。
- PlayMode 134／134（131 項行為、3 項量測）、EditMode 14／14（12 項 Catalog、2 項 Reload）通過。測試與工具僅保留本機；UPM、Player 與 packed content 驗證仍屬第三階段。

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
