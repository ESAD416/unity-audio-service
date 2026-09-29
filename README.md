# Unity Audio Service

2D 音訊服務，提供 BGM／SFX／Voice、獨立播放 handle、可重用聲源、非同步載入、素材持有與音量設定。第一、第二階段已實作；UPM 套件分離仍屬第三階段。

## 目前驗證環境

- Unity `6000.6.3f1`、Addressables `2.11.2`、Unity Test Framework `1.8.0`。
- Runtime assembly：`Controller.Audio`，目前仍依賴 `Unity.Addressables`／`Unity.ResourceManager`。尚未支援不安裝 Addressables 的獨立核心套件。
- 已驗證本機 Editor／PlayMode、原始 Prefab／場景、Addressables Editor 資產模式及無 Domain Reload 的 Play Mode 重入。
- Addressables content build、Player、其他 Unity 版本與平台尚未驗證。不得將 Editor 通過視為已完成部署驗證。

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
- 終態立即更新；`Completed` 在本次核心狀態變更完成後於主執行緒派送，回呼建立的播放視為新請求。巢狀完成通知依序派送，避免改寫尚未完成的內部轉換。
- `Completed` 每個訂閱在結束時通知一次。結束後新訂閱會立即收到保留的結果；回呼拋出例外會記錄且不阻止其他訂閱。
- 結束後 `IsValid` 為 false，控制方法回傳 false；舊 handle 無法控制池中重用的聲源，也不公開 AudioSource。
- Loading 中 Pause 會記住暫停需求，載入完成後直接保持 Paused。暫停中的 Stop 立即完成，即使有淡出時長；暫停不會被當成自然結束。
- 空 ID／null clip／無效分類回傳 Failed，不取代現有播放。失敗或上限拒絕不需等下一幀才可訂閱結果。

## 音效目錄與來源政策

`AudioId` 是區分類別、大小寫敏感的邏輯識別字。建議使用 `ui.confirm`、`music.menu` 等名稱；未設定目錄時，既有字串 key 照常使用。

建立 `Assets > Create > Audio Service > Catalog` 資產，填入 Entries，並指定 Controller 的 Catalog。每筆資料包含 Id、Category、ResourcesKey、AddressablesKey、Aliases 與 MaxInstances。同一類別的別名應唯一，不同素材不要共用同一 ID。

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

改變 Fallback 的 Policy／Configure 會通知服務，取消舊待播放請求並建立新快取世代；已播放的舊素材保留到播放結束。更換 Catalog 也會刷新；直接修改同一 Catalog 的 Entries 後，呼叫 `RefreshClipProvider()` 使舊解析快取失效。

`TryGetCachedClip` 是純快取查詢，不發起載入。Resources 的舊 `TryGetClip` 仍可能同步載入。`AllowAsyncLoad=false` 不等待進行中的載入：Resources 可同步取得，Addressables 只使用已駐留素材，且取得獨立持有權。

## 素材持有、預載與釋放

服務區分載入等待者、播放使用者、普通快取與預載群組。共用同一邏輯 ID 的請求只載入一次；取消某個等待者不取消其他等待者。Addressables 另外依實際位置合併 address／GUID 別名，使取得／釋放成對。

