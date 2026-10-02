# 變更紀錄

## Unreleased - 回呼邊界與素材取得精簡（2026-10-02）

- Controller 在初始化設定回呼、Update 來源／Catalog 同步及 ID 解析後核對引擎實例，結束已失效的舊流程。修正 6 個空參考、2 個舊 Update 推進新引擎與 3 個初始化殘留訂閱案例；播放／預載／準備／快取查詢不自動跨生命週期重試。
- Addressables 共用已完成操作查詢、lease 建立及交付規則；快取命中省去 Request 與未使用的定位／延後交付閉包。保留世代、持有計數、同批取消、alias 及刷新／銷毀後舊 lease 有效的契約。
- 失敗診斷直接接收分類、ID 與原因；未就緒控制操作不再為警告建立丟棄的 handle。真正播放失敗、警告文字／去重／64 筆上限／開關／開發版本限制不變，公開 API、Prefab 與序列化欄位不變。
- 同機 Editor：直接 Addressables callback 暖命中每次 320 → 192 bytes，已去重的未就緒控制警告 88 → 0 bytes；既有暖播放保持 192 bytes。來源刷新／音訊重設的池化快照試作雖減少配置，但 CPU、常駐容量與流程複雜度不划算，最終保留原快照；詳見改善計畫 §5.21。
- PlayMode 367／367、Editor／Reload 26／26、macOS packed Player 無圖形／批次 Metal 各 288／288、Resources-only 獨立建置及播放／準備 smoke 通過。既有案例、GUID、專案設定與建置輸入保留；測試／工具僅留本機，人工聽感、其他平台及 IL2CPP 未驗收。

## Unreleased - 通知、載入與準備責任精簡（2026-10-01）

- Catalog.Changed、Fallback.Changed 與 handle.Completed 共用逐訂閱者例外隔離，包含晚訂閱的多播委派；維持通知快照與同步時序。lease 交付仍為單一所有權，VolumeChanged 的例外語意及高頻路徑不改。
- Store 同步／inline 完成後不建立取消閉包，交付快照只容納有效等待者，全部取消則省略。Fallback 同步取得先於 async 請求狀態建立，保留世代檢查、重入、晚到素材釋放與一次完成；同步來源拋錯前已指派的 lease 也會釋放。
- PrepareClip 狀態集中至內部 AudioPreparationQueue，與引擎共用原回呼佇列並每次接收當下 Store；Runtime／Editor 共用唯讀 AudioMixerLayout。公開 API、Prefab、序列化欄位及設定不變。
- 同機量測：Fallback 同步命中每次 448 → 192 bytes；256 等待者全部取消後交付 4,128 → 0 bytes。冷同步 Store 256 次 208,980 → 165,972 bytes；部分 CPU 測點上升，詳見改善計畫 §5.20，不宣稱全面加速。
- PlayMode 326／326、Editor／Reload 26／26、macOS Player 無圖形／批次 Metal 各 250／250，以及 Resources-only 獨立建置／播放／準備 smoke 通過。初次新增測試 GUID 錯誤經清單核對修正，最終已確認新增 22 項行為案例確實執行。測試／工具只留本機；其他平台、IL2CPP、人工操作與聽感未驗收。

## Unreleased - Catalog 與素材交付精簡（2026-10-01）

- Catalog 每筆保存一份不可變解析資料，ID／alias 索引只保存位置，完整建立後一次發布；保留大小寫、首筆衝突優先、無效 ID 及 null／空來源 key 語意。
- 分開 AudioCatalog 映射更新與 Provider 底層刷新。ReplaceEntries、Catalog 指派及序列化變更只更新服務自己的需求／快取世代，不取消外部直接取得素材的請求；來源內容更新仍用 RefreshClipProvider 或 Changed，舊播放 lease 保持有效。
- Store 的兩份交付暫存改為單一快照，仍先保留整批 lease 再回呼；合併三次診斷掃描，移除只有單一呼叫者的 StopAt 轉送層。PrepareClip 拆分延後，未新增播放 API 或改設定／Prefab。
- 同機 Editor、1,000 筆各一個 alias 的索引重建配置每次 158,788 → 124,196 bytes；32 份索引的增量堆觀察約每份 160 → 128 KiB。完全無 alias 時配置增加，詳見改善計畫 §5.19；不宣稱全面加速或零配置。
- 完整 PlayMode 301／301、Editor／Reload 25／25、macOS Player 無圖形／批次 Metal 各 228／228 通過，Resources-only 獨立建置及播放 smoke 通過。測試／工具仍只留本機，其他平台、IL2CPP 與人工聽感未驗收。

