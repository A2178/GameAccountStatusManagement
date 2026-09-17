# 協作工作區

版本：1.0　｜　基準日期：2026-09-09　｜　文件語言：繁體中文

本 repository 包含 M0／M1 的環境、暱稱入口及區域協調，以及 M2 的可配置名稱、帶值標籤、帳密表、自由表格與視圖。工作區名稱可在設定頁修改。

## 開發需求

| 工具 | 版本／用途 |
| --- | --- |
| .NET SDK | 10.x；`global.json` 的基準為 10.0.100 |
| Node.js | 20.x，搭配 npm |
| Docker | Docker Compose，用於本機 PostgreSQL 17 |
| PowerShell | Windows 使用 `.ps1` 腳本時需要 |

所有設定均為本機開發用途，不得將正式資料庫連線字串或真實帳密放入 repository。

## 啟動與驗證

Linux／macOS：

```bash
bash scripts/bootstrap.sh
bash scripts/dev.sh
bash scripts/verify.sh
bash scripts/package.sh
```

Windows PowerShell：

```powershell
pwsh scripts/bootstrap.ps1
pwsh scripts/dev.ps1
pwsh scripts/verify.ps1
pwsh scripts/package.ps1
```

`bootstrap` 檢查 SDK 與 Node 主要版本並安裝依賴。`dev` 只會依 `compose.yaml` 啟動開發用 PostgreSQL，套用 migration，然後在 `http://localhost:5173` 啟動 Vue，API 位於 `http://localhost:5080`。`verify` 執行 .NET restore／build／test，以及前端型別、測試與建置。`package` 將 Web 發布到 `artifacts/web`，其中包含編譯後的 Vue 靜態檔案。

M0／M1 已使用的 12 位時間前綴 migration ID 保持不變，DbContext 的相容解析器支援以完整 ID 或名稱指定版本；新 migration 仍產生標準 14 位時間前綴。請勿改名既有 migration 或清除資料庫歷史。CI 會驗證從 M0 升級後仍保留額外加入的測試帳號，並驗證空資料庫建置（FR-020／AC-053）。

## M1 雙使用者預覽驗收

1. 執行 `bash scripts/dev.sh`（Windows 使用 `pwsh scripts/dev.ps1`），開啟 `http://localhost:5173`。
2. 在一般視窗選「小明」，在無痕視窗選「小林」；即使輸入相同暱稱，兩個瀏覽器工作階段也會取得不同參與者 ID。
3. 小明為「青鳥一號」預約「迷霧森林」，確認小林的畫面透過 SignalR 更新。
4. 小林嘗試讓同帳號「青鳥二號」預約「赤色峽谷」，應看到繁體中文拒絕原因，且重新整理後不留下赤色峽谷預約。
5. 小明回報入場；只回村一張仍不能解除同帳號其他有效預約。將同帳號全部明確回村／取消後，才可預約另一區域。
6. 關閉分頁、斷線、將卡片改成「未使用」都不會解除預約或占用。斷線時頁面會提示，重連後會重新取得最新快照。
7. 另開無痕視窗選「Admin」：Admin 的操作同樣受區域限制並寫入日誌；普通視窗只看到必要業務狀態，不會看到 Admin 的操作者名稱。只有 Admin 畫面會顯示操作紀錄。

拖曳尚未預約的卡片到區域卡可建立預約；每張卡片也提供完整的按鈕替代操作。所有成功狀態均以前端收到後端保存結果為準，衝突時會刷新資料庫快照。

瀏覽器自動驗收可執行：

Playwright 案例放在 `src/Workspace.Client/tests/e2e/`，與前端套件共用已鎖定的依賴；Vitest 只收集 `src/` 內的單元測試。

```bash
npx --prefix src/Workspace.Client playwright install chromium
npm run e2e --prefix src/Workspace.Client
```

M3 的在線／編輯位置提示、M4 的資格／金幣及後續 Windows 桌面程式尚未實作。M2 階段分頁是同一批卡片的篩選視圖；移到「正式活動」不代表已有入場資格。Web 精簡面板視圖不等於 WPF 桌面程式。

