# Unity Audio Service

以簡單、好用、操作直覺為目標的 2D 音訊服務。日常程式使用 `Controller.Audio.AudioService`：播放 BGM、音效與語音，取得 handle 控制單一聲音，或調整玩家音量。

後續修正、重構、優化、擴充與套件化均遵循 [固定易用性準則](unity-audio-service-improvement-plan.md#21-後續修改的固定易用性準則) 與 [固定程式碼維護準則](unity-audio-service-improvement-plan.md#22-後續修改的固定程式碼維護準則)：保持簡單、好用、操作直覺，並使程式碼盡可能精簡優雅，移除或優化可能造成技術債的程式碼。

```csharp
using Controller.Audio;

AudioService.PlayBgm("ukulele_song");
AudioService.PlaySfx("minigame_win");
var voice = AudioService.PlayVoice("minigame_drink");
// 保留 voice，之後可在停止按鈕中呼叫 voice.Stop()。
```

以上呼叫放在 **Start 或之後**，並先在場景放入下方的 AudioCtrl Prefab。一般播放不需要手動初始化、預熱或預載。

## 先試一次

1. 開啟 [AudioQuickStart 場景](Assets/AudioService/Samples/AudioQuickStart.unity)，按 Play。
2. 在 Game 視窗按 Play sound、Play music 或 Play voice；重複播放語音會替換上一句，音效則各自播放。
3. 選取 `Quick Start Controls`，把自己的 AudioClip 拖進 Music／Sound／Voice 欄位，再按對應按鈕。
4. 用停止按鈕確認只停止該範例持有的聲音；Master 音量滑桿會保存設定。

場景已放好 AudioCtrl 與 AudioListener。[AudioQuickStart.cs](Assets/AudioService/Samples/AudioQuickStart.cs) 也提供可綁定 Unity UI Button／Slider 的 void 方法。

如果重新播放失敗或被上限拒絕，範例仍保留原本聲音的控制權；停止按鈕與離開範例場景會清理自己持有的音樂與語音，不影響其他物件後來播放的音樂或語音。

## 在本專案的另一個場景播放自己的音效

1. 把 [AudioCtrl.prefab](Assets/AudioService/Runtime/Prefabs/AudioCtrl.prefab) 拖到場景根層。Prefab 已連接素材來源與音量設定，會跨場景保留。
2. 確認場景有一個啟用的 AudioListener，例如主攝影機上的元件。
3. 把 `click.wav` 放進 `Assets/Resources/Audio/SFX/`。
4. 建立 `FirstSound.cs`，貼上下方程式並掛到場景物件，按 Play。

```csharp
using Controller.Audio;
using UnityEngine;

public class FirstSound : MonoBehaviour
{
    private void Start()
    {
        AudioService.PlaySfx("click");
    }
}
```

預設 Resources 對應如下；key 不含副檔名，保留大小寫。子資料夾也要放進 key，例如 `UI/click`。

| 類別 | 音檔位置範例 | 播放呼叫 |
| --- | --- | --- |
| 音樂 | `Assets/Resources/Audio/BGM/title.mp3` | `AudioService.PlayBgm("title")` |
| 音效 | `Assets/Resources/Audio/SFX/click.wav` | `AudioService.PlaySfx("click")` |
| 語音 | `Assets/Resources/Audio/Voice/line_1.wav` | `AudioService.PlayVoice("line_1")` |

基本使用不必建立 Catalog。也可直接傳入 Inspector 指定的 `AudioClip`，例如 `AudioService.PlaySfx(myClip)`；服務不會卸載外部持有的 clip。

## 播放規則只有一套

所有 `AudioService.Play*` 方法都回傳 `AudioHandle`。不需要控制時忽略回傳值；要暫停、停止或接收結果時才保存它。是否保存 handle 不影響播放規則。

| 方法 | 預設行為 |
| --- | --- |
| `PlayBgm` | 循環；成功的新音樂替換 BGM 槽中的舊音樂 |
| `PlaySfx` | 獨立播放；指定 Loop 後仍是獨立實例 |
| `PlayVoice` | 播放一次；成功的新語音替換對話槽中的舊語音，Loop 不改變替換槽 |
| `PlayVoice(..., mode: VoicePlaybackMode.Overlap)` | 明確允許獨立語音；替換對話槽不會停止這些語音 |

找不到素材、無效請求或上限拒絕不會先停止正在播放的聲音。對話槽同時有新的待載入請求時，舊等待請求會被取消；已播放的對話會等新素材成功才被替換。

```csharp
var voice = AudioService.PlayVoice("line_1");
// 在暫停按鈕中：voice.Pause();
// 在繼續按鈕中：voice.Resume();
// 在停止按鈕中：voice.Stop(0.2f); // 只停止這一次播放，淡出 0.2 秒。

AudioService.PlayVoice("crowd", mode: VoicePlaybackMode.Overlap);

var wind = AudioService.PlaySfx("wind", new PlayOptions
{
    Loop = true,
    Volume = 0.4f
});
// 不再需要這個循環時：wind.Stop();
```

`PlayOptions` 是額外需求才使用的設定。BGM 轉場可設定 `FadeOutSeconds` 與 `FadeInSeconds`，目前為先淡出再淡入；停止單次播放的時長傳給 `handle.Stop(seconds)`。

## 停止、音量與淡出的範圍

| 目的 | 呼叫 |
| --- | --- |
| 停止這一個聲音 | `handle.Stop(0.5f)` |
| 停止所有 BGM／SFX／Voice | `AudioService.StopBgm()`／`StopSfx()`／`StopVoice()`；可傳淡出秒數 |
| 修改玩家音量並保存 | `AudioService.SetMasterVolume(0.8f)`；另有 `SetBgmVolume`、`SetSfxVolume`、`SetVoiceVolume` |
| 暫時淡出整個音效分類 | `AudioService.FadeBus(AudioChannel.Sfx, 0f, 0.5f)` |
| 恢復分類的暫時增益 | `AudioService.FadeBus(AudioChannel.Sfx, 1f, 0.5f)` |
| 暫時靜音，不停止播放 | `AudioService.SetMuted(AudioChannel.Master, true)` |
| 遊戲暫停／繼續 | `AudioService.SetGamePaused(true)`／`SetGamePaused(false)` |

音量使用 0～1。`FadeBus` 涵蓋全部同類聲音，不改玩家保存的音量；淡變後的增益也套用到後續新聲音，需恢復至 1 才解除分類衰減。單純停止一批聲音請用 `StopSfx(seconds)` 等方法。

`Time.timeScale = 0` 不會自動呼叫音訊暫停。暫停時仍需播放的 UI 音效，可在 `PlayOptions` 設 `IgnoreGamePause = true`。

## 沒有聲音時

先選取場景中的 **AudioCtrl → AudioController → Audio checks**。這裡會集中提示缺少／重複的有效 AudioListener、Mixer 配置問題；Play Mode 時也會顯示靜音、零音量、FadeBus 歸零與暫停的原因及恢復方式。提示會自動更新，也可按 `Refresh audio checks`。檢查不會自動修改設定；靜音與暫停可能是你刻意的操作。

`Ready` 只表示服務已初始化。檢查不涵蓋音檔是否本身無聲、Mixer 效果、作業系統音量或喇叭輸出；Prefab 資產本身也不必放 AudioListener，場景才需要。

播放失敗時再看 Console。Editor 與 Development Build 預設顯示播放失敗的類別、ID、原因，以及內建 Provider **目前設定的查找 key／Resources 路徑**。這是設定診斷，不代表每個來源的實際查詢順序；自訂 Provider 會提示查它自己的診斷。

- 缺少 Controller：放入並啟用 AudioCtrl，從 Start 或之後呼叫。
- 找不到素材：檢查類別資料夾、key 大小寫與副檔名；有 Catalog 時檢查映射。
- 播放成功但聽不到：檢查 AudioListener、Master／分類音量、靜音、暫停，以及先前是否把 FadeBus 降到 0。

同一設定下，相同失敗只提示一次，最多記錄 64 種，再顯示一次抑制提示。重新啟用 Controller、更換／刷新 Provider 後重設。Controller Inspector 可關閉 `Log Playback Failures`；一般並發拒絕不自動警告。

需要在程式處理結果時：

```csharp
var sound = AudioService.PlaySfx("click");
sound.Completed += h =>
{
    if (h.Result == AudioCompletion.Failed || h.Result == AudioCompletion.Rejected)
        Debug.LogWarning(h.FailureReason);
};
```

結束後訂閱仍會收到結果。缺少或停用 Controller 時，播放入口也會回傳 Failed handle，便於處理；服務不會默默建立場景物件。Controller Inspector 顯示 Ready、播放數與最近一次歷史失敗。

Inspector 的 `Max Concurrent Sounds` 是 BGM、SFX、Voice 合計的並發請求上限，包含載入中與暫停，`0` 表示服務不設上限。音量設定元件的 `Auto Save` 會在最後一次音量變動後等待 `Save Delay` 再儲存，聲音音量立即生效；停用元件或進入背景仍會保存待寫入設定。這兩項顯示名稱調整不改序列化欄位；本輪依賴配置等其他變更仍須依遷移指南調整自訂 Prefab。

## 需要時再使用

- **載入畫面與換場**：[完整場景素材範例](Documentation/SceneAudio.md)，包含準備、取消、停止自己持有的聲音、釋放保留，以及跨場景 BGM 的歸屬。
- **Catalog、自訂來源、快取、預熱與並發限制**：[進階 API 參考](Documentation/AudioServiceReference.md)。
- **Addressables**：安裝 Addressables 後使用 [AudioCtrlAddressables.prefab](Assets/AudioService/Integrations/Addressables/AudioCtrlAddressables.prefab)。基本 AudioCtrl 僅使用 Resources；adapter 位於獨立 assembly。
- **既有程式升級**：[重構遷移指南](Documentation/Migration.md)。舊 Controller 播放包裝、分支淡變、Bootstrap 與舊 Provider 介面已移除；`AudioService` 的播放規則保持一致。

## 專案狀態

目前是 Unity 專案，尚未完成 UPM 套件交付。驗證環境為 Unity 6000.6.3f1、Addressables 2.11.2、Test Framework 1.8.0。核心不再引用 Addressables；可選整合位於 `Integrations/Addressables`。範例不代表已驗證其他版本或平台。

本輪後續精簡：PlayMode 326／326、Editor／Reload 26／26、macOS Player 無圖形／批次 Metal 各 250／250 通過。未安裝 Addressables 的獨立專案亦重新完成 Player 建置與播放／PrepareClip 驗證；詳見改善計畫 §5.20。視窗畫面、人工操作與聽感仍未驗收，也尚未進行新使用者操作研究。

目前已收斂通知例外隔離、同步／取消載入路徑，並將 PrepareClip 與共用 Mixer 定義分離成內部職責；公開播放／準備 API 不變。指定量測中，Fallback 同步命中每次配置 448 → 192 bytes，全部取消的 Store 交付不再建立暫存陣列；暖快取播放仍為每次 192 bytes。先前 Catalog／來源刷新／診斷與別名釋放優化保留，但並非所有 CPU／配置測點都更低，歷次量測及取捨見改善計畫 §5.17～5.20。

可靠性、效能與本輪使用流程驗證見[改善計畫](unity-audio-service-improvement-plan.md)，相容性紀錄見 [CHANGELOG](CHANGELOG.md)。入門場景和範例會隨專案提供；本機測試及工具依既有規則留在受 Git 忽略的 Tests／Tools／work。
