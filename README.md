# Unity Audio Service Module 指南

面向整合、開發與維護本音訊模組的工程師。內容涵蓋功能概覽、需求條件、使用流程、自訂擴充方式、預載建議與實務注意事項。

---

## 功能總覽

- **核心控制**：`AudioController` 管理 BGM / SFX / Voice 三類聲音播放、淡入淡出、切換、音量控制，支援 key-based 播放與非同步載入。
- **提供者架構**：透過 `IAudioClipProvider` / `IAsyncAudioClipProvider` 抽象音訊來源，內建 Resources、Addressables、Fallback 雙來源等實作。
- **設定持久化**：`IAudioSettingsHandler` 定義音量保存介面；預設 `AudioSettingsPlayerPrefs` 實作 PlayerPrefs 儲存與同步。
- **啟動整合**：`AudioBootstrap` 協助在場景中連接控制器、clip provider 與設定 handler，可選擇自動套用與廣播既有音量。
- **除錯工具**：`AudioControllerDebugMenu` 透過 Unity `ContextMenu` 提供假資料播放、音量調整、清除偏好設定等測試操作。

---

## 系統需求與相容性

- **目前驗證環境**：Unity `6000.6.3f1`、Addressables `2.11.2`、Unity Test Framework `1.8.0`。第一階段已完成 Editor／PlayMode 播放驗證；Player 建置、Addressables content build 與其他 Unity 版本尚未驗證。
- **腳本 assembly**：Runtime 使用 `Controller.Audio.asmdef`，明確引用 `Unity.Addressables` 與 `Unity.ResourceManager`；現有腳本 GUID 保留。原始場景與 Prefab 已通過播放及引用檢查。
- **目前依賴**：此階段需安裝 Addressables。將核心與 Addressables adapter 分離、提供完全無 Addressables 的安裝方式，屬第三階段工作。

---

## 快速導入 (推薦流程)

1. **拖入 Prefab**  
   - 將 `Assets/AudioService/Runtime/Prefabs/AudioCtrl.prefab` 放到入口場景。  
   - Prefab 包含 `AudioController`、`FallbackAudioClipProvider`（Addressables + Resources 雙來源）與 `AudioSettingsPlayerPrefs`。

2. **確保常駐**  
   - `AudioController` 會在 `Awake` 時 `DontDestroyOnLoad`。若你的專案有自己管理跨場景物件，請確保 Prefab 在專案中只被實例化一次。

3. **配置 Mixer (選擇性)**  
   - 預設會載入 `Resources/Audio/MasterMixer`。若使用自訂 Mixer，請在 Inspector 指定或修改路徑。

4. **音量 UI / 設定**  
   - 透過 `AudioBootstrap` 提供的 `ApplyVolume` 或直接呼叫 `AudioController.Set*Volume` 控制音量。  
   - 若使用 PlayerPrefs handler，音量變動會立即儲存 (`saveImmediately=true` 可開關)。

5. **播放音效**  
   - 直接呼叫 `AudioController.Instance.PlayBgm("key")` 等 API。Key 必須是對應 provider 能解析的識別字串。

6. **非同步載入**  
   - `PlayBgm/PlaySfx/PlayVoice` 皆有 `allowAsyncLoad` 參數。若為 `true` 且 provider 支援 async，會啟動 coroutine 載入後再播放。

---

## 第一階段播放與淡出契約

- BGM、一般 Voice、循環 SFX／Voice 各自以最後一個有效請求為準。非同步、同步命中和直接傳入 AudioClip 共用取消規則。空 key／null clip 不取代現有播放。
- 非循環 SFX 保留重疊播放能力；不同待載入音效不互相取消。`StopSfx()` 取消當時全部 SFX 待播放請求，`stopLoopOnly: true` 只影響循環分支；Voice 同樣依循環／非循環分支停止。
- 每個聲源同時只有一個播放轉換。新的播放、Stop 或聲源 Fade 會取代舊轉換；聲源 Fade 也使該來源的舊待播放請求失效，避免載入完成後覆蓋淡出指令。
- `FadeChannel(Master, ...)` 的非停止淡出套用全域暫時增益，涵蓋 BGM、SFX、Voice 及兩個循環聲源。它與各來源增益相乘，不修改 Mixer 中的玩家音量或 PlayerPrefs。
- `FadeChannel(Sfx/Voice, ..., useLoopSource)` 仍只選擇指定的循環或非循環聲源，並非整類淡出。`FadeBgmTo`、`FadeSfxLoopTo`、`FadeVoiceLoopTo` 的增益持續到下一個明確 Fade 指令；新播放保留此增益。
- `stopAfter: true` 取消當時相關待播放請求，以獨立的停止包絡淡出現有聲音，結束後恢復播放包絡。Master 停止影響全部分支；停止淡出期間接受的新播放只取代自己聲源的舊停止流程，不會讓其他舊聲音漏停。
- BGM 轉場維持先淡出、再淡入。轉場中 Fade／Stop 可取代它，舊流程不會重新開始下一首。
- 非循環 `PlaySfx(..., fadeInSeconds: ...)` 仍忽略淡入時長，因為 one-shot 共用聲源；獨立播放實例及單次音效淡入留待第二階段。
- 更換 Provider 使舊 Provider 的待播放請求失效。停用 Controller 或整個 GameObject 會使舊請求／轉換失效並停止聲音；重新啟用後需重新發出播放指令。取消播放需求不會直接釋放其他需求共用的載入。

