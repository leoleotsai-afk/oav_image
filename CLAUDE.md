# 專案說明：本機圖片辨識／相似圖片搜尋系統

讓使用者用三種方式查詢圖片庫裡最相似的圖片：**語音**、**上傳圖片**、**相機拍照**。
全程在本機執行，不連網、不呼叫雲端 AI／向量資料庫，只用 Windows 內建元件。

## 素材實際位置（與任務描述不符，先注意）

使用者訊息說「圖片都已經存放在工作目錄下了」，但 `C:\20260923\05_圖片辦別系統`
一開始是空目錄，沒有任何圖片。目前 `images\` 資料夾是空的——**使用者要放圖片
進來系統才有東西可以比對**，程式本身已經完整可用，只是索引會是 0 張。
放好圖片後按「🔄 重建圖片索引」（或直接重開程式，開啟時會自動重建）即可。

## 為什麼用 WinForms + 內建 csc.exe（不是 Python/Node/瀏覽器）

跟 [03_工單回報桌機版](../03_工單回報桌機版/README.md) 同一套環境限制：這台機器
**沒有安裝 Python、Node.js、.NET SDK**，只有 Windows 內建的
`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（.NET Framework 4.8）。
用瀏覽器（Web Speech API / getUserMedia）本來是語音+相機最順手的做法，但這台
沒有能起本機伺服器的執行環境，所以改用 WinForms 桌面程式，三個功能全部改用
Windows 內建 API 達成：

| 功能 | 用什麼 | 備註 |
|---|---|---|
| 語音輸入 | `System.Speech.Recognition`（SAPI，GAC 內建） | 本機已安裝 `zh-TW` 語音辨識引擎（`MS-1028-80-DESK`），不需另外下載語言包 |
| 相機拍照 | 叫起系統內建「相機」App（`microsoft.windows.camera:` URI），監看「圖片庫\Camera Roll」抓新照片 | 不寫 DirectShow/Media Foundation，避免驅動相容性問題 |
| 圖片相似度 | 自製 256-bit 感知雜湊（dHash，見下方） | 不依賴 OpenCV / ONNX / 任何 NuGet 套件 |
| JSON 索引檔 | `System.Web.Script.Serialization.JavaScriptSerializer`（`System.Web.Extensions.dll`，Framework 內建） | 不需要 Newtonsoft.Json |

**`csc.exe` 語言版本上限是 C# 5**（`/langversion:5`），不能用字串插值 `$""`、
null 條件運算子 `?.`、運算式主體成員、自動屬性內建初始值等 C# 6+ 語法，
所有原始碼都刻意寫成 C# 5 相容寫法。

## 圖片相似度演算法：4 平面 dHash（重要踩坑記錄）

一開始只用**單一 64-bit 亮度 dHash**（`0.299R+0.587G+0.114B` 算灰階後比相鄰
像素大小），用 3 張「形狀完全相同、純色不同」的合成測試圖驗證，結果紅色圖跟
藍色圖的相似度幾乎打平（都 92.2%），因為亮度 dHash **只編碼灰階邊緣結構，
完全不編碼色相**——兩張圖除了顏色以外構圖一樣，亮度梯度就會長得幾乎一樣。

修正方式：把雜湊擴充成 **Y／R／G／B 四個 64-bit dHash 平面**（各自對該色版
獨立算「左像素>右像素」的梯度位元），總共 256 bits，Hamming 距離除以 256
當作差異度。同樣測試圖重跑後，紅色查詢圖對紅色圖片給 89.5%、對藍色圖只剩
75.4%、對綠色圖 71.5%，差距明顯拉開，符合預期。**這是本專案能不能正確分辨
「顏色不同但構圖相似」物件照片（例如設備、零件照片常見的情況）的關鍵，
之後如果要再優化演算法，不要退回單一灰階 dHash。**

實作在 [src/ImageHasher.cs](./src/ImageHasher.cs) 的 `PerceptualHash` struct
（`Y`/`R`/`G`/`B` 四個 `ulong`），索引檔存成 64 字元 hex 字串（`ImageRecord.HashHex`），
`ImageRecord.Hash` 是 `[ScriptIgnore]` 的計算屬性，**故意不讓
`JavaScriptSerializer` 序列化它**——早期版本沒加這個屬性時，`Hash` 會被序列化
成一個會失真的 JS number，反序列化回來再蓋掉正確的 `HashHex`，這是隱藏很深的
bug，加 `[ScriptIgnore]` 才解決。

## 語音查詢的比對方式

