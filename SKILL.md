---
name: offline-winforms-image-similarity-search
description: 在沒有 Python/Node/.NET SDK、只有 Windows 內建 .NET Framework（csc.exe）的機器上，用 WinForms 做一套支援語音／圖片上傳／相機拍照三種輸入方式的本機圖片相似度搜尋系統，不依賴任何 NuGet 套件或雲端 AI。
---

# 本機零套件圖片相似度搜尋（WinForms + 內建 csc.exe）

## 何時使用

使用者要求「本機執行」「不能裝額外套件/SDK」的圖片辨識、以圖搜圖、或
「語音/拍照/上傳找最像的圖片」需求，且環境檢查發現沒有 Python、Node.js、
`dotnet` SDK（只有 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）。
這套做法完全用 Windows 內建元件湊出「語音輸入＋相機拍照＋以圖搜圖」，
不需要 OpenCV、ONNX Runtime、Newtonsoft.Json、AForge 等任何外部套件。

## 前置檢查：先確認環境限制，不要預設有 Python/Node

```powershell
(python --version) 2>&1; (dotnet --version) 2>&1
Test-Path "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /help 2>&1 | Select-String langversion
```

`csc.exe`（.NET Framework 4.8 內建版）目前語言版本上限是 **C# 5**
（`/langversion:` 選項只列到 5），寫程式時**不要用** 字串插值 `$""`、
`?.`/`??` 空值運算子、運算式主體方法、自動屬性內建初始值——這些是
C# 6+ 語法，會編譯失敗。物件初始化器 `new Foo { X = 1 }`、Lambda、LINQ、
`var` 都是 C# 3 就有的語法，可以放心用。

## 語音輸入：`System.Speech.Recognition`，注意 DLL 不在 Framework 資料夾裡