第一階段驗收與本機重跑位置見 [改善計畫 §4.5](unity-audio-service-improvement-plan.md#45-第一階段實作與驗收紀錄)。

---

## 自訂 Clip Provider

1. **實作 `IAudioClipProvider` 或 `IAsyncAudioClipProvider`**  
   - 至少要提供 `TryGetClip` 與 `GetClip`；若支援非同步，實作 `LoadClipAsync`、`IsLoading`、`IsCached`、`ReleaseClip`。
   - 若 provider 是 `MonoBehaviour`，建議繼承後掛在常駐物件下，以免場景切換被銷毀。
   - 可選擇實作 `IResultAudioClipProvider` 的 `LoadClipAsync(category, key, Action<AudioClip>)`，在列舉器正常完成時回報該次載入結果一次；失敗或已釋放回報 null。內建 Addressables／Fallback 已支援，避免舊等待者誤讀同 key 的新快取。原 `IAsyncAudioClipProvider` 仍可使用，Controller 只向該次原始 Provider 查詢結果。

2. **註冊方式**  
   - 透過 `AudioController.RegisterClipProvider(customProvider)` 設成全域預設。  
   - 或使用 `AudioBootstrap`，在 Inspector `clipProviderSource` 指向自訂元件，可選擇是否同步註冊全域。

3. **注意事項**  
   - `AudioController` 會檢查 provider 是否仍存活；若來自會被卸載的場景，請確保在毀滅前主動註銷或掛在 `DontDestroyOnLoad` 階層。  
   - 提供者若使用 Addressables，避免在 `TryGetClip` 內呼叫阻塞式 API；改由 `LoadClipAsync` 處理載入。

---

## 自訂 Settings Handler

1. **實作 `IAudioSettingsHandler`**  
   - 提供四個音量屬性與 `UpdateVolume`，並在值變動時調用 `VolumeChanged`。
   - 若需同步 UI，可在 handler 內部處理廣播或事件註冊。

2. **掛接方式**  
   - 將 handler `MonoBehaviour` 指派給 `AudioBootstrap.settingsHandlerSource`。  
   - Bootstrap 會在啟動時自動呼叫 `ApplyStoredVolumesToController`（可選）與監聽 `VolumeChanged`。

3. **持久化策略**  
   - 預設 `AudioSettingsPlayerPrefs` 支援 PlayerPrefs，包含「儲存時立即 `Save`」的開關。  
   - 若要改用雲端、設定檔等，只需換 handler 實作。

---

## Addressables 與預載策略

- `AddressablesAudioClipProvider` 預設支援：
  - 快取：每個 key 的載入紀錄持有成功結果及唯一負責釋放的 Addressables handle。
  - 非同步：`LoadClipAsync` 透過 coroutine 進行；`TryGetClip` 不會阻塞主執行緒。
  - 預載：`preloadOnAwake`（預設 true）會初始化 Addressables 並遍歷可定位為 AudioClip 的 catalog key，不依賴特定 label。address／GUID 別名的統一識別與快取政策留待第二階段。
- **建議流程**：
  1. 在遊戲啟動或場景載入時，以 `StartCoroutine(provider.LoadClipAsync(...))` 預載常用素材。
  2. 播放時先呼叫 `TryGetClip`；若回傳 `false` 且允許 async，再讓 `AudioController` 觸發載入。
  3. 確認沒有播放中的使用者需要素材後，可呼叫 `ReleaseClip` 釋放。第一階段尚未提供播放持有計數，不會自動保護呼叫端主動釋放的播放中素材。
  4. 共用載入的各等待者不自行釋放 handle；載入失敗、主動釋放或 Provider 銷毀由操作紀錄統一收尾。取消單次播放需求與主動釋放素材是不同操作。

---

## 注意事項與最佳實務

- **單例管理**：確保專案中只有一個 `AudioController`；若需多個，可考量重構為 Service Locator 或 DI。
- **Verbose Logging**：目前 `VerboseLogging` 常數預設 `true`，整合進量產專案可視需求改為序列化欄位或直接關閉。
- **Mixer 參數**：控制器預期 Mixer 參數名稱為 `masterVolume` / `bgmVolume` / `soundVolume` / `voiceVolume`。若使用自訂 Mixer，請維持同樣命名或更新程式碼。
- **測試工具**：`AudioControllerDebugMenu` 適合在開發中掛載於場景物件，透過右鍵選單快速播放／清除設定。但請注意當 handler 改變 PlayerPrefs key 時需同步調整。
- **異步流程**：`PlayByKey` 裡的 coroutine 會在 controller 上啟動；確保 controller 存在於場景且未禁用。
- **記憶體管理**：大量預載音效會佔用記憶體；建議結合 Addressables 的分群策略或根據場景切換釋放。

---

## 常見整合步驟範例

```csharp
public class AudioExample : MonoBehaviour
{
    [SerializeField] private string bgmKey;
    [SerializeField] private string sfxKey;

    private void Start()
    {
        // 直接播放 BGM（允許非同步載入）
        AudioController.Instance.PlayBgm(bgmKey, allowAsyncLoad: true, fadeOutSeconds: 1f, fadeInSeconds: 1f);
    }

    public void OnButtonClick()
    {
        AudioController.Instance.PlaySfx(sfxKey);
    }

    public void OnMasterSliderChanged(float value)
    {
        AudioController.Instance.SetMasterVolume(value);
    }
}
```

---

## 參考連結

- `Assets/AudioService/Runtime/AudioController.cs`
- `Assets/AudioService/Runtime/AudioBootstrap.cs`
- `Assets/AudioService/Runtime/AddressablesAudioClipProvider.cs`
- `Assets/AudioService/Runtime/AudioSettingsPlayerPrefs.cs`
- `Assets/AudioService/Runtime/AudioControllerDebugMenu.cs`
- `Assets/AudioService/Runtime/Prefabs/AudioCtrl.prefab`

---

有任何整合或維護上的疑問，建議先檢查此文件是否涵蓋；若仍需協助，可在專案內尋找 `AudioService` 模組的聯絡負責人。祝整合順利！