語音走的不是「唸出形容詞去猜圖片內容」這種語意比對（本機沒有裝 LLM/embedding
模型），而是**把辨識出來的文字，跟圖片檔名做關鍵字比對**（整詞比對加分、
逐字重疊比對當備援，見 `ImageIndex.FindByKeyword`）。這代表**圖片檔名本身
必須有意義**（例如「紅色馬達.jpg」「軸承_SKF6205.png」），檔名亂碼或流水號
的圖片庫，語音查詢會找不到東西——這點應該告知使用者，為圖片庫命名時盡量用
看得懂的中文/英文檔名。

## 相機拍照流程（為什麼不直接讀 webcam）

按「拍照查詢」時：

1. 記錄目前「圖片庫\Camera Roll」資料夾裡已有哪些檔案（`SnapshotExistingFiles`）
2. 用 `Process.Start("microsoft.windows.camera:")` 叫起系統相機 App
   （已確認本機有 `USB2.0 HD UVC WebCam`，且此 URI 在 Windows 11 有效）
3. 用一個 700ms 的 `System.Windows.Forms.Timer` 輪詢該資料夾，找「快照時沒有、
   現在有、而且檔案已經寫完（`FileShare.Read` 打得開）」的新檔案
4. 抓到新照片就自動停止輪詢，把它當成查詢圖片跑視覺相似度搜尋；180 秒沒拍照
   會自動逾時取消

沒有直接用 DirectShow/Media Foundation 讀 webcam 串流，是因為那條路需要
P/Invoke 大量 COM 介面、對不同廠牌驅動相容性風險高，而借用系統相機 App
幾乎零風險、任何裝了相機的 Windows 11 都能動。

## 索引檔與設定

- `config.ini`：`ImagesFolder`（預設 `images`，可填絕對路徑）、`TopK`
  （每次顯示幾筆相似結果，預設 8）。純手刻 `key=value` 解析，沒有用任何
  ini 套件。
- `image_index.json`：快取每張圖的路徑、最後修改時間戳、雜湊值。重建索引時
  用「最後修改時間有沒有變」判斷要不要重算雜湊，避免每次開程式都整庫重掃。
  這個檔案是自動產生的衍生檔，不用手動編輯，圖片庫搬家或大量改動後刪掉它
  重跑「重建圖片索引」即可。
- 路徑解析：程式用 `AppDomain.CurrentDomain.BaseDirectory`（也就是
  `bin\`）往上找一層當作「專案根目錄」去讀 `config.ini`/`image_index.json`，
  所以 `bin\ImageSearch.exe` 不管從哪裡執行，都會抓到 `05_圖片辦別系統\`
  底下的設定，不需要把 `config.ini` 複製進 `bin\`。

## 建置與執行

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
.\bin\ImageSearch.exe
```

不需要安裝 .NET SDK、Visual Studio 或任何 NuGet 套件，跟
[03_工單回報桌機版](../03_工單回報桌機版/README.md) 一樣只依賴 Windows
內建的 .NET Framework 4.x（`csc.exe`）。`System.Speech.dll` 不在
`Framework64\v4.0.30319\` 資料夾裡，是從 GAC 用完整路徑參照的：
`%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll`
（見 `build.ps1`）。

## 驗證方式（UI Automation 對這種 WinForms 程式沒用，別浪費時間）

跟 [04_自動錄入回報內容/SKILL.md](../04_自動錄入回報內容/SKILL.md) 記錄的坑
完全一樣：這支程式的按鈕/控制項在 `System.Windows.Automation` 底下
`FindAll` 抓不到任何子元素（`Descendants` 回傳 0 個），沒辦法用
`InvokePattern` 點按鈕，也沒辦法用 `SendKeys` 可靠操作原生
`OpenFileDialog`。**這次驗證用的方法**：

1. 截圖確認 UI 有正常畫出來、索引計數正確更新（`Screen.PrimaryScreen` +
   `Graphics.CopyFromScreen`，操作前用 `ShowWindow(SW_RESTORE)` +
   `SetForegroundWindow` 把視窗拉到前景）
2. 另外寫一支獨立的 console 測試程式（`TestSearch.cs`，臨時檔，不在本專案
   內），**直接參照 `src\ImageHasher.cs` / `src\ImageIndex.cs` 這兩個原始檔**
   一起編譯，繞過 UI 直接呼叫 `ImageIndex.FindMostSimilar` /
   `FindByKeyword`，拿合成測試圖驗證雜湊與排序邏輯是否正確（就是上面
   「4 平面 dHash」那段的驗證過程）。測試完把合成測試圖跟測試索引檔都
   清掉了，`images\` 資料夾現在是乾淨的空資料夾。

## 檔案結構

```
05_圖片辦別系統/
├── build.ps1                重新編譯用的指令稿
├── config.ini                圖片庫路徑／顯示筆數設定
├── image_index.json          自動產生的雜湊索引快取（重建索引時自動更新）
├── images/                   放圖片庫的地方（使用者要自己放圖片進來）
├── bin/ImageSearch.exe        編譯完成的執行檔
└── src/
    ├── Program.cs             程式進入點
    ├── MainForm.cs             主視窗（UI + 三種查詢方式的操作邏輯）
    ├── ImageHasher.cs          256-bit（YRGB 四平面）感知雜湊 + 相似度計算
    ├── ImageIndex.cs           圖片庫掃描／索引快取（JSON）／視覺比對／關鍵字比對
    ├── VoiceSearch.cs          System.Speech 語音辨識包裝
    ├── CameraCapture.cs        叫起相機 App + 監看 Camera Roll 抓新照片
    └── app.manifest            DPI 感知設定