`System.Speech.dll` 不在 `Framework64\v4.0.30319\` 底下，只存在 GAC，
`csc /reference:System.Speech.dll` 這種簡單參照會找不到，**要給完整路徑**：

```powershell
$speechDll = "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll"
```

用之前先確認本機真的有裝語音辨識引擎、以及有沒有中文引擎：

```powershell
Add-Type -AssemblyName System.Speech
[System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers() |
    ForEach-Object { "$($_.Culture) - $($_.Name)" }
```

程式碼裡用 `DictationGrammar()`（自由聽寫，不是固定指令詞的
`Choices`/`GrammarBuilder`），挑選 culture 名稱以 `zh` 開頭的辨識器優先，
找不到就退回第一個可用的。**一定要判斷 `InstalledRecognizers().Count == 0`
的情況並讓語音按鈕自動 disable**，不要假設每台機器都有裝語音引擎。

## 圖片相似度：dHash 一定要做成「多色版平面」，單一灰階雜湊會有色盲問題

最簡單的感知雜湊做法是把圖縮成 9×8 灰階小圖，逐列比較相鄰像素亮度大小，
湊出 64-bit dHash，用 Hamming 距離比相似度。**這樣做出來的雜湊對顏色不敏感
——兩張構圖（形狀/邊緣分布）一樣、純色不同的圖片，雜湊會幾乎相同。**
實測過：紅色圖 vs 藍色圖（同構圖）用單平面灰階 dHash 相似度都是 92.2%，
幾乎分不出來。

**修正做法**：對 Y（亮度）、R、G、B 四個色版**各自獨立**做一次 64-bit
dHash（同一組 9×8 縮圖，四次「左像素該色版數值 > 右像素該色版數值」的
位元比較），拼成 256-bit 雜湊，相似度 = `1 - HammingDistance/256`。同樣的
紅藍測試圖，改用四平面後紅圖對紅圖 89.5%、對藍圖掉到 75.4%，差距才有
意義。**做這類系統時直接一開始就用四平面（或至少色版+亮度混合），不要
只做灰階 dHash 再事後補。**

驗證方式：故意造幾張「構圖相同、顏色不同」的合成測試圖（`Graphics.FillRectangle`
+ `FillEllipse` 換色即可），這是最快戳破「只做灰階雜湊」這個坑的方法，
比拿真實照片測還快看出問題。

## JSON 索引檔：`JavaScriptSerializer`，不用裝 Newtonsoft.Json

`System.Web.Extensions.dll`（`System.Web.Script.Serialization.JavaScriptSerializer`）
是 .NET Framework 內建組件，在 `Framework64\v4.0.30319\` 資料夾裡就找得到，
可以直接 `/reference:System.Web.Extensions.dll` 參照，不需要 NuGet 版的
Json.NET。

**注意 64-bit 數值的精度問題**：`JavaScriptSerializer` 把數字序列化成 JS
number（IEEE double），超過 2^53 的 `ulong`/`long` 會失真。存雜湊這種
64-bit 值時**存成 hex 字串**，不要直接存數值型別。

**注意 `[ScriptIgnore]` 遮蔽計算屬性**：如果模型類別裡有一個「從字串算出
數值」的唯讀/讀寫計算屬性（例如 `HashHex` 存字串、額外開一個 `Hash`
屬性方便程式內使用），**這個計算屬性也是 public get/set 就會被序列化**，
輸出成一個會失真的數字，反序列化回來時两個屬性的 setter 執行順序不保證，
可能讓失真的值蓋掉正確的 `HashHex`。要在計算屬性上標
`[System.Web.Script.Serialization.ScriptIgnore]` 排除它。

## 相機拍照：不要碰 DirectShow，借用系統相機 App + 監看資料夾

不要花時間用 P/Invoke 接 DirectShow/Media Foundation 讀 webcam 串流
（驅動相容性風險高、程式碼量大）。改用：

1. `Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true })`
   叫起 Windows 內建「相機」App（Windows 10/11 都有這個 URI scheme）
2. 呼叫前先記錄 `Environment.GetFolderPath(SpecialFolder.MyPictures) + "\Camera Roll"`
   資料夾目前有哪些檔案
3. 用 `System.Windows.Forms.Timer` 輪詢（不要用阻塞式 `Thread.Sleep` 卡住 UI
   thread），找「快照時沒有、現在有、而且用 `FileShare.Read` 打得開（代表
   相機 App 已經寫完檔案，不是還在寫入中）」的新檔案
4. 設一個逾時（例如 180 秒）自動取消輪詢，避免使用者不拍照時計時器一直跑

## 測試／驗證：這類 legacy WinForms 程式，UI Automation 基本上抓不到子元素

跟 [04_自動錄入回報內容/SKILL.md](../04_自動錄入回報內容/SKILL.md) 記錄的
坑一致：`AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, TrueCondition)`
對這種原生 WinForms 視窗常常只回傳 1 個元素（視窗本身），按鈕、清單完全
搜不到，`InvokePattern`/`SendKeys` 對 `OpenFileDialog` 這類系統共用對話框
也不好可靠地自動化。**更划算的驗證方式**：

1. **UI 有沒有正常長出來**：截圖驗證（`ShowWindow(hwnd, 9)` 還原視窗 +
   `SetForegroundWindow` 拉到前景，等 800ms 以上再 `Graphics.CopyFromScreen`）
2. **演算法邏輯對不對**：另外寫一支獨立的 console 測試程式，**直接把
   `src\` 底下要驗證的 `.cs`（不含 `MainForm.cs`）跟測試程式一起丟給
   `csc.exe` 編譯**，繞過 WinForms UI 直接呼叫索引/比對函式，用合成測試圖
   驗證排序結果符合預期。這比硬做 UI 自動化快很多，而且真的驗證到核心
   邏輯（UI 自動化就算點得到按鈕，也驗證不了雜湊算得對不對）。

## 把這類本機專案發佈到 GitHub：`gh` 沒裝、沒登入時的完整流程

適用情境：要把一個原本只在本機跑的專案（尤其是這種「刻意不依賴外部套件」
的環境）推上 GitHub，但機器上**沒有 `gh` CLI，也沒有任何已儲存的 git/GitHub
憑證**（`git config --global credential.helper` 沒設值、`cmdkey /list`
找不到 github 項目、環境變數沒有 `GH_TOKEN`/`GITHUB_TOKEN`）。

### 1. 裝 `gh`：用 `winget install`，一定要指定 `--source winget`

```powershell
winget install --id GitHub.cli --source winget --accept-package-agreements --accept-source-agreements -e
```

**不要漏掉 `--source winget`**：預設 `winget` 會先問要不要接受 `msstore`
來源條款（互動式 Y/N 提示），非互動環境（沒有 stdin）會直接炸掉
「讀取輸入提示時發生錯誤」。指定 `--source winget` 繞過 `msstore` 來源，
全程不需要任何互動確認。

### 2. 剛裝好的 CLI，同一個 shell session 抓不到新 `PATH`

`winget` 把新程式的路徑寫進登錄機／使用者環境變數，但**目前這個 shell
process 的 `$env:Path` 是啟動當下複製的一份，不會自動更新**，裝完馬上
`gh --version` 會出現「不是可辨識的 Cmdlet」。兩種解法擇一：

```powershell
# 方法一：從登錄檔重讀 PATH 塞進目前 session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path","User")
```

```bash
# 方法二（bash 工具）：直接用完整路徑呼叫，不依賴 PATH
"/c/Program Files/GitHub CLI/gh.exe" --version
```

### 3. 登入：不要用互動式問卷模式，用裝置驗證碼（device flow）+ 背景工具

`gh auth login`（不帶參數）會跳互動式選單（方向鍵選 GitHub.com/HTTPS/…），
這種需要 TTY 的問卷在非互動 shell 工具裡完全沒辦法用。**帶齊參數跳過問卷，
直接進裝置驗證碼流程**：

```bash
gh auth login --hostname github.com --git-protocol https --web
```

這個指令會印出一組一次性代碼（例如 `F80E-945B`）跟
`https://github.com/login/device` 網址，然後**卡住等使用者在瀏覽器完成授權
才會結束**（可能長達幾分鐘，取決於使用者多快去操作）。因此**一定要用背景
執行的方式跑**（例如 Claude Code 的 `Bash` 工具 `run_in_background: true`），
不要同步等待卡住整個對話。跑起來之後：

