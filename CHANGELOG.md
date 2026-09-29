# 變更紀錄

## Unreleased — 第一階段播放可靠性（2026-09-29）

- 修正舊非同步請求覆蓋新播放、停止後重啟、Provider 更換後誤播。保留 SFX one-shot 並發及只停止循環分支的行為。
- 統一聲源轉換管理，修正淡出後 SFX 重播音量、循環換片被舊淡出停止，以及 BGM 轉場與一般 Fade 競爭。
- 修正 Master 淡出僅影響 BGM：現在涵蓋全部五個聲源，與來源增益及播放包絡相乘，且不修改持久化音量。
- Addressables 使用單一操作紀錄負責完成與釋放；新增可選 `IResultAudioClipProvider`，內建 Addressables／Fallback 直接回報原載入結果，避免失效 handle 或舊等待者誤讀新快取。
- 新增 `Controller.Audio` Runtime assembly，保留腳本 GUID。現階段仍需 Addressables；可選 adapter 的套件化尚未完成。

### 行為說明

- 非停止 Fade 的 Master／來源增益持續到下一個明確調整；停止或新播放只重設播放包絡。
- 聲源 Fade 取代該來源舊待播放請求及轉換。Master 非停止 Fade 只調整全域增益；Master 停止取消當時全部分支請求。
- 停用 Controller 或其 GameObject 會停止聲音並取消舊流程，重新啟用後須重新播放。
- 非循環 SFX 的 `fadeInSeconds` 仍被忽略；獨立單次淡入留待第二階段。

### 驗證

Unity 6000.6.3f1／Addressables 2.11.2：39 項本機 PlayMode 測試全部通過；原範例場景及 Editor 非零音訊輸出檢查通過。測試與輔助工具只保留本機，不隨 Git 交付。Addressables content build、Player 與其他版本／平台尚未驗證。