```

## 發佈到 GitHub（2026-09-23）

已建立並推送到 <https://github.com/leoleotsai-afk/oav_image>（public，
`main` 分支）。這次「改寫以便發佈」實際做的事：

- 新增 [.gitignore](./.gitignore)：排除 `bin\`（編譯產物，`build.ps1` 可重新
  產生）、`image_index.json`（自動產生的雜湊快取）、**`images\` 底下的實際
  圖片檔**（只留 `images/.gitkeep` 跟 `images/README.md` 進版控）。這台機器
  的 `images\` 當時已經放了 15 張真實設備照片（`A001_1.jpg`...），**問過
  使用者後決定不把實際照片推上公開 repo**，只發佈程式本身——公司內部設備
  照片可能牽涉機密/隱私，公開 repo 沒有存取控制，這個決定不能自己悶著頭做，
  已經用 `AskUserQuestion` 明確問過（是否上傳照片、public/private）才動手。
  之後要換一批「可以公開的範例圖片」進 `images\`，直接調整
  `.gitignore`（拿掉 `images/*` 那條）即可。
- 新增給人看的 [README.md](./README.md)（英文/中文說明、功能表、安裝與
  執行方式、專案結構、已知限制）。原本的 `CLAUDE.md`／`SKILL.md` 是寫給
  AI agent 看的「任務記錄＋可重用技巧」風格（大量踩坑細節、決策過程），
  不適合當 GitHub 訪客第一眼看到的門面，兩者分工保留、互相連結。
- 掃過所有原始碼／文件確認沒有機密資訊（DB 連線字串、token、個人資料）才
  推上去——這個專案本來就沒有連資料庫，比對起來比
  [01_建資料表](../01_建資料表/CLAUDE.md) 這類會踩到 DB 密碼的專案單純很多。

### 這台機器一開始沒有 `gh` CLI，過程記錄見 SKILL.md

環境檢查發現沒有 `gh`（GitHub CLI）也沒有任何已存的 git/GitHub 憑證
（`git config --global credential.helper` 沒設、`cmdkey /list` 也沒有
github 相關項目）。裝 `gh`、登入、建 repo、推送的完整可重用做法（含
`winget` 裝套件會卡 msstore 條款互動提示的坑、新裝的 CLI 在目前 shell
裡 `PATH` 抓不到的坑、`gh auth login` 裝置驗證碼流程怎麼在背景工具裡跑）
都寫在 [SKILL.md](./SKILL.md)，這裡不重複。

`leoleotsai-afk/oav_image` 這個 repo 名稱**使用者其實已經先手動建好了**
（空的、public），第一次 `gh repo create ... --push` 因為「Name already
exists on this account」失敗，改成 `git remote add origin` +
`git push -u origin main` 就成功了。**之後類似任務，`gh repo create`
失敗如果是這個訊息，先用 `gh repo view <owner>/<repo> --json isEmpty` 確認
是不是已經有一個空 repo 在那裡，不要當成錯誤處理，直接接上去推送即可。**

## 已知限制

- dHash 系列演算法抓的是「整體構圖＋色彩分布」，不是物件辨識，**對同一物件
  換角度拍、大幅裁切、或背景差異很大的照片，相似度分數會明顯下降**。這不是
  bug，是沒有用深度學習模型（本機沒有 Python/ONNX Runtime 可用）情況下的
  合理取捨。
- 語音查詢是「辨識文字比對檔名」，**不是**「聽語音描述去理解圖片內容」，
  依賴圖片檔名本身命名有意義。
- 相機拍照依賴系統「相機」App 把照片存進「圖片庫\Camera Roll」；如果使用者
  在相機 App 裡把儲存位置改掉，抓新照片的邏輯會失效。
