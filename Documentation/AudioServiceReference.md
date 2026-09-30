# AudioController：進階與相容介面參考

日常播放請先閱讀 [快速入門](../README.md)，使用 `AudioService`。本頁保留 `AudioController` 的相容 API、來源、快取、生命週期與技術驗證說明。`PlayVoiceHandle` 的並發行為、`FadeChannel` 的分支範圍與推薦入口不同，請勿只依名稱互換。

2D 音訊服務，提供 BGM／SFX／Voice、獨立播放 handle、可重用聲源、非同步載入、素材持有與音量設定。第一、第二階段已實作；UPM 套件分離仍屬第三階段。

## 目前驗證環境

以下數字保留 §5.13 的效能優化驗收；最新推薦入口、範例與 §5.14 使用流程驗證見 [README](../README.md#專案狀態)。

- Unity `6000.6.3f1`、Addressables `2.11.2`、Unity Test Framework `1.8.0`。
- Runtime assembly：`Controller.Audio`，目前仍依賴 `Unity.Addressables`／`Unity.ResourceManager`。尚未支援不安裝 Addressables 的獨立核心套件。
- 已驗證本機 Editor／PlayMode、原始 Prefab／場景、Addressables Editor 資產模式及無 Domain Reload 的 Play Mode 重入。
- 2026-09-30：BGM 載入、正常播放配置與批次控制優化完成，完整 PlayMode **219／219**、Editor／Reload **18／18** 通過；macOS Player 選定測試在無圖形模式與 Metal 圖形模式各 **101／101** 通過，含真實 Addressables catalog／AssetBundle 更新。
- 實際遊戲場景的畫面／聽感、其他 Unity 版本／平台與實體耳機拔插仍需驗證。前輪可靠性修正見[改善計畫第 5.12 節](../unity-audio-service-improvement-plan.md)，本輪變更、CPU／配置 bytes 與 GC 觀察見同文件 **§5.13**。

## 快速導入

1. 將 `Assets/AudioService/Runtime/Prefabs/AudioCtrl.prefab` 放入入口場景；保持一個有效 Controller。根層 Controller 會常駐，重複實例會被移除。
2. 使用原始 `Assets/AudioService/AudioSampleScene.unity`／DebugMenu 檢查 BGM、SFX、Voice 與轉場。
3. Prefab 的 Bootstrap 連接 Provider 與設定 handler。預設 Fallback 可以使用 Resources，或按需載入 Addressables；新建立的 Addressables provider 預設不再全量預載。
4. 使用 `AudioController.Instance`。需要控制單次播放時取得 handle；簡單呼叫仍可使用原有 `void Play*()`。

```csharp
using Controller.Audio;

var audio = AudioController.Instance;
audio.PlayBgm("ukulele_song", fadeInSeconds: 1f);
audio.PlaySfx("minigame_win");

AudioHandle voice = audio.PlayVoiceHandle("minigame_drink", new PlayOptions
{
    Volume = 0.8f,
    FadeInSeconds = 0.1f
});
voice.Completed += h => UnityEngine.Debug.Log($"Voice ended: {h.Result}");
voice.Pause();
voice.Resume();
voice.Stop(0.2f);
```

## 播放 API 與 handle

| 介面 | 行為 |
| --- | --- |
| `PlayBgm`／`PlayBgmHandle` | 共用 BGM 替換槽；最後一個有效請求優先，維持先淡出、再淡入 |
| 舊 `PlayVoice` | 循環與非循環各自替換，保留原來的分支選擇 |
| 舊 `PlaySfx(loop: false)` | 可並發，現在每次播放都使用獨立聲源，也支援 `fadeInSeconds` |
| `PlaySfxHandle`／`PlayVoiceHandle` | 每次呼叫都是獨立實例，可分別停止／暫停 |
| `Play(category, AudioId, options)` | 依音效 ID 播放獨立實例 |
| `Play(category, AudioClip, options)` | 播放外部素材；素材由外部擁有，服務不卸載它；循環需由 options 指定 |

服務 API 與 Provider 完成回呼須在 Unity 主執行緒使用。

`PlayOptions` 在請求時複製，包含 Loop、Volume、Pitch、FadeInSeconds、FadeOutSeconds、IgnoreGamePause、AllowAsyncLoad、MaxInstances 與 ConcurrencyPolicy。`FadeOutSeconds` 用於 BGM 替換；停止單次播放的時長傳給 `handle.Stop(seconds)`。

- 狀態為 Loading、Playing、Paused、Finished；結果區分 Completed、Stopped、Cancelled、Failed、Rejected。
- Loading 中 Stop／被替換／服務關閉得到 Cancelled；播放中主動停止得到 Stopped；自然播完得到 Completed。
- 終態、名額及聲源清理立即更新；Provider lease 的實際釋放與 `Completed` 通知在本次核心狀態變更完成後於主執行緒派送。釋放動作或完成回呼再次 Play／Shutdown 時，會看到已完成的內部狀態；新請求仍遵守並發上限。
- `Completed` 每個訂閱在結束時通知一次。結束後新訂閱會立即收到保留的結果；回呼拋出例外會記錄且不阻止其他訂閱。
- 結束後 `IsValid` 為 false，控制方法回傳 false；舊 handle 無法控制池中重用的聲源，也不公開 AudioSource。
- Loading 中 Pause 會記住暫停需求，載入完成後直接保持 Paused。暫停中的 Stop 立即完成，即使有淡出時長；暫停不會被當成自然結束。
- 空 ID／null clip／無效分類回傳 Failed，不取代現有播放。失敗或上限拒絕不需等下一幀才可訂閱結果。

## 音效目錄與來源政策

`AudioId` 是區分類別、大小寫敏感的邏輯識別字。建議使用 `ui.confirm`、`music.menu` 等名稱；未設定目錄或查無對應項目時，既有字串 key 照常使用。

建立 `Assets > Create > Audio Service > Catalog` 資產，填入 Entries，並指定 Controller 的 Catalog。每筆資料包含 Id、Category、ResourcesKey、AddressablesKey、Aliases 與 MaxInstances。同一類別的別名應唯一，不同素材不要共用同一 ID。

Catalog 使用分類＋ID／別名索引。Inspector 會顯示重複 key、空 ID／別名、前後空白、無效分類、空來源 key 及負數 MaxInstances；也可按 `Validate Catalog` 或呼叫 `GetValidationIssues()` 取得檢查結果。驗證不修改資料、不載入素材；來源 key 有值不代表 Provider 或建置後的 catalog 一定能找到素材，仍須驗證實際載入。來源 key 為 null 時沿用 ID，明確填空字串則停用該內建來源的 key。

為維持既有行為，同分類 ID／別名衝突仍由 Entries 中先出現者優先，不因改用索引而變更播放對象；不同分類可使用相同 key。請依 Inspector 訊息修正衝突資料。

```csharp
var catalog = UnityEngine.ScriptableObject.CreateInstance<AudioCatalog>();
catalog.Entries = new[]
{
    new AudioClipAddress("ui.result", AudioCategory.Sfx)
    {
        ResourcesKey = "minigame_win",
        AddressablesKey = "minigame_lose",
        Aliases = new[] { "result" }
    }
};
audio.Catalog = catalog;
```

Fallback 有兩個明確政策：

- `PreferAvailable`（預設）：先使用主來源已駐留的素材；其次使用備援已駐留素材或同步 Resources；仍無素材才載入主來源，失敗後再嘗試備援。
- `PrimaryThenBackup`：等待主來源結果，失敗才使用備援，不因備援已快取而提前取代主來源。

改變 Fallback 的 Policy／Configure 會通知服務，取消舊待播放請求並建立新快取世代；已播放的舊素材保留到播放結束。更換 Catalog 也會刷新；直接修改同一 Catalog 的 Entries、ID 或 Aliases 後，呼叫 `RefreshClipProvider()` 重建索引並使舊素材快取失效。新索引會先建立，再發出取消通知，回呼內的新播放會使用更新後的映射。

`Addressables.UpdateCatalogs` 成功後，呼叫 `audio.RefreshClipProvider()`，使服務及內建 Addressables Provider 一起重新解析 key。刷新會取消舊等待請求、清除定位與別名快取；舊世代已發出的 lease 持續有效，晚到的載入結果不能回填新世代。Fallback 會向支援刷新介面的主／備來源轉送一次，不遞迴發送 Changed。若要讓新舊 AssetBundle 同時使用，仍須配置相應的 Addressables 更新策略；本機更新測試使用 `UniqueBundleIds=true`，本 API 不會替專案修改建置設定。

Controller 初始化／重新啟用也會重建索引。直接使用 `AudioCatalog.TryResolve()` 時，首次查詢、Entries 陣列替換或 Unity 編輯／反序列化後會自動重建；程式直接修改既有 entry／alias 內容後須呼叫 `RebuildIndex()`。服務使用中的 Catalog 應使用上述 `RefreshClipProvider()`，才能一起處理待載入請求與快取。索引需要額外管理記憶體，重建有配置成本，適合在初始化或資料更新時執行。

`TryGetCachedClip` 是純快取查詢，不發起載入。Resources 的舊 `TryGetClip` 仍可能同步載入。`AllowAsyncLoad=false` 不等待進行中的載入：Resources 可同步取得，Addressables 只使用已駐留素材，且取得獨立持有權。

## 素材持有、預載與釋放

服務區分載入等待者、播放使用者、普通快取與預載群組。共用同一邏輯 ID 的請求只載入一次；取消某個等待者不取消其他等待者。Addressables 另外依實際位置合併 address／GUID 別名，使取得／釋放成對。

```csharp
audio.Preload(AudioCategory.Sfx, "ui.result", "level-1", loaded =>
    UnityEngine.Debug.Log($"Preload: {loaded}"));

// 在載入畫面依序初始化、預熱聲源、準備素材。
// audio 已指向場景中的 AudioController；所有呼叫在主執行緒進行。
audio.Initialize();
audio.PrewarmSources(24);
audio.PrepareClip(AudioCategory.Bgm, "music.title", "level-1", ready =>
{
    if (ready) audio.PlayBgmHandle("music.title");
});
```

離開用途群組時，再移除該群組的保留需求：

```csharp
audio.ReleaseGroup("level-1");
// 普通播放建立的快取另由此方法標記釋放；仍在使用者結束後才實際歸還。
audio.ReleaseUnusedClips();
```

`Preload` 成功只保證取得並保留 AudioClip 資產；`PrepareClip` 另外呼叫需要的 `LoadAudioData`，成功回呼時 `loadState == Loaded`。兩者均在主執行緒呼叫，完成回呼可能同步發生；背景載入由匯入設定決定。`PrepareClip` 不保證播放端到端延遲或 Streaming 全曲已解碼，也不修改音檔匯入設定。準備成本與記憶體仍存在，適合在載入畫面對即將使用的素材執行。

- `PrepareClip` 的失敗、群組取消、Provider 世代切換及 Shutdown 都回呼 false 一次；取消服務端等待不會中止 Unity 已啟動的底層載入。成功後由群組持有，普通快取／播放亦可能延長持有時間。服務不主動呼叫 `UnloadAudioData`，避免影響共用同一 clip 的外部使用者。
- 預載／Provider 回呼及 lease release 的例外會記錄並隔離，不中斷其他等待者或必要清理。Provider 自己釋放失敗的資源仍由該 Provider 負責；服務保障 handle、聲源與持有計數不因例外卡在半完成狀態。`ReleaseGroup` 只取消呼叫當時的需求，回呼中新建的需求保留。
- `ReleaseGroup` 取消該群組尚未完成的預載與資料準備，移除群組保留；不會停止使用中的聲音，也不清除其他群組／普通快取的保留。
- `ReleaseUnusedClips` 清除普通快取保留；有播放使用者或群組保留時，素材持續有效。
- 原始 `ReleaseClip` 不會使內建 Provider 的有效 lease 提前失效。Provider 更換／銷毀後，已取得的播放 lease 仍保持到最後使用者離開。
- Resources 在最後 lease／快取保留離開後移除自身引用，不強制 `UnloadAsset`，避免傷及外部持有者；實際 native 記憶體回收依 Unity 的未使用資產清理。
- 直接傳入的 AudioClip 不納入 Provider 的卸載責任。不要由外部主動銷毀仍在播放的 clip。
- 舊的全量預載旗標仍可明確開啟，但預設使用按需載入與上述指定群組預載。尚未加入 LRU／記憶體預算淘汰。

### BGM 匯入設定與載入範例

兩首範例 BGM 使用 **Streaming、Load In Background 開啟、Preload Audio Data 關閉**，保留原壓縮品質與取樣率。長音樂的資料改在播放時逐段讀取／解碼；短 SFX 與 Voice 的設定保留。`PrepareClip` 的 Loaded 表示 Unity 已可使用該 clip，不表示 Streaming 全曲已載入記憶體或無裝置輸出延遲。

選取 AudioClip 後，可使用 `Tools > Audio Service > Audio Imports`：

- `Review Selected Clips`：唯讀提示長音樂的解壓／首次載入成本與目前平台 override。
- `Apply Streaming ...`／`Apply Background Decompression ...`：明確選用 Default 匯入設定；保留壓縮格式、品質、取樣率及平台 overrides，平台設定需另外檢查。工具不會在匯入時自動修改音檔。

在 Play Mode 的 `AudioControllerDebugMenu` 使用 `Audio/Loading/Prepare and Play BGM`，可執行初始化 → 聲源預熱 → PrepareClip → 成功後播放。`Release Prepared BGM` 或停用元件會取消等待、停止此範例持有的 handle、釋放群組；離開整個場景時再按需呼叫 `ReleaseUnusedClips()` 清除普通快取保留。準備中的 key 有快照，取消後的舊回呼不會遲到播放。

## 並發、聲源池與暫停

`MaxVoices=0` 表示不設服務上限，避免任意替專案決定預算。可設定服務總上限，及 PlayOptions／Catalog 的單音效上限。計數包含 Loading 中的預約；單音效的限制以分類＋解析後 ID 計算。

並發限制會先完整判斷，再執行替換。因限制而 `Rejected` 的請求不停止舊聲音、不取消既有 Loading，也不重設 BGM／Voice 的轉場或淡出。單音效 `StealOldest` 釋出的名額可同時滿足全域限制；若仍無法符合全域 `RejectNew`，整個新請求遭拒絕。播放中調低上限本身不會停止聲音，下次允許替換的請求才依最舊順序移除所需數量，可能一次替換多個。R10 修正與驗收見改善計畫 §5.9。

```csharp
audio.MaxVoices = 24; // 範例值，請依自己的場景量測。
audio.ConcurrencyPolicy = AudioConcurrencyPolicy.StealOldest;
var handle = audio.PlaySfxHandle("ui.result", new PlayOptions
{
    MaxInstances = 2,
    ConcurrencyPolicy = AudioConcurrencyPolicy.RejectNew,
    IgnoreGamePause = true
});
```

超過上限可回傳 Rejected 或停止最舊實例。聲源歸還時清除 clip、pitch、loop、Mixer、暫停標記、播放包絡與轉換。每幀僅巡覽使用中的聲源；增益改變時才更新 volume，分類／Master 淡出仍同步更新舊 API 聲源。池降低重複建立的成本；同時混音的數量仍由並發政策與 Unity AudioSettings 分別決定。

可在主執行緒的載入流程明確預熱聲源：

```csharp
int created = audio.PrewarmSources(32);
// 動態聲源總容量（使用中＋閒置）至少達到 32；回傳這次新增的數量。
// 五個舊 API 相容聲源另外計算；重複呼叫只補不足部分。
```

未呼叫時不額外預熱。`PrewarmSources` 同步建立閒置聲源與預留播放清單容量，不播放或載入 clip、不改 `MaxVoices`；非正數或服務尚未 Ready 時回傳 0，不自動啟動服務。較小目標不縮池，播放需求超過預熱容量時仍按需擴充。預熱會提前保留物件與記憶體，動態聲源在 Shutdown 清理；若重新 Initialize，需要時再預熱。素材準備使用 `PrepareClip`，聲源預熱本身不保證首次播放延遲。

可在換場或載入階段明確縮減閒置聲源：

```csharp
int removed = audio.TrimIdleSources(minimumCapacity: 32, maxToRemove: 16);
// 每次最多移除 16 個閒置動態聲源；動態總容量不因縮池降到 32 以下。
```

`minimumCapacity` 與預熱一樣指動態總容量（使用中＋閒置），不是保留的閒置數量。只處理池中的閒置動態聲源，不停止播放、不動暫停／載入預約，也不刪除五個相容聲源。負數最低容量視為 0；非正數移除預算或服務未 Ready 回傳 0。回傳本次移出池的數量，Unity 實際 Destroy 在幀末完成；預設每次最多 32 個，不自動縮池。後續可重新預熱，或由播放按需擴充；請考量重建成本。

- `SetGamePaused(true)` 暫停一般播放；`IgnoreGamePause=true` 的 UI 聲音略過此原因。
- 外部 `AudioListener.pause` 亦列入暫停原因，在服務更新、載入完成或 handle 暫停／停止時同步；不再把 one-shot 誤判為播完。`IgnoreGamePause=true` 同時略過遊戲與 Listener 暫停。
- 個別 `handle.Pause()` 的原因獨立保存，解除遊戲／Listener 暫停不會擅自恢復個別暫停。暫停期間單次播放的淡入淡出凍結；分類／分支的共用增益淡變仍維持原有行為。
- `SetBackgroundPaused`／預設啟用的 `pauseOnBackground` 適用全部播放，包括 UI。
- `SetMuted(channel, true)` 只抑制輸出，不停播、不改 PlayerPrefs；`Time.timeScale=0` 本身不等於音訊暫停，遊戲需明確呼叫服務。
- 重複設定相同的遊戲／背景暫停或同通道靜音會直接返回；新播放、載入完成與重用聲源仍套用目前狀態。

並發檢查通過後才建立設定快照、播放資料及所需載入回呼。一般入口僅有全域限制時直接使用現有播放數量；一般單音效限制首次使用時建立分類＋ID 計數索引，包含 Playing／Paused／Loading，之後隨接納與結束更新；空場後停止維護，需要時再建立。BGM／Voice 替換分支保留排除被替換槽的完整檢查，StealOldest 仍依原先順序選取。拒絕仍回傳獨立 handle，保留 ID、原因與晚訂閱通知。效能測點與成本見改善計畫 §5.10、§5.12。

內部設定與淡變使用值資料；handle 直接定位播放資料，完成即清除內部引用，公開 handle 仍各自保留最終結果。批次控制重用快照緩衝並維持原有播放順序；首次緩衝擴張、正常播放、完成事件與使用者回呼仍可能配置。24／64／256 聲音的 CPU、配置 bytes 與持續 GC 觀察見改善計畫 §5.13。

## 音量與設定

最終音量由玩家 Master／分類設定、暫時 Master／分類增益、舊 API 分支增益、單次播放音量與播放包絡組成。Mixer 可用時玩家設定由 Mixer 套用；`SetMixer(null)` 明確使用無 Mixer 模式，由來源計算相同音量。預設會尋找 `Resources/Audio/MasterMixer`。

- Mixer 群組為 BGM／Sound／Voice，exposed parameter 為 `masterVolume`／`bgmVolume`／`soundVolume`／`voiceVolume`。`ValidateMixer()` 與 `LastMixerIssue` 提供基本診斷；缺失的路由／參數使用來源增益備援。
- `SetMasterVolume`／`SetBgmVolume`／`SetSfxVolume`／`SetVoiceVolume` 經已綁定 handler 同步儲存與事件。Bootstrap 的 `ApplyVolume(..., persist:false)` 可暫時調整而不寫回設定。
- 同值設定省略音訊套用，但仍可把先前的暫時音量存入 handler；`VolumeChanged` 僅在有效數值改變時通知。Mixer 更新只寫入改變的通道；有聲源備援增益變化時才更新聲源。初始化及 `SetMixer(...)` 仍會完整套用四個通道。
- `FadeBus` 涵蓋該分類所有新舊實例；`FadeChannel(Sfx/Voice, ..., useLoopSource)` 保留舊 API 的循環／非循環分支，只影響經舊入口建立的聲音。
- `FadeChannel(Master)`／`FadeBus(Master)` 涵蓋全部分類。停止淡出只作用於呼叫當時的播放，後續新播放不受舊停止流程控制。
- 非停止淡出的分類／分支增益持續有效。停止或重播只重設播放包絡，不覆寫玩家設定，也不清除刻意設定的分類衰減。
- `AudioSettingsPlayerPrefs.ResetSettings()` 刪除實際配置的 key，重載記憶體預設值，並廣播四個音量；DebugMenu 使用此入口。
- **儲存相容性調整**：原 `saveImmediately=true` 現在表示自動延後合併儲存，預設閒置 0.25 秒後 `Save`，避免每次滑桿變動都同步寫磁碟。可明確 `Flush()`；停用或進入背景亦會 Flush。
- Settings Inspector 將 `saveImmediately` 顯示為 **Auto Save**，`saveDelaySeconds` 顯示為 **Save Delay (seconds)**；序列化欄位名稱、預設值與儲存行為均保留。Default volumes／Storage keys 在 Play Mode 期間唯讀，已有儲存值時不會因調整 default 自動重設。
- API 音量 clamp 到 0～1；NaN／Infinity 取 0。損壞的已存音量使用正規化預設值。時長負值／非有限值取 0；pitch clamp 到 0.01～3，非有限值取 1；dB 最低 -80。

## 集中檢查與唯讀狀態

AudioController Inspector 的 **Audio checks** 檢查已載入場景中有效 AudioListener 的數量、Controller 啟用與根物件位置，以及目前 Mixer 缺少的群組／exposed parameters。Play Mode 另列出 Listener 音量／暫停、遊戲／背景暫停、各分類及 Master 的零音量、靜音與 FadeBus 歸零。刻意的輸出控制以資訊提示呈現；提示列出對應的恢復方法，由呼叫端決定是否恢復。

檢查僅在 Editor Inspector 存活時每 0.5 秒執行，也可按鈕立即更新；不在播放熱路徑掃描場景，不載入音檔、不初始化服務、不修改 Mixer 或玩家設定。Prefab 資產及 Prefab Mode 不進行場景 Listener 檢查。沒有發現問題不代表音訊一定可聽見；音檔內容、單次播放／相容 API 分支衰減、Mixer 效果與外部音訊輸出仍需另外檢查。

需要程式化讀取時，可使用 `AudioController.GetChannelDiagnostics(channel)` 取得該通道的 `Volume`、`FadeGain`、`Muted`；這些是分開的服務控制值，尚未合併 Master、Listener、Mixer 或單次播放增益，也不代表實際分貝或輸出波形。`Diagnostics.GamePaused`／`BackgroundPaused` 提供服務的暫停原因；呼叫前確認 `Ready`，唯讀查詢不會恢復播放或改變狀態。

Inspector 的 **Max Concurrent Sounds** 對應既有 `maxVoices`／`MaxVoices`：含 BGM、SFX、Voice 的 Playing／Paused／Loading 請求總數。僅更改顯示名稱，並發政策與序列化相容性不變。

## 生命週期與自訂 Provider

Controller 提供 `Initialize()`、`Ready`、`Shutdown()`。Shutdown 回呼期間不接受重新初始化；需等 Shutdown 返回後再 Initialize。停用元件或 GameObject 會停止播放、取消舊載入需求並解除訂閱；重新啟用會重新初始化，需重新發出播放。重複 Prefab、一般／additive 場景及關閉 Domain Reload 的重入已有本機測試。

收到 `AudioSettings.OnAudioConfigurationChanged`（包含程式 Reset 或裝置變更通知）時，服務採以下政策：

- 已 Playing／Paused 的 handle 結束為 **Failed**；尚在 Loading 的請求結束為 **Cancelled**，原因為 `Audio system configuration changed`，完成通知各一次。
- 取消尚未完成的預載／資料準備，回呼 false；清除舊服務快取及群組保留，停止舊淡變。需要的素材群組應重新預載／準備。
- 重套 Mixer 路由與音量，保留玩家設定、遊戲／背景暫停、靜音及目前分類／分支增益；不自動續播 BGM。個別 handle 已結束，其個別暫停不移轉到新 handle。
- 呼叫端可在完成通知中重新播放。若傳入腳本建立且已被重設失效的 AudioClip，須先由擁有者重建素材。

Bootstrap 以自身作為全域 Provider 註冊擁有者；舊擁有者退訂不會清除後來的註冊。自訂整合可使用 `RegisterClipProvider(provider, owner)`／`UnregisterClipProvider(owner)`。

既有 `IAudioClipProvider`、`IAsyncAudioClipProvider`、`IResultAudioClipProvider` 均保留。需要明確安全持有時，建議實作：

| 附加介面 | 契約 |
| --- | --- |
| `IAudioClipCache` | `TryGetCachedClip` 不觸發載入 |
| `IAudioClipLeaseProvider` | `AcquireClip(address, callback)` 正常完成恰好回呼一次；失敗 null；成功 lease 到 Dispose 前保持素材有效 |
| `IAudioSynchronousClipProvider` | 明確的同步取得，回傳 lease；不能偷偷等待非同步結果 |
| `IAudioClipProviderChanges` | Provider 政策／映射變更時發送 Changed，使服務取消舊需求及更新快取世代 |
| `IAudioClipProviderRefresh` | 可選的 `RefreshClipLookup()`：在主執行緒使定位快取失效，取消舊取得請求，先公布新查找狀態再通知；保留已發出 lease，方法內不得再發送 Changed |

舊 Provider 的 coroutine／巢狀 enumerator 例外會轉為載入失敗。未提供 lease 契約的外部 Provider，仍需自己協調服務以外的使用者與 ReleaseClip，服務無法替未知外部持有者計數。正式程式不引用本機測試或工具。

## 診斷與驗證

`Diagnostics` 提供 Playing、Paused、Loading、PooledSources、CreatedSources、CachedClips、ClipUsers、PendingLoads、PreparingClips 與 LastFailure。服務 Ready 時，`CreatedSources` 是目前持有的聲源數（含五個相容聲源），不是累計建立次數；縮池後會下降。`PreparingClips` 包含等待資產或音訊資料的準備需求，`PendingLoads` 僅計服務資產載入。快取統計指目前 Provider 世代的服務快取；切換後仍在播放的舊 lease 會保持有效，但不列入新世代快取統計。Addressables Provider 的 `CachedOperationCount` 也只計目前世代，舊 lease 仍可能持有舊 native operation。`verboseLogging` 可開關額外日誌。

原第二階段驗收見 [改善計畫 §5.4](../unity-audio-service-improvement-plan.md#54-第二階段實作與驗收紀錄)，後續修正與效能前後量測見同文件 §5.5；音量去重、Catalog 索引／驗證與 2026-09-30 驗收見 §5.7，R11～R15、計數索引／縮池見 §5.12，本輪 BGM／配置／批次控制及 Metal Player 驗收見 §5.13，相容性變更見 [CHANGELOG](../CHANGELOG.md)。本機測試位於受忽略的 `Assets/AudioService/Tests/`；重跑工具在 `Tools/AudioService/`，XML／Profiler 操作紀錄在 `work/stage-two/`、`work/stage-two-hardening/`、`work/core-optimization-20260930/`、`work/reliability-repair-20260930/` 與 `work/performance-refinement-20260930/`，均不隨 Git 發布。
