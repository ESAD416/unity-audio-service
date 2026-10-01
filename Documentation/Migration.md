# 2026-10-01 重構遷移指南

本輪依使用者核准移除舊相容模型，屬於破壞性變更。`AudioService` 的 BGM 替換、對話替換、獨立音效及 handle 契約維持；舊 Controller 包裝與 Provider 需遷移。以下不提供長期相容層。

## 播放與控制

| 舊入口／行為 | 新入口／處理方式 |
| --- | --- |
| `controller.PlayBgm(...)`／`PlayBgmHandle(...)` | `AudioService.PlayBgm(idOrClip, options)`，回傳 handle |
| `controller.PlaySfx(...)`／`PlaySfxHandle(...)` | `AudioService.PlaySfx(idOrClip, options)`，每次獨立播放 |
| `controller.PlayVoice(...)` | `AudioService.PlayVoice(idOrClip, options)`；Loop 不再選擇另一個替換槽 |
| `controller.PlayVoiceHandle(...)` | `AudioService.PlayVoice(id, options, VoicePlaybackMode.Overlap)`，必須明確保留原並發意圖 |
| `loop`／`allowAsyncLoad`／`fadeInSeconds` 等位置參數 | 移入 `PlayOptions` 的具名欄位 |
| `StopSfx/StopVoice(stopLoopOnly: true)` | 保存所擁有的循環 handle，再呼叫 `handle.Stop(seconds)` |
| `StopSfx/StopVoice(false, seconds)` | `AudioService.StopSfx/StopVoice(seconds)`，停止該分類當時全部請求 |
| `FadeChannel`／`FadeSfxLoopTo`／`FadeVoiceLoopTo` | 沒有相同的舊分支概念；整類音量改用 `FadeBus`，停止個別聲音用 handle |
| `FadeBgmOut(seconds)` | `AudioService.StopBgm(seconds)` |
| `FadeBgmTo(value, seconds)` | 需要整類衰減時改用 `FadeBus(AudioChannel.Bgm, value, seconds)` |

不要機械式將所有 `FadeChannel` 改名為 `FadeBus`：後者涵蓋整類聲音，且不會取消 BGM 的等待替換或播放包絡。舊版任意分支的持續淡變不再支援；先釐清呼叫端要控制「一個聲音」還是「整個分類」。單次初始音量可用 `PlayOptions.Volume`，單次淡入用 `FadeInSeconds`。

SFX 循環不再自動替換上一個循環。需要風聲等單一擁有者時，由該物件保存 handle，在換聲音時停止舊 handle。對話只有一個替換槽；背景人聲改用 `Overlap`。

`AudioController.Play(category, idOrClip, options)` 保留為明確的獨立播放入口，不使用 BGM／對話替換槽。日常程式仍優先使用 `AudioService`。

## 場景與 Prefab

- 移除自訂 Prefab／場景上的 `AudioBootstrap` 元件，在 AudioController 指定 **Clip Provider Source** 與 **Settings Source**。不再有全域註冊、Bootstrap override 或依賴搜尋。
- 原始 AudioCtrl Prefab 的 GUID、Controller 與設定元件引用保持不變，內部 Bootstrap 已改為純組織用 Dependencies 物件；現在預設只連接 Resources Provider。
- 需要 Resources＋Addressables 時，使用 `Integrations/Addressables/AudioCtrlAddressables.prefab`，或手動配置 Fallback 的主／備 Provider。Fallback 不再自動建立元件。
- 自訂 `RegisterClipProvider`／`UnregisterClipProvider` 改由宿主明確 `controller.SetClipProvider(provider)`；設定採 `BindSettings`。停用後重新啟用沿用該實例的明確配置。
- 不要依賴名為 BGM／Sound／Sound_Loop／Voice／Voice_Loop 的固定 AudioSource 子物件；所有來源都按需建立並共用動態池。
- 自訂 UnityEvent 若綁定已移除的 Controller 播放方法，須重新綁定自己的 void adapter；可參考 Samples 的 AudioQuickStart。靜態 AudioService 不能直接取代 Inspector 裡的舊事件目標。
- `PrewarmSources`／`TrimIdleSources`／`Diagnostics.CreatedSources` 現在計算全部聲源，不再另加五個相容聲源。Loading 預約算並發名額，但不占用 AudioSource。
- DebugMenu 已移至 Samples 並保留腳本 GUID；自訂 assembly 若引用此類型，需參照 `Controller.Audio.Samples`。

## 自訂 Provider

原本 `IAudioClipProvider`、`IAsyncAudioClipProvider`、`IResultAudioClipProvider`、`IAudioClipCache`、`IAudioClipLeaseProvider`、`IAudioSynchronousClipProvider` 收斂成單一必要介面：

```csharp
public interface IAudioClipProvider
{
    bool TryGetCachedClip(ResolvedAudioClip address, out AudioClip clip);
    bool TryAcquireClip(ResolvedAudioClip address, out AudioClipLease lease);
    void AcquireClip(ResolvedAudioClip address, Action<AudioClipLease> completed);
}
```

查快取不得載入；同步取得不得等待 async。每次成功取得都有自己的 lease，直到 Dispose 前素材必須有效；callback 在主執行緒恰好完成一次，失敗傳 null。將 coroutine 轉成 Provider 內部實作即可，不再由核心辨識多種載入協定。沒有同步能力時 `TryAcquireClip` 回傳 false，lease 為 null。

Resources 舊 `GetClip`／`TryGetClip` 改為 `TryAcquireClip`，取得者保存並釋放 lease。Addressables 的 `LoadClipAsync`／`ReleaseClip`／`preloadOnAwake` 已移除：一般播放由 Store 持有；預載使用 Controller 的具名群組；直接使用 Provider 時由呼叫端管理 lease。不再以 alias 快取代替所有權。

`IAudioClipProviderRefresh`、`IAudioClipProviderChanges` 保留為可選能力；診斷可實作 `IAudioClipProviderDiagnostics`，核心不再依具體 Provider 型別決定載入或描述方式。Addressables 相關型別移入 `Controller.Audio.Addressables`，使用者 assembly 須明確增加該參照。

## Catalog

`Entries` 公開陣列移除，使用 `GetEntriesCopy()` 取得編輯副本，再以 `ReplaceEntries(...)` 一次發布。輸入、aliases 及讀回副本都不共享內部可變資料；`TryResolve` 回傳不可變的 `ResolvedAudioClip` 值。

```csharp
var entries = catalog.GetEntriesCopy();
entries[0].ResourcesKey = "UI/confirm";
catalog.ReplaceEntries(entries);
```

替換會先建立新索引，再通知 Controller 取消舊 Loading／準備與快取查找；正在播放的 lease 不提前釋放。不再公開 `RebuildIndex()`，也不需要在 ReplaceEntries 後手動刷新。

Inspector 與 Unity 資產的舊 `Entries` 欄位透過 `FormerlySerializedAs` 遷移。自訂 JSON 若使用 Unity JsonUtility，欄位需改為 `entries`；外部資料匯入更適合解析成 authoring 資料後呼叫 ReplaceEntries，避免依賴私有序列化欄位。來源 key 的 null 沿用 ID、空字串停用來源、分類／大小寫／alias 首筆優先規則保持。