## Unreleased - 架構收斂與技術債重構（2026-10-01）

- 播放收斂為 BGM／對話替換槽與獨立播放，共用動態聲源池；移除五個固定相容聲源、bank／分支增益與淡變，以及 Controller 舊播放包裝。Loading 不先占用 AudioSource；保留取消、回呼重入、素材持有、舊 handle 失效與暫停保護。
- Provider 改為單一必要 lease 契約，清除 coroutine／cache／result 相容分支；查快取不載入，同步取得與回呼取得都有釋放責任。Fallback 明確配置主備來源，不再依具體型別分支或自動建立元件。
- Controller 成為單一初始化與依賴宿主，移除 Bootstrap／全域註冊；Mixer 與設定同步抽成 AudioMixerState。Addressables 移入可選 assembly，基本 Prefab 改為 Resources-only，另提供整合版 Prefab。
- Catalog 發布深複製資料、查詢回傳不可變結果，改用 GetEntriesCopy／ReplaceEntries，移除公開可變 Entries。DebugMenu 移入 Samples 並保留 GUID；範例、Inspector 與本機測試同步遷移。
- **破壞性變更**：舊 Controller API、Bootstrap、自訂 Provider、UnityEvent 及 Catalog JSON 需依 [遷移指南](Documentation/Migration.md) 調整；AudioService 日常播放語意保持。舊 FadeChannel 不能一律換名成 FadeBus，循環音效由 handle 管理。
- 完整 PlayMode 285／285、Editor／Reload 24／24、macOS Player 無圖形／批次 Metal 各 209／209 通過；無 Addressables 的乾淨專案匯入／Player 建置與播放 smoke 通過。測試與工具繼續只留本機。
- 暖播放仍為每次 192 bytes；1,024 容量、1 使用中的 1,000 次 Master 淡變 0.3561 → 0.1182 ms。部分控制／釋放 CPU 及不可變 Catalog 索引記憶體、重建成本增加；完整前後資料與限制見改善計畫 §5.18，不宣稱全面加速或零技術債。UPM、人工聽感、其他平台與 IL2CPP 尚未驗收。

## Unreleased - 固定程式碼維護準則（2026-10-01）

- 將「盡可能精簡優雅，並移除或優化可能造成技術債的程式碼」納入改善計畫 §2.2，適用於所有後續修改與維護，並與既有易用性準則一併執行。
- 明訂減少重複與過時設計、清楚的狀態與所有權、相容層的保留依據，以及以行為回歸和必要效能量測驗證簡化；README 與開發流程加入連結。
- 本次僅更新文件，未修改程式、移除既有 API 或重新執行 Unity 測試。

## Unreleased — 播放配置、增益更新與 Addressables 釋放（2026-10-01）

