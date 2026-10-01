# Audio Service 進階參考

日常播放先看 [快速入門](../README.md)。從舊版升級請先閱讀 [遷移指南](Migration.md)；本頁只描述目前契約，不保留舊 API 作為另一套使用方式。

## 結構與配置

| 元件 | 責任 |
| --- | --- |
| `AudioService` | 推薦播放與分類控制入口 |
| `AudioController` | 唯一宿主；序列化依賴、啟停與 Unity 通知 |
| `AudioPlaybackEngine` | handle、並發、替換槽、動態聲源池、暫停與淡變 |
| `AudioClipStore` | 等待者、播放使用者、普通快取與預載群組的持有責任 |
| `IAudioClipProvider` | 底層素材查詢、取得與 lease 釋放 |
| `AudioMixerState` | Mixer 路由、音量備援與設定同步；不是額外場景元件 |
| `AudioCatalog` | 編輯資料與不可變執行期解析結果 |

`Runtime/Controller.Audio.asmdef` 不引用 Addressables。`Integrations/Addressables/Controller.Audio.Addressables.asmdef` 依賴核心及 Addressables，依套件存在與否啟用。Editor 與 Samples 只引用核心；UPM 套件 manifest／完整宿主分離尚未完成。

在入口場景根層放入一個 AudioCtrl，並保留一個有效 AudioListener。基本 Prefab 明確連接 Resources 與 PlayerPrefs 設定；需要 Addressables 時使用整合版 Prefab。自訂宿主在 Controller 的 Clip Provider Source／Settings Source 指定實作元件，或透過 SetClipProvider／BindSettings 明確配置。Fallback 不建立隱含元件。

## 播放與完成

所有 API 與 Provider 回呼在 Unity 主執行緒呼叫。

- BGM 與對話各有一個邏輯替換槽。舊等待請求被新有效請求取消；正在播放的聲音只在新素材成功後替換。BGM 轉場是先淡出再淡入，不是 crossfade。
- SFX 與 Overlap 語音各自獨立，Loop 不改變擁有權。進階 `controller.Play(category, idOrClip, options)` 亦是獨立播放。
- PlayOptions 在接納時複製；後續修改呼叫端物件不改變既有請求。FadeOutSeconds 用於 BGM 替換，單次停止使用 `handle.Stop(seconds)`。
- 狀態為 Loading／Playing／Paused／Finished，結果為 Completed／Stopped／Cancelled／Failed／Rejected。Loading 取消回傳 Cancelled；已播放主動停止為 Stopped。
- 終態、並發名額與聲源清理先提交，再派送 Provider 釋放與 Completed。回呼重新播放或關閉服務時看到完整狀態；例外隔離，晚訂閱仍收到一次保留結果。
- 結束後 handle 不能控制重用聲源，且不保留播放資料或素材。直接傳入的 AudioClip 由外部擁有，服務不卸載它。
- 找不到素材、空 ID／null clip、無效分類及並發拒絕，不會先停止現有替換槽。

## Catalog 與來源

Catalog 依分類＋大小寫敏感 ID／alias 查詢，找不到映射時直接以 ID 作為 key。Inspector 驗證空 key、重複、前後空白、無效分類／限制與未配置來源；不載入資產，也不保證建置後能找到資產。同類別衝突由第一筆優先。

```csharp
var catalog = UnityEngine.ScriptableObject.CreateInstance<AudioCatalog>();
catalog.ReplaceEntries(new AudioClipAddress("ui.result", AudioCategory.Sfx)
{
    ResourcesKey = "minigame_win",
    AddressablesKey = "minigame_lose",
    Aliases = new[] { "result" }
});
AudioController.Instance.Catalog = catalog;
```

Authoring 資料複製後才發布；`GetEntriesCopy()` 不可直接修改內部資料。`TryResolve` 回傳 readonly `ResolvedAudioClip`，含 Id、Category、ResourcesKey、AddressablesKey、MaxInstances。索引每筆只保存一份解析值，ID／aliases 共用該筆位置。來源 key 為 null 時使用 ID；空字串明確停用該來源。

`ReplaceEntries` 先發布索引，再通知 Controller 取消舊待播放／預載／準備請求，更新服務自己的素材快取與群組保留。指派新的 Catalog 也走相同流程；Inspector／反序列化只標記索引版本，由 Controller 在主執行緒更新或下一次查詢時處理。已播放的素材由原 lease 保持有效，取消回呼內的新請求使用新映射。

