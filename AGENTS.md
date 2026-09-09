# Repository 開發指引

本文件供 Codex 或其他開發者執行本專案工作使用。此初始套件只有規格；不得宣稱應用程式或測試已完成。

## 1. 開始工作

依序閱讀：

1. `docs/PRODUCT_SPEC.md`
2. `docs/DECISIONS.md`
3. `docs/ARCHITECTURE.md`
4. `docs/DEVELOPMENT_PLAN.md`
5. 與目前工作相關的 `docs/ACCEPTANCE.md` 案例

先檢查既有程式、Git 狀態與局部 AGENTS，再編輯。保留使用者未提交的修改。使用者最新明確指示優先；文件中的「預設」可以依實測調整，但不能擅自改寫已確認需求。

## 2. 不可破壞的條件

- 同帳號的有效野外預約與占用，只能指向最多一個不同資源 ID。所有寫入路徑均適用，包括 Admin、拖曳、表格與 API。
- 使用者暱稱、帳號擁有者、主要操作者、編輯者、使用狀態、階段、資源占用、資格與活動紀錄必須分開。
- 回村／取消是明確持久操作；斷線、換日、隱藏欄位、改名、未使用與封存要求不能默默清空占用。
- 顯示名稱與固定識別分離；程式採中性、具意義的命名。不得以中文顯示字串判斷規則。
- 標籤包含型別與值；共通定義、個別值與全體共享值是不同概念。
- 每日 04:00 換日，時區初始預設 Asia/Taipei；資格取嚴格晚於達標時間的下一個換日邊界。
- 功勳兌換 1:1 增加每張卡片的累積金幣，同時扣除可用功勳；兌換不撤銷已啟用資格。
- 兌換／收入請求須可重試且不重複計算；涉及多筆資料以交易處理。
- 暱稱入口沒有密碼，普通成員同權。伺服器簽發工作階段，不能採信任意前端 actor ID。
- 去除前後空白後精確等於 `Admin` 的暱稱可讀日誌且隱身。不要增加未要求的管理密碼、二次審批或註冊流程。
- Admin 的在線、編輯及身分中繼資料不得發送給普通用戶；Admin 的業務更新、占用與版本衝突必須照常生效。
- 帳密表依使用者要求直接顯示與明文保存，不自行加入遮罩或欄位加密。
- 日誌仍記錄 Admin 行為，只有 Admin 能讀。後端驗證日誌權限，不能只隱藏頁面。
- 正式帳密、執行資料、資料庫備份與部署憑證不提交到 Git；測試使用虛構資料。

## 3. 技術與責任邊界

- .NET 10、EF Core 10、Npgsql、PostgreSQL；Vue 3、TypeScript、Vite。
- 一個 repo；Vue 編譯成果隨 ASP.NET Core 發布。正式環境不需要常駐 Vite／Node 開發伺服器。
- Domain 不依賴 EF、HTTP、SignalR 或 Vue。Application 實作使用案例；Infrastructure 實作持久化及通知；Web 是組裝根與端點。
- 桌面 WPF＋WebView2 重用 Vue 面板，不複製資料庫或領域規則。
- API 的命令與 SignalR Hub 共用 Application 使用案例，不維護兩套寫入邏輯。
- 不直接在一般欄位 PATCH 裡寫核心占用／資格／金幣狀態；綁定欄位必須轉成專用命令。
- 先做簡單的單一實例實作，保留必要介面。不預先加入微服務、Redis、通用事件溯源或任意腳本引擎。

## 4. 實作方式

- 以 FR 編號與 AC 編號界定工作範圍，一次完成可驗收的完整流程。
- 對可逆的一般 UI 微調不新增模仿實作的測試；核心交易、並行、權限投影與日期邊界必須有行為測試。
- 先確認專案既有元件與指令，避免新增同用途的套件或另一套狀態來源。
- API 型別透過 OpenAPI 契約生成或檢查，避免手動重複定義漂移。
- 需要時寫 ADR／更新決策文件；不得將實作假設包裝成使用者已確認的遊戲規則。
- 在授權範圍內自主完成工作及必要修正，不為普通實作選擇反覆要求確認。
- 環境或驗收仍有實質阻礙時，先完成能完成的工作，再清楚報告限制與下一步。

## 5. 測試與建置

M0 需提供並驗證跨平台開發／驗證腳本，再把精確指令寫入 README。以下是目標契約，檔案在規格階段尚不存在：

| 目標指令 | 預期作用 |
| --- | --- |
| `bash scripts/bootstrap.sh` / `pwsh scripts/bootstrap.ps1` | 檢查 SDK／Node 版本、安裝依賴、準備開發設定 |
| `bash scripts/dev.sh` / `pwsh scripts/dev.ps1` | 啟動開發流程；不得連正式資料庫 |
| `bash scripts/verify.sh` / `pwsh scripts/verify.ps1` | 執行與本次變更相關的型別、建置與測試驗證 |
| `bash scripts/package.sh` / `pwsh scripts/package.ps1` | 產生 Web 發布成果；Desktop 另在 Windows 打包 |

- `.NET` 測試使用 xUnit；前端用 Vitest；瀏覽器用 Playwright。
- PostgreSQL 整合測試必須用實際 PostgreSQL；不能以 SQLite 或 EF InMemory 宣稱驗證資料列鎖或同時寫入。
- 以獨立連線／DbContext 驗證真正並行，不能把請求依序呼叫後稱為競態測試。
- GitHub Actions 的 PostgreSQL service container 是必要整合測試執行位置；Codex 環境如可提供本機 PostgreSQL，也執行同套測試。
- Codex 環境若不能啟動 Docker，使用支援的本機資料庫方式或交由 CI 執行，不改成錯誤的替代 provider。
- Migrations 同時驗證從空庫建立及從前一版升級，不能以 `EnsureCreated` 取代正式 migration 流程。
- WPF 在 Windows runner 建置；實際置頂、熱鍵、DPI、多螢幕與遊戲顯示模式需 Windows 驗收。
- 不宣稱未實際執行的測試已通過；報告執行位置、結果與未驗證項目。

## 6. Pull request 交付

PR 說明問題、變更後行為、對應 FR／AC、實際驗證及剩餘限制。可視的介面變更提供截圖或短錄影；使用虛構資料。

涉及 schema 時，包含 migration、升級方式及資料風險說明。資料庫降版與程式版本回退不能混為一談。

未收到部署授權時，完成可審閱成果及預覽流程設定即可，不自行部署正式環境。使用者已明確授權的操作按授權執行，不再增加重複確認。

## 7. 文件維護

- 修改需求時，同步更新 PRODUCT_SPEC、DECISIONS 及相關 ACCEPTANCE。
- 修改啟動／測試／部署方式時更新 README 與實際腳本。
- 日誌、錯誤提示與使用說明使用繁體中文、24 小時制。
- 保持使用者可自訂的用語來自定義，不在多個元件重複硬編碼。
