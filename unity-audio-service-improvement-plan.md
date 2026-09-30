# Unity Audio Service 改善與成熟化計劃

文件日期：2026-09-30

文件版本：1.19

狀態：完成範例控制權修正、Inspector 名稱與集中音訊檢查；258 項 PlayMode、24 項 Editor／Reload、119 項 macOS Player 選定測試於無圖形／批次 Metal 模式各通過（見 5.15）；視窗畫面與人工操作尚未驗證；第三階段套件化尚未開始

本文件整合專案審查、三階段改善目標，以及開源專案的參考方式。目標是把目前的輕量音訊模組改善成播放可靠、容易重用、能以套件交付的 Unity 音訊服務。使用者已明確指定「簡單、好用、操作直覺」為產品目標：基本播放有單一推薦路線，控制範圍與語音替換規則明確，效能與資源管理能力按需求使用。

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

### 2.1 後續修改的固定易用性準則

**「簡單、好用、操作直覺」是使用者指定的長期設計與驗收原則。** 後續缺陷修正、效能優化、新增功能、API、Inspector／編輯器工具、範例、文件及套件化，都必須依循此原則；不只適用於本次入門流程改善。

1. **基本操作有單一推薦路線。** 讓使用者以少量必要設定完成播放、停止與音量調整；文件先展示最常用的做法，再按需求介紹其他選項，避免要求新手先理解整個內部架構。
2. **名稱與操作結果一致。** 方法名稱、參數及 Inspector 提示應能預示結果，清楚區分單一聲音、整個分類與全域控制。取得 handle 不應改變播放規則；循環、替換與並發各有明確語意。
3. **預設合理，進階能力按需啟用。** 一般 2D 播放沿用簡單流程；預熱、預載、自訂 Provider、快取政策，以及未來的 3D、DSP、LRU、重要聲音保護均按情境選用。增加能力時，不把進階設定變成基本播放的必填步驟。
4. **內部優化減少使用負擔。** 優先由系統處理可自動管理的狀態與資源責任。若新能力確實需要呼叫端管理生命週期，提供清楚的持有、取消與釋放方式及最小範例；不只為了效能數字而增加日常呼叫或設定。
5. **出錯時知道下一步。** 提示應說明具體問題與可採取的修正方式，避免無界重複訊息。API、Inspector、入門文件與可執行範例保持一致，讓使用者能照著操作並確認結果。
6. **既有使用方式可預期。** 優先維持相容性；必要的行為變更須說明原因、影響與遷移方式。推薦入口保持清楚，避免不斷增加功能相近、難以選擇的入口。

在行為正確與資源安全的前提下，優先選擇步驟、概念與例外較少的方案。若需求需要增加操作複雜度，記錄具體使用情境、必要性，以及如何讓不需要該能力的人繼續使用基本流程。

**每次修改的易用性檢查：** 依改動影響在實作說明或驗收紀錄中簡要確認：

- 改善的是哪一項使用任務；修改前後的必要步驟、參數或設定是否增加，增加時是否有明確理由。
- 使用者能否由名稱與提示預期行為，基本播放與既有操作是否仍然直覺。
- 受影響的錯誤提示、文件與範例是否同步，進階功能是否仍可選。
- 使用流程有變動時，以對應任務實際驗證，例如初次播放、替換語音、停止單一聲音或換場清理；記錄驗證結果與尚未驗證的部分。功能測試通過或效能改善，不能單獨視為易用性已驗收。

純內部或文字修正可簡要註明對使用流程的影響，不要求每次都做完整的新手試用；涉及安裝或入門流程的大幅變更時，再安排乾淨專案與新使用者驗證。

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

### 5.5 第二階段補強：研究發現與執行紀錄（2026-09-29）

基準為 `d10d57d`。原 92 項 PlayMode 通過紀錄保留；後續增加三項獨立重現測試，三項皆失敗，揭露原矩陣未涵蓋的回呼邊界。本節將 Codex 工作區的補充研究結論合併至此主檔，後續決策與驗收以本節為準。

| 項目 | 已重現的問題 | 修正與驗收方向 |
| --- | --- | --- |
| R7 | BGM 取消回呼內再次 Play，後續載入完成後仍有 handle 卡在 Loading | 完成內部狀態變更後再通知；驗證 Play／Stop／Shutdown／StealOldest／Provider 切換的回呼重入，不能留下無對應載入的 Loading handle |
| R8 | 共用 Addressables operation 第一個回呼立即 Dispose，第二個取得 null，native operation 提早 release | 交付期間保留 operation；回呼例外隔離；驗證舊 store 丟棄晚到結果時，新 store 仍可取得素材，最後釋放恰好一次 |
| R9 | 預載取消回呼拋例外，ReleaseGroup 跳過其餘通知 | 統一安全回呼派送；Shutdown 清理完整，新的預載不被舊群組取消批次捕獲；補設定同步旗標的 finally 還原 |

本機原重現結果：`work/stage-two-review/regressions.xml`，3／3 失敗（2026-09-29 18:14 台北）。補強後測試與量測輸出放在 `work/stage-two-hardening/`；測試仍依既定規則受忽略。

**效能候選與決策順序（執行前紀錄）：** 修正 R7～R9 後，先量 24／64／256 聲源的首次／快取播放、全部停止後的 Tick CPU 與 GC，再決定使用中聲源更新、無限制 MakeRoom 快速路徑及資料預載。現有 Tick 掃描全部歷史建立的聲源、MakeRoom 無限制仍掃 active、快取命中仍配置 Waiter／delivery 容器，是可定位的工作量；尚未量測前不宣告改善幅度。現有五個 mp3 未啟用 preloadAudioData／loadInBackground，資產載入完成不代表音訊資料已準備，須另量首次 AudioSource.Play。Player／packed content 效能仍屬第三階段驗證。