## M2 操作與驗收

- **設定**：改工作區與卡片稱呼、設定分頁名稱、區域／階段名稱。選擇資料集後，管理欄位與視圖。名稱、顏色、順序與選項改名都保留固定 ID。
- **欄位**：文字、整數、數字、單選、多選、核取方塊、日期、日期時間、帳號／卡片關聯、純標記。預設為共通定義、各自填值；也可選共用值或手動掛載的個別標籤。
- **帳號**：登入帳號、密碼、擁有者都是直接顯示的普通文字欄位。名稱按鈕可改別名；這一列與卡片的 AccountId 是同一個身分，不建立第二份帳號。測試只輸入虛構帳密。
- **自由表格**：在設定新增資料集及欄位，再切到新分頁新增資料列、點儲存格編輯。搜尋與臨時排序只影響自己的畫面；「調整視圖」保存的是全體共用的名稱、欄位顯示、篩選、排序及顯示方式。
- **階段與視圖**：卡片上的階段選單移動同一張卡片。表格、看板、簡易儀表板與精簡面板共用資料。固定「區域操作」入口隨時可以預約／取消／入場／回村，隱藏或移除區域欄位外觀不會解除占用。
- **衝突**：不同欄位可分別保存；同一值被更新時保留草稿並顯示最新值，需要點「採用最新版本，保留草稿」才可再次提交。定義刪除後仍可複製草稿。0 與空值不同。

有既有值時，切換共通／個別／共享範圍會拒絕，避免隱含複製或遺失資料；可另建欄位再明確填入。型別、選項變更需通過現值驗證。全部欄位值都不寫入 AuditEvent 描述，密碼因此不會被複製到日誌；日誌保留目標 ID、名稱及操作。

### 從目前 Codespaces 的 M1 升級

先停止舊的網站程序，確認工作目錄沒有需要保留的未提交修改，再切到已取得的 M2 分支（合併後可使用 main）。在已準備好 .NET 10.0.1xx 與 Node.js 20 的 Bash 終端執行：

```bash
git fetch origin
git switch codex/m2-configurable-data
git pull --ff-only
bash scripts/bootstrap.sh
docker compose up -d --wait
dotnet ef database update --project src/Workspace.Infrastructure --startup-project src/Workspace.Web
dotnet publish src/Workspace.Web -c Release -o artifacts/codespaces-m2
cd artifacts/codespaces-m2
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 dotnet Workspace.Web.dll
```

保留 5080 連接埠為 Private，再重新整理原預覽網址。若任一命令失敗，停止後續步驟。正式資料庫升級前需自行完成備份；上述流程針對既有開發預覽庫，不會清空資料。

`20260917013218_ConfigurableData` 新增 M2 資料表與 metadata/stage 欄位；保留 M0／M1 migration ID、帳號、卡片與占用資料。舊卡片初始階段為「未指定」，不猜測其遊戲進度。降版會移除 M2 自訂資料，不可當成無損程式回退。CI 同時驗證空庫、M0 升級，以及含有效占用的 M1 升級。

API 型別由後端 DTO 產生到 `contracts.generated.ts`，驗證腳本會檢查是否漂移。修改 DTO 後執行：

```bash
dotnet run --project tools/Workspace.Contracts
```

具體測試、範圍與升級證據見 [M2 驗證紀錄](docs/M2_VALIDATION.md)。

若要單獨重建開發資料庫，可先刪除開發 volume，再重新啟動：

```bash
docker compose down -v
docker compose up -d --wait
dotnet tool restore
dotnet ef database update --project src/Workspace.Infrastructure --startup-project src/Workspace.Web
```

## 核心目標

讓約 4–5 位朋友共同查看及管理多個帳號底下的卡片。第一個用途是角色狀態協調：**同一帳號的多個角色，不能同時占用不同的野外區域**。可改名稱、帶值標籤、自由表格及桌面面板都建立在這個限制之上。

系統依使用者回報資料，不自動讀取或操作遊戲。主要介面是 Web App，另提供 Windows 置頂面板。

