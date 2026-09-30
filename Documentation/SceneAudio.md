# 可選的載入與換場範例

一般播放只需 `AudioService.Play*`。這份範例適用於需要提前準備素材、或在完整換場時清理快取的專案。

## 直接使用範例元件

1. 入口場景放好 AudioCtrl Prefab，保留一個有效 AudioListener。
2. 在會隨場景卸載的物件加入 [SceneAudioSample](../Assets/AudioService/Samples/SceneAudioSample.cs)。
3. 把循環環境音放進預設的 `Assets/Resources/Audio/SFX/`，將不含副檔名的 key 填入 `Looping Sfx Key`，例如 `wind`。空 key 不會自動播放。
4. Start 時，元件會用自己的唯一群組準備素材，成功後播放循環，保存該次 handle。
5. 停用元件或卸載場景，元件會取消尚未完成的準備、停止自己持有的聲音並釋放保留。重新啟用可再次準備。

載入畫面也可明確呼叫 `PrepareAndPlay()`；Start 不會再重複發一次。重複手動呼叫會結束此元件上一輪需求，再以目前 key 開始新需求。

## 範例內的生命週期

| 時點 | 做什麼 | 為什麼 |
| --- | --- | --- |
| 準備 | `controller.PrepareClip(category, key, ownGroup, callback)` | 取得資產並等待音訊資料可用；群組保留素材 |
| 準備成功 | 保存 `AudioService.PlaySfx(...)` 回傳的 handle | 後續只控制這次播放 |
| 離場 | 先作廢準備版本，再 `ownedHandle.Stop()` | 舊回呼不能在離場後重新播放 |
| 釋放保留 | `controller.ReleaseGroup(ownGroup)` | 取消該群組等待、移除群組保留；本身不停止聲音 |
| 完整換場清理 | `controller.ReleaseUnusedClips()` | 清除服務內普通快取保留；其他群組與仍在播放的素材繼續有效 |

準備可能同步完成，也可能延後完成。範例捕捉當次 key、音量、Controller 與版本；取消後、停用後、再次準備後，舊回呼均不能啟動聲音。這些保護已寫在範例。

`ReleaseUnusedClips()` 是整個服務的普通快取政策，不僅限於這個元件。範例適用於**完整換場**；若只是卸載 additive 子場景或一小塊區域，將此呼叫移到應用程式的統一換場管理器。需要跨場景保留但當下沒在播放的素材，可由長駐物件用自己的預載群組持有。

## 跨場景 BGM

讓長駐的遊戲流程物件呼叫 `AudioService.PlayBgm` 並管理其 handle。上述場景範例只停止自己的 SFX，不呼叫 `StopBgm`、`StopSfx` 或 Shutdown；清理普通快取也不會使仍在播放的 BGM 失效。

如果 BGM 應隨場景結束，由該場景的擁有者保存與停止自己的 BGM handle。不要用服務的全分類停止代替「停止我擁有的聲音」，否則會影響其他物件。

## 提前準備的界線

`PrepareClip` 不保證 Streaming 音樂整首已解碼，也不會修改音檔匯入設定；資料準備仍可能依設定阻塞。`PrewarmSources` 只提前建立聲源，與素材準備是兩件事，皆非基本播放的必要步驟。

若只需持有 AudioClip 資產，進階介面另有 `Preload`。兩者的完整差異、Provider 與快取責任見[進階參考](AudioServiceReference.md#素材持有預載與釋放)。
