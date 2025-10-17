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

- **Unity 版本**：建議 Unity 2022.2 以上；若需支援 Unity 2021 LTS，可將 `AudioBootstrap` 等使用 `FindFirstObjectByType` 的地方改為 `FindObjectOfType`，或加上條件編譯。
- **腳本執行階段**：所有腳本在 `Assembly-CSharp`；若搬移到自訂 Assembly Definition，請確保新的 Assembly 可以存取 Unity 基礎 API 以及任何外部套件（例如 Addressables）。
- **套件依賴**：非必要；僅 Addressables 提供者需 Unity Addressables Package。

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

## 自訂 Clip Provider

1. **實作 `IAudioClipProvider` 或 `IAsyncAudioClipProvider`**  
   - 至少要提供 `TryGetClip` 與 `GetClip`；若支援非同步，實作 `LoadClipAsync`、`IsLoading`、`IsCached`、`ReleaseClip`。
   - 若 provider 是 `MonoBehaviour`，建議繼承後掛在常駐物件下，以免場景切換被銷毀。

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

## Addressables 与預載策略

- `AddressablesAudioClipProvider` 預設支援：
  - 快取：使用 `_cache` 儲存成功載入的 `AudioClip`。
  - 非同步：`LoadClipAsync` 透過 coroutine 進行；`TryGetClip` 不會阻塞主執行緒。
  - 預載：`preloadOnAwake`（預設 true）會初始化 Addressables 並嘗試載入所有標註為 `AudioClip` 的資源。
- **建議流程**：
  1. 在遊戲啟動或場景載入時，以 `StartCoroutine(provider.LoadClipAsync(...))` 預載常用素材。
  2. 播放時先呼叫 `TryGetClip`；若回傳 `false` 且允許 async，再讓 `AudioController` 觸發載入。
  3. 不再需要的音效可呼叫 `ReleaseClip` 釋放 handle。

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