**AudioCatalog 映射變更不刷新 Provider 底層查找**，也不取消外部直接向 Provider 發出的取得請求。若底層來源內容改變，即使 key 相同，仍須明確呼叫 `RefreshClipProvider()` 或由 Provider 發送 Changed；不要用 ReplaceEntries 代替來源刷新。診斷中的素材數只統計目前的服務快取世代，舊播放的 lease 仍由原世代持有到結束。

Fallback 支援兩種政策，僅依 Provider 契約選擇，不判斷 Resources／Addressables 型別：

- `PreferAvailable`：依序嘗試主／備的同步取得，再嘗試主來源 async，失敗後嘗試備援。Resources 同步取得可能載入；Addressables 同步取得只接受已駐留素材。
- `PrimaryThenBackup`：async 等待主來源，失敗才取得備援；同步請求只嘗試主來源，不因備援快取而繞過主來源政策。

Policy、Configure 或來源 Changed 會取消舊需求並刷新。主／備來源必須明確配置，禁止循環依賴。

Addressables catalog 更新成功後呼叫 `controller.RefreshClipProvider()`。它取消舊查找、清除位置與 alias 索引，並保留已發出 lease。Fallback 向支援刷新的子來源轉送；RefreshClipLookup 不得再次發送 Changed。新舊 AssetBundle 並存仍需專案自己的更新策略；服務不修改建置設定。

## Provider 契約

必要介面只有 `IAudioClipProvider`：

| 方法 | 契約 |
| --- | --- |
| `TryGetCachedClip(address, out clip)` | 純查詢，不發起載入；不轉移所有權 |
| `TryAcquireClip(address, out lease)` | 明確同步取得，不等待 async；成功的 lease 保持素材有效，失敗回傳 false／null |
| `AcquireClip(address, completed)` | 可同步或稍後完成，恰好回呼一次；失敗 null，成功轉移一份 lease 給接收者 |

沒有同步能力仍實作 TryAcquireClip 並回傳 false。每份 lease 的 Dispose 冪等；Provider 銷毀或刷新後，已發出且未釋放的 lease 仍有效。Provider 實作負責實際 native 資產操作，不能以「目前沒在快取表」推論資產已無使用者。

可選 `IAudioClipProviderChanges` 提供 Changed；`IAudioClipProviderRefresh` 提供刷新；`IAudioClipProviderDiagnostics.DescribeLookup` 提供失敗時的來源描述。核心不依具體型別分支，也不再代跑舊 coroutine。

Resources 只移除自身引用，不強制 UnloadAsset；外部所有者可能仍使用同一 clip。Addressables 依 resource location 合併 address／GUID 的 native operation，最後一份 lease／待派送持有離開後才 release。alias 索引不是額外的快取持有。

## 預載與持有

```csharp
var audio = AudioController.Instance;
audio.PrewarmSources(24);
audio.PrepareClip(AudioCategory.Bgm, "music.title", "level-1", ready =>
{
    if (ready) AudioService.PlayBgm("music.title");
});
// 離開使用範圍時：
audio.ReleaseGroup("level-1");
audio.ReleaseUnusedClips();
```

Preload 保證取得 AudioClip 資產；PrepareClip 另呼叫需要的 LoadAudioData，完成時 Unity 回報 Loaded。主執行緒呼叫可能阻塞，適合載入畫面；Streaming 的 Loaded 不代表全曲已解碼或輸出零延遲。

- 同一分類＋解析後 ID 的等待者共享 Store 載入；取消一位不取消其他使用者。取消不表示底層 Unity 載入能被中止，遲到結果仍正確歸還。
- ReleaseGroup 取消該群組的等待／準備並移除保留，不停止播放，也不動其他群組／普通快取。回呼新建的需求不屬於舊批次。
- ReleaseUnusedClips 清除普通快取保留；播放使用者或群組仍存在時不釋放。普通播放也會建立快取保留。
- Provider 更換／刷新、Shutdown 與音訊重設使尚未完成的 PrepareClip 回呼 false 一次。成功準備由群組繼續持有，直到釋放。
- 不主動 UnloadAudioData；直接 clip 的生命週期由外部負責。尚無 LRU 或自動記憶體預算淘汰。

完整換場、取消與 BGM 擁有者範例見 [SceneAudio](SceneAudio.md)。長 BGM 的 Streaming／背景載入設定可透過 `Tools > Audio Service > Audio Imports` 明確檢查或套用；工具不會在匯入時自動修改檔案。