- 無 Catalog 的暖播放／拒絕請求省去預設地址物件；引擎的駐留素材持有改為值資料，省去逐次 user lease／釋放閉包。直接 clip 的 ID 使用不持有 clip 的弱參照快取，Shutdown 清空。保留公開 handle、Provider lease、晚訂閱與原來源 key 語意。
- 音量／靜音與分類淡變只向使用中集合及五個相容聲源套用受影響增益；Tick 合併分類與個別淡變。暫停、延遲載入與池重用保持最新狀態，動態閒置池的歷史容量不再增加平常增益巡訪範圍。
- Addressables 以各操作持有的別名索引移除映射，省去每次釋放的全表掃描與暫存清單；多別名才建立清單。查找索引與快取保留分開，刷新先隔離世代，舊 lease 釋放不會刪除新映射。
- 同機 Editor：256 次暖快取播放 108,544 → 49,152 bytes（每次 424 → 192）；1,024 容量、1 使用中的 1,000 次 Master 淡變 56.2738 → 0.3607 ms；256 操作各 8 別名的釋放 11.1005 → 0.4536 ms、51,200 → 0 bytes。索引建立仍有配置與持有成本，播放仍有配置，沒有宣稱全部控制 CPU 或整體遊戲幀率改善；條件與持續負載尖峰見改善計畫 §5.17。
- 完整 PlayMode 275／275、Editor／Reload 24／24、macOS Player 無圖形／批次 Metal 各 159／159 通過，無跳過；包含真實 packed content、HTTP catalog 更新與範例控制。視窗畫面、人工操作／聽感、其他平台與 IL2CPP 未驗收。測試與輔助資料依原規則僅留本機。

## Unreleased — 範例控制權與集中音訊檢查（2026-09-30）

- 修正 AudioQuickStart 在新音樂／語音失敗或被拒絕後覆蓋舊 handle，導致停止與停用清理失效的問題；保留仍播放聲音的控制權。替換完成回呼中的停止、停用與再次播放，以最新操作為準，避免遺留聲音或覆蓋新 handle。
- Controller Inspector 新增唯讀 Audio checks：集中顯示有效 Listener、Mixer 群組／參數、靜音、零音量、FadeBus 歸零與暫停原因，附恢復提示。檢查不載入音檔、不更改設定，只在 Editor Inspector 存活時更新；不代表已驗證音訊輸出或聽感。
- Inspector 將 Save Immediately 改顯示為 Auto Save／Save Delay，Max Voices 改顯示為 Max Concurrent Sounds；保留原序列化名稱、API、預設值及行為。
- 新增唯讀 GetChannelDiagnostics 與 Diagnostics 的遊戲／背景暫停狀態，供診斷使用；基本播放方式維持不變。
- PlayMode 258／258、Editor／Reload 24／24、macOS Player 無圖形／批次 Metal 各 119／119 通過。本輪桌面工具連線逾時，畫面／人工操作與聽感未驗收；詳見改善計畫 §5.15，測試與輔助資料依原規則僅留本機。

## Unreleased — 入門流程與一致播放介面（2026-09-30）

- 新增 `Controller.Audio.AudioService` 日常入口，所有 Play 方法回傳 handle；取得或忽略 handle 不改播放行為。BGM 替換並循環，SFX 獨立播放，Voice 預設替換對話槽，明確選用 `VoicePlaybackMode.Overlap` 才並發。
- 原 `AudioController` 方法及 UnityEvent 可使用的 void 簽章保留。新 Voice 預設共用舊非循環語音槽；舊 `PlayVoiceHandle` 仍獨立並發、舊循環槽仍保留。全分類操作只推薦 FadeBus／Stop*，不改舊 FadeChannel 的分支語意。
- Editor／Development Build 預設提示有界、去重的播放失敗，包含目前設定的來源 key／路徑；可由 Controller 的 Log Playback Failures 關閉。此為設定診斷，不是實際逐次來源追蹤；一般 Rejected 不自動警告。
- 新增 Controller／Bootstrap Inspector 提示、可操作的 AudioQuickStart 場景、Unity UI adapter 與 SceneAudioSample；README 改為基本導入，技術契約移至 Documentation，完整換場與取消範例另附說明。
- 本輪 PlayMode 243／243、Editor／Reload 18／18、macOS Player 無圖形／批次 Metal 各 104／104 通過，詳見改善計畫 §5.14。Mac 鎖定期間未完成視窗畫面／人工操作驗收；尚未做新使用者操作時間研究。核心的 Addressables 依賴仍在，尚未拆為 UPM。

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
