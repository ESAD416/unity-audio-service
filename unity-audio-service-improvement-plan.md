# Unity Audio Service 改善與成熟化計劃

文件日期：2026-09-29

文件版本：1.5

狀態：第一階段 R1～R6、第二階段 C1～C7 實作及本機 Editor 驗收完成；第三階段尚未開始

本文件整合專案審查、三階段改善目標，以及三個開源專案的參考方式。目標是把目前的輕量音訊模組改善成播放可靠、容易重用、能以套件交付的 Unity 音訊服務。

## 1. 專案基準與審查範圍

下表與 1.1 為 2026-09-28 的原始審查紀錄，保留供前後比較；升級後的目前環境與驗證進度見 1.2。

| 項目 | 基準 |
| --- | --- |
| 專案 | [ESAD416/unity-audio-service](https://github.com/ESAD416/unity-audio-service) |
| 審查提交 | [`496a9210b2fdf4178d2c3a4e12f5c0bf40869eb1`](https://github.com/ESAD416/unity-audio-service/tree/496a9210b2fdf4178d2c3a4e12f5c0bf40869eb1)，提交日期 2025-10-17 |
| Unity 專案版本 | `6000.2.6f2` |
| Addressables | `2.7.4` |
| Unity Test Framework | `1.6.0` |
| 正式工作目錄 | `/Users/michael/Unity/Git Repo/unity-audio-service`；使用者已指定後續實作在此進行 |
| 計劃文件主檔 | 正式工作目錄根目錄的 `unity-audio-service-improvement-plan.md` |
| 副本狀態 | 非淺層 clone；本次複查時位於 `main`，117 個已追蹤檔案與原審查副本逐檔一致，無已追蹤檔案修改 |
| 複查時未追蹤檔案 | 本計劃文件、`.DS_Store`；本次只更新計劃文件，不處理其他檔案 |

已檢查程式碼、介面、Prefab、Mixer、範例場景、文件與套件設定。尚未執行 Unity PlayMode、平台建置或效能壓測。

因此，程式流程中可辨識的問題應先轉成可重現測試；平台相容性與效能改善必須以後續實測為準。現有 README 的版本建議，不直接視為已驗證的支援範圍。

### 1.1 指定工作目錄的複查紀錄

2026-09-28 重新遍歷 Git 追蹤內容、比對全部 117 個檔案，並核對核心呼叫流程、Prefab／場景引用、Mixer、Addressables 設定與依賴。三階段方向不變，以下事實納入實作與驗收基準：

| 項目 | 查核結果 | 對計劃的影響 |
| --- | --- | --- |
| 程式與測試 | 9 個 C# 檔案、共 1,915 行；仍無測試程式、asmdef、UPM 套件 manifest、LICENSE 或 CI workflow | 第一階段建立測試基準；第三階段補套件交付，不能把已安裝 Test Framework 視為已有測試 |
| 範例入口 | Build Settings 啟用 `Assets/AudioService/AudioSampleScene.unity`；場景使用 `AudioCtrl.prefab`，DebugMenu 的設定引用指向該 Prefab 內的 handler | 基準驗證從此場景開始，保持設定引用一致；不把場景中的 stripped reference 誤判為第二個 handler |
| Resources 素材 | `Assets/Resources/Audio/` 內有 Mixer 與 4 個 MP3：2 首 BGM、1 個 SFX、1 個 Voice | 套件化必須盤點模組資料夾之外的資產，不能只搬 `Assets/AudioService/Runtime` |
| Addressables 範例 | `Assets/AudioService/minigame_lose.mp3` 以 `minigame_lose` 註冊；label 為空，群組設定同時將 address 與 GUID 放入 catalog | 明確指定此素材驗證 adapter；預載／快取要考量多個 key 指向同一素材，不能只依字串 key 判定不同資源 |
| 現有範例的覆蓋範圍 | DebugMenu 的四個播放 key 都對應 Resources 素材，未預設使用 `minigame_lose` | 按範例選單成功出聲不足以證明按需非同步播放正常；須另測 Addressables 冷載入、失敗、取消與釋放 |
| Mixer 與 Prefab | 既有 Mixer 有 BGM／Sound／Voice 與四個 exposed volume 參數；Prefab 的 `saveImmediately` 為 true，程式欄位預設為 false | 目前問題包含 API 行為與驗證不足，不能直接宣稱現有 Mixer 缺參數；測試同時涵蓋 Prefab 與自行建立元件 |
| Unity 音訊設定 | `ProjectSettings/AudioManager.asset` 設定 32 個 real voices、512 個 virtual voices | 效能報告區分 AudioSource 池、模組並發政策及引擎混音設定；此數字不代表模組已實作並發上限 |
| 資產與專案依賴 | 已追蹤的 Assets 檔案均有對應 `.meta`；示範專案依賴 URP、2D、Input System 及編輯器套件，另有 Cursor Git 依賴 | 分離套件時保留 GUID；先記錄完整專案的匯入基準，再隔離示範與核心依賴 |

本次沒有開啟 Unity 執行測試，也沒有修改上述程式、場景、Prefab 或設定。

### 1.2 Unity 6.6 升級與 API 相容性修正紀錄

記錄日期：2026-09-29（Asia/Taipei）。使用者已用 Unity `6000.6.3f1` 開啟專案；本次依使用者指示修正升級後出現的 `CS0618` 警告。

| 項目 | 目前狀態 |
| --- | --- |
| Editor／專案版本 | `6000.6.3f1`，revision `45d8eee7de74` |
| 主要套件 | Addressables `2.11.2`、URP `17.6.0`、Unity Test Framework `1.8.0`（依目前 manifest 核對） |
| 文件更新時的 Git 基準 | `main`，HEAD `3c53702`（更新編輯器版本）；兩處 API 修正已包含於目前 HEAD，本次文件更新不另建立提交 |
| 驗證場景狀態 | Editor 顯示 `Untitled` 場景，未執行音訊範例播放 |

**修正內容與原因：**

- `Assets/AudioService/Runtime/AudioBootstrap.cs:100`：Controller 自動搜尋由 `FindFirstObjectByType<AudioController>` 改為 `FindAnyObjectByType<AudioController>`；保留明確指定 Controller、既有 Singleton、最後才搜尋的優先順序。
- `Assets/AudioService/Runtime/AudioControllerDebugMenu.cs:67`：設定處理器的備援搜尋由 `FindFirstObjectByType<AudioBootstrap>` 改為 `FindAnyObjectByType<AudioBootstrap>`。
- 兩處均保留 `FindObjectsInactive.Include`，繼續包含未啟用的 GameObject。新版警告指出舊 API 依賴 Instance ID 排序，官方建議改用不依賴此排序的搜尋方式。畫面的三筆警告對應兩處呼叫，其中 DebugMenu 訊息重複出現。
- `FindAnyObjectByType` 不保證多個符合物件中的選取順序。場景有多個候選元件時，應明確指定引用或由後續生命週期設計處理，不能把這次替換當成已解決多實例選取問題。

**已完成的驗證：**

- 檢查差異，程式修改僅為上述兩處 API 替換；`git diff --check` 通過。
- 在已開啟的 Unity Editor 執行 Assets > Refresh，Editor.log 記錄編譯程序 `ExitCode: 0`，並成功重新載入 assembly。
- 重新編譯後的 Console 畫面顯示 0 警告、0 錯誤；此為當次檢查結果，不代表未來所有執行情境皆無問題。

**尚未驗證：** 音訊範例實際播放、EditMode／PlayMode 回歸測試、多候選物件搜尋、Addressables content build、Player 建置與目標平台播放。R1～R6、第二及第三階段均尚未因這次修正而完成；本紀錄不宣告全面支援 Unity 6.6。

## 2. 目標與設計原則

保留目前有價值的架構：`IAudioClipProvider`／`IAsyncAudioClipProvider`、`IAudioSettingsHandler`、BGM／SFX／Voice 分類，以及 Mixer 路由。

改善工作遵循以下原則：

1. 先修正現有行為，再增加能力；每個階段都能獨立驗收。
2. 保留常用的 `PlayBgm()`、`PlaySfx()`、`PlayVoice()` 入口；進階控制以新增介面提供。
3. 同一播放實例只能有一個有效的轉換流程；舊的載入或淡出操作不得控制新的播放。
4. 使用者音量、分類音量、單次播放音量與暫時淡出倍率分開管理。
5. 素材的載入、播放期間持有、快取與釋放，都有明確責任歸屬。
6. 小型專案可以使用簡單入口；只有需要獨立控制時，才接觸播放 handle 與進階設定。
7. 以測試及 Profiler 資料驗證行為與成本；不先宣稱零 GC、零延遲或特定效能提升。
8. 依使用者指定，正式測試、輔助腳本與臨時資料均只保留於本機並由 Git 忽略；正式功能不得依賴它們，完工後可移除。詳細位置與清理方式見 9.1。

前三階段的範圍以目前的 2D 音訊服務為核心。3D 定位／跟隨、節拍同步、進階音樂系統與完整音效編輯器，列為後續擴充。

## 3. 三階段總覽

| 階段 | 目標 | 主要交付 | 驗收重點 |
| --- | --- | --- | --- |
| 第一階段：播放可靠性 | 現有播放、停止、淡入淡出與非同步載入的結果可預期 | 狀態與取消管理、缺陷修正、回歸測試 | 操作交錯時符合最後的有效指令，舊流程不干擾新播放 |
| 第二階段：可重用核心 | 能控制每次播放並管理其素材與聲源生命週期 | 播放 handle、聲源管理、資源持有、設定與服務生命週期 | 單次播放可控制，資源安全回收，初始化與場景切換可預期 |
| 第三階段：對外套件 | 新專案可依文件安裝、使用與更新 | UPM、可選依賴、範例、文件、授權、版本管理與 CI 建置檢查 | 乾淨專案可安裝及執行；支援版本有測試證據 |

執行順序為第一階段 → 第二階段 → 第三階段。第二階段前先固定第一階段的本機回歸測試，保留至跨階段驗證完成；第三階段以第二階段穩定的 API 為基礎。測試程式本身不納入 Git。

## 4. 第一階段：播放可靠性

### 4.1 工作項目

| 編號 | 現況與問題 | 處理方式 |
| --- | --- | --- |
| R1 | BGM、Voice 等非同步載入完成後直接播放，舊請求可能覆蓋新請求；Stop 也未使待播放請求失效 | 加入請求版本與取消狀態。替換式播放與 Stop 使舊請求失效；直接傳入 clip、快取命中與非同步路徑使用相同規則。回呼核對原請求、服務與 Provider 版本，不向已更換的 Provider 重新查詢後誤播 |
| R2 | SFX 淡出停止後，下一次 `PlayOneShot()` 沿用零音量或殘留低音量 | 建立清楚的播放增益基準；新播放不繼承上次停止用的淡出狀態，同時保留玩家設定 |
| R3 | 循環 SFX／Voice 換片且不淡入時，舊淡出可能繼續修改音量並停止新 clip | 所有替換分支先終止舊轉換；延後執行的停止／清除動作核對播放版本 |
| R4 | BGM 切換流程與一般 Fade 各自修改同一聲源 | 統一 BGM 的播放、停止與轉換管理，確保同一時間只有一個有效轉換 |
| R5 | 多個等待者共用 Addressables handle，載入失敗或釋放後仍可能讀取失效 handle | 每筆載入由單一操作紀錄負責完成與釋放；等待者接收結果。恢復執行時核對有效性與操作身分，避免舊操作移除新紀錄 |
| R6 | `FadeChannel(Master)` 實際只影響 BGM；SFX／Voice 分支則既有 `useLoopSource` 聲源選擇語意 | 修正 Master 對所有分類生效，保留 SFX／Voice 的既有聲源選擇；整個分類的增益控制以新增、名稱明確的介面提供，詳見 4.4 |

一次性 SFX 可以並發，不能因加入「最後請求有效」而只剩最後一個音效。其待載入請求須能獨立取消；停止整個 SFX 分類時，應使當時所屬的待播放請求失效。是否丟棄延遲過久的音效，留給明確的播放政策。

取消某次播放需求，不等同於直接釋放仍被其他需求共用的 Addressables 載入。底層載入可能繼續完成，之後再依持有狀態處理結果。

### 4.2 實施方式

1. 在原始基準上確認專案可匯入、編譯與執行範例，記錄既有問題。
2. 建立可控制完成順序與失敗結果的測試 Provider，穩定重現非同步交錯。
3. 以小批次修正 R1～R6；必要時增加最小的 assembly 分離以支援測試。
4. 使用 Unity PlayMode 測試驗證 AudioSource 與 coroutine 的實際行為。
5. 保留現有播放 API；任何語意修正列入變更紀錄。

### 4.3 驗收條件

- [x] A 載入較慢、B 載入較快時，先要求 A 再要求 B，最後維持 B。
- [x] 舊非同步請求之後，改播放快取素材或直接傳入 clip，舊結果同樣不能覆蓋新播放。
- [x] 載入中停止，完成後不會重新發聲。
- [x] `stopLoopOnly: true` 只取消／停止循環分支，保留一次性播放及其待載入請求。
- [x] SFX 淡出停止後能以正確音量重播。
- [x] 循環音效淡出中換片，新片不會被舊流程停止。
- [x] BGM 切換中再淡出或停止，不會被先前切換流程重新啟動。
- [x] 相同 key 的多個等待者遇到失敗或釋放時，不讀取失效 handle，不重複釋放。
- [x] Master 淡出涵蓋所有分類，舊 SFX／Voice 的 `useLoopSource` 選擇仍正確，且不覆寫使用者持久化設定。
- [x] 回歸測試通過，原有範例完成手動播放檢查。

### 4.4 舊 API 的行為與相容性界線

| 介面／情境 | 複查到的現況 | 規劃採取的行為 |
| --- | --- | --- |
| `FadeChannel(Master, ...)` | Master 與 BGM 共用同一個分支，只控制 BGM source | 第一階段修正全域增益／停止語意；`stopAfter` 為 true 時，停止當時所有分類並使相關待播放請求失效。新播放不受殘留停止協程控制 |
| `FadeChannel(Sfx/Voice, ..., useLoopSource)` | 旗標明確選擇循環或非循環 source | 第一階段保留此選擇；第二階段另增如 `FadeBus` 的分類介面，涵蓋該分類所有實例。名稱屬設計方向，不直接把舊 API 的預設行為改為整類淡出 |
| `StopSfx`／`StopVoice(stopLoopOnly)` | 可選擇只停止循環來源 | 取消標記同樣依播放分支作用，不能用一個共用版本號誤取消未指定停止的分支 |
| `PlaySfx(loop: false, fadeInSeconds > 0)` | 非循環分支忽略 `fadeInSeconds`，直接 `PlayOneShot()` | 第一階段補清楚文件／診斷；第二階段以獨立播放實例支援單次淡入，避免對共用 source 淡入而連帶影響其他 one-shot |
| 停止淡出與持續的分類淡出 | 停止淡出使用的 source volume，與之後播放所需基準混在一起 | 停止完成後新播放恢復自身播放基準；使用者刻意設置的分類增益仍持續生效，直到明確恢復 |

這項相容性修訂取代 v1.0 將所有 `FadeChannel` 分支直接改為整類增益的描述；Master 錯誤仍列第一階段修正。

### 4.5 第一階段實作與驗收紀錄

日期：2026-09-29（Asia/Taipei）。起始提交 `3c53702`；實作分支 `improvement/stage-one-reliability`。本節為最新狀態，1.1／1.2 的尚未驗證描述保留作歷史紀錄。

**正式實作：**

| 項目 | 完成內容 |
| --- | --- |
| R1 | 各聲源的請求版本、Provider 版本及 Controller 生命週期版本；直接 clip／同步查詢／非同步採一致規則；SFX one-shot 共用停止世代但不互相取消；循環停止不取消非循環分支 |
| R2／R3 | 播放包絡與持續來源增益分離；重播清除舊停止包絡；換片前取消舊轉換，結尾再次核對轉換版本 |
| R4 | BGM 換片、淡入、淡出與停止使用同一轉換欄位；中途 Fade／Stop 不會被舊轉場重新啟動 |
| R5 | Addressables 每筆操作單一擁有 handle；等待者只讀操作結果；釋放冪等、依紀錄身分移除，舊等待者不移除新紀錄。新增可選 `IResultAudioClipProvider`，Addressables／Fallback 回傳原操作結果，避免同 key 重載後舊請求誤讀新快取；舊 Provider 介面保留 |
| R6 | Master 暫時增益乘到全部五個聲源，不寫入玩家設定；Master 停止取消全部當時請求，各來源停止流程可被新播放獨立取代；SFX／Voice 的 `useLoopSource` 選擇不變 |
| 測試所需 assembly | 新增正式 `Controller.Audio.asmdef`，明確引用 Addressables／ResourceManager；保留既有腳本及資產 GUID，原 Prefab／場景引用驗證通過 |

**確定的相容性行為：**

- 非停止的 Master／來源 Fade 增益持續生效；播放增益為 Master × 來源增益 × 當次播放包絡，再交由既有 Mixer 路由處理玩家音量。停止完成後只恢復包絡，不清除刻意設定的持續增益。
- 聲源 Fade 取代該來源舊的待播放請求及轉換。Master 的非停止 Fade 是全域增益，不取消各來源的正常播放／載入。
- Controller 元件停用與整個 GameObject 停用均取消舊請求、停止轉換與聲音；重新啟用須發出新播放指令。完整服務註冊所有權、場景／Domain Reload 設計仍屬 C6。
- 非循環 SFX 的單次淡入仍未實作，README 已明示限制。播放 handle、獨立 emitter／聲源池、素材播放持有計數與別名統一仍屬第二階段。

**驗證證據：**

| 驗證 | 結果 |
| --- | --- |
| 修正前 PlayMode 基準 | 21 項：3 通過、18 失敗；重現舊請求覆蓋、停止後重啟、舊淡出停止新片及 Master 範圍等問題 |
| 同一批修正後測試 | 21／21 通過 |
| 最終 PlayMode／整合回歸 | 39／39 通過，0 失敗、0 跳過；包含受控 ResourceManager 的共用載入成功／失敗／釋放／重試、Provider 銷毀、舊操作不能讀取新快取，以及實際 `minigame_lose` 冷載入／釋放／重載 |
| 原始範例與 Prefab | 載入 `AudioSampleScene.unity`，檢查腳本及 settings handler 引用；執行原 DebugMenu 的 BGM／SFX／Voice／BGM 轉場，並經 Prefab 的 Fallback 路徑按需載入 Addressables 範例 |
| 有音訊輸出的 Editor 手動操作 | 原場景兩首 BGM 轉場、SFX、Voice、Master 淡出停止及重播；AudioListener 非零輸出峰值：首曲 0.324499、轉場後 0.996679、SFX 0.426237、Voice 0.071228、全部重播 0.396335。這是 UI 操作、播放狀態與 PCM 輸出檢查，不宣稱人工聽感評估 |
| 音量相容性 | Master／來源增益相乘、停止後保留持續增益、Mixer exposed 值不受暫時淡出修改、無 Mixer 的暫時增益及舊聲源選擇測試通過 |

環境：macOS Editor，Unity `6000.6.3f1`，Addressables `2.11.2`，Test Framework `1.8.0`。批次測試未使用 `-nographics` 或停用音訊。詳細 XML／log 與手動輸出紀錄在 `work/stage-one/`；測試在 `Assets/AudioService/Tests/`，含可選 Editor 播放檢查面板；重跑入口及說明在 `Tools/AudioService/`。以上均由既有 Git 忽略規則排除，正式 Runtime 不引用本機驗證 assembly。

**仍未驗證：** Addressables content build／Player 實機播放、其他 Unity 版本與平台、長時間壓測／效能門檻，以及第二階段的完整生命週期矩陣。此次 Addressables 實際素材檢查使用 Editor 資產模式，不能替代 Player 部署驗證。第一階段完成不代表第二、第三階段完成。

Unity 開啟及測試時自動升級序列化的 `ProjectSettings.asset` 差異已恢復至開工基準，不納入本階段功能變更。未推送遠端、未建立 PR、未發布版本。

## 5. 第二階段：可重用核心

### 5.1 預計責任分工

以下名稱代表設計方向，實作時再確定正式命名。

| 元件 | 責任 |
| --- | --- |
| `AudioController`／服務入口 | 保留容易呼叫的 API，協調各子系統 |
| `AudioId`／音效目錄 | 統一音效身分，映射到不同 Provider 的路徑或 address |
| `PlayOptions` | 保存單次播放設定，避免持續增加布林值與位置參數 |
| `AudioHandle` | 控制及查詢某一次播放，不直接暴露可被重用的池內物件 |
| 播放實例／`AudioEmitter` | 管理自身 AudioSource、播放狀態與淡入淡出 |
| 聲源管理器 | 分配、重用、回收聲源，執行並發限制政策 |
| 素材管理層 | 透過既有 Provider 載入，管理等待者、使用計數與快取持有 |
| 設定與增益管理 | 同步玩家設定、Mixer 與 UI，分離持久化音量和暫時效果 |
| 啟動與關閉流程 | 管理初始化、註冊、取消、退訂、釋放與靜態狀態 |

拆分以責任與測試需求為依據，不以類別數量作為完成指標。

### 5.2 核心工作

**C1：提供播放 handle 與結果通知。**

新增可回傳 handle 的播放介面，支援 Stop、Pause、Resume 與狀態查詢；區分 Loading、Playing、Paused，以及完成、取消、停止和失敗等結果。聲源回收到池後，舊 handle 必須失效，不能控制下一次借用同一聲源的播放。完成通知需有「只觸發一次」與訂閱時機的明確契約。

**C2：統一音效識別與 Fallback 策略。**

決定 `AudioId` 的命名空間，讓 Resources 與 Addressables 解析到相同邏輯音效。明確區分查詢快取和發起載入。Fallback 定義為可配置政策，例如立即使用可用來源，或等待主來源失敗後才使用備援；測試不再依預載完成時機偶然選到不同素材。

**C3：建立素材持有與快取規則。**

區分載入中的等待者、播放中的使用者與快取的保留需求。播放結束不必一律卸載，但仍被使用的素材不得提前釋放。外部直接傳入的 AudioClip 由外部持有，服務不得當成自身取得的 Addressables handle 釋放。

預設採按需載入與明確指定的常用素材預載，提供群組／場景用途的釋放入口與快取統計。預算、閒置淘汰或 LRU 的複雜度，依實測需求決定。

目前的預載遍歷 catalog key，而示範群組同時包含 address 與 GUID；同一素材可能經不同 key 保留多個 handle。統一 AudioId／素材映射後，測試別名的去重與成對釋放，不把多個快取名稱直接視為多份音訊記憶體。Resources provider 的查詢目前會同步載入，C2 的新契約須明確區分純快取查詢與可能觸發載入的呼叫。

**C4：加入可重用聲源與並發政策。**

需要獨立控制的 SFX／循環聲音使用獨立播放實例，透過聲源池重用。提供全域與單音效的數量限制，以及拒絕新聲音或替換舊聲音等政策。取出與歸還時完整重設 clip、音量、音高、Mixer、目標引用、事件與轉換狀態。

聲源池減少物件建立／銷毀，但不自動減少正在混音的聲音數量；兩者分別驗證。

**C5：整理設定、音量與暫停語意。**

明確區分玩家設定、單次播放設定與暫時淡出倍率。修改玩家設定時同步儲存與 UI；淡出不寫回 PlayerPrefs。補齊實際的初始化廣播、重設設定與記憶體值更新，避免清除磁碟設定後仍廣播舊值。

設定儲存採合併或明確儲存時機，避免滑桿每次變動都同步寫入。區分遊戲暫停、個別播放暫停、背景切換與靜音；UI 音效是否略過暫停由設定決定。

補上 API／儲存值的輸入正規化：目前 `LoadFromPrefs()` 只 clamp 預設值，未 clamp 已存值；`LinearToDb()` 對非零極小值也未套用最低 dB 下限。先定義超出範圍、NaN／Infinity、負時長與空 key 的處理政策，再加入邊界測試。DebugMenu 的重設操作應使用 handler 的實際 key，不能只刪除寫死的預設 key。

**C6：完成服務與 Provider 的生命週期。**

建立 Initialize／Ready／Shutdown 與註冊所有權。服務關閉或 Provider 更換時，使舊請求失效；正確取消、退訂與釋放。驗證重複 Prefab、跨場景、additive scene、重新進入 Play Mode，以及關閉 Domain Reload 的情境。

Fallback 的主／備 Provider 以介面持有，也須檢查其 Unity 物件是否仍存活，不能只檢查最外層 Fallback。另分別驗證停用元件與停用整個 GameObject，以及重新啟用後的轉換和待播放狀態；兩種停用情境分開記錄測試結果。

**C7：補齊 Mixer 驗證與基本診斷。**

驗證群組、exposed parameter 與設定結果。為保留文件中 Mixer 可選的定位，無 Mixer 模式提供一致的來源增益計算；Mixer 模式則維持相同的邏輯音量語意。詳細記錄可關閉，提供正在播放數、待載入數、失敗原因與快取統計。

### 5.3 相容性與驗收

舊的 `void Play*()` 保留為方便入口，內部轉交新核心。需要 handle 的呼叫使用新增 API，避免直接改動舊方法回傳型別造成相容性問題。若 Provider 需要新增持有能力，優先評估附加介面或 adapter，並提供遷移範例。

- [x] 兩個相同素材的播放可分別停止，彼此不干擾。
- [x] 暫停不被誤判成播放完成並回收。
- [x] 舊 handle 無法控制已被重用的聲源。
- [x] 播放完成、停止、取消與載入失敗可被上層正確區分。
- [x] 共用素材仍有使用者時保持有效；最後持有者離開後依政策釋放。
- [x] 超過聲音上限時有可預期且可診斷的結果。
- [x] 音量、靜音、重設與重新初始化後的設定一致。
- [x] Provider 更換與服務關閉後，舊結果不會重新作用。
- [x] 不同素材別名不造成失配釋放；無效輸入與已損壞設定值有明確結果。
- [x] 新增的分類淡出涵蓋該分類所有實例，舊聲源選擇 API 維持原有範圍。
- [x] 第一階段回歸測試持續通過。

### 5.4 第二階段實作與驗收紀錄

實作日期：2026-09-29。以第一階段本機提交 `97567cd` 為基準，在 `improvement/stage-two-core` 完成。以下是第二階段實際結果；第 4.5 節保留第一階段當時的驗收快照。

| 項目 | 實作結果與決策 |
| --- | --- |
| C1 | 新增 `AudioHandle`、`PlayOptions`、`AudioId`；保留舊 void 播放簽名並轉交 `AudioPlaybackEngine`。單次播放可停止／暫停／恢復，結果區分完成、停止、載入取消、失敗與上限拒絕。每個完成訂閱通知一次，晚訂閱立即收到既有結果；結束 handle 不再持有控制權 |
| C2 | `AudioCatalog` 依分類＋大小寫敏感 ID 映射 Resources／Addressables key 與別名。`IAudioClipCache` 純查快取；`IAudioSynchronousClipProvider` 明確同步取得 lease。Fallback 預設 PreferAvailable，也支援 PrimaryThenBackup；變更政策／Provider／Catalog 取消舊待播放並刷新快取世代 |
| C3 | `AudioClipStore` 分開載入等待者、播放子 lease、普通快取保留與預載群組；提供 Preload／ReleaseGroup／ReleaseUnusedClips。Addressables 依 resource location 合併 address／GUID 的 native operation，所有權成對釋放。舊播放可持有跨 Provider 更換的 lease；外部 clip 不卸載。Resources 只移除自身引用，不強制卸載可能仍被外部持有的資產 |
| C4 | 保留 5 個相容用聲源，其餘獨立 emitter 進池重用；非循環 SFX 現在支援單次淡入。全域／單音效上限計入 Loading 預約，支援 RejectNew／StealOldest。尚無專案預算資料，預設 0 表示不限，不任意宣告適用於所有平台的數量 |
| C5 | 玩家音量、分類／分支增益與播放包絡分離；FadeBus 涵蓋所有分類實例，舊 FadeChannel 保留分支範圍。個別、遊戲、背景暫停各自保存；UI 可略過遊戲暫停。設定延後合併 Save，Flush／停用／背景時保存；Reset 使用實際 key 並重載記憶體及廣播。數值、損壞 Prefs、極小 dB 有明確正規化 |
| C6 | Initialize／Ready／Shutdown、Provider 註冊所有權、停用退訂與重新啟用、重複 Prefab 防護、靜態重設及 Bootstrap 重接。Fallback 檢查內層 Unity Provider 存活；舊 coroutine 例外轉成失敗，遲到 lease 安全歸還 |
| C7 | 驗證 Mixer 群組與 exposed parameter，缺失時以來源增益備援；明確 SetMixer(null) 可禁用預設 Mixer。提供播放／暫停／待載入／聲源池／快取統計與 LastFailure／LastMixerIssue，詳細日誌可關閉 |

本階段自行實作所需核心，沿用第 7 節的設計參考，未複製第三方程式碼。3D、DSP 排程、完整編輯器音效庫、LRU／記憶體預算策略未加入。

**自動驗證：** Unity `6000.6.3f1`、Addressables `2.11.2`、Test Framework `1.8.0`。最終本機 PlayMode **92／92 通過**，其中保留第一階段全部 39 項回歸。另有 **2 項 EditMode 生命週期情境通過**：關閉 Domain Reload、同時關閉 Domain／Scene Reload，各反覆進出 Play Mode；XML 含 Test Framework 的 RequiredTest，總數為 3／3。

覆蓋單次控制、終態／晚訂閱、池重用、預約上限、暫停與自然結束、預載共享／取消／回呼重入、Provider 切換與關閉、別名及實際 Addressables 持有／失敗／重試、Resources 同步路徑、巢狀 Provider 失效、設定與 Mixer 備援、原場景／additive 重複 Prefab、場景卸載、元件與 GameObject 停用／重啟。實際 Addressables 素材仍使用 Editor 資產模式。

**Editor 操作及 Profiler 紀錄：** 在原範例場景，兩次相同素材可各自停止；暫停第二個時輸出降至 0，恢復後重新測得非零音訊輸出，未誤回收。24 個循環 SFX 同播時共建立 29 個聲源（含 5 個相容用聲源），停止後池內 24 個；第二輪再播放 24 個，建立總數仍為 29。分類淡出作用於全部實例；Stop／ReleaseUnused 後快取與使用數回到 0。

`ProfilerRecorder` 的 `AudioService.StageTwo.Burst` 標記有效，操作期間記錄到的最大耗時為 4,339,625 ns（約 4.340 ms）。這是單次本機 Editor 基準，包含初次建立／快取與 Editor 成本，不能視為暖池單次成本、平台效能保證、改善百分比或零 GC 證據。

**本機重跑資料：** `Assets/AudioService/Tests/`、`Tools/AudioService/run-stage-two.sh`、`Tools/AudioService/STAGE-TWO.md`、`work/stage-two/final-playmode.xml`、`work/stage-two/lifecycle.xml`、`work/stage-two/manual-playback.txt`。測試、工具與結果皆依既定規則受 Git 忽略；正式 runtime 不依賴它們。尚未刪除本機驗證資料。

Unity 自動重新序列化的 `ProjectSettings.asset`／`EditorSettings.asset` 已回復至開工基準，不納入功能提交。README 與 CHANGELOG 記錄新 API、舊 API 範圍、按需載入、lease 遷移及自動儲存改為合併延後的相容性影響。

**尚未驗證／交付：** Addressables content build、Player 建置與實機播放、其他 Unity 版本／平台、長時間壓測及資源預算門檻。UPM 核心／可選依賴分離、發布 CI、授權盤點仍為第三階段。未推送遠端、建立 PR 或發布版本。

## 6. 第三階段：對外套件

### 6.1 套件結構與依賴

將核心模組與展示專案分離，形成可由 UPM 安裝的套件。整理 Runtime、Editor、Samples~ 與文件，保留 Unity 資產 GUID 及必要的序列化相容性。本機回歸測試保留在受忽略的宿主專案測試目錄，不隨 UPM 套件發布，也不因套件化而移到會被 Git 追蹤的位置。

遷移清單須包含 `Assets/Resources/Audio/MasterMixer.mixer`、四個 Resources 範例素材、`Assets/AudioService/minigame_lose.mp3`、Addressables 群組／設定、Prefab 與 Build Settings 的場景引用。先以引用關係決定核心、可選 adapter 與 sample 的歸屬，再搬移檔案。範例失去隱含的 Resources／catalog 配置後仍須可依文件重建。

Prefab 的 AudioController 與場景的 DebugMenu 留有舊 namespace 的 `m_EditorClassIdentifier`，但其 `m_Script` GUID 可對應現有腳本；此靜態差異不直接等於 Missing Script。加入 asmdef／變更 assembly 時，以 Unity 匯入與重新序列化結果驗證，避免只改字串卻破壞引用。

Addressables 整合獨立成 adapter 套件／assembly，隔離全部相關引用，包括 Fallback 中的自動建立邏輯。核心＋Resources 模式在未安裝 Addressables 時仍須能編譯。套件位置、名稱與 Git 安裝網址在正式整理時確定，並用乾淨專案驗證。

示範專案可以保留自己的渲染與編輯器套件，音訊核心只宣告實際需要的依賴。

### 6.2 文件與範例

交付內容包括：

- 安裝、初始化、Provider 設定與 Mixer 設定指南。
- 基本播放、音量 UI、BGM 切換、語音完成事件與非同步取消範例。
- Resources-only 與 Addressables adapter 的獨立使用範例。
- 明確涵蓋目前 `minigame_lose` 的 Addressables 按需載入範例，包含關閉全量預載的操作方式。
- 音效 ID、Fallback、停止／取消／暫停及資源釋放的 API 契約。
- 舊版升級指南、相容性變更、常見錯誤與診斷方式。
- CHANGELOG、版本標籤、依選定授權補齊的 LICENSE，以及範例素材來源與授權資訊。

修正文件與實作的差異，包括全量預載描述、音量持久化路徑、Mixer 可選時的行為與初始化廣播。

### 6.3 相容性、本機測試與 CI 建置

目前專案已升級至 Unity `6000.6.3f1`，後續先在此環境補齊播放與建置基準。原始審查版本 `6000.2.6f2` 保留作歷史比較；兩處 API 修正及第一、第二階段 PlayMode 已通過；Addressables content build 與 Player 尚未驗證，不能視為完整部署相容性驗收。Unity 2022／2021 等其他版本只有在實際通過編譯與測試後，才列為已驗證支援。

EditMode／PlayMode 回歸測試在本機執行，詳細結果放在受忽略的 work 目錄，摘要回填本文件。由於測試與輔助腳本不隨 Git 提供，遠端 CI 僅規劃使用已追蹤內容執行套件匯入／編譯與適當的範例建置，不引用本機測試或 Tools 腳本。Unity 授權與執行環境設定納入落地工作；本文件不預設相關憑證已備妥。

Addressables 驗證分為編輯器資產模式與實際 content build／Player 模式，不能只在 Editor 直接讀取 AssetDatabase 時通過就視為部署完成。先驗證目前的本機群組；遠端下載、網路重試與 WebGL 等額外平台行為，在列入支援範圍後才加入對應驗收。

### 6.4 驗收條件

- [ ] 乾淨 Unity 專案能依 README 安裝並執行基本範例。
- [ ] 核心套件未安裝 Addressables 時可編譯。
- [ ] 安裝 adapter 後，非同步載入與取消範例可執行。
- [ ] Addressables content build 完成後，測試 Player 可載入示範素材；Resources-only 也有獨立建置驗證。
- [ ] 套件移動後，Prefab、Mixer 與範例引用仍有效。
- [ ] 文件、API、依賴與實際測試過的版本一致。
- [ ] 版本、變更紀錄、授權與範例素材資訊齊全。
- [ ] CI 在宣告支援的環境完成已追蹤內容的匯入／編譯與範例建置，並產生可查閱的結果。
- [ ] 移出本機測試、輔助腳本與臨時目錄後，正式專案／套件仍可編譯、建置與基本播放。

## 7. 開源專案的具體參考方式

比較以本次下載的固定提交為依據；版本號是該提交的套件宣告，不等同於最新正式 release。這些專案提供設計參考，不構成其全部實作已驗證可靠的保證。

### 7.1 JSAM／Simple Unity Audio Manager

基準：[`a28f36e`](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/tree/a28f36e6548045e9af1936bebe35b59b6134d8e6)，套件宣告 3.1.1。

| 具體參考 | 可採用的概念 | 套用位置與調整 |
| --- | --- | --- |
| [BaseAudioFileObject](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/a28f36e6548045e9af1936bebe35b59b6134d8e6/Runtime/Scripts/BaseAudioFileObject.cs) | 集中保存素材變化、音量、音高、優先度、循環與並發設定 | 第二階段先建立必要的 ID／設定模型；完整 ScriptableObject 音效定義與編輯工具留待擴充。保留 Provider 載入能力 |
| [AudioManager 播放 API](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/a28f36e6548045e9af1936bebe35b59b6134d8e6/Runtime/Scripts/AudioManager.cs) | 每次播放回傳 helper，可指定位置或 Transform | 第二階段參考播放實例概念；以有版本識別的 handle 保護池重用情境，3D 介面後續再加 |
| [BaseAudioFileObjectEditor](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/a28f36e6548045e9af1936bebe35b59b6134d8e6/Editor/BaseAudioFileObjectEditor.cs) | 預覽、波形與循環點編輯流程 | 先改善範例與設定驗證；完整音效編輯器列為後續擴充 |
| [package.json](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/blob/a28f36e6548045e9af1936bebe35b59b6134d8e6/package.json) | UPM 資訊、獨立匯入的功能範例 | 第三階段提供按功能分開的 Samples~ |

### 7.2 Carter Games Audio Manager

基準：[`44bb924`](https://github.com/CarterGames/AudioManager/tree/44bb92407120032b1bacee376ec182b303c940b2)，套件宣告 3.1.2。

| 具體參考 | 可採用的概念 | 套用位置與調整 |
| --- | --- | --- |
| [AudioPlayer](https://github.com/CarterGames/AudioManager/blob/44bb92407120032b1bacee376ec182b303c940b2/Code/Runtime/Audio/Player/AudioPlayer.cs) | Pause／Resume／Stop，以及自然完成、停止等事件 | 第二階段定義播放控制與結果通知，另外補齊本專案需要的載入、取消與失敗狀態 |
| [IEditModule](https://github.com/CarterGames/AudioManager/blob/44bb92407120032b1bacee376ec182b303c940b2/Code/Runtime/Audio/Edit%20Modules/IEditModule.cs) | 播放設定可組合，套用後可恢復 | 第二階段先採輕量 PlayOptions，聲源回收時完整重設；不必立即引入大量設定類別 |
| [AudioScanner](https://github.com/CarterGames/AudioManager/blob/44bb92407120032b1bacee376ec182b303c940b2/Code/Editor/Systems/Scanning/AudioScanner.cs) | 編輯器維護音效庫、手動與檔案變更掃描入口 | 第二階段建立 ID 與素材映射；第三階段補必要驗證，完整自動掃描視規模擴充 |
| [AudioPool](https://github.com/CarterGames/AudioManager/blob/44bb92407120032b1bacee376ec182b303c940b2/Code/Runtime/Systems/Pooling/Sources/AudioPool.cs) | 播放物件與聲源分配／回收 | 第二階段參考責任分工，另驗證事件退訂、舊 handle 失效與素材持有 |

該提交的 README／套件資訊標示 MIT，但 [Licence.md](https://github.com/CarterGames/AudioManager/blob/44bb92407120032b1bacee376ec182b303c940b2/Licence.md) 與部分程式檔頭為 GPL-3.0。此計劃以理解設計並自行實作為基礎；若之後要直接採用程式碼，須先釐清適用授權。

### 7.3 Unity Audio Pooling

基準：[`44570b9`](https://github.com/adammyhre/Unity-Audio-Pooling/tree/44570b934a89420386d6984dced4c3a24fc6f532)。定位為小型實作與教學參考。

| 具體參考 | 可採用的概念 | 套用位置與調整 |
| --- | --- | --- |
| [SoundBuilder](https://github.com/adammyhre/Unity-Audio-Pooling/blob/44570b934a89420386d6984dced4c3a24fc6f532/Assets/_Project/Scripts/AudioSystem/SoundBuilder.cs) | 將一次播放的條件與取得聲源的流程分開 | 第二階段用 PlayOptions／播放請求表達需要的條件 |
| [SoundEmitter](https://github.com/adammyhre/Unity-Audio-Pooling/blob/44570b934a89420386d6984dced4c3a24fc6f532/Assets/_Project/Scripts/AudioSystem/SoundEmitter.cs) | 每個發聲物件管理自身 AudioSource，結束後回收 | 第二階段加入明確狀態，不能只靠 isPlaying 判斷所有完成情境 |
| [SoundManager](https://github.com/adammyhre/Unity-Audio-Pooling/blob/44570b934a89420386d6984dced4c3a24fc6f532/Assets/_Project/Scripts/AudioSystem/SoundManager.cs) | 物件池、高頻音效達上限時替換最舊聲音 | 第二階段擴充成明確的全域／單音效上限；原實作的 frequentSound 共用計數不直接視為完整政策 |

三個參考專案主要協助第二、第三階段。第一階段的非同步競態與淡出缺陷，仍需依本專案的程式流程設計與驗證修正。

## 8. 驗證計劃

| 測試面向 | 主要案例 | 方式 |
| --- | --- | --- |
| 非同步播放 | 完成順序顛倒、改播放快取／直接 clip、載入中停止、只停止循環、Provider 更換、服務關閉 | 可控制結果的測試 Provider＋PlayMode |
| 淡入淡出 | 淡出後重播、淡出中換片、BGM 轉換中停止、Master、新分類 API 與舊聲源選擇相容性 | PlayMode，檢查播放及音量狀態 |
| Addressables | 冷載入、共用載入失敗、釋放時仍有等待者、重新載入同 key、address／GUID 別名、content build 後 Player 播放 | 真實 adapter 整合測試、停用預載的範例與操作紀錄 |
| 播放 handle | 完成一次通知、暫停／恢復、取消、池重用後舊 handle | 狀態測試＋PlayMode |
| 素材生命週期 | 多播放共用素材、外部 clip、最後持有者離開、預載群組釋放 | 使用計數與實際載入／釋放整合驗證 |
| 聲源池與上限 | 大量短音效、循環播放、上限達成、回收後設定無殘留 | PlayMode＋壓力場景 |
| 設定 | 儲存、自訂 key 重設、廣播、靜音、無效數值、無 Mixer、參數缺失、Prefab 與程式預設差異 | 隔離測試用設定 key，EditMode／PlayMode |
| 生命週期 | 重複 Prefab、場景切換、additive scene、Domain Reload 關閉、停用／重新啟用元件或 GameObject、Fallback 內部 Provider 銷毀 | Unity 整合場景 |
| 套件交付 | 乾淨安裝、Resources-only、adapter 安裝、範例引用 | 乾淨專案與 CI |
| 效能 | 冷／熱載入、密集音效、快取回收、記錄與存檔成本 | Unity Profiler；需要時再加入目標裝置量測 |

先記錄基準，再依目標平台與代表性素材量訂定可量測門檻。報告需包含 Unity／套件版本、素材條件、測試場景、測試結果與未驗證範圍。

音訊驗證至少分兩層：本機批次測試檢查請求、handle、引用計數與狀態；有音訊裝置的 Editor 或目標 Player 驗證實際播放、暫停、轉場與可聽結果。遠端 CI 的範圍依 6.3 限定為已追蹤內容的匯入／編譯與建置。無音訊裝置的環境應記錄限制，不把「沒有聲音」直接判定為程式缺陷，也不把純狀態測試當成已驗證聽感。

## 9. 工作副本、提交與執行順序

1. 使用者已指定 `/Users/michael/Unity/Git Repo/unity-audio-service` 為正式工作目錄；後續程式實作、測試、文件與提交均以此為準。舊的 Codex 審查副本只保留作參考，不作實作目標；本文件以專案根目錄這一份為主檔。
2. 開工時重新確認本機變更、分支與遠端基準，再建立改善分支並記錄開始提交。目前此 clone 非淺層，無需預先補取歷史。保留使用者未提交的內容，首次提交時明確納入計劃文件，不順帶加入 `.DS_Store`。
3. 分批完成：基準與測試 → 播放取消 → 淡出一致性 → Addressables 安全性 → handle／素材持有 → 聲源與設定 → 套件與文件。
4. 每批保留可審查的提交，說明問題、實作、相容性影響與驗證結果；提交正式功能與必要文件，本機測試、輔助腳本與臨時輸出不納入提交。
5. 每階段通過驗收後再往下一階段推進；新發現的問題記錄到對應階段，避免混入無關功能。
6. 發布前彙整安裝方式、升級影響、授權與測試矩陣。遠端推送、PR 與版本發布依後續實作／發布指示執行。

本文件不設定未經基準測試支持的工期。完成標準以可驗證的里程碑為主。

### 9.1 本機驗證資料與完工清理

所有路徑以 `/Users/michael/Unity/Git Repo/unity-audio-service` 為根目錄。

| 內容 | 位置 | Git 與清理方式 |
| --- | --- | --- |
| EditMode／PlayMode 回歸測試、測試專用 assembly、測試場景與素材 | `Assets/AudioService/Tests/` | 整個目錄及 `Assets/AudioService/Tests.meta` 由 Git 忽略，完工後可移除 |
| 可重用的驗證／建置輔助腳本及其使用說明 | `Tools/AudioService/` | 目錄及可能的同名 `.meta` 由 Git 忽略，完工後可移除；不擴大忽略其他 Tools 內容 |
| 臨時腳本、測試輸出、日誌與分析資料 | `work/` | 根目錄的 `/work/` 由 Git 忽略，確認結果已摘要保存後可移除 |
| 進度、驗收摘要與決策 | 本計劃文件 | 沿用專案根目錄這一份主檔；本次不把計劃文件加入忽略規則 |

測試只依賴正式程式；Runtime、正式 Editor 工具、Prefab、範例場景與套件設定不得反向引用上述本機目錄。測試專用 asmdef、程式與素材放在 Tests 目錄內，正式功能需要的 assembly 或工具仍屬交付內容，不放進可刪除區。

本機測試與腳本不會出現在其他人的 Git clone，移除後也無法直接重跑，因此完工前先完成所有驗收，將使用版本、測試結果與限制摘要回填本文件。清理前列出可移除目錄，暫時移出它們並驗證正式專案仍可匯入、編譯、建置及基本播放；確認無依賴後，由使用者移除或依其清理指示執行。v1.2 的調整僅更新忽略規則與計劃，沒有刪除資料。

## 10. 後續擴充與待確認事項

### 10.1 完成核心後再評估

- 3D 位置、Transform 跟隨、距離衰減與空間音訊設定。
- 雙聲源交叉淡化、前奏接循環、循環點與 DSP 排程／節拍同步。
- 語音播放時降低背景音量、對話佇列、群組隨機／依序播放。
- 完整 ScriptableObject 音效資產、音效庫掃描、波形與循環點編輯工具。

這些功能不作為前三階段完成的必要條件。普通 BGM 切換在第一階段仍維持先淡出再淡入的既有形式，只修正其控制一致性。

### 10.2 執行到相關階段時確認

| 決策 | 確認時機 | 目前規劃 |
| --- | --- | --- |
| 正式工作副本 | 已由使用者指定；開工時只核對狀態 | `/Users/michael/Unity/Git Repo/unity-audio-service` |
| Unity 與目標平台支援範圍 | 基準測試與第三階段 | 目前以 `6000.6.3f1` 補齊播放／建置驗證；其他版本與平台以實測納入 |
| 資源預算與並發預設值 | 第二階段量測後 | 根據代表性場景設定，避免任意給值 |
| 正式 API 與套件名稱 | 第二、第三階段 | 本文件類別名屬設計方向 |
| 授權與發布形式 | 第三階段發布前 | 由專案擁有者選定，不預先套用授權 |

## 11. 完成狀態記錄

- [x] 原始碼與比較專案的靜態檢視。
- [x] 三階段規劃與參考方式整理。
- [x] 指定工作目錄的 117 檔案比對、場景／資產／套件設定複查，以及 v1.1 計劃更新。
- [x] 依使用者偏好加入本機測試、輔助腳本與臨時目錄的忽略規則，更新為 v1.2。
- [x] Unity `6000.6.3f1` 的兩處過時 API 替換、Editor 重新編譯與 Console 檢查（2026-09-29，見 1.2）。
- [x] Unity 播放基準執行與缺陷重現（21 項基準中 18 項失敗，見 4.5）。
- [ ] 升級後 Addressables content build 與 Player 建置／播放驗證。
- [x] 第一階段實作與驗收（最終 39／39 PlayMode 測試及 Editor 原場景播放檢查通過，見 4.5）。
- [x] 第二階段 C1～C7 實作與本機 Editor 驗收（92／92 PlayMode、2 項 Play Mode 重入情境及原場景操作／聲源池基準通過，見 5.4）。
- [ ] 第三階段實作與驗收。

## 12. 原專案與 Unity 技術依據

- [AudioController：播放、淡出、非同步與音量](https://github.com/ESAD416/unity-audio-service/blob/496a9210b2fdf4178d2c3a4e12f5c0bf40869eb1/Assets/AudioService/Runtime/AudioController.cs)
- [AddressablesAudioClipProvider：載入、快取與釋放](https://github.com/ESAD416/unity-audio-service/blob/496a9210b2fdf4178d2c3a4e12f5c0bf40869eb1/Assets/AudioService/Runtime/AddressablesAudioClipProvider.cs)
- [FallbackAudioClipProvider：來源選擇](https://github.com/ESAD416/unity-audio-service/blob/496a9210b2fdf4178d2c3a4e12f5c0bf40869eb1/Assets/AudioService/Runtime/FallbackAudioClipProvider.cs)
- [AudioBootstrap：註冊與設定同步](https://github.com/ESAD416/unity-audio-service/blob/496a9210b2fdf4178d2c3a4e12f5c0bf40869eb1/Assets/AudioService/Runtime/AudioBootstrap.cs)
- [Unity：Addressables handle 的持有與釋放](https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/AddressableAssetsAsyncOperationHandle.html)
- [Unity：PlayOneShot 可重疊播放](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayOneShot.html)
- [Unity：PlayScheduled 排程播放](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)

- [Unity 6.6：FindFirstObjectByType 過時原因與替代 API](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/object/findfirstobjectbytype)

## 13. 文件修訂紀錄

| 版本 | 日期 | 更新內容 |
| --- | --- | --- |
| 1.0 | 2026-09-28 | 建立三階段改善計劃、開源參考、測試與驗收條件 |
| 1.1 | 2026-09-28 | 確認使用者指定的正式 clone 與文件主檔；記錄逐檔比對及實際資產配置；修正 FadeChannel 的相容性策略；補上混合載入路徑、循環分支取消、one-shot 淡入限制、素材別名、數值邊界、停用生命週期、資產遷移與 Addressables Player 驗證。僅更新文件，尚未實作程式 |
| 1.2 | 2026-09-28 | 正式測試與輔助腳本改為本機保留、Git 忽略、完工可移除；一併忽略 work 與測試資料夾的 Unity .meta；調整套件／CI 範圍，補上移除後仍可獨立運作的驗收。未刪除資料、未實作播放功能 |
| 1.3 | 2026-09-29 | 記錄 Unity `6000.6.3f1` 升級後的目前套件版本、兩處 FindAnyObjectByType 修正及 Editor 編譯／Console 驗證；更新工作狀態與後續驗證基準，保留原始審查快照，明列播放、建置與多實例情境尚未驗證 |
| 1.4 | 2026-09-29 | 完成第一階段 R1～R6、39 項本機回歸與 Editor 原場景播放檢查；記錄結果回傳的可選 Provider 介面、來源／Master 增益及停用語意；更新驗收與仍未驗證範圍 |
| 1.5 | 2026-09-29 | 完成第二階段 C1～C7：handle、目錄、素材 lease、聲源池、設定／暫停與生命週期；92 項 PlayMode 與 2 項重入情境通過，記錄 Editor 操作、29 聲源重用及 Profiler 基準，明列相容性變更與第三階段界線 |