1. 把印出來的代碼＋網址直接告訴使用者，請他們去瀏覽器完成授權
2. 背景工具跑完（工具會主動通知／可輪詢輸出檔）會看到
   `✓ Authentication complete.` `✓ Logged in as <username>`
3. 用 `gh auth status` 確認 `Token scopes` 有 `repo`（建 repo、push 都需要）

**不要用 `git config --global credential.helper` 現有的 `manager`
（Git Credential Manager）硬 push 賭它會自動彈瀏覽器**——那條路對「建立
新 repo」沒用（GCM 只處理 git http 認證，不能呼叫 GitHub API 建 repo），
`gh auth login` 才是同時解決「建 repo」和「push 認證」兩件事的正確做法
（`gh` 登入後，`git push` 也會透過 GCM 用同一組憑證，不需要再另外設定）。

### 4. 發佈前：先決定「哪些東西不上公開 repo」，不要自己悶著頭排除或悶著頭全推

這類專案常見兩種需要排除、但**不能自己片面決定**的內容：

- **`bin\`／編譯產物**：這個可以直接自己決定排除（`build.ps1` 隨時能重編，
  純衍生檔，沒有爭議）。
- **使用者放進 `images\`（或其他資料夾）的實際業務資料/照片**：這種**要先
  用 `AskUserQuestion` 問過**「要不要連同資料一起公開」「repo 要 public
  還是 private」，不要假設。理由：本機專案的資料夾裡放的往往是使用者自己
  公司的實際資料（設備照片、內部文件），一旦 `git push` 到公開 repo，
  即使事後 `git rm` 也已經留在 GitHub 的歷史紀錄跟可能的第三方快取裡，
  是不可逆的外流風險，跟建 repo/push 這種「可逆」操作性質不同，值得多問
  一步。做法：`.gitignore` 排除實際檔案，只保留一個資料夾內的
  `README.md`／`.gitkeep` 說明「東西放這裡、預設不進版控」。

### 5. 建 repo + push：先假設對方可能已經手動建好空 repo

```bash
gh repo create <owner>/<repo> --public --source=. --remote=origin --push
```

如果失敗訊息是 `GraphQL: Name already exists on this account`，**不是
真的錯誤**，通常代表使用者自己已經在 GitHub 網站上手動建好一個空 repo
（這個情境常見於「使用者先建好殼、叫 AI 把內容填進去」）。改用：

```bash
gh repo view <owner>/<repo> --json isEmpty,visibility   # 確認真的是空的
git remote add origin https://github.com/<owner>/<repo>.git
git branch -M main
git push -u origin main
```

## 建置指令骨架

跟 [03_工單回報桌機版/build.ps1](../03_工單回報桌機版/build.ps1) 同一套
模式，只是多了 `System.Web.Extensions.dll` 跟完整路徑的 `System.Speech.dll`：

```powershell
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
$speechDll = "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll"
$references = "System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,$speechDll"
& $csc /nologo /target:winexe /platform:anycpu /out:"$outExe" /win32manifest:"$manifest" /reference:$references $sources
```