**新增設計參考：** [AudioConductor 的分類／優先序限額](https://github.com/CyberAgentGameEntertainment/AudioConductor#throttle-typethrottle-limit)、[配置驗證](https://github.com/CyberAgentGameEntertainment/AudioConductor#cuesheet-validation) 與 [ID 生成](https://github.com/CyberAgentGameEntertainment/AudioConductor#cue-enum-definition)，可對應重要聲音保護、Catalog 重複／缺失檢查；[LucidAudio SetLink](https://github.com/annulusgames/LucidAudio#setlink)／[Grouping](https://github.com/annulusgames/LucidAudio#grouping) 可參考播放擁有者與播放群組。這輪核對作者 README／API 文件，未驗證其內部實作或效能、未匯入第三方程式碼。

**Unity 一手依據：** [LoadAudioData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.LoadAudioData.html)、[ObjectPool](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.ObjectPool_1.html)、[Audio Profiler](https://docs.unity3d.com/6000.0/Documentation/Manual/ProfilerAudio.html)、[Addressables 2.11 記憶體](https://docs.unity3d.com/Packages/com.unity.addressables@2.11/manual/memory-assets.html)。release 不等於立即卸載；快取保留不直接等同洩漏。優先權／分類預算、播放 owner、Catalog 索引、TTL／LRU、3D、DSP 排程與雙聲源交叉淡化維持獨立候選，不混入可靠性修正。

**可靠性實作：** 新增核心回呼佇列：終態即時更新，通知延至本次內部狀態變更完成後依序派送；回呼新建播放是較新的請求。StealOldest 改迴圈執行，避免通知重入引發遞迴。Addressables 交付批次增加持有保護，失敗回呼不阻斷其他接收者。ReleaseGroup 先固定取消清單，保留回呼中新建的需求；設定旗標用 finally 還原。Shutdown 清理期間拒絕 Initialize，返回後可正常初始化。

**可靠性驗收：** 106／106 PlayMode 全數通過，包含原 92 項與新增 14 項。新增測試涵蓋 R7～R9、Stop 回呼重新播放、取消回呼 Stop／Shutdown、Provider 新世代、StealOldest 回呼重播、Shutdown 回呼 Initialize、群組回呼新增需求、設定事件拋錯、Addressables 第一個回呼例外／取消其餘等待者／store 世代刷新。另兩項無 Domain Reload／無 Domain 與 Scene Reload 重入情境通過；XML 包含 RequiredTest 共 3／3。結果檔為 `reliability.xml` 與 `lifecycle.xml`。

**效能實作：** 每幀只巡訪使用中的 emitter，回收時以尾端交換移除；增益未變不重寫 AudioSource.volume。分類／Master／舊分支淡出仍在增益變更時更新全部來源，保留既有可觀察行為。無全域／單音效限額時 MakeRoom 直接返回；移除 Loaded 的重複 active 搜尋。快取 key 使用分類＋ID tuple，命中時直接交付獨立 lease，省去 waiter／交付容器；失敗回呼立即重試同一 ID 時建立新 entry，避免舊失敗紀錄干擾重試。聲源池容量與預設並發上限不變。

**資料準備 API：** 新增 `PrepareClip(category, id, group, completed)`；資產取得後視需要呼叫 LoadAudioData，等到 loadState 為 Loaded 才回呼 true。`Preload` 保持只保證資產取得的原語意。準備完成後由群組保留；ReleaseGroup、Provider 世代變更或 Shutdown 取消尚未完成的準備並回呼 false 一次。取消不等於中止 Unity 底層載入，服務不強制 UnloadAudioData；`Diagnostics.PreparingClips` 包含等待資產及音訊資料的需求。所有入口在主執行緒呼叫，回呼可能同步；未啟用 loadInBackground 時仍可能阻塞，應在載入階段呼叫。Streaming 的 Loaded 亦不等於全曲已解碼或保證實際出聲延遲。

**前後量測方法：** 同一台 macOS 27.0、Unity 6000.6.3f1 Editor；可靠性提交 `f5daaa0` 對本次優化版本。兩次均單獨執行 `AudioPerformanceMeasurements.Capture`，相同 fixture／設定：無服務並發上限，24／64／256 個四秒循環測試 clip，先建立並回收來源；每個暖池測點 7 次，表格取中位數。Tick 為同一幀內直接呼叫 Tick(0) 一千次，使用中測點無淡出，不能解讀為一千幀、整體 frame time 或 Unity 混音成本。全數停止後仍保留峰值來源（含 5 個相容聲源），因此可檢驗歷史峰值對閒置更新的影響。

| 聲源峰值 | 測點 | 優化前 ms | 優化後 ms |
| --- | --- | --- | --- |
| 24 | 快取播放整批 | 0.1977 | 0.0570 |
| 24 | 使用中 Tick × 1,000 | 1.2008 | 0.1380 |
| 24 | 停止後 Tick × 1,000 | 1.1284 | 0.0344 |
| 64 | 快取播放整批 | 0.2371 | 0.1297 |
| 64 | 使用中 Tick × 1,000 | 2.5979 | 0.3110 |
| 64 | 停止後 Tick × 1,000 | 2.5605 | 0.0346 |
| 256 | 快取播放整批 | 1.3422 | 0.5697 |
| 256 | 使用中 Tick × 1,000 | 9.8204 | 1.1885 |
| 256 | 停止後 Tick × 1,000 | 9.5355 | 0.0339 |

`GC.Alloc` 使用主執行緒 ProfilerRecorder 的 sample Count 記錄**配置事件次數**，每次執行先用已知配置校驗；不是配置 bytes。暖快取整批配置在 24／64／256 聲音下，分別由 432／1,152／4,608 次降為 240／640／2,560 次，即每次播放由 18 降為 10 次。上述 active／idle Tick 測點均未記錄到配置，不表示整個服務零 GC。計時包含相同的 profiler 採樣負擔；單次冷建立值保留 CSV，但受 JIT／原生物件建立影響，不據此推算穩定改善率。

**音訊資料 A/B：** 在優化後同一次量測中，以範例 ukulele_song 的已取得資產測 3 次：音訊資料 Unloaded 時，AudioSource.Play 呼叫中位數為 257.971 ms；先 LoadAudioData 後 Play 為 0.0188 ms，而準備本身仍花 239.404 ms。這證明可將本範例的同步準備成本移出首次播放，沒有消除解碼工作。AudioListener.GetOutputData 首次非零值的輪詢觀察分別為 271.076／7.542 ms；受幀／音訊緩衝影響，不等同使用者耳端延遲。準備後 Profiler 回報此 clip 約 24.5 MB，不代表專案完整 Audio Memory，也不據此承諾 ReleaseGroup 後立即歸零。正式五個音檔匯入設定未修改；背景模式只使用受忽略的測試副本驗證。

**最終驗收：** 118 項不同的 PlayMode 行為測試通過，另有 1 項量測檢查。`final-playmode.xml` 為 117／117（116 行為＋1 量測）；之後 `final-background.xml` 的 PrepareClip fixture 12／12 通過，其中 10 項重跑、2 項新增真實背景 Loading／途中取消情境。正式程式保持不變，獨立 `after.xml` 量測 1／1 通過；`final-lifecycle.xml` 3／3 通過，包含 2 項 Reload 生命週期情境及框架 RequiredTest。新增測試也涵蓋池中交換移除的同幀大量淡出、閒置分類淡出後重用、暫停時設定變更、PrepareClip 的共享／失敗／回呼重入／Provider 切換／Shutdown。原 106 項可靠性回歸保留。

**重跑與交付：** 本機 runner 為 `Tools/AudioService/run-hardening.py`，說明為 `Tools/AudioService/HARDENING.md`；原始量測為 `work/stage-two-hardening/perf-baseline.csv`、`perf-after.csv`，同目錄保留 XML 與日誌。資料、測試與 runner 均受 Git 忽略，正式功能不依賴它們。可靠性提交為 `f5daaa0`，本次效能/API 另以獨立提交保存；僅本機 commit，沒有 push。

本次完成定位到的更新／快取成本與顯式資料準備；尚未進行 Player、packed Addressables、平台長時間壓測、GC bytes、DSP CPU 或完整音訊記憶體量測。Catalog 索引／驗證、重要聲音保護、池容量政策、播放 owner、LRU 與 3D／DSP 功能仍留待需求明確後評估；第三階段尚未開始。

### 5.6 套件化前再次搜尋與優化複查（2026-09-29）

本輪依使用者要求重新搜尋目前可取得的開源專案與 Unity 官方資料，對照正式 Runtime `1a3086b`，並補做本機量測。這是研究與驗證紀錄；正式 Runtime 未修改，第三階段及第 10.1 節的可選擴充尚未開始。既有 118 項行為測試紀錄保留，本輪只執行新增的研究量測 fixture，不能視為完整回歸重跑。

**本輪查核來源與範圍：**

| 來源 | 固定版本／資料日期 | 實際查核與可參考部分 |
| --- | --- | --- |
| [AudioConductor](https://github.com/CyberAgentGameEntertainment/AudioConductor/tree/e35687772a3283c6264ecf709b50f9aec8985372) | GitHub HEAD `e356877`，2026-09-04；package 宣告 2.5.1 | 讀取播放控制、更新、池與驗證規則原始碼；使用 handle 索引、可重用移除清單、one-shot 交換移除，並有 Prewarm／Shrink 與重複 ID／缺失素材檢查 |
| [LucidAudio](https://github.com/annulusgames/LucidAudio/tree/97baab11fce25f19999713b397031bc4aab60480) | GitHub HEAD `97baab1`，2023-07-10 | 讀取 AudioPlayer、Manager 與 AudioLinkTrigger；播放跟隨物件停用／銷毀的行為可作後續 owner／scope 參考。提交較舊，不據此宣稱支援目前 Unity 版本 |
| [Unity3D-SoundManager](https://github.com/baratgabor/Unity3D-SoundManager/blob/b8e13d7082492d3b71d39fb425e47254ab6386a6/SoundManager.cs) | GitHub HEAD `b8e13d7`，2019-05-11 | 新增的歷史設計參考；核對 Inspector 資料轉 Dictionary 及初始聲源建立。僅參考索引／預熱方式，不作為套件替換或效能優劣依據 |
| [Unity Open Project 1 音訊系統](https://github.com/UnityTechnologies/open-project-1/wiki/Audio-system) | 作者 Wiki，2021-07-30 | 文件說明啟動時建立 SoundEmitter 池與獨立 AudioConfig；本輪未執行或完整審查該專案 |

JSAM、Carter Games、Unity Audio Pooling 的現有公開資料亦列入搜尋比較，先前固定版本的設計對應維持第 7 節紀錄。本輪未執行上述第三方專案，未測量它們與本服務的速度差，也未將第三方程式碼移入正式功能。下載的參考檔案與提交 metadata 只保存在受忽略的 `work/optimization-research-20260929/`。

**可確認的改善空間：**

| 項目 | 本專案證據 | 建議與優先程度 |
| --- | --- | --- |
| 音量更新去除重複工作 | `AudioController.SetVolume` 不因值相同而省略套用；ApplyMixerSettings 寫四個參數並 RefreshGains，返回後再 RefreshGains。已套用 Mixer 且 handler 廣播新值時，回傳事件再進一次 SetVolume，形成一次設定最多 8 次 SetFloat、4 次全池增益巡訪的路徑 | 近期優先候選：區分實際改變的參數、設定持久化與來源增益更新，合併重複套用。保留初始化、Mixer 備援、設定 Reset 與事件契約，不能只在最外層看到相同值就跳過所有工作 |
| Catalog 索引與編輯時驗證 | `AudioCatalog.TryResolve` 每次巡覽 Entries／Aliases；同分類重複 ID／alias 目前由先找到者決定，缺少 authoring validation | 近期優先候選：建立分類＋ID／alias 索引，明確處理重複、空值與來源缺失；Catalog 變更及 RefreshClipProvider 後重建。索引需保留無 Catalog 時的字串 key 相容性。參考 [AudioConductor 重複 ID 規則](https://github.com/CyberAgentGameEntertainment/AudioConductor/blob/e35687772a3283c6264ecf709b50f9aec8985372/Packages/AudioConductor/Editor/Core/Tools/Validation/Rules/DuplicateCueIdRule.cs) |
| 聲源池預熱與閒置保留政策 | 動態聲源在 Rent 時建立，停止後全部保留至 Shutdown；目前沒有預熱／縮池 API。已降低閒置 Tick 成本，仍保留峰值 GameObject／AudioSource | 有首次大量播放或峰值保留需求時評估，先量冷建立與 native 物件保留，再增加選用的預熱、閒置上限或縮池。參考 [AudioConductor ObjectPool](https://github.com/CyberAgentGameEntertainment/AudioConductor/blob/e35687772a3283c6264ecf709b50f9aec8985372/Packages/AudioConductor/Runtime/Core/Shared/ObjectPool.cs)；預熱聲源與 Preload／PrepareClip 準備素材是不同工作，不任意更改預設並發上限 |
| handle 查找與批次清理 | Find 使用 active.Find；StopCategory 先 FindAll，再逐一查找與移除；Finish 的 List.Remove 亦有搜尋／搬移成本，部分取消／停止路徑會配置快照與 closures | 次要量測候選：以 handle 索引與適當的批次處理減少重複搜尋。可參考 [AudioConductor 更新流程](https://github.com/CyberAgentGameEntertainment/AudioConductor/blob/e35687772a3283c6264ecf709b50f9aec8985372/Packages/AudioConductor/Runtime/Core/Conductor.Update.cs)；保留 StealOldest 的時間順序及回呼重入安全，不直接共用一個可被巢狀呼叫覆寫的暫存清單，也不回收外部仍可能保留的 AudioHandle |
| 按素材用途選擇匯入策略 | 五個正式 MP3 均為 loadType=0（Decompress On Load），preloadAudioData／loadInBackground 為 0；前次 ukulele_song 準備後約 24.5 MB 的量測仍有效 | 納入 Player 驗證：以短 SFX、長 BGM、長語音分別比較 Decompress On Load／Compressed In Memory／Streaming，記錄首次播放、DSP／Streaming CPU 與音訊記憶體。依 [Unity AudioClip 匯入文件](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioClip.html) 評估 CPU／RAM／串流取捨，不把全部素材一律改成 Streaming |

**新增量測方法與結果：** 同一台 macOS、Unity `6000.6.3f1` Editor，以正式 Runtime 原樣執行 `OptimizationResearchMeasurements.Capture`。各測點先暖身，量 7 次取中位數；計時使用 Stopwatch，GC 指標使用經已知配置校驗的 ProfilerRecorder sample Count。兩次嘗試取得 GC bytes 均未通過校驗，未採用這些數值；本輪配置數只代表事件次數。研究 fixture 最終 1／1 通過，原始結果為 `work/stage-two-hardening/research20260929c.xml`，量測資料為 `work/optimization-research-20260929/current-costs.csv`。

| 測點 | 規模／操作數 | 中位數 | 判讀 |
| --- | --- | --- | --- |
| 現行 Catalog 查詢最後一筆 alias | 10／100／1,000 entries，各查詢 10,000 次 | 5.1023／48.1160／483.4649 ms | 確認查詢成本隨目錄大小增加；這是刻意選末筆的情境，不代表一般遊戲每幀的查詢量 |
| Dictionary 索引原型，同一批查詢 | 10／100／1,000 entries，各查詢 10,000 次 | 0.4725／0.4878／0.5276 ms | 原型只在測試內建立；未整合正式 Catalog，未計索引建置、額外記憶體與更新成本，不能宣稱整體播放已有此改善 |
| 聲源全部停止後，重設同一 Master 音量 | 曾建立 256 個動態聲源；無 Mixer／handler，1,000 次 | 16.1031 ms，配置事件 0 | 同值設定仍會巡訪歷史峰值聲源；單次約 0.0161 ms |
| 聲源全部停止後，交替更新 Master 音量 | 同上；無 Mixer／handler，1,000 次 | 23.8237 ms，配置事件 0 | 現行更新基準 |
| 同上，綁定會回傳 VolumeChanged 的 handler | 同上；無 Mixer，1,000 次 | 39.7743 ms，配置事件 0 | 測試 handler 僅模擬設定同步，不寫 PlayerPrefs；增加的同步路徑有可量測成本 |
| 同上，啟用原始 Mixer | 256 個動態聲源峰值、handler；1,000 次 | 41.7449 ms，配置事件 0 | 單次約 0.0417 ms；不能把 1,000 次總和當成一般滑桿每幀成本 |
| 批次停止播放 | 暖池 24／64／256 個循環聲音，各停止一整批 | 0.0210／0.0557／0.2192 ms；103／264／1,034 次配置事件 | 有減少配置的空間，但本次耗時量級不支持將它列為阻擋套件化的效能問題 |

Catalog 原型比較交替執行順序，資料為每筆一個 alias；未測重複 alias 的正式衝突政策。聲源量測使用四秒靜音 clip，峰值另含 5 個相容聲源；每次停止後核對池重用且 Playing 歸零。這些是局部 Editor 操作成本，不是完整 frame time、音訊混音、實機播放延遲或第三方效能對比；依 [Unity 目標平台效能指引](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/profiling-applications/profiling-collect-data-introduction)，正式預算仍需以 Player／目標裝置補驗。

**決策：** 保留進入套件化的方向；將音量更新與 Catalog 索引／驗證列為近期改善候選，聲源池政策與匯入策略依場景及平台量測決定，批次清理列次要。第 10.1 節四項可選擴充不因此改成必做。後續實作每一候選時，分開保留前後基準及相容性回歸，避免把數項改動的總效果歸因到單一修改。重跑方式與研究限制見本機 `work/optimization-research-20260929/README.md`。

### 5.7 音量去重與 Catalog 索引／驗證實作（2026-09-30）

依使用者同意實作第 5.6 節兩項近期候選。正式 Runtime 基準為 `1a3086b`，先重新取得當日基準，再分別完成音量及 Catalog 改善；不是拿前一天數字或 Dictionary 原型直接當成正式程式的改善結果。第三階段尚未開始，第 10.1 節擴充維持可選。

**完成內容與相容性：**

- `AudioController.SetVolume` 只在正規化數值改變時套用音訊；只寫入改變的 Mixer 參數，比較三個分類的聲源備援增益後，必要時才巡訪聲源。相同數值仍可交給 handler 儲存先前的暫時值，並可同步以 `applyStored=false` 綁定的 handler。設定事件的回傳不再重複套用音訊。初始化及 `SetMixer` 仍完整套用四通道，Reset、無 Mixer 與缺少參數的備援保留。
- `AudioSettingsPlayerPrefs` 直接選取通道 key／預設值，移除每次變更建立的 key 陣列；自訂 key、正規化、延後 Flush 與設定事件不變。
- `AudioCatalog` 以分類＋字串 tuple 建立 ID／alias 索引，保留大小寫敏感、Entries 中先出現者優先，以及原本無法解析時的字串來源備援。無效 ID 的首筆 alias 仍不會偷偷改選後面的素材；Inspector 會指出問題。Entries 為 null 現安全視為空目錄。
- 初始化／重新啟用及 `RefreshClipProvider` 重建索引；刷新時先公布新索引，再取消舊需求，回呼內新增的播放可取得新映射。直接查詢可偵測 Entries 陣列替換；Editor 修改與反序列化只令本物件索引失效，下次查詢重建。直接修改既有 entry／alias 內容後，獨立使用者呼叫 `RebuildIndex`；服務使用者呼叫 `RefreshClipProvider` 同時處理快取。索引不在每次查詢時遍歷資料偵測修改。
- 新增 `GetValidationIssues` 及獨立的 `Controller.Audio.Editor` assembly／Catalog Inspector，檢查重複 ID／alias、空 entry／key、前後空白、無效分類、缺少內建來源 key 及負數 MaxInstances。驗證不改資料、不載入素材；有來源 key 不代表 Resources 路徑、Addressables 建置或自訂 Provider 一定有素材，實際可載入性仍由 Provider／Player 驗收確認。Inspector 支援修改、Undo／Redo 與手動重新檢查。

**當日效能比較：** 同一台 macOS、Unity `6000.6.3f1` Editor，各點暖身後取 7 次中位數。基準 `core-before20260930.csv`、只修改音量後的 `core-volume20260930.csv`、兩項實作完成後的 `core-full20260930.csv`，皆保存在 `work/core-optimization-20260930/`。下表以當日基準與最終正式實作比較。

| 測點 | 規模／操作數 | 改善前 | 改善後 |
| --- | --- | --- | --- |
| Catalog 查詢最後一筆 alias | 10／100／1,000 entries，各 10,000 次 | 4.4414／42.0219／423.0434 ms | 0.7106／0.6695／0.6876 ms |
| 停止後重設同一 Master 音量，無 Mixer／handler | 256 個動態聲源峰值，1,000 次 | 14.2255 ms | 0.0068 ms |
| 停止後交替更新 Master 音量，無 Mixer／handler | 同上，1,000 次 | 20.2245 ms | 15.2873 ms |
| 同上，加入設定事件同步 handler | 同上，1,000 次 | 34.6994 ms | 15.3765 ms |
| 加入原始 Mixer 與設定事件同步 handler | 同上，1,000 次 | 36.8321 ms | 0.1967 ms |
| 原始 Mixer＋實際 PlayerPrefs handler | 同上，1,000 次，不含 Flush | 37.5265 ms；1,000 次配置事件 | 0.9156 ms；0 次記錄到的配置事件 |

聲源峰值另含 5 個相容聲源，全部停止後才量音量設定。無 Mixer 且音量實際改變時，仍須更新聲源；此路徑維持對歷史池峰值的巡訪。原始 Mixer 與同步 handler 的改善後單次成本約 0.000197 ms，1,000 次總和不代表一般遊戲每幀負載。Catalog 為最末筆別名情境，不代表實際查詢分布。GC 欄僅是經校驗的 ProfilerRecorder 配置事件次數，不是 bytes，也不宣稱服務整體零 GC。PlayerPrefs 測試使用獨立隨機 key，結束後移除，不包含磁碟 Flush 成本。

**索引代價：** `CatalogIndexMeasurements` 對每個規模重建 100 次、量 7 組。10／100／1,000 entries（各一個 alias）每次重建約 0.000927／0.008156／0.093747 ms；每次記錄到 3 次配置事件。以 32 個 Catalog 共用相同 authoring 資料、建立索引前後 `GC.GetTotalMemory(true)` 差額估算並取 7 次中位數，1,000 entries 每個索引約增加 90,240 bytes（約 88 KiB）管理記憶體。已知 2 MiB 配置校驗讀到約 1.88 MiB，通過量級檢查；此為受 Editor／GC 雜訊影響的保留量估算，非精確配置 bytes，不含 entry／字串、native 素材或音訊資料。原始數值與範圍見 `core-full20260930-catalog-build.csv` 及 `summary.json`。

**回歸結果：**

| 驗收 | 結果 | 範圍與檔案 |
| --- | --- | --- |
| 音量獨立驗收＋量測 | 16／16 通過 | 15 項設定測試及 1 項量測，`work/stage-two-hardening/core-volume20260930.xml` |
| 完整 PlayMode | 134／134 通過 | 既有 118 項行為＋7 項音量＋6 項 Catalog 整合＝131 項行為，另 3 項量測；`core-full20260930.xml` |
| EditMode＋Reload | 14／14 通過 | 12 項 Catalog 索引／驗證及 2 項關閉 Domain／Scene Reload 的生命週期測試；`core-edit20260930.xml` |

Catalog 驗證包含與舊線性解析器比較的衝突資料案例、同分類首筆優先、跨分類與大小寫、陣列替換、原地修改後刷新、SerializedObject／JSON 反序列化、舊字串入口、alias 並發限制、刷新保留播放中 handle，以及取消回呼重入時的新映射。音量驗證涵蓋相同暫時值轉儲存、新 handler 同步、單參數寫入、Mixer 切換／缺參數、Reset、重啟及原有回呼例外測試。

測試／量測／輔助資料維持本機忽略，不成為正式依賴。尚未驗證 Player、packed Addressables、實機音訊 CPU 或其他 Unity 版本。套件化前的兩項近期改善已完成本機驗收；下一階段依第 6 節進行 UPM 分離及部署驗證。重跑方法與限制見 `work/core-optimization-20260930/README.md`。索引失效回呼的使用依據：[Unity OnValidate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScriptableObject.OnValidate.html)、[ISerializationCallbackReceiver](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ISerializationCallbackReceiver.html)。

### 5.8 優化後再次搜尋與邊界補測（2026-09-30）

依使用者再次搜尋的要求，對照第 5.7 節完成後的實際工作目錄，而非只查 Git HEAD。`runtime-hashes-before.json` 記錄本輪 Runtime／Editor C# 的 SHA-256；測試後逐一比對相同。本輪僅研究、補量與文件更新，未再修改正式 C#，也未執行／整合第三方套件。

**本輪新增查核內容：**

| 來源 | 本輪取得的固定版本 | 查核與採用界線 |
| --- | --- | --- |
| [AudioConductor](https://github.com/CyberAgentGameEntertainment/AudioConductor/tree/e35687772a3283c6264ecf709b50f9aec8985372) | HEAD `e356877`，2026-09-04，package 2.5.1；與前次相同 | 新增詳讀 CanPlay、ThrottleContext 與 [AtomicReject 測試](https://github.com/CyberAgentGameEntertainment/AudioConductor/blob/e35687772a3283c6264ecf709b50f9aec8985372/Packages/AudioConductor/Tests/Runtime/Core/ConductorThrottleTests.AtomicReject.cs)：先選擇淘汰候選，全部限制允許後才停止舊播放。其程式明列播放期間修改上限不受支援，不能宣稱已替我們驗證動態調低上限的情境 |
| [CycloneGames.Audio／UnityStarter](https://github.com/MaiKuraki/UnityStarter/tree/c103e1e346113560fa6dd7679e705077b0160b64/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio) | 儲存庫 HEAD `c103e1e`，2026-09-29；audio package 1.0.0 | 選讀 AudioPoolConfig、AudioManager 的活躍實例計數、TryShrinkPool、TrimIdleMemory，以及平台／記憶體設定。可參考初始池、閒置縮池與每次維護工作量上限；未完整審查龐大的 manager，也未實測其效能或採用裝置預設數值 |
| [Rumyoonomicon AudioManager](https://github.com/perezromeojohn/unity-audiomanager/tree/06648b7984b3ec34d5d1e166fe3b39d5da8ea445) | HEAD `06648b7`，2026-08-31；package 1.1.3 | 核對啟動建立 SFX 聲源池與 activeByClip 分組。可參考預熱／分組概念；其播放入口仍有 Array.Find、全分組清理及池搜尋，不作為整體比本服務更快的依據 |
| [Unity-AudioLoader](https://github.com/IvanMurzak/Unity-AudioLoader/tree/c178f48d85fc34c29f6181aa3cda79b51955f107) | HEAD `c178f48`，2026-05-28；package 1.0.6 | 核對非同步載入協調、記憶體／磁碟快取介面；未測其磁碟實作。MemoryCache 清除直接 DestroyImmediate clip，與本服務需保護播放中 lease 的生命週期不同，本輪不採用此清除方式，也不新增 UniTask 依賴 |

Unity 官方資料另核對 [Audio Profiler](https://docs.unity3d.com/6000.0/Documentation/Manual/ProfilerAudio.html)、[AudioClip 匯入](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioClip.html) 與 [Addressables 2.11.2 記憶體管理](https://docs.unity3d.com/Packages/com.unity.addressables@2.11/manual/memory-assets.html)。後續實機量測須區分 DSP CPU、Streaming CPU、real／virtual voices 與素材記憶體；釋放 lease 不保證 AssetBundle 立即卸載，過度積極清快取亦可能造成卸載後立即重載。

**R10：本輪研究時重現的並發判斷邊界（後續已修正，見 5.9）。**

1. 在無全域限制時播放 3 個循環聲音：1 個 SFX A、2 個 Voice。
2. 播放中將 `MaxVoices` 降為 1，全域政策設為 `RejectNew`。
3. 再請求 SFX A，單音效 `MaxInstances=1`、政策為 `StealOldest`。
4. 實際結果：新 handle 為 `Rejected`，原 SFX A 卻已是 `Stopped`；兩個 Voice 繼續播放，總數由 3 降至 2。

原因是 `MakeRoom` 在檢查單音效限制時立即執行 `Finish`，下一輪才發現全域條件仍無法接受新請求。建議先完整判斷是否能接納新播放、保留淘汰候選，確認後一次執行；遭拒絕的請求應不改變既有播放。這是參考原子拒絕設計後在本專案重現的行為問題，不是第三方效能推論。修正時須明確保留動態上限、Loading 預約、替換式 BGM／Voice 與回呼重入的既有規則；不能直接複製只支援穩定上限的演算法。驗收應增加此情境的「新請求拒絕、舊播放仍有效」回歸案例。

**目前版本的新量測：** 使用 `PostOptimizationResearchMeasurements`，先暖身 managed/native 播放流程，再於 7 個新 Controller 分別比較首次建立動態聲源與同一池重用。Clip 是預先建立的十秒靜音資料、外部直接播放、固定 PlayOptions；不含素材載入／解碼。每個 Controller 另有 5 個相容聲源。以下均為 7 次中位數，單位 ms。

| 測點 | 操作數 | 24 聲源 | 64 聲源 | 256 聲源 | 配置事件／判讀 |
| --- | --- | --- | --- | --- | --- |
| 新池首次播放一批 | 24／64／256 次 | 0.1758 | 0.4808 | 1.8787 | 346／910／3,604 次事件，包含集合成長與 native 聲源建立相關操作 |
| 同一池停止後重播一批 | 同上 | 0.0466 | 0.1042 | 0.4176 | 240／640／2,560 次事件；每次播放仍約 10 次事件。兩列是同版冷／暖狀態，不是已實作新預熱 API 的前後改善 |
| 全域上限已滿，連續請求被拒絕 | 1,000 次 | 1.3842 | 2.5094 | 8.1153 | 各規模均 9,000 次事件；256 規模單次約 0.0081 ms |
| 單音效上限已滿，連續請求被拒絕 | 1,000 次 | 1.3951 | 2.5113 | 8.2018 | 各規模均 9,000 次事件；MakeRoom 仍掃描 active，拒絕前已建立 options／handle／playback 等資料 |
| 播放中反覆設定相同 GamePaused=false | 1,000 次 | 0.1749 | 0.3487 | 1.4267 | 各規模均 1,000 次事件；SetPaused 仍建立 active 快照 |
| 全部停止後反覆設定相同 SFX muted=false | 1,000 次 | 0.8518 | 1.9968 | 7.5054 | 0 次記錄到的事件，仍巡訪歷史峰值聲源 |

計時含 ProfilerRecorder 記錄，配置事件以已知配置校驗，非 GC bytes。新池比較只支持「可將部分首次成本前移」；不代表所有冷播放成本皆由建池造成，也未測出 native 物件的保留記憶體。過載測點刻意同次呼叫 1,000 次，不能將總和當成一般每幀成本。沒有與第三方執行速度比較，亦沒有目標 Player／音訊執行緒驗證。

**排序與後續決策：**

- **優先修正 R10。** 先完善既有並發限制的結果一致性，再考慮降低拒絕路徑配置或維護活躍數索引；保留無限制快速路徑及外部持有 handle 的安全性。
- **聲源預熱屬有量測支持的選用能力。** 大量同時起播時可預先建池；縮池須同時考量重新建立成本，依低用量時段／工作量預算處理，不任意套用外部專案的上限。
- **相同 Pause／Mute 狀態去重屬次要改善。** 若呼叫端每幀反覆推送同值，才有持續成本；一般只在狀態切換時呼叫，收益有限。批次停止的舊候選也維持次要。
- **素材匯入與快取保留政策繼續放入目標 Player 驗證。** LRU／TTL／按預算分批回收皆可研究，須保護 Loading、播放與預載群組，觀察重載次數和 AssetBundle 實際卸載；目前不預先宣稱能省多少 RAM 或改變全部 MP3 匯入設定。第 10.1 節可選擴充不升格為必做。

研究 fixture 1／1 通過代表成功擷取測點與邊界觀察，**不代表 R10 行為正確或已修復**。JSON 記錄 `partialEvictionObserved=true`；本輪未重跑完整行為回歸，原 134／134、14／14 為第 5.7 節歷史驗收，不涵蓋這個新增邊界。原始 XML 在 `work/stage-two-hardening/post-research20260930.xml`，CSV、並發 JSON、來源快照與固定版本 metadata 在 `work/post-optimization-research-20260930/`，皆僅保留本機。

### 5.9 R10 並發拒絕修正與驗收（2026-09-30）

本節保留 R10 修正完成當時的實作與量測；後續接納流程拆分及新增優化見 5.10，原子拒絕規則仍保留。

依使用者同意先修 R10，正式 C# 僅修改 `AudioPlaybackEngine`；保留第 5.7 節已完成的音量／Catalog 改善。設計參考為第 5.8 節 AudioConductor 的「所有限制允許後才淘汰」原則，實作依本服務的動態上限、Loading 與相容播放分支自行撰寫，未搬入第三方程式或依賴。

**修正後的行為：**

- `MakeRoom` 先計數並算出單音效與全域各需替換多少個實例，兩者均可滿足才執行停止。單音效候選所釋出的名額先從全域需求扣除，保留單音效 `StealOldest` 可讓全域 `RejectNew` 接受新播放的既有行為。
- 播放中調低上限本身不會停止舊聲音；後續 `StealOldest` 請求可一次替換多個，仍依「單音效最舊優先，再全域剩餘最舊」順序。Loading 與 Paused 均計入；被替換的 Loading 回報 `Cancelled`，已播放／暫停回報 `Stopped`。
- 因並發限制而 `Rejected` 時，既有播放、待載入請求、lease 與完成通知不受影響。替換式 BGM／Voice／循環 SFX 的 pending 取消及轉場／淡出重設，移至接納之後；原 BGM 轉場得以繼續。
- 保留同一替換分支的計數排除，舊非循環 SFX 仍獨立計數；沿用現有完成通知佇列，使 `Completed` 回呼看到本次狀態已完成，再把回呼內播放當成新請求。

無限制路徑仍直接通過。有設定限制時先掃描 active；拒絕或不需替換時不建立候選快照。只有接受且確實需替換時，配置一個大小等於替換數量的陣列，保留選取與通知順序。本次目標是結果一致性，未加入活躍計數索引，也不宣稱替換路徑零配置。

**實測證據：** Unity `6000.6.3f1`／Addressables `2.11.2`，本機 Editor。

| 驗證 | 結果 | 證據 |
| --- | --- | --- |
| 修正前新增回歸 | 14 項中 6 項失敗、8 項通過 | 誤停播放、取消 Loading／BGM／Voice pending、打斷 BGM 轉場及淡出均被測試捕捉；`r10-before20260930.xml` |
| 修正後相關回歸 | 67／67 通過 | 新增 14 項、核心 42 項、回呼 11 項；`r10-focused20260930.xml` |
| 完整 PlayMode | 149／149 通過 | 145 項行為、4 項量測／觀察；`r10-full20260930.xml` |
| EditMode／Reload | 本專案 14／14 通過 | 12 項 Catalog、2 項 Reload 生命週期；`r10-edit20260930.xml` 總計 15／15，另含 1 項 Addressables DocExample 的 `TestStub.RequiredTest`，不計入本專案覆蓋 |
| 原研究情境再現 | 新 handle `Rejected`，舊 SFX 仍有效、兩個 Voice 仍有效，播放數 3 → 3 | `r10-full20260930-admission.json`：`partialEvictionObserved=false`；修正前為 3 → 2、true |

14 項新回歸另涵蓋動態調低兩種上限、多個候選的選取／完成順序、Loading 遲到交付、Paused 計數、拒絕時不釋放最後一份素材 lease、完成回呼再次播放，以及替換分支與舊 one-shot 的差異。

完整回歸亦重跑第 5.8 節測點：1,000 次全域／單音效拒絕仍各記錄 9,000 次配置事件；24／64／256 聲源的暖池播放仍每次約 10 次事件。256 聲源的 1,000 次全域／單音效拒絕中位數為 8.2782／8.3755 ms（前次 8.1153／8.2018 ms）；暖池播放 256 次為 0.4178 ms（前次 0.4176 ms）。這些是同機 Editor 回歸觀察，不作效能提升或平台效能保證，亦未另量化新候選陣列的替換成本。

XML 位於 `work/stage-two-hardening/`；本輪來源雜湊、修正前引擎備份及彙整在 `work/r10-admission-20260930/`；原研究與重跑 CSV／JSON 保存在 `work/post-optimization-research-20260930/`。新增測試為 `Assets/AudioService/Tests/ConcurrencyAdmissionTests.cs`，測試／工具／證據均依既定規則由 Git 忽略。本輪尚未執行 Player／packed content；聲源預熱、縮池、Pause／Mute 去重、E1～E4 與 UPM 套件化維持原規劃。

### 5.10 狀態去重、聲源預熱與拒絕路徑優化（2026-09-30）

依使用者確認的三項追加優化分步實作；這三步屬第二階段核心的後續改善，與第 6 節的套件化階段分開。正式 C# 修改限於 `AudioPlaybackEngine`、`AudioController` 與 `AudioPlayback`，未引入第三方程式或依賴。

**完成內容與契約：**

1. **暫停／靜音同值去重。** `SetGamePaused`、`SetBackgroundPaused` 與同通道 `SetMuted` 值相同時直接返回，不建立 active 快照、不巡訪聲源。真正改變暫停時仍沿用既有快照與套用流程；遊戲／背景／個別暫停各自獨立，新播放、延遲載入及池重用仍在 Start 套用當下狀態。
2. **可選 `int PrewarmSources(int targetCount)`。** 主執行緒同步把動態聲源總數（使用中＋閒置）補到指定數量，五個相容聲源另外計算；回傳本次新建數量。非正數或服務未 Ready 回傳 0，不自動啟動；不呼叫就不額外預熱。預熱與一般播放共用建立流程，明確停止新聲源，預留 active／updating 清單容量；閒置聲源不進入每幀播放更新。重複呼叫只補不足、不縮池、不改 `MaxVoices`，播放超過容量仍按需擴充；Shutdown 清理動態聲源，重新初始化後可再預熱。預熱不取得 clip，資料準備仍使用 `PrepareClip`。
3. **先判斷接納，再建立必要播放資料。** `TryPlanAdmission` 保留 R10 全部規則；一般入口只有全域限制時直接取 active.Count，包含 Loading／Paused。單音效限制及替換分支仍掃描；通過後才由 `StartAccepted` 複製設定、建立 Playback 並執行替換，載入回呼僅在確實需要載入時建立。設定快照在任何淘汰／Provider lease 釋放前完成；`PlayBgmHandle` 的循環覆寫移到這份快照，避免額外複製或修改呼叫端設定。handle 沒有完成訂閱者時不配置通知用閉包；有訂閱時的佇列、例外隔離及晚訂閱語意不變。

拒絕仍回傳各自獨立的 handle、ID、原因及終態，沒有重用外部可能繼續持有的 handle。沒有新增單音效計數索引；其掃描成本仍存在，避免讓所有正常播放先承擔索引維護成本。無限制路徑仍直接通過。

**同條件量測：** Unity `6000.6.3f1`／Addressables `2.11.2`，同一台本機 Editor、主執行緒。新增 `OptionalOptimizationMeasurements`，先暖身程式及 native 音訊流程，再於 24／64／256 聲源各跑 7 次，取中位數。外部 clip 為預先建立的十秒靜音資料，使用固定非 null PlayOptions；ID 測點透過本機測試 Provider 取得已駐留 clip，不含首次素材載入／解碼。計時含 ProfilerRecorder；空迴圈與已知配置分別校驗 0／非 0 事件，緩衝未滿。以下事件是 GC.Alloc 次數，非 GC bytes。

基準為本輪修改前 `optional-before20260930.csv`，結果為最終完整回歸 `optional-full20260930.csv`；表中皆取 256 聲源，時間單位 ms。

| 測點 | 操作數 | 修改前 → 修改後 | 配置事件前 → 後 |
| --- | --- | --- | --- |
| 重複 GamePaused=false | 1,000 | 1.3690 → 0.0016 | 1,000 → 0 |
| 重複 GamePaused=true | 1,000 | 1.3988 → 0.0016 | 1,000 → 0 |
| 重複 BackgroundPaused=true | 1,000 | 1.2150 → 0.0016 | 1,000 → 0 |
| 真正切換遊戲暫停 | 200 | 1.7926 → 1.6552 | 200 → 200 |
| 使用中重複 SFX muted=true | 1,000 | 7.4101 → 0.0018 | 0 → 0 |
| 峰值停止後重複 SFX muted=false | 1,000 | 7.1281 → 0.0018 | 0 → 0 |
| 全域上限拒絕，外部 clip | 1,000 | 8.3599 → 0.4486 | 9,000 → 5,000 |
| 單音效上限拒絕，外部 clip | 1,000 | 8.2961 → 7.8607 | 9,000 → 5,000 |
| 全域上限拒絕，字串 ID | 1,000 | 3.5427 → 0.2095 | 6,000 → 2,000 |
| 單音效上限拒絕，字串 ID | 1,000 | 3.5625 → 3.3316 | 6,000 → 2,000 |
| 暖池無限制播放，外部 clip | 256 | 0.4032 → 0.4034 | 2,560 → 2,304 |
| 暖池全域有限制的成功播放 | 256 | 1.5427 → 0.4295 | 2,560 → 2,304 |
| 暖池單音效有限制的成功播放 | 256 | 1.4475 → 1.3799 | 2,560 → 2,304 |
| 暖快取 ID 成功播放 | 256 | 0.4095 → 0.4118 | 2,560 → 2,560 |

配置拆分測點顯示：1,000 次外部 clip 的 ID 建立仍有 4,000 次事件，1,000 份 PlayOptions 快照有 1,000 次事件，1,000 個新 handle 有 1,000 次事件；「建立 handle 並直接完成、沒有訂閱者」由 2,000 降為 1,000 次事件。拒絕路徑移除了設定快照、Playback、載入用閉包與無訂閱通知閉包；外部 clip 請求仍有 ID 建立與獨立 handle 的成本。未命中 Catalog 的字串 ID 測點還會建立 AudioClipAddress，故不是零配置。單音效拒絕的時間小幅差異不作穩定加速保證，明確收益為配置事件減少；正常無限制／暖快取播放的時間接近，未看到明顯退步。

**預熱的成本轉移：** 以下三列均為最終同版、256 個動態聲源的 7 次中位數；每個 Controller 另有 5 個相容聲源。

| 最終版本測點 | 時間 ms | 配置事件 | 物件與播放狀態 |
| --- | --- | --- | --- |
| 未預熱，首次建立並播放 256 次 | 1.7089 | 3,348 | 動態聲源 0 → 256，播放 256 個 |
| 載入階段先 `PrewarmSources(256)` | 0.9560 | 1,039 | 動態聲源 0 → 256，全部閒置、未載入 clip |
| 預熱後首次播放 256 次 | 0.6305 | 2,304 | 動態聲源維持 256，無額外建源 |

預熱與播放仍各有成本，這是將部分工作移到載入時機；預熱後首次播放也不等於已播放過再重用的成本。記錄的是物件數量，未量出完整 native／managed 保留記憶體，不能換算成裝置 RAM 節省或總遊戲效能提升。256 僅為壓測規模，不是建議預設預算。全域拒絕的 1,000 次也是批次測點，不能當成一般每幀負載。

**驗收與本機證據：**

| 範圍 | 結果 | XML 標籤 |
| --- | --- | --- |
| 修改前專用量測 | 1／1 通過 | `optional-before20260930` |
| 狀態去重＋核心＋量測 | 47／47 通過 | `optional-state20260930` |
| 加入預熱後相關驗收 | 56／56 通過 | `optional-prewarm-v320260930` |
| 三項優化＋R10／回呼相關驗收 | 88／88 通過 | `optional-admission20260930` |
| 完整 PlayMode | 170／170 通過：165 項行為、5 項量測／觀察 | `optional-full20260930` |
| 本專案 EditMode／Reload | 14／14 通過：12 項 Catalog、2 項 Reload | `optional-edit20260930` |

本輪新增 20 項行為回歸（狀態 4、預熱 9、接納 7）與 1 項量測。涵蓋重複狀態後新播放／載入完成、UI 例外與個別暫停、預熱容量與五個相容聲源區別、載入預約、超額播放、Shutdown 清理、獨立拒絕 handle、Provider 在 Acquire／Release 內修改呼叫端設定，以及完成事件例外與再次播放。R10 原研究情境仍為新 handle Rejected、舊聲音 3 → 3，`partialEvictionObserved=false`。

XML／log 在 `work/stage-two-hardening/`；逐步 CSV、修改前／階段間 C# 備份、前後雜湊及彙整在 `work/optional-optimizations-20260930/`。測試、工具與證據維持 Git 忽略，正式程式不依賴它們。本輪無 Player／packed content 驗證；自動縮池、單音效計數索引、E1～E4 與 UPM 套件化仍未實作。

### 5.11 再次開源／官方資料審查與缺陷重現（2026-09-30）

以下保留修正前的審查與失敗證據；R11～R15 後續修正及最新驗收見 5.12。

依使用者要求重新搜尋，對照 **5.10 完成後的工作目錄版本**。本輪重點為 Provider 釋放邊界、Unity 全域暫停／音訊系統重設，以及 Addressables 對應更新；新增本機測試與文件，正式 Runtime／Editor C# 未修改。原有 SHA-256 基準全部相同，基準未含的兩個 Interface 檔案另核對與 HEAD 相同。以下為研究與待修清單，沒有把發現列為已修復。

**重新核對的開源來源：** 使用 GitHub API 核對當日預設分支提交，再下載固定提交中相關的 30 個原始碼／package manifest 檔案。前四項提交與 5.8 相同，CarterGames 為本輪新增比較對象；版本號取自各 package.json。僅檢查相關子系統，未宣稱完整審計這些專案，也未執行或匯入第三方程式。

| 專案與固定提交 | 版本 | 本輪檢查及適用界線 |
| --- | --- | --- |
| [AudioConductor — e356877](https://github.com/CyberAgentGameEntertainment/AudioConductor/tree/e35687772a3283c6264ecf709b50f9aec8985372) | 2.5.1 | 播放／更新、完成回呼的例外隔離測試、池的 Shrink 能力與 Addressables handle 釋放；作為狀態與資源生命週期的比較依據 |
| [UnityStarter / CycloneGames.Audio — c103e1e](https://github.com/MaiKuraki/UnityStarter/tree/c103e1e346113560fa6dd7679e705077b0160b64/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio) | 1.0.0 | AudioBankClipLease 先清除所有權，再逐項安全釋放；另有生命週期世代、主執行緒檢查、閒置縮池門檻。只參考局部設計，不引入其服務／UniTask 等依賴 |
| [Simple-Unity-Audio-Manager — a28f36e](https://github.com/jackyyang09/Simple-Unity-Audio-Manager/tree/a28f36e6548045e9af1936bebe35b59b6134d8e6) | 3.1.1 | 檢查 AudioManagerInternal 與聲音／音樂 channel helper 的停用、背景暫停及播放進度；其循環與 channel 架構不直接等同本服務的獨立 handle |
| [Unity-AudioLoader — c178f48](https://github.com/IvanMurzak/Unity-AudioLoader/tree/c178f48d85fc34c29f6181aa3cda79b51955f107) | 1.0.6 | 檢查載入、AudioSource 輔助與記憶體快取。清除快取會銷毀 clip，沒有本服務相同的播放 lease 契約，因此不直接搬用其清理方式 |
| [CarterGames AudioManager — 44bb924](https://github.com/CarterGames/AudioManager/tree/44bb92407120032b1bacee376ec182b303c940b2) | 3.1.2 | 檢查池初始容量、借還與 AudioSourceInstance 狀態／重設；其借用會巡訪清單，不作為效能必定優於目前 Stack 池的依據 |

**已重現的五類缺陷：** 測試環境為 Unity `6000.6.3f1`／Addressables `2.11.2`、本機 Editor PlayMode。每一列都有程式路徑與可重複的觸發條件；並非只因其他專案具備某功能就判定本服務有缺陷。

| 編號 | 觸發條件與實際結果 | 原因與建議修正方向 |
| --- | --- | --- |
| R11 | 自訂 Provider 的 lease 釋放動作再次呼叫服務。MaxVoices=1、StealOldest 淘汰舊播放時，內層再 Play，結果內外兩個新 handle 都 Playing，總數變 2；內層改為 Shutdown，則 Ready=false 卻仍留下有效的 Loading handle、Diagnostics.Loading=1 | `StartAccepted` 在完成淘汰後直接接納，未防範釋放動作改變容量或服務世代。完成事件佇列只隔離 Completed 訂閱者，未涵蓋 lease release。需在外部程式執行前穩定內部狀態，並在接納流程保留容量／重新確認服務世代，確保重入也遵守上限與關閉契約 |
| R12 | 自訂 Provider 的 lease release 拋例外。Stop 拋出該例外後，handle 仍 Playing、IsValid=true，但 Diagnostics.Playing=0；Completed 沒通知，第二次 Stop 回傳 false | `Finish` 先移除 active，再執行外部 Dispose，例外使 Recycle 與 handle 終態流程未執行。需保障必要清理與一次終態通知，即使 Provider 清理失敗仍保持一致；記錄例外，不能僅吞掉錯誤。另補多 lease／Shutdown 的同類驗收 |
| R13 | 外部使用 `AudioListener.pause=true`，包括先暫停再 Play、以及 Play 後才暫停，十秒 one-shot 都在幾幀內被標成 Finished／Completed。改用本服務 SetGamePaused 或 IgnoreGamePause 的 UI 播放則通過對照 | Tick 將 `!AudioSource.isPlaying` 當成自然完成，但未納入 Listener 暫停。需區分外部全域暫停與真正播完，並明確定義淡入淡出在此期間的行為；僅使用本服務暫停 API 的一般流程未重現此問題 |
| R14 | 以磁碟 BGM 素材循環播放，呼叫 AudioSettings.Reset 將 DSP buffer 1024 改為 512。Reset=true、收到一次 configuration callback，聲源 isPlaying=false，但 handle 與 Diagnostics 仍 Playing | 尚未處理 OnAudioConfigurationChanged。需定義重設後恢復可重播素材或結束失效 handle 的政策，保留原有暫停／靜音狀態；腳本產生的 clip 另需重建契約。本輪實際驗證的是程式觸發的 Reset，deviceChanged=false，沒有實測拔插耳機 |
| R15 | 同一 Addressables key 先載入舊 clip、停止並釋放快取，替換 resource locator，直接用 Addressables.LoadAssetAsync(key) 已得到新 clip；呼叫本服務 RefreshClipProvider 再播放卻仍取得舊 clip，載入位置序列為 old → new → old | AddressablesAudioClipProvider 的 knownLocations 不失效，Controller 刷新僅更換 AudioClipStore。需加入明確的對應刷新／世代機制，重新解析 key，同時保護仍被舊播放持有的 lease。若不支援執行期 catalog 更新，文件需明列此限制；單純固定 catalog 冷載入未受此案例影響 |

R11、R12 的必要前置步驟是呼叫 ReleaseUnusedClips 移除服務的快取保留，使最後一個播放停止時確實執行自訂 Provider 的 release；一般無回呼／不拋錯的 Provider 停止流程通過對照。R15 採可控的 ResourceLocationMap 與 ResourceProviderBase 重現對應失效，**尚未建置遠端 catalog／AssetBundle 的完整更新流程**。

**官方依據與開源參考的作用：**

- [Unity AudioListener.pause](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioListener-pause.html) 定義全域暫停及新請求從暫停狀態開始的行為；本服務的 Completed 結果與此暫停語意不一致。不是因為文件保證任何版本的 isPlaying 值，而是本機已驗證該值會觸發誤判。
- [Unity Audio Settings](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSettings.html) 與 [OnAudioConfigurationChanged](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.OnAudioConfigurationChanged.html) 說明音訊重設後播放狀態需重建，腳本產生的素材也需另外處理。這是 R14 的預期行為依據，不是新 DSP 排程功能的需求。
- [Addressables 2.11 catalog 管理](https://docs.unity3d.com/Packages/com.unity.addressables@2.11/manual/LoadContentCatalogAsync.html) 定義執行期 catalog 載入／更新；同時核對本機實際安裝 2.11.2 的 `Documentation~/LoadContentCatalogAsync.md`。官方更新機制不會自動清除本服務自行保存的 IResourceLocation；應以 R15 的測試證據區分模組快取與 AssetBundle 更新配置。
- [CycloneGames AudioBankClipLease](https://github.com/MaiKuraki/UnityStarter/blob/c103e1e346113560fa6dd7679e705077b0160b64/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio/Runtime/AudioBankClipLease.cs) 提供先解除內部持有、再安全逐項釋放的參考；[AudioConductor 完成回呼測試](https://github.com/CyberAgentGameEntertainment/AudioConductor/blob/e35687772a3283c6264ecf709b50f9aec8985372/Packages/AudioConductor/Tests/Runtime/Core/ConductorCallbackTests.cs) 示範例外不應阻止後續通知。它們是設計比較，不代表第三方專案已驗證本服務的 R11／R12 或可直接套用修法。

**仍有價值的效能候選：**

| 候選 | 現有證據 | 採用條件與代價 |
| --- | --- | --- |
| 單音效並發查找索引 | 每次啟用 MaxInstances 仍巡訪 active；5.10 的 256 聲源、1,000 次同音效拒絕，ID 路徑約 3.33 ms、外部 clip 約 7.86 ms。這是先前同版量測，非新增修正後數字 | 代表性場景若有大量同音效請求，再評估按 category／ID 計數或成員索引。需處理 Loading／Paused、替換、取消、動態上限及重入，並比較正常播放的維護成本與記憶體；先修 R11，避免替錯誤狀態建立索引 |
| 可選閒置聲源縮減 | 動態池目前保留歷史峰值到 Shutdown，PrewarmSources 也只增不減；已有 256 聲源峰值／閒置證據。AudioConductor ObjectPool 有 Shrink，CycloneGames 有閒置時間／使用率門檻 | 可先提供載入／換場時顯式 TrimIdleSources，僅銷毀閒置動態聲源，保留最低容量與每次處理預算。是否自動縮池需量測重建尖峰及 native 記憶體；目前是有意保留供重用，不能稱為記憶體洩漏 |

來源：[AudioConductor ObjectPool](https://github.com/CyberAgentGameEntertainment/AudioConductor/blob/e35687772a3283c6264ecf709b50f9aec8985372/Packages/AudioConductor/Runtime/Core/Shared/ObjectPool.cs)、[CycloneGames AudioPoolConfig](https://github.com/MaiKuraki/UnityStarter/blob/c103e1e346113560fa6dd7679e705077b0160b64/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio/Runtime/AudioPoolConfig.cs)。本輪沒有建立上述兩項優化原型，也沒有估計未量測的加速比例或 RAM 節省。E1～E4 仍維持 10.1 的可選需求，不因比較專案有功能就提升為必要工作。

**驗證結果與證據：**

| 執行範圍 | 結果 | XML 標籤 |
| --- | --- | --- |
| 初次六個定向重現 | 0 通過／6 失敗，均為預期行為斷言失敗 | `system-audit20260930` |
| 既有測試＋新增九項重現／對照 | 共 179 項：172 通過／7 失敗／0 略過；既有 170 項全通過，新測試 2 通過／7 失敗 | `system-audit-full20260930` |

七個失敗對應五類缺陷：R11 兩項、R12 一項、R13 兩項、R14 一項、R15 一項。對照涵蓋普通 lease 停止與通知、服務 SetGamePaused 暫停／恢復，以及忽略全域暫停的 UI 聲音。沒有 C# 編譯錯誤或警告。新增測試保留「應有行為」斷言，因此完整 suite 目前確實為失敗，不將測試執行成功混同為系統驗收通過。

測試位於 `Assets/AudioService/Tests/SystemAudit20260930Tests.cs`；XML／log 在 `work/stage-two-hardening/`；逐例 JSON、來源 manifest／SHA、前後正式 C# 雜湊及彙整在 `work/system-audit-20260930/`，皆沿用本機 Git 忽略。Reset 測試還原原始音訊配置，runner 保留／還原專案設定，未改 OS 音訊裝置。14 項 EditMode／Reload 是 5.10 的歷史通過結果，本輪未重跑。Player／packed content／真實裝置切換仍未驗證。

**建議次序：** 先修 R11／R12 的釋放與生命週期一致性，再修 R13／R14 的 Unity 音訊狀態整合；R15 在需要執行期素材更新時一併補齊。以新測試轉為通過及既有回歸維持通過作為驗收，再決定單音效索引或縮池的效能工作。套件化前應處理已確認缺陷或明訂支援限制，不能只沿用 5.10 的 170／170 就宣告目前所有邊界已成熟。

### 5.12 R11～R15 修正、計數索引與閒置縮池（2026-09-30）

依使用者核准的方案實作，沿用 5.11 的缺陷重現與官方契約，不新增第三方 runtime 依賴。正式變更集中在回呼／lease 生命週期、Unity 音訊狀態整合與 Provider 刷新；E1～E4 仍為可選規劃。

**修正內容與相容性**

| 項目 | 完成的行為 |
| --- | --- |
| R11／R12 釋放重入與例外 | 共用 mutation／回呼佇列；先更新終態、播放名額、來源與 store 使用計數，再執行外部 Provider lease 釋放及通知。釋放內 Play 仍遵守上限，Shutdown 不留 Loading；釋放例外會記錄且不阻止其餘清理與一次完成通知。無法代替故障的 Provider 修復其自身資源 |
| R13 Listener 暫停 | 同步 AudioListener.pause，與個別、遊戲、背景原因組合；新播放與既有 one-shot 不再誤判完成。UI 的 IgnoreGamePause 略過遊戲／Listener 暫停，背景仍適用。單次播放包絡在暫停時凍結，共用 bus／bank 增益淡變沿用既有行為 |
| R14 音訊配置重設 | OnAudioConfigurationChanged 結束 Playing／Paused 為 Failed、Loading 為 Cancelled，原因 `Audio system configuration changed`；取消舊預載／準備、使服務快取及群組保留失效、停止舊淡變並重套 Mixer。保留服務音量／暫停／靜音／當前分類增益；不自動續播 BGM。完成回呼可新建播放；失效的自有 AudioClip 由擁有者重建 |
| R15 定位刷新 | 新增可選 IAudioClipProviderRefresh，RefreshClipProvider 更新服務 store 及 Addressables 定位／別名世代；Fallback 轉送且不遞迴發送 Changed。取消舊取得請求，晚到結果不污染新世代；已發出的舊 lease 持續有效到 Dispose。普通 Addressables 完成交付仍保留較早回呼可取消後續等待者的契約 |

AudioSettings 的事件訂閱在 Initialize 使用先解除再註冊，Shutdown 解除，避免重複初始化／Reload 留下重複訂閱。音訊重設後個別 handle 已結束，個別 Pause 不移轉至新 handle；需要的素材群組須重新 Preload／PrepareClip。正式來源未引用本機驗證程式。

**量測後保留的兩項優化**

1. 一般單音效限制使用分類＋ID 計數，首次遇到 MaxInstances 才建立；包含已存在的 Playing、Paused、Loading。之後隨接納／結束更新，空場後停止維護，下次需要時重建；保留字典容量供重用。BGM／Voice 替換分支仍掃描並排除被替換槽；StealOldest 的選取順序與 R10 完整拒絕規則不變。
2. 新增 `TrimIdleSources(minimumCapacity = 0, maxToRemove = 32)`，只移除閒置動態聲源，每次受預算限制，Unity 在幀末完成 Destroy。最低容量指使用中＋閒置的動態總數，不含五個相容來源；不停止播放／暫停／Loading。較大的最低值不建立新來源，負值視為 0；無有效預算或未 Ready 回傳 0。不自動縮池，需要時重新預熱或按播放需求建立。

同機 Unity 6000.6.3f1 Editor，256 聲源，暖身後七次中位數。依序保存本輪前、僅可靠性修正、加入索引／縮池三版；下表 CPU 為該欄指定的整批呼叫成本，GC 為 Profiler 的 **GC.Alloc 事件次數，不是 bytes**。

| 測點 | 本輪前 ms | 僅修正 ms | 加索引 ms | GC 事件（僅修正 → 加索引） |
| --- | ---: | ---: | ---: | ---: |
| 同音效 ID 拒絕 1,000 次 | 3.3000 | 3.3534 | 0.2580 | 2,000 → 2,000 |
| 同音效外部 clip 拒絕 1,000 次 | 7.7387 | 7.8391 | 0.5488 | 5,000 → 5,003 |
| 暖池、有單音效限制，播放 256 次 | 1.3797 | 1.4059 | 0.4556 | 2,304 → 2,304 |
| 暖池、不限量，播放 256 次 | 0.4046 | 0.4115 | 0.4096 | 2,304 → 2,304 |
| 暖池、只有全域限制，播放 256 次 | 0.4415 | 0.4286 | 0.4372 | 2,304 → 2,304 |
| 暖快取 ID、不限量，播放 256 次 | 0.4061 | 0.4162 | 0.4204 | 2,560 → 2,560 |

外部 clip 拒絕測點第一次啟用索引有三次額外配置事件；ID 測點在前置播放已啟用。索引有按不同 category／ID 數量成長的管理記憶體成本，未量得整個遊戲的記憶體／幀率收益。後續完整 suite 的同版覆核較慢：ID 拒絕 0.3278 ms、外部 clip 拒絕 0.6585 ms、不限量暖池 0.4663 ms；仍支持索引改善該熱路徑，不能把單次 Editor 數值視為裝置效能保證。

縮池測點以七次中位數計：動態池 256 → 32，分七次各移除 32，呼叫 CPU 合計約 0.1326 ms，單次最大值的中位數約 0.0238 ms；重新建立 224 個來源約 1.1330 ms。幀末後含相容來源的 AudioSource 數由 261 → 37，`Profiler.GetRuntimeMemorySizeLong(AudioSource)` 合計 359,136 → 50,912 bytes。此數值僅為 AudioSource 物件回報，**不是完整 GameObject、native DSP 或 process RSS**；CPU 不含幀末真正銷毀成本。故只提供由呼叫端選擇時機的縮池，不自動在每幀回收。

**最終驗收**

| 驗證 | 結果與範圍 |
| --- | --- |
| 完整 Editor PlayMode | 205／205（199 行為＋6 量測），0 failed／skipped。原 179 項保留，新加 25 項行為＋1 項縮池量測；原七個失敗均轉為通過 |
| 本專案 EditMode／Reload | 14／14，含 12 項 Catalog 與 2 項關閉 Domain／Scene Reload 的重入情境 |
| macOS Standalone Player | 選定 78／78，0 failed／skipped；包含 R11～R15、額外生命週期、Provider 刷新、索引／縮池及核心播放回歸 |
| Packed content | 原專案素材 address／GUID 共用同一載入 operation，最後釋放回到零 |
| 真實 catalog 更新 | 兩版獨立建置的 catalog／AssetBundle，loopback HTTP 取得舊版，CheckForCatalogUpdates／UpdateCatalogs 後 RefreshClipProvider；舊 handle 持有 hot-v1，新 handle 播 hot-v2；新舊同時有效且能各自釋放。測試設定 UniqueBundleIds=true，正式專案建置設定未改 |
| AudioSettings.Reset | Editor 與 Player 均收到程式 Reset 回呼，舊 BGM 得到 Finished／Failed 及指定原因；原配置在測試後還原。不是實體耳機拔插測試 |

開發過程保留失敗紀錄：首次針對性測試 106／107，發現把 Addressables 普通交付一律延後會破壞較早回呼取消後續等待者，修正後核心完整 196／196。Player 首次建置成功但在 macOS Metal 畫面呈現等待卡住，180 秒無測試活動逾時；改用 Test Framework 1.8.0 本機原始碼所示的 TestPlayerBuildModifier／TestRunCallback 分離建置與執行，以 `-batchmode -nographics` 驗證。第二次 77／78，原因是測試群組誤入初始 catalog；隔離測試資產並加初始不存在斷言後，最終 78／78。沒有把這兩次失敗算成通過。

因此本輪確認的是 **macOS 無圖形 Player 的音訊／素材生命週期**，尚未完成圖形 Player 正常呈現、其他 Unity 版本／平台或實體裝置切換驗證；未宣稱人工聽感或端到端延遲通過。最終 C# 沒有編譯錯誤／警告，專案設定已還原，`git diff --check` 通過。

本輪正式前版、核心版與最終來源快照、CSV／XML 摘要及雜湊在 `work/reliability-repair-20260930/`；完整 Editor XML／log 為 `work/stage-two-hardening/repair-final20260930.*`、`repair-edit20260930.*`，Player 證據在 `work/reliability-repair-20260930/player/`。前兩次 Player 紀錄保留於 `player-attempt1/`、`player-attempt2/`，原始缺陷證據仍在 `work/system-audit-20260930/`。測試與工具繼續依使用者規則僅保留本機、由 Git 忽略；README／CHANGELOG 記錄對外契約。

### 5.13 BGM 載入、播放配置與批次控制優化（2026-09-30）

本輪依使用者核准的三項方向實作，保留 §5.12 的修正及可選擴充清單。測量來自本專案本機 A/B，不以開源專案的效能數字代替實測，也沒有搬入其他音訊框架。

**完成的修改**

- BGM：以兩首範例的副本比較原設定、背景解壓、Streaming、Compressed In Memory。正式兩首 BGM 改用 Streaming＋背景載入，關閉資料預載，品質／取樣率不變；SFX／Voice 不變。加入明確選用的 Audio Imports 選單，以及 DebugMenu 的初始化 → 聲源預熱 → PrepareClip → 播放／取消範例。
- 播放配置：公開 PlayOptions 維持 class，接受請求後才製作內部 readonly struct 快照；淡變狀態改為 struct，使用動作種類與目標播放資料取代 Tween 物件／閉包。值資料以 ref 累積時間，完成前先清掉舊動作，保持暫停與聲源重用正確。
- 素材持有：直接傳入外部 clip 不再製作無釋放動作的 lease；駐留快取可直接取得獨立 user lease，省去載入回呼。未改公共 lease、provider 釋放排序、快取保留或舊 handle 晚訂閱語意。
- 大量控制：handle 保存僅內部可用的播放引用，完成即清空；查找不再掃描清單或產生 predicate 閉包，不另建 Dictionary。批次停止直接使用已選取的 Playback，快照清單以 try/finally 歸還、清空並可重入重用；暫停迴圈不製作沒有必要的陣列。
- 保留有序 active 清單與 Remove 的線性成本，因此 StealOldest／回呼順序不變。256 個暖機批次停止已降到約 0.17 ms，現有量測不足以支持換掉容器或進一步池化 Playback。handle 增加一個內部引用欄位，快照緩衝會保留容量；首次建立／擴張仍有配置。

**量測方法與校驗**

同機 Unity 6000.6.3f1 Editor；預熱 256 個來源，24／64／256 聲音各暖身後測 7 次，中位數如下。CPU 包含相同的 Profiler／原生回呼量測負擔，沒有設定 CI 時間門檻。

.NET `GC.GetAllocatedBytesForCurrentThread` 回傳 0，批次 Editor 的 RawFrameDataView 也未取得有效幀，這些失敗結果未採用。改用已安裝 Unity 的官方 `IUnityProfilerCallbacks` API，讀取 GC.Alloc 第一筆整數 metadata；thread-local 記錄限定在指定的主執行緒呼叫區間，不在回呼中配置物件。已知 4,096-byte 陣列讀到 **4,128 bytes、1 次事件**，各區間檢查 metadata 型別，並與 ProfilerRecorder 事件總數交叉檢查。這個原生測量程式與測試只留本機，不是正式 Runtime 的依賴。

| 256 聲音測點 | CPU 前 → 後（ms） | 配置 bytes 前 → 後 | 配置事件前 → 後 |
| --- | --- | --- | --- |
| 暖池外部 clip 播放 | 0.5364 → 0.5548 | 103,936 → 86,528 | 2,304 → 1,536 |
| 暖池駐留 ID 播放 | 0.5683 → 0.4978 | 163,840 → 108,544 | 2,560 → 1,536 |
| 暖池外部 clip 淡入 | 0.5590 → 0.5006 | 114,176 → 86,528 | 2,560 → 1,536 |
| 批次停止 | 0.2413 → 0.1652 | 51,588 → 0 | 778 → 0 |
| 逐一反序停止 | 0.3289 → 0.1970 | 47,104 → 0 | 768 → 0 |
| 逐一暫停／恢復，共 512 次 | 0.2486 → 0.0272 | 77,824 → 0 | 1,024 → 0 |
| 設定批次淡出停止 | 0.1970 → 0.0125 | 94,596 → 0 | 1,290 → 0 |

24／64 聲音批次停止為 0.0258 → 0.0175 ms／0.0605 → 0.0416 ms；駐留 ID 播放為 0.0604 → 0.0570 ms／0.1521 → 0.1350 ms。外部 clip 一般播放的 CPU 沒有穩定改善，保留的收益是配置減少；不同執行的 EntityId 字串長度也會影響少量 bytes。零配置只適用於已暖機、沒有使用者完成回呼的上述控制路徑，不代表整個服務零 GC。

持續負載維持最多 64 聲音，約每 1/60 秒替換 8 個，共 1,800 批／14,400 次播放。呼叫配置合計 **11,853,824 → 6,105,600 bytes**，降低約 48%；整個 Editor 程序觀察到的 GC.CollectionCount(0) 增量 **11 → 6**。呼叫 CPU p99 為 0.1260 → 0.1015 ms；最大值反而由 0.1635 → **3.3913 ms**，後者區間同時有一次 GC。這證明配置壓力下降，**不證明 GC 尖峰已消除或遊戲幀率提升**。GC／幀觀察包含 Editor 和測試框架；CSV 的 frameMilliseconds 是每批結束時的一幀樣本，不是全部幀的完整尖峰追蹤。

**長音樂的匯入 A/B**

副本維持同品質與取樣率，每種設定 3 次；先卸載資料再 LoadAudioData，讀取 Loaded 後播放，並檢查游標前進、暫停與尾端循環。以下是完整 Editor suite 的中位數：

| 音樂／設定 | LoadAudioData 呼叫（ms） | 到 Loaded（ms，含幀等待） | Profiler 單一 clip bytes |
| --- | --- | --- | --- |
| ukulele_song／原同步解壓 | 227.5189 | 227.5200 | 24,469,697 |
| ukulele_song／背景解壓 | 0.0083 | 257.0942 | 24,469,697 |
| ukulele_song／Streaming＋背景 | 0.0095 | 1.1937 | 197,965 |
| ukulele_song／Compressed In Memory＋背景 | 0.0157 | 0.7873 | 4,709,929 |
| maou_bgm_acoustic50／原同步解壓 | 152.5056 | 152.5067 | 24,437,441 |
| maou_bgm_acoustic50／背景解壓 | 0.0060 | 170.7951 | 24,437,441 |
| maou_bgm_acoustic50／Streaming＋背景 | 0.0063 | 1.0172 | 197,965 |
| maou_bgm_acoustic50／Compressed In Memory＋背景 | 0.0146 | 0.8204 | 2,602,345 |

選擇 Streaming 的依據是這兩首長音樂的 clip 記憶體與主執行緒準備成本；音訊解碼／磁碟工作仍會在播放期間發生。這裡的記憶體只取 Profiler.GetRuntimeMemorySizeLong(clip)，不是完整 Audio Memory 或 RSS。列出的 Audio Profiler counters 沒有取得有效 samples，以 NaN／0 samples 留存，**不以零值宣稱沒有 DSP 或串流 CPU 成本**。Prepared Play 呼叫很短也不等同耳端延遲；未進行長時間磁碟壓力、真實裝置聽感或所有平台驗收。

Editor 工具只檢查被選取音檔；30 秒或 float PCM 估計 8 MiB 以上只是提示門檻，不是引擎限制。兩個可選 profile 僅修改 Default 的 loadType／preload 與背景載入，不覆蓋各平台設定；聲音用途及目標平台仍需自行實測選擇。

**驗收與證據**

- 完整 PlayMode **219／219**：既有行為、新增 12 項回歸及 8 項量測／觀察；涵蓋設定快照、淡變累積／取消／暫停、舊 handle、外部 clip 所有權、共用素材持有、批次重入、StealOldest 及載入範例停用。
- Editor／Reload **18／18**：原 14 項加 4 項匯入檢查／profile 測試，包含品質、取樣率及平台 override 保留。
- macOS Player 選定測試在 **無圖形／Metal 圖形模式各 101／101** 通過，包含真實 packed address／GUID、HTTP catalog／AssetBundle 更新、程式音訊重設、新增控制回歸及四種 BGM 設定各三次的游標／暫停／循環驗證。Player clip 回報約 24.44～24.47 MB → 197,508 bytes，與 Editor 的方向一致。Metal 模式此輪沒有重現前輪停滯；並未因此宣告已找出或修復 Unity 圖形問題，實際遊戲場景畫面／主觀聽感、實體裝置拔插及其他平台仍待驗證。
- 本輪基準、副本比較、CPU／bytes／GC CSV、前後來源與摘要位於 `work/performance-refinement-20260930/`；Editor XML／log 位於 `work/stage-two-hardening/refine-*.{xml,log}`。量測基準為 `refine-native-before2`，最終 Editor 對照為 `refine-full`；先前未通過的量測校驗與兩次 Player runner 嘗試另外保留，不混入成功結果。Player 初次失敗是 Test Framework 強制 ConnectToHost，Editor 已退出後約 10 秒產生連線錯誤；檢查已安裝 1.8.0 的 PlayerLauncher.cs 後，在僅供拆分執行的 build modifier 移除該旗標及自動 Profiler 連線，沒有忽略音訊錯誤或放寬行為斷言。

參考：[Unity 音訊匯入](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioClip.html)、[Audio Profiler 指標定義](https://docs.unity3d.com/6000.0/Documentation/Manual/ProfilerAudio.html)、[GC.Alloc metadata 範例](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.RawFrameDataView.GetSampleMetadataAsLong.html)；實際量測 API 以本機 Unity 6000.6.3f1 的 `Contents/Resources/PluginAPI/IUnityProfilerCallbacks.h` 與 `IUnityProfiler.h` 為準。

### 5.14 使用流程與推薦介面（2026-09-30）

依使用者核准的方向，優先降低導入與日常使用的理解成本。本輪沿用既有播放引擎、素材持有、回呼與並發規則，沒有導入外部音訊框架，也沒有開始 UPM／Addressables adapter 拆分。

**已實作的流程**

- 新增 `Controller.Audio.AudioService` 日常入口。六個 ID／clip 播放 overload 均回傳 handle，忽略回傳值不改變播放語意。BGM 共用循環替換槽，SFX（包含 Loop）獨立播放，Voice 預設替換對話槽；只有明確指定 `VoicePlaybackMode.Overlap` 才獨立並發。語音的 Loop 不會切換替換槽。
- `AudioController` 原公開介面保留；新 Voice 替換入口共用舊非循環語音槽，舊循環分支與 `PlayVoiceHandle` 的並發語意不變。README 說明避免混用兩套對話控制。新入口直接使用原有接納邏輯，保留拒絕／失敗不停止舊聲音的規則。
- 單一聲音使用 handle；全分類停止使用 StopBgm／StopSfx／StopVoice；全分類淡變只推薦 FadeBus，涵蓋所有入口。淡變持續增益、保存音量、靜音與暫停的差異直接放入操作表。
- 開發環境的播放失敗預設提示類別、ID、原因及目前設定的內建來源 key／Resources 路徑。訊息明確是 configured lookup，不宣稱有逐次來源查詢軌跡；自訂來源不捏造路徑。相同失敗去重、最多保存 64 種，再顯示一次抑制提示；正常成功路徑不建立診斷字串，一般並發拒絕不自動警告。
- Controller Inspector 將較少使用的設定折疊，顯示 Ready、播放數及歷史失敗；Play Mode 期間設定欄位避免直接繞過 runtime 更新方法。Bootstrap Inspector 提示未配置來源及不相容元件。
- 新增可直接開啟的 `Samples/AudioQuickStart.unity`：Prefab、AudioListener、三種素材、播放／停止按鈕與保存音量滑桿。`AudioQuickStart` 的 void adapter 可接 Unity UI 事件。
- 新增 `SceneAudioSample`：準備素材、保存 handle、取消版本／key 快照、停用清理及重啟；完整換場只停止自己持有的音效，保留仍播放的 BGM 與其他群組。明示 ReleaseUnusedClips 清除全服務普通快取保留；部分區域卸載應由應用程式統一決定此政策。
- README 改為入門及常見操作，原詳細技術內容移至 `Documentation/AudioServiceReference.md`；換場步驟見 `Documentation/SceneAudio.md`。所有進階機制仍可選，基本播放不必先 Initialize／Preload／PrepareClip／PrewarmSources。

**驗證與界線**

第一輪聚焦回歸 101／101 通過。新增 24 項播放契約／診斷／換場與實際範例場景測試；完整 PlayMode **243／243**、Editor／Reload **18／18** 通過。macOS Player 選定測試在無圖形與批次 Metal 模式各 **104／104** 通過，包含真實打包素材、新場景引用、範例按鈕對應方法、舊 handle 不停止新對話，以及場景卸載後保留跨場景 BGM。驗收對象為功能、建置與生命週期，音訊輸出以狀態／游標驗證，沒有宣稱真人聽感通過。

首次完整 PlayMode 243 項中 242 通過，唯一失敗為批次 Editor 未產生 Game view 截圖；播放、場景引用與卸載檢查已通過。兩次有視窗 Player 嘗試皆在測試開始前逾時，第二次曾使用僅限測試建置的 runInBackground 設定排查；原生 UI 工具隨後確認 Mac 鎖定、無法存取測試視窗。最終改用批次 Metal 完成功能回歸，**視窗截圖、畫面配置與人工操作仍未驗證**。上述失敗 XML／log／timeout status 保留，不以跳過截圖宣稱視覺驗收完成。

這些是 API 契約、場景與建置驗證，尚未做新使用者操作研究，不能宣稱已實測降低多少學習時間。後續以相同任務比較首次成功時間、文件查找與 API 誤用次數。核心仍直接依賴 Addressables；其可選化與 UPM 發布留在第三階段。

證據目錄為本機 `work/usability-20260930/`；測試 XML／log 位於 `work/stage-two-hardening/usability-*`。範例與文件隨專案保留；測試、fixture builder 與 runner 依既有規則只留本機。

### 5.15 範例控制權與集中音訊檢查（2026-09-30）

依 §2.1 的易用性準則，改善「重新播放失敗後仍可停止原本聲音」及「Ready 卻聽不到時知道從哪裡排查」兩項使用任務。基本播放的必要步驟、API 與設定未增加，進階擴充與套件化範圍不變。

**已實作**

- AudioQuickStart 的直接 clip 請求失敗或被拒絕時，保留仍有效的原音樂／語音 handle；停止與 OnDisable 只清理自己持有的聲音。新增操作版本檢查，處理替換完成回呼重入停止、停用或再次播放，避免返回中的舊請求重新取得控制權。範例畫面會顯示音樂／語音未起播的原因。
- Controller Inspector 的 Audio checks 集中檢查已載入場景中有效 Listener 數量、Controller 啟用／根物件位置與 Mixer 群組／exposed parameters；Play Mode 另顯示 Listener 音量／暫停、遊戲／背景暫停、Master／分類音量、靜音與 FadeBus 歸零。刻意的靜音／暫停等狀態使用資訊提示，附恢復方式，由使用者決定是否調整。
- 診斷僅在 Editor Inspector 存活時每 0.5 秒更新，並提供立即刷新；不在播放熱路徑查找場景物件，不載入音檔、不初始化服務、不自動修改設定。Prefab 資產及 Prefab Mode 不套用場景 Listener 數量要求；所有已發現的 Mixer 缺失分別列出。
- 加入 `GetChannelDiagnostics` 的唯讀 Volume／FadeGain／Muted 及 `Diagnostics.GamePaused`／`BackgroundPaused`；這些是服務控制值，沒有把它們當成實際混音波形或可聽見保證。逐分類數值可在 Inspector 展開查看。
- Inspector 顯示名稱改為 Auto Save／Save Delay 與 Max Concurrent Sounds；保留 `saveImmediately`／`saveDelaySeconds`／`maxVoices` 原序列化名稱、預設值及所有公開 API。Settings Inspector 分開基本儲存設定與 Storage keys，Play Mode 不直接修改 defaults／keys，避免繞過既有設定載入流程。
- README 的排查入口改為 AudioCtrl → AudioController → Audio checks，再依 Console 的素材失敗訊息處理；進階參考記錄檢查範圍、唯讀 API 及相容性。

**驗收與界線**

修正前已在 Unity 重現 **8／8 失敗**：音樂／語音失敗後停止或停用、被拒絕後清理，以及替換回呼中停用元件。修正後首輪聚焦 **58／58** 通過；再補停止／再次播放重入、唯讀狀態及真實範例場景卸載，完整 PlayMode **258／258** 通過。Editor／Reload **24／24** 通過，包含 Listener 啟用與 additive 場景、Prefab 不需自身 Listener、Mixer 多項缺失、設定相容性、唯讀檢查與解除原因後更新提示。

macOS Player 選定測試在無圖形與批次 Metal 模式各 **119／119** 通過，包含本輪新增播放／控制狀態、實際範例失敗後卸載、既有 packed content 與 HTTP catalog 更新；建置成功，測試無跳過。ProjectSettings 最終與開工快照逐檔雜湊相同，`git diff --check` 通過。

首次 Editor 檢查為 20／24；失敗涉及本機 fixture 的未儲存 additive 場景、PartialMixer 內容假設與 EnterPlayMode 後重跑 SetUp 干擾場景。修正 fixture 並保留產品斷言後重跑至 24／24，首輪結果另存。最初沙箱啟動 Unity 因授權 IPC 受限中止；清理本次遺留的授權程序，依工具核准在可使用本機授權的環境重跑。

本輪桌面操作工具連線逾時，**視窗配置、人工點按與人工聽感尚未驗證**；沒有進行新使用者操作研究。診斷不涵蓋音檔內容、單次播放／相容分支增益、Mixer 效果或作業系統／喇叭輸出；本輪未進行效能前後對照評估，不宣稱 GC、CPU 或學習時間改善幅度。

本輪基準、來源副本、差異與驗證摘要保留於 `work/usability-followup-20260930/`，Editor XML／log 位於 `work/stage-two-hardening/usability-followup-*`。本機測試與工具依 §9.1 保持 Git 忽略，正式程式不依賴它們。

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

目前專案已升級至 Unity `6000.6.3f1`，後續先在此環境補齊播放與建置基準。原始審查版本 `6000.2.6f2` 保留作歷史比較；兩處 API 修正及第一、第二階段 PlayMode 已通過；Addressables content build、macOS 無圖形 Player 播放與 HTTP catalog／AssetBundle 更新已通過 §5.12 驗證；§5.13 另完成 Metal 圖形模式的測試 Player；實際遊戲場景聽感、其他平台及實體裝置切換仍不算已驗收。Unity 2022／2021 等其他版本只有在實際通過編譯與測試後，才列為已驗證支援。

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
5. 每批修改依 [2.1 的易用性準則](#21-後續修改的固定易用性準則) 檢查對使用流程的影響；每階段通過驗收後再往下一階段推進。新發現的問題記錄到對應階段，避免混入無關功能。
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

### 10.1 可選擴充計畫

以下四項列為依遊戲需求選用的後續計畫，目前均未實作、未排定時程，也不作為前三階段完成或目前套件化的必要條件。現行服務仍以 2D 音訊為主；啟動個別項目前，先確認使用情境、目標平台與可驗證的需求。

| 編號 | 可選能力 | 用途與遊戲情境 | 預計擴充範圍 |
| --- | --- | --- | --- |
| E1 | 3D 空間音效 | 聲音隨位置與距離改變，例如右側火堆的方向感、走遠後音量降低；2D 遊戲亦可依需求使用 | 單次播放的位置、Transform 跟隨、距離衰減與空間音訊設定；處理跟隨對象銷毀、場景卸載，以及池內聲源回收／重用時的完整重設 |
| E2 | DSP 排程與進階音樂控制 | 依音訊時鐘安排起播，例如下一小節切換、前奏接循環，以及音樂段落串接 | 增加已排程但尚未起播的狀態與素材提前準備，定義取消、暫停、恢復及素材未及時就緒的政策；視需求加入雙聲源交叉淡化、循環點與節拍同步 |
| E3 | LRU 快取淘汰 | 快取達到預算時，優先放掉最久未使用且可回收的素材，例如大量對話／語音播放後控制素材保留量 | 在 AudioClipStore 記錄最近使用順序並加入可配置預算；以既有 lease、播放使用數及預載群組判斷可淘汰項目。預算採項目數或記憶體估算，依實測決定 |
| E4 | 重要聲音保護 | 同時播放過多聲音時，優先保留任務語音、BGM 等重要內容，避免被一般音效替換 | 在現有並發政策加入重要性、保護標記或分類保留額度；定義可替換對象與沒有可替換對象時的拒絕規則，並搭配 Unity 的 AudioSource.priority 與實際聲道預算 |

**整合原則：** 沿用現有 AudioHandle、播放引擎、聲源池及素材持有機制，依功能擴充播放設定與必要的狀態流程；保留既有 API 的 2D 預設行為。E2 對播放流程影響較大，優先評估新增音樂控制層。是否另拆套件／assembly，待功能範圍明確後決定。現行普通 BGM 切換維持先淡出再淡入。

**各項最低驗收方向：**

- **E1：** 位置、左右方向與距離衰減符合設定；跟隨物件移動／銷毀及場景卸載後行為明確；聲源由 3D 回收再用於 2D 時，不殘留位置、跟隨目標或空間設定。基礎 API 參考 [Unity spatialBlend](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spatialBlend.html)。
- **E2：** 已排程尚未起播不被誤判為完成；取消或服務關閉後不遲到發聲；暫停／恢復、素材逾期及段落切換依明確政策執行，素材在排程／播放期間保持有效。以目標 Player 量測起播與串接，區分音訊時鐘排程精度及裝置輸出延遲。基礎 API 參考 [Unity PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)。
- **E3：** 淘汰順序符合最近使用紀錄；載入中、播放中及仍被預載群組保留的素材不得因快取預算提前釋放。所有候選均受保護時，回報無法達成預算的狀態；驗證被淘汰素材可重新載入，並量測換場與大量語音的保留量及重載成本。解除服務持有不保證 native 記憶體立即下降，須依 [Addressables 記憶體管理](https://docs.unity3d.com/Packages/com.unity.addressables@2.11/manual/memory-assets.html) 與目標平台實測判讀。
- **E4：** 全域／單音效上限與 Loading 預約仍一致；受保護播放不被一般音效的搶占政策終止；沒有可淘汰對象時依政策拒絕新請求，且回呼重入不突破上限。分別驗證服務選擇停止誰，以及 Unity 實際混音聲道的取捨；只設定 [AudioSource.priority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-priority.html) 不等於完成服務層的保護政策。

**評估順序：** 一般 2D 專案可優先評估 E4；有場景定位需求時評估 E1，大量素材或實測記憶體壓力出現時評估 E3，需要節拍／段落串接時評估 E2。此順序為需求評估建議，不表示四項均須實作，也不預先承諾效能改善幅度。

其他後續候選仍保留：語音播放時降低背景音量、對話佇列、群組隨機／依序播放，以及完整 ScriptableObject 音效資產、音效庫掃描、波形與循環點編輯工具；同樣不列為前三階段的必要條件。

### 10.2 執行到相關階段時確認

| 決策 | 確認時機 | 目前規劃 |
| --- | --- | --- |
| 正式工作副本 | 已由使用者指定；開工時只核對狀態 | `/Users/michael/Unity/Git Repo/unity-audio-service` |
| Unity 與目標平台支援範圍 | 基準測試與第三階段 | 目前以 `6000.6.3f1` 補齊播放／建置驗證；其他版本與平台以實測納入 |
| 資源預算與並發預設值 | 第二階段量測後 | 根據代表性場景設定，避免任意給值 |
| 正式 API 與套件名稱 | 第二、第三階段 | 日常推薦 API 已實作為 `Controller.Audio.AudioService`（5.14）；套件名稱仍於第三階段確定 |
| 授權與發布形式 | 第三階段發布前 | 由專案擁有者選定，不預先套用授權 |

## 11. 完成狀態記錄

- [x] 原始碼與比較專案的靜態檢視。
- [x] 三階段規劃與參考方式整理。
- [x] 指定工作目錄的 117 檔案比對、場景／資產／套件設定複查，以及 v1.1 計劃更新。
- [x] 依使用者偏好加入本機測試、輔助腳本與臨時目錄的忽略規則，更新為 v1.2。
- [x] Unity `6000.6.3f1` 的兩處過時 API 替換、Editor 重新編譯與 Console 檢查（2026-09-29，見 1.2）。
- [x] Unity 播放基準執行與缺陷重現（21 項基準中 18 項失敗，見 4.5）。
- [x] 升級後 Addressables content build 與 macOS 無圖形 Player 播放／catalog 更新驗證（見 5.12）；圖形呈現、其他平台與實體裝置另列未驗證。
- [x] 第一階段實作與驗收（最終 39／39 PlayMode 測試及 Editor 原場景播放檢查通過，見 4.5）。
- [x] 第二階段 C1～C7 實作與本機 Editor 驗收（92／92 PlayMode、2 項 Play Mode 重入情境及原場景操作／聲源池基準通過，見 5.4）。
- [x] 第二階段補強 R7～R9、效能前後量測與 PrepareClip（118 項行為測試、量測及 Reload 驗收通過，見 5.5）。
- [ ] 第三階段實作與驗收。
- [x] 登錄 E1～E4 可選擴充計畫、整合原則與最低驗收方向（僅完成規劃，功能尚未實作，見 10.1）。
- [x] 再次搜尋開源／官方資料並補做目錄、音量更新及批次停止量測（正式 Runtime 未修改，近期改善候選見 5.6）。
- [x] 音量更新去重與 Catalog 索引／驗證實作、前後量測及完整本機回歸（134 項 PlayMode、14 項 EditMode／Reload，見 5.7）。
- [x] 優化後再搜尋開源與官方資料，補量冷池／過載／重複狀態成本並重現 R10（研究紀錄見 5.8）。
- [x] 修正 R10：並發拒絕保留既有播放／待載入／轉場，補動態上限、多個替換及回呼回歸（149 項 PlayMode、14 項 EditMode／Reload，見 5.9）。
- [x] 完成暫停／靜音去重、可選聲源預熱與拒絕路徑優化，保留逐步前後量測與原子拒絕契約（170 項 PlayMode、14 項 EditMode／Reload，見 5.10）。
- [x] 再次核對五個固定提交的開源專案與官方資料，新增九項邊界／對照測試並確認 R11～R15（179 項 PlayMode 中 172 通過／7 失敗，見 5.11）。
- [x] 修正 R11～R15，完成按需計數索引、顯式閒置縮池與前後量測；205 項 PlayMode、14 項 EditMode／Reload、78 項 macOS Player 測試通過（見 5.12）。
- [x] 完成 BGM 載入、正常播放配置及批次控制優化；219 項 PlayMode、18 項 Editor／Reload、101 項 macOS Player 於無圖形／Metal 模式各通過，含 bytes 校驗與持續 GC 觀察（見 5.13）。
- [x] 完成 AudioService 推薦入口、明確語音語意、開發診斷、Inspector 與入門／換場範例；243 項 PlayMode、18 項 Editor／Reload、104 項 macOS Player 於無圖形／批次 Metal 模式各通過（見 5.14）。
- [x] 完成範例失敗／拒絕後的控制權與重入修正、Inspector 名稱及唯讀集中檢查；258 項 PlayMode、24 項 Editor／Reload、119 項 macOS Player 於無圖形／批次 Metal 模式各通過（見 5.15）。
- [ ] 新範例與 Inspector 的視窗畫面／人工操作及新使用者任務驗證（前輪 Mac 鎖定，本輪桌面工具連線逾時，尚未完成，見 5.14、5.15）。

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
| 1.6 | 2026-09-29 | 合併第二階段後續研究、三項已重現的回呼缺陷與新增開源參考；106 項 PlayMode 與 Reload 生命週期驗收通過，效能量測待執行 |
| 1.7 | 2026-09-29 | 完成使用中聲源更新、快取及無限制播放路徑優化；新增 PrepareClip 與背景載入／取消驗證；回填同環境 CPU／配置事件／音訊資料 A/B、118 項行為測試與第三階段界線 |
| 1.8 | 2026-09-29 | 登錄 3D 空間音效、DSP 排程、LRU 快取淘汰與重要聲音保護四項可選擴充；補上用途、實作範圍、整合原則與最低驗收方向，明列尚未實作且不作為目前套件化的必要條件。僅更新文件 |
| 1.9 | 2026-09-29 | 重新搜尋並固定 AudioConductor 2.5.1、LucidAudio 與 Unity3D-SoundManager 的參考提交；補量 Catalog 索引原型、重複音量更新與批次停止，記錄優先順序、GC 指標校驗限制及 Player 驗證界線。正式 Runtime 未修改 |
| 1.10 | 2026-09-30 | 完成音量去重、PlayerPrefs key 配置移除、Catalog 索引與 Inspector 資料驗證；保留解析／設定／刷新契約，補當日前後量測及索引管理記憶體代價，134 項 PlayMode、14 項 EditMode／Reload 通過；第三階段尚未開始 |
| 1.11 | 2026-09-30 | 優化後再次核對 4 個固定提交的開源專案與官方資料；補量冷／暖池、過載拒絕與相同 Pause／Mute，重現 R10 多重並發限制的部分停止問題並列為優先待修。正式 C# 未修改 |
| 1.12 | 2026-09-30 | 修正 R10：完整判斷並發限制後才替換，拒絕保留既有聲音、待載入與 BGM 轉場／淡出；保留動態上限、替換順序與回呼語意。新增 14 項回歸，修正前 6 項失敗；修正後 149 項 PlayMode、14 項 EditMode／Reload 通過 |
| 1.13 | 2026-09-30 | 完成暫停／靜音同值去重、PrewarmSources 可選容量預熱、接納前判斷與拒絕配置減少；補逐步量測、20 項行為回歸與 1 項量測，170 項 PlayMode、14 項 EditMode／Reload 通過；保留 R10、handle、快照及生命週期契約 |
| 1.14 | 2026-09-30 | 重新核對五個開源專案與 Unity／Addressables 官方資料，重現 Provider 釋放重入／例外、Listener 暫停誤判、音訊重設狀態與定位快取五類缺陷 R11～R15；新增九項測試，完整 179 項中 172 通過／7 失敗。列出修正順序及單音效索引／縮池候選；正式 C# 未修改，缺陷尚未修復 |
| 1.15 | 2026-09-30 | 修正 R11～R15，明訂音訊重設 Failed／Cancelled 政策，加入 Provider 刷新世代、按需單音效計數索引與限量閒置縮池；205 項 PlayMode、14 項 EditMode／Reload、78 項 macOS 無圖形 Player 驗收通過，含真實 HTTP catalog／AssetBundle 更新；保留量測、失敗與修正紀錄及平台限制 |
| 1.16 | 2026-09-30 | 完成 BGM Streaming／背景準備範例與選用匯入工具、值型設定與淡變、駐留素材直接取得、直接 handle 定位及批次快照重用；補校驗後的配置 bytes、持續 GC 觀察及 24／64／256 聲音量測，見 §5.13 |
| 1.17 | 2026-09-30 | 以簡單、好用、操作直覺為目標，加入 AudioService 推薦入口、明確語音替換／並發、有界開發診斷、Inspector 提示、可操作場景與換場範例；README 與進階參考分開，保留既有 API，驗證見 §5.14 |
| 1.18 | 2026-09-30 | 將「簡單、好用、操作直覺」列為所有後續修改的固定準則，補上適用範圍、設計取捨及依改動規模執行的易用性檢查，並從 README 與開發流程連結；僅更新文件 |
| 1.19 | 2026-09-30 | 修正範例失敗／拒絕後的控制權與回呼重入；新增唯讀集中音訊檢查，改善 Inspector 名稱並保留序列化相容性；依易用性準則記錄使用流程、驗證與界線，見 §5.15 |