## 並發與聲源

MaxVoices=0 表示服務不設限制。總量及單音效限制包含 Playing／Paused／Loading；單音效以分類＋解析後 ID 計算。替換槽的現有與等待播放不重複占用替換的接納預算。

先驗證全部限制，再執行 StealOldest。單音效淘汰可同時釋出全域容量；仍遇 RejectNew 則完全拒絕，不先破壞舊播放。降低上限不立即停止現有聲音，下一次接納才按政策處理。

所有 AudioSource 共用動態池，沒有固定五組相容聲源。Loading 只預約並發名額，直到素材可播放才借用來源。回收清除 clip、loop、pitch、路由、暫停與包絡；使用中來源才參與 Tick 與增益更新，閒置來源在下次播放前套用目前狀態。

PrewarmSources(targetCount) 同步補齊使用中＋閒置的**總容量**，回傳新建數量；不載入素材、不改 MaxVoices、不自動啟動服務。較小目標不縮池，實際需求仍可擴張。TrimIdleSources(minimumCapacity, maxToRemove) 僅刪除閒置來源，保留總容量下限，回傳移出數量；實際 Destroy 於幀末完成。未 Ready 或非正數預算不執行。

池與快取是不同責任；預熱不是預載，也不保證第一次播放端到端延遲。暖播放保留低配置 Usage 快路徑、按需單音效計數索引與弱引用外部 clip ID。

## 暫停、音量

個別暫停、遊戲暫停、AudioListener.pause 與背景暫停分開保存。IgnoreGamePause 略過遊戲／Listener 暫停，但不略過背景或 handle.Pause。Loading 中 Pause 會套用於完成後；暫停中的 Stop 立即完成。播放包絡在暫停時凍結，分類增益仍依非縮放時間淡變。Time.timeScale=0 不等於音訊暫停。

最終增益由玩家 Master／分類音量、暫時 Master／分類增益、PlayOptions.Volume 及播放包絡組成。SetMuted 不停止、不寫入玩家設定；FadeBus 不改保存音量。FadeBus(stopAfter:true) 停止呼叫當時的一批聲音，不把新聲音留在零增益。

Mixer 的群組為 BGM／Sound／Voice，exposed parameters 為 masterVolume／bgmVolume／soundVolume／voiceVolume。有效路由與參數由 Mixer 套用玩家設定，缺少時由 AudioSource 補上相應增益；SetMixer(null) 明確不用 Mixer。ValidateMixer／LastMixerIssue 提供診斷。

BindSettings 連接 IAudioSettingsHandler。ApplyVolume(..., persist:false) 暫時改值；之後同值的保存操作仍會同步 handler，VolumeChanged 僅在有效值改變時通知。PlayerPrefs 設定保留 debounce 儲存、背景／停用 flush、實際 keys 的 ResetSettings、輸入正規化與事件重入保障。

## 啟停與診斷

Controller 的 Awake、重新啟用與 Reload 重接都走 Initialize。依賴由同一宿主管理，沒有全域 Provider 註冊或 Bootstrap 場景搜尋。Shutdown 先標記不可用，再取消／停止、退訂與釋放；其回呼期間不能重新初始化。重新啟用不復活舊 handle。

AudioSettings.OnAudioConfigurationChanged 將已播放／暫停 handle 結束為 Failed，Loading 結束為 Cancelled；清除舊準備與素材快取、停止淡變，重套路由與音量。保留遊戲／背景暫停、靜音及分類增益，不自動續播 BGM。外部被音訊重設失效的 clip 由其擁有者重建。

Diagnostics 提供播放、等待、準備、快取與池數量。CreatedSources 是**目前持有**的總來源數，非累計建立次數；縮池後會下降。快取與 CachedOperationCount 只計目前世代，舊播放仍可能持有舊 lease。GetChannelDiagnostics 的 Volume／FadeGain／Muted 是分開的控制值，不代表實際波形或可聽分貝。

Editor Audio checks 提供唯讀場景 Listener、Mixer 與播放控制診斷，不載入素材、不自動修改狀態。Ready 不代表可聽見；實際音檔、Mixer 效果、OS／硬體輸出需另外驗收。

目前環境、完整回歸與量測見 [改善計畫 §5.19](../unity-audio-service-improvement-plan.md#519-catalog-與素材交付精簡)。本機 Tests／Tools／work 維持 Git 忽略，不隨正式模組發布。
