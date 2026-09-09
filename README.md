# 協作工作區：開發規格包

版本：1.0　｜　基準日期：2026-09-09　｜　文件語言：繁體中文

本套件是可放入 GitHub repository 的需求與開發交接文件。目前只有文件，尚未建立應用程式、資料庫、CI 或部署環境；「協作工作區」是中性的暫定名稱。

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
