# M2 驗證與實作範圍

對應 FR-003、FR-005、FR-006、FR-015，以及 AC-015–AC-022、AC-011／AC-012／AC-014 與 AC-053。

## 資料與交易

- 保留既有 Accounts／Cards；帳密列的 RecordId 就是 AccountId。自由資料列保存於 DataRecord，不建立新的占用群組。
- FieldDefinition、RecordFieldValue、SharedFieldValue、CollectionDefinition、ViewDefinition、StageDefinition 與 WorkspaceSettings 皆為資料。新增一般欄位不需要 DDL 或 migration。
- 自訂值使用 jsonb；選項與關聯保存 ID。個別標籤移除後保留版本墓碑，避免舊的首次保存覆蓋後來的重新掛載。
- 定義及其值的修改在交易中取得同一個 PostgreSQL advisory transaction lock（穩定 field ID 的 hash）。這包含首次新增欄位／值，避免不存在的資料列無法上鎖。不同欄位使用不同鎖；共享值只保存一列。
- 定義版本、儲存格版本、資料集歸屬、型別、必填、選項與關聯都在取得鎖後重新驗證。不同欄位的獨立提交不使用整包 JSON 覆寫。
- 寫入、workspace version 及 AuditEvent 同交易提交。推播使用既有不含 actor 的版本通知；帳密只在選擇帳號資料集時載入，不放入核心看板快照。
- 取得資料集快照使用 Repeatable Read。即時通知後、重連後、聚焦與每 15 秒重新校對，草稿與伺服器值分開。
- M1 的歷史模型原本位於 ModelSnapshot 內；移至 M1 Designer 檔保留，內容不改動。M2 snapshot 由 EF 產生，舊 migration ID 不變。

## 驗證對照

| 案例 | 自動驗證 |
| --- | --- |
| AC-015／AC-016 | PostgreSQL 獨立值、新卡片自動取得定義、資料集隔離、定義刪除；雙瀏覽器看到不同角色的獨立值 |
| AC-017 | 兩個真正工作階段同時寫入共用值，僅一筆成功；瀏覽器共享同步、最新值比較、草稿保留 |
| AC-018 | 選項 ID 在改名後保留；設定頁及視圖改名；帳號改名後 ID 仍相同 |
| AC-019／AC-020 | 獨立 DbContext／連線同時寫不同欄位都成功；同儲存格首次插入只成功一次，409 帶最新值；Vitest／瀏覽器驗證草稿保留 |
| AC-021 | 型別變更驗證現值、過期定義拒絕、刪除後保留草稿、null 與 0 區別 |
| AC-022 | 自由表格建立、關聯與資料集隔離、虛構密碼直接顯示及 jsonb 原文保存、日誌不包含密碼值 |
| 核心回歸 | M1 同帳號異區競爭、Admin 投影、過期解除，以及 M2 一般欄位不能寫核心占用 |
| AC-053 | 真正 PostgreSQL 上從 M1 建立 Occupied 資料後升級，帳號／卡片 ID、占用與版本保持；空庫 migration 與 pending model 檢查 |

本機驗證使用工作目錄內獨立 PostgreSQL 17、.NET SDK 10.0.100 與 Chromium，沒有連到 Codespaces 或正式資料庫。PR 的 CI 另外在 Linux／Node.js 20／PostgreSQL service container 執行相同測試。

2026-09-17 本機結果：19 個 Domain、24 個 PostgreSQL／API 整合測試、4 個 Vitest、5 個 Playwright 案例均通過。`npm run build`、DTO 契約漂移檢查、EF pending-model 檢查與 Release publish 通過；發布成果獨立啟動後 `/health`、SPA 與打包的 JavaScript 均回應 200。GitHub Actions 結果以 PR checks 為準。

虛構資料的畫面：[設定](evidence/m2/settings.png)、[自由表格](evidence/m2/table.png)、[共享值衝突](evidence/m2/conflict.png)、[帳密表](evidence/m2/accounts.png)。

## 界線

M2 提供階段的名稱、移動及視圖篩選，沒有把階段當成所在地或參與資格。資格、數值與活動規則仍屬 M4。M3 的完整 presence／游標提示及 WPF 桌面驗收尚未包含；本次的 Web 精簡面板重用相同資料與欄位。

帳密依需求明文顯示／保存。示範、測試與截圖全部為虛構資料。沒有修改 Codespaces 正在使用的資料庫，也沒有部署正式環境。
