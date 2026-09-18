# M3 多人協作驗證

對應 FR-004、FR-012–FR-014、FR-016–FR-018；AC-019–AC-021、AC-025–AC-032、AC-056，並回歸 M1／M2。

## 實作與界線

- 在線狀態只存在單一 Web 實例的記憶體。工作階段下多個 ConnectionId 合併成一名參與者，目標去重；代表色與短碼由固定 ParticipantId 決定，同名不同工作階段不合併。
- 心跳傳送資料集、資料列、欄位 ID 與查看／編輯模式。伺服器驗證目標並產生名稱，共用欄位統一為資料集層級。草稿、每次按鍵及帳密值不進入 presence，也不寫心跳日誌。
- 所有 REST 與 SignalR presence 使用相同公開投影，排除 Admin。隱身者進出或改焦點不增加公開 presence 版本；業務寫入仍增加工作區版本、保存日誌並通知更新。
- 每次 HTTP 驗證 Cookie 時從資料庫刷新暱稱與能力，每次 Hub 心跳也讀取目前工作階段。改名與心跳在同一個 presence gate 下更新，避免舊心跳重新加入可見身分。改名保留 ParticipantId，向本人既有分頁發送不帶身分的 `sessionChanged`，普通受眾只收到已過濾名單。
- 先建立 SignalR 連線及事件處理，再讀取快照。單一同步迴圈在完整快照（核心、設定、目前資料集）前後核對工作區版本，並追上讀取期間收到的版本通知。重連使舊迴圈失效，重新完成一輪才恢復操作。聚焦及定期校對也會修復漏收的通知。
- 正常背景校對不暫停操作；伺服器仍逐欄位／帳號驗證版本。斷線停用保存，草稿與最新值分開；相同儲存格或共用值衝突需明確比較後才能再送出。不自動重送業務命令。
- presence 以 epoch 加版本避免重啟後版本歸零造成錯誤丟棄。TTL 及斷線只移除提示；占用、使用狀態、主要操作者皆留在 PostgreSQL。
- 通知傳送失敗會記錄錯誤，已提交資料仍以成功結果回傳。後續版本校對可恢復；不新增 Redis 或 outbox。

## 設定

| 環境變數 | 預設秒數 | 用途 |
| --- | --- | --- |
| `Collaboration__HeartbeatSeconds` | 10 | 在線與焦點心跳 |
| `Collaboration__PresenceTimeoutSeconds` | 30 | 未收到心跳時清除提示；背景每 2 秒清理 |
| `Collaboration__ReconcileSeconds` | 15 | 漏收通知後重新核對資料 |

心跳及校對可設定 1–60 秒，TTL 可設定 3–300 秒；心跳間隔應短於 TTL。背景分頁受瀏覽器節流時可能暫時從名單消失，恢復後仍是相同身分，不會解除占用。多實例部署須替換 presence 與通知設施，第一版維持 A-012 單實例。

## 自動驗證

2026-09-18 本機使用隔離的 PostgreSQL 17、.NET SDK 10.0.100、Chromium，沒有連到 Codespaces 或正式資料庫。

| 範圍 | 證據 |
| --- | --- |
| 同欄位／不同欄位競態、草稿及定義變更 | 既有 M2 真正獨立 PostgreSQL 連線競態測試、Vitest 與雙瀏覽器案例 |
| Admin 身分與日誌權限 | 精確暱稱變體、改名後舊 Cookie 撤權、既有 Hub 連線重新取權限、多分頁立即移除日誌 UI |
| Admin 隱身 | 隱身者進出／焦點不改公開 payload 或 revision；瀏覽器直接檢查 presence REST 及收到的 WebSocket frames 不包含 Admin ID／暱稱；業務值與衝突照常同步 |
| 多分頁及 TTL | 同工作階段去重、同名分開、關閉其中一頁仍在線、假時鐘 TTL 邊界與重連固定身分 |
| 占用與編輯分離 | 遊戲操作者與欄位編輯者不同；斷網後提示消失但預約、主要操作者、使用狀態保持 |
| 重連／漏通知 | 真實離線後保留草稿並取得新值；攔截並丟棄 snapshotChanged 後定期校對收斂；刻意延遲首次快照並在途中寫入仍能收斂 |
| 通知故障 | 實際 notifier 對失敗的 Hub transport 吞下傳送錯誤，資料與版本仍在 PostgreSQL 提交 |

測試結果：19 個 Domain、34 個 PostgreSQL／API 整合、9 個 Vitest 與 9 個 Playwright 案例全套通過。DTO 契約檢查、EF pending-model 檢查及 Release publish 通過；發布成果獨立啟動後 `/health`、頁面及打包 JavaScript 均回應 200。GitHub Actions 另外在 Linux／Node 20／PostgreSQL service container 執行全套，狀態以 PR checks 為準。

Playwright 全套案例共用一個工作區，因此設定一個 worker；案例內仍用多個獨立瀏覽器工作階段測試並行。測試伺服器採心跳 1 秒／TTL 5 秒／校對 2 秒，縮短故障驗證等待時間；產品預設不變。

虛構資料畫面：[多人在線及共用欄位衝突](evidence/m3/collaboration.png)、[重連草稿與最新值](evidence/m3/reconnect.png)。CI artifact `m3-browser-evidence` 保存 Playwright 報告及失敗 trace。

## 升級

M3 不更動 EF schema 或 migration ID。由 M2 升級只需更新程式、重建並重啟；`database update` 可照常執行，會保持既有資料。重啟會清除短暫在線提示，瀏覽器心跳重新建立，資料庫占用保持不變。操作步驟見 README 的 M3 Codespaces 預覽。

M4 資格、04:00 邊界、金幣與活動，以及 Windows 桌面驗收仍屬後續階段。

本次安裝／發布時，`npm audit` 回報現有開發及測試工具的 5 項公告（1 moderate、3 high、1 critical，涉及 Vite、Vitest／mocker、Playwright）。M3 沿用原有 package.json／lockfile；工具版本升級與相容驗證另行處理。