## 文件索引

| 文件 | 用途 |
| --- | --- |
| [AGENTS.md](AGENTS.md) | 交給 Codex 的工作規範、不可破壞的條件與交付要求 |
| [docs/PRODUCT_SPEC.md](docs/PRODUCT_SPEC.md) | 完整產品需求、使用流程與行為規則 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 技術邊界、資料模型、同步、交易與部署設計 |
| [docs/ACCEPTANCE.md](docs/ACCEPTANCE.md) | 可追蹤的驗收案例與測試要求 |
| [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md) | 分階段開發工作、Issue 拆分方式及完成定義 |
| [docs/DECISIONS.md](docs/DECISIONS.md) | 已確認決策、明確標註的實作預設與待實測事項 |
| [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md) | PR 說明格式 |

## 已選定的技術

- ASP.NET Core 10、EF Core 10、Npgsql、PostgreSQL；Code-First Migrations。
- Vue 3、TypeScript、Vite；Vue Router、Pinia、SignalR JavaScript client。
- 一個 repository；前端編譯成果隨 ASP.NET Core 發布，同源提供畫面、API 與 SignalR。
- 精簡 Clean Architecture：Domain、Application、Infrastructure、Web；Vue 與桌面程式是介面層。
- WPF＋WebView2：重用 Vue 精簡面板，提供 Windows 視窗功能。
- xUnit、Vitest、Playwright；以 GitHub Actions 驗證。資料庫行為使用真正的 PostgreSQL 測試。
- 初期主機在使用者家中，透過 Tailscale 使用；私人筆電只做遠端操作或開啟網站。

## 放入 repository 的方式

1. 建立空的私人 repository，或選擇要承接開發的既有 repository。
2. 將本套件資料夾**裡面的內容**放在 repository 根目錄，讓 `AGENTS.md` 位於根目錄。
3. 若已有 README、AGENTS 或 PR 範本，合併內容並保留既有規範，勿直接覆蓋。
4. 提交一筆文件基準版本，例如 `docs: establish product baseline v1`。
5. 依開發計畫建立里程碑與 Issues。先完成 M0、M1，驗證實際的區域衝突防護，再逐步擴充。

本套件不包含真實帳密或正式資料。日後應用程式允許帳密表明文，但執行資料、備份及本機設定不屬於原始碼提交內容。

## 第一個 Codex 工作指令

```text
先閱讀根目錄 AGENTS.md，以及 docs/PRODUCT_SPEC.md、docs/ARCHITECTURE.md、
docs/ACCEPTANCE.md、docs/DECISIONS.md、docs/DEVELOPMENT_PLAN.md。

檢查目前 repository 後，完成 M0 與 M1：建立可重建的環境與 CI，並完成
暱稱入口、帳號／卡片、使用狀態、區域預約／進場／回村、SignalR 同步與
真正 PostgreSQL 上的並行衝突測試。先做可使用的完整流程。

核心限制必須在所有寫入入口生效，包括 Admin、直接 API 呼叫與未來表格編輯。
不要把 SQLite 或 EF InMemory 的測試當作 PostgreSQL 並行驗證。
不得自行把明文帳密、Admin 暱稱模式等已定案需求改回其他方案。

產出分支與 PR，說明已完成項目、實際測試結果、啟動方式與可查看的成果。
可自主修正範圍內問題。若環境限制使某項檢查無法執行，清楚列出並讓 CI 補驗。
本次不部署正式環境，不要為了通過測試而刪除核心規則。
```

以上指令用於開始開發，不代表本文件套件已執行其中工作。

## 推動方式

以「一個可驗收行為、一張 Issue、必要時一個 PR」推進。每張 Issue 引用需求編號與驗收案例；PR 通過相關測試後，在獨立預覽環境操作確認，再安排正式發布。這能讓使用者主要負責大方向與實際體驗，而不必在私人筆電安裝完整開發環境。

需求優先順序為：使用者最新明確指示、已確認產品規則、此套件中的實作預設。新增決策應同步更新文件與驗收案例。