```csharp
audio.Preload(AudioCategory.Sfx, "ui.result", "level-1", loaded =>
    UnityEngine.Debug.Log($"Preload: {loaded}"));

// 在載入畫面準備實際音訊資料；可能依匯入設定同步解碼。
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
- 預載／Provider 回呼的例外會記錄並隔離，不中斷其他等待者。`ReleaseGroup` 只取消呼叫當時的需求，回呼中新建的需求保留。
- `ReleaseGroup` 取消該群組尚未完成的預載與資料準備，移除群組保留；不會停止使用中的聲音，也不清除其他群組／普通快取的保留。
- `ReleaseUnusedClips` 清除普通快取保留；有播放使用者或群組保留時，素材持續有效。
- 原始 `ReleaseClip` 不會使內建 Provider 的有效 lease 提前失效。Provider 更換／銷毀後，已取得的播放 lease 仍保持到最後使用者離開。
- Resources 在最後 lease／快取保留離開後移除自身引用，不強制 `UnloadAsset`，避免傷及外部持有者；實際 native 記憶體回收依 Unity 的未使用資產清理。
- 直接傳入的 AudioClip 不納入 Provider 的卸載責任。不要由外部主動銷毀仍在播放的 clip。
- 舊的全量預載旗標仍可明確開啟，但預設使用按需載入與上述指定群組預載。尚未加入 LRU／記憶體預算淘汰。

## 並發、聲源池與暫停

`MaxVoices=0` 表示不設服務上限，避免任意替專案決定預算。可設定服務總上限，及 PlayOptions／Catalog 的單音效上限。計數包含 Loading 中的預約；單音效的限制以分類＋解析後 ID 計算。

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

- `SetGamePaused(true)` 暫停一般播放；`IgnoreGamePause=true` 的 UI 聲音略過此原因。
- 個別 `handle.Pause()` 的原因獨立保存，解除遊戲暫停不會擅自恢復個別暫停。
- `SetBackgroundPaused`／預設啟用的 `pauseOnBackground` 適用全部播放，包括 UI。
- `SetMuted(channel, true)` 只抑制輸出，不停播、不改 PlayerPrefs；`Time.timeScale=0` 本身不等於音訊暫停，遊戲需明確呼叫服務。

## 音量與設定

最終音量由玩家 Master／分類設定、暫時 Master／分類增益、舊 API 分支增益、單次播放音量與播放包絡組成。Mixer 可用時玩家設定由 Mixer 套用；`SetMixer(null)` 明確使用無 Mixer 模式，由來源計算相同音量。預設會尋找 `Resources/Audio/MasterMixer`。

- Mixer 群組為 BGM／Sound／Voice，exposed parameter 為 `masterVolume`／`bgmVolume`／`soundVolume`／`voiceVolume`。`ValidateMixer()` 與 `LastMixerIssue` 提供基本診斷；缺失的路由／參數使用來源增益備援。
- `SetMasterVolume`／`SetBgmVolume`／`SetSfxVolume`／`SetVoiceVolume` 經已綁定 handler 同步儲存與事件。Bootstrap 的 `ApplyVolume(..., persist:false)` 可暫時調整而不寫回設定。
- `FadeBus` 涵蓋該分類所有新舊實例；`FadeChannel(Sfx/Voice, ..., useLoopSource)` 保留舊 API 的循環／非循環分支，只影響經舊入口建立的聲音。
- `FadeChannel(Master)`／`FadeBus(Master)` 涵蓋全部分類。停止淡出只作用於呼叫當時的播放，後續新播放不受舊停止流程控制。
- 非停止淡出的分類／分支增益持續有效。停止或重播只重設播放包絡，不覆寫玩家設定，也不清除刻意設定的分類衰減。
- `AudioSettingsPlayerPrefs.ResetSettings()` 刪除實際配置的 key，重載記憶體預設值，並廣播四個音量；DebugMenu 使用此入口。
- **儲存相容性調整**：原 `saveImmediately=true` 現在表示自動延後合併儲存，預設閒置 0.25 秒後 `Save`，避免每次滑桿變動都同步寫磁碟。可明確 `Flush()`；停用或進入背景亦會 Flush。
- API 音量 clamp 到 0～1；NaN／Infinity 取 0。損壞的已存音量使用正規化預設值。時長負值／非有限值取 0；pitch clamp 到 0.01～3，非有限值取 1；dB 最低 -80。

## 生命週期與自訂 Provider

Controller 提供 `Initialize()`、`Ready`、`Shutdown()`。Shutdown 回呼期間不接受重新初始化；需等 Shutdown 返回後再 Initialize。停用元件或 GameObject 會停止播放、取消舊載入需求並解除訂閱；重新啟用會重新初始化，需重新發出播放。重複 Prefab、一般／additive 場景及關閉 Domain Reload 的重入已有本機測試。

Bootstrap 以自身作為全域 Provider 註冊擁有者；舊擁有者退訂不會清除後來的註冊。自訂整合可使用 `RegisterClipProvider(provider, owner)`／`UnregisterClipProvider(owner)`。

既有 `IAudioClipProvider`、`IAsyncAudioClipProvider`、`IResultAudioClipProvider` 均保留。需要明確安全持有時，建議實作：

| 附加介面 | 契約 |
| --- | --- |
| `IAudioClipCache` | `TryGetCachedClip` 不觸發載入 |
| `IAudioClipLeaseProvider` | `AcquireClip(address, callback)` 正常完成恰好回呼一次；失敗 null；成功 lease 到 Dispose 前保持素材有效 |
| `IAudioSynchronousClipProvider` | 明確的同步取得，回傳 lease；不能偷偷等待非同步結果 |
| `IAudioClipProviderChanges` | Provider 政策／映射變更時發送 Changed，使服務取消舊需求及更新快取世代 |

舊 Provider 的 coroutine／巢狀 enumerator 例外會轉為載入失敗。未提供 lease 契約的外部 Provider，仍需自己協調服務以外的使用者與 ReleaseClip，服務無法替未知外部持有者計數。正式程式不引用本機測試或工具。

## 診斷與驗證

`Diagnostics` 提供 Playing、Paused、Loading、PooledSources、CreatedSources、CachedClips、ClipUsers、PendingLoads、PreparingClips 與 LastFailure。`PreparingClips` 包含等待資產或音訊資料的準備需求，`PendingLoads` 僅計服務資產載入。快取統計指目前 Provider 世代的服務快取；切換後仍在播放的舊 lease 會保持有效，但不列入新世代快取統計。`verboseLogging` 可開關額外日誌。

原第二階段驗收見 [改善計畫 §5.4](unity-audio-service-improvement-plan.md#54-第二階段實作與驗收紀錄)，後續修正與效能前後量測見同文件 §5.5，相容性變更見 [CHANGELOG](CHANGELOG.md)。本機測試位於受忽略的 `Assets/AudioService/Tests/`；重跑工具在 `Tools/AudioService/`，XML／Profiler 操作紀錄在 `work/stage-two/` 與 `work/stage-two-hardening/`，均不隨 Git 發布。
