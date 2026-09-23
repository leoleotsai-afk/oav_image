# 圖片辨識系統（本機、零套件安裝）

一套在 Windows 本機執行的圖片相似度搜尋工具：可以用**語音**、**上傳圖片**、
**相機拍照**三種方式，從你自己的圖片庫中找出最相似的圖片。

不連網、不呼叫雲端 AI／向量資料庫，只使用 Windows 內建元件（.NET Framework
的 `csc.exe`、`System.Speech` 語音辨識、系統「相機」App），**不需要安裝
Python、Node.js、.NET SDK 或任何 NuGet 套件**。

## 功能

| 查詢方式 | 說明 |
|---|---|
| 🎤 語音查詢 | 用本機離線語音辨識（SAPI）把說話內容轉成文字，比對圖片檔名 |
| 📁 上傳圖片查詢 | 選一張圖片，用視覺相似度找出圖片庫裡最像的圖片 |
| 📷 拍照查詢 | 叫起 Windows「相機」App 拍照，自動抓取新照片做視覺相似度搜尋 |

視覺相似度用自製的 **256-bit 感知雜湊**（Y/R/G/B 四個色版各自的 dHash），
同時考慮構圖與顏色，細節見 [CLAUDE.md](./CLAUDE.md) 與 [SKILL.md](./SKILL.md)。

## 需求

- Windows 10/11，內建 .NET Framework 4.x（`csc.exe`，系統本來就有，不用另外裝）
- 語音查詢需要系統已安裝對應語言的語音辨識引擎（可用「設定 → 時間與語言 →
  語音」確認／安裝）
- 拍照查詢需要有可用的攝影機，且系統「相機」App 能正常開啟

## 使用方式

1. 把要搜尋的圖片放進 [`images/`](./images/) 資料夾（檔名請取有意義的名稱，
   語音查詢是靠比對檔名關鍵字）
2. 編譯：

   ```powershell
   powershell -ExecutionPolicy Bypass -File build.ps1
   ```

3. 執行：

   ```powershell
   .\bin\ImageSearch.exe
   ```

   第一次開啟會自動掃描 `images\` 建立索引；之後圖片庫有異動，按介面上的
   「🔄 重建圖片索引」即可（只會重新處理新增/修改過的檔案）。

## 設定

`config.ini`：

```ini
ImagesFolder=images   ; 圖片庫資料夾（相對於專案根目錄，或填絕對路徑）
TopK=8                 ; 每次查詢顯示幾筆相似結果
```

## 專案結構

```
.
├── build.ps1            重新編譯用的指令稿（內建 csc.exe，不需 SDK）
├── config.ini             圖片庫路徑／顯示筆數設定
├── images/                圖片庫（實際圖片不進版控，見 images/README.md）
├── bin/ImageSearch.exe     編譯完成的執行檔（build.ps1 產生，不進版控）
└── src/
    ├── Program.cs           程式進入點
    ├── MainForm.cs           主視窗（UI + 三種查詢方式的操作邏輯）
    ├── ImageHasher.cs        256-bit（YRGB 四平面）感知雜湊 + 相似度計算
    ├── ImageIndex.cs         圖片庫掃描／索引快取／視覺比對／關鍵字比對
    ├── VoiceSearch.cs        System.Speech 語音辨識包裝
    ├── CameraCapture.cs      叫起相機 App + 監看 Camera Roll 抓新照片
    └── app.manifest          DPI 感知設定
```

## 已知限制

- 視覺相似度用感知雜湊而非深度學習模型，抓的是整體構圖與色彩分布，對同一
  物件換角度拍、大幅裁切、背景差異很大的照片，相似度分數會明顯下降。
- 語音查詢是「辨識文字比對檔名關鍵字」，不是語意層級的圖片內容理解。
- 拍照查詢依賴系統「相機」App 把照片存進「圖片庫\Camera Roll」；若相機
  App 的儲存位置被改掉，自動抓取新照片的邏輯會失效。

更完整的設計決策、踩過的坑（例如感知雜湊一開始對顏色不敏感的問題），
記錄在 [CLAUDE.md](./CLAUDE.md) 與 [SKILL.md](./SKILL.md)。

## 授權

本專案未附加開源授權條款，預設保留所有權利。
