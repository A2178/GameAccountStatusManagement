# 技術架構與資料設計

版本：1.0　｜　需求基準：[PRODUCT_SPEC.md](PRODUCT_SPEC.md)

本文件定義實作邊界與最低必要約束，不是已存在的程式碼。具體 schema 及方法簽章在 M0／M1 建立時依下列約束落地。

## 1. 部署與開發單位

| 單位 | 內容 | 執行位置 |
| --- | --- | --- |
| Web | ASP.NET Core API、SignalR、Vue 靜態發布成果 | 家中預覽／正式服務 |
| Database | PostgreSQL、migration history | 家中獨立資料庫；CI 用臨時庫 |
| Desktop | WPF 視窗殼＋WebView2 | 每位使用者 Windows 電腦 |
| 開發工具 | .NET SDK、Node、Vite、測試工具 | Codex 環境、CI、家中開發機 |

前端以 `/api` 與 `/hubs` 存取同源後端；正式環境不額外執行 Node server。SPA 路由回退不能吞掉不存在的 API 路由或錯誤回應。

開發環境以 Vite 代理 API／SignalR；固定 SDK、Node、套件相容版本及 lockfile。具體 patch 版本於 M0 實測後鎖定，文件不憑空指定尚未驗證的套件組合。

## 2. 建議 repository 結構

| 路徑 | 職責 |
| --- | --- |
| `src/Workspace.Domain/` | 純規則、值物件、資格與占用狀態 |
| `src/Workspace.Application/` | 使用案例、DTO、持久化及通知介面 |
| `src/Workspace.Infrastructure/` | EF Core、Npgsql、migration、通知與在線狀態實作 |
| `src/Workspace.Web/` | API、SignalR Hub、工作階段、組裝與靜態前端 |
| `src/Workspace.Client/` | Vue、TypeScript、元件、視圖與前端資料層 |
| `src/Workspace.Desktop/` | WPF、WebView2、視窗／系統匣／快捷鍵 |
| `tests/Workspace.Domain.Tests/` | 領域規則及日期邊界 |
| `tests/Workspace.IntegrationTests/` | PostgreSQL 交易、API 與回應投影 |
| `tests/e2e/` | 多使用者瀏覽器流程 |
| `scripts/` | 可重建的啟動、驗證、打包與 migration 操作 |
| `.github/workflows/` | 實際建立並驗證的 CI／部署流程 |

`Workspace` 是可更換的中性專案前綴，不是要對使用者固定展示的產品名稱。

依賴方向：Application → Domain；Infrastructure → Application／Domain；Web 組裝這些實作。Vue、WPF 均透過 API 或既有網頁功能使用系統。Domain 不知道畫面名稱、資料庫或通訊協定。

## 3. 核心概念與資料結構

### 3.1 共用資料

| 概念 | 重要內容 |
| --- | --- |
| `Workspace` | 名稱、時區、換日設定、設定版本 |
| `Participant` | 穩定 ID、暱稱與顯示偏好 |
| `ParticipantSession` | 伺服器工作階段、能力、隱身狀態；不可用 SignalR ConnectionId 當身分 |
| `WorkspaceMember` | 工作區關聯與未來角色位置；初版普通協作者同權 |
| `CollectionDefinition` | 資料集、名稱、預設視圖與適用欄位 |
| `DataRecord` | 資料列／卡片的穩定 ID、名稱、資料集、封存時間 |
| `AccountState` | 帳號的正式關聯與交易鎖定列 |
| `ManagedRecordState` | 主卡片的帳號 ID、使用狀態、主要操作者、階段與永久限制 |
| `FieldDefinition` | 固定 ID、顯示名稱、型別、選項、作用範圍、值範圍、綁定類型 |
| `RecordFieldValue` | RecordId、FieldId、型別化值／jsonb、版本；唯一鍵為 RecordId＋FieldId |
| `SharedFieldValue` | 共用欄位的一筆值及版本，避免複製到所有卡片 |
| `ViewDefinition` | 表格／看板／儀表板／面板配置，引用固定 ID |
| `WorkflowDefinition`、`StageDefinition`、`TransitionDefinition` | 可改名階段及支援的轉換 |

建議帳號列與主卡片都建立在 DataRecord 上，透過一對一擴充狀態保留核心關聯。帳密表對同一個帳號列呈現普通文字欄位；卡片的 AccountId 指向該帳號身分，不建立第二份帳號。

自由表格的普通列不必具有占用、資格或活動狀態。核心帳號、卡片與一般自由資料可以共用欄位／視圖能力，但正式關聯與業務狀態有獨立約束。

### 3.2 業務狀態

| 概念 | 重要內容 |
| --- | --- |
| `ResourceDefinition` | 資源 ID、名稱、範圍類型與是否啟用 |
| `ResourceReservation` | 卡片、帳號、資源、預約／已進入／已解除、時間與版本 |
| `QualificationCycle` | 達標時間、EligibleFrom、首次進入、失效時間、門檻／時區／換日快照 |
| `MetricState` | 目前確認的可用數值；與資格里程碑分離 |
| `ActivitySession` | 卡片、資格輪次、分流、OccurredAt、RecordedAt、EndedAt、操作者 |
| `CreditState` | 每卡片累積取得量及版本；不代表可支用餘額 |
| `CreditEntry` | 收入／兌換／更正的最小來源紀錄與請求 ID |
| `CommandReceipt` | 可重送命令的識別、payload 指紋與已提交結果 |
| `AuditEvent` | 操作者及可見性快照、目標 ID、變更類型、必要前後值、名稱快照 |

不使用以遊戲名詞命名的類別，例如特定國戰或職業類別。初始場景透過設定建立中文階段、欄位及規則綁定。

## 4. 規則與可設定範圍

規則用固定的 `RuleKind` 與明確參數表示，欄位／資源／階段的連結使用 ID。

第一版必要規則包括：

- 同一帳號同時最多一個不同資源。
- 合法的預約／進入／解除狀態轉換。
- 資格的達標與換日邊界。
- 已啟用資格、重新累積與永久限制。
- 兌換的有效資格、可用數值、比例與累積數值更新。

欄位綁定分為普通值與核心投影。普通欄位走欄位命令；核心投影走預約、回村、活動、兌換等專用命令。直接改核心綁定的 FieldValue 必須被拒絕或由 Application 轉換為相同專用使用案例。

初版不提供取消 FR-001 的開關，也不以腳本或名稱比對決定是否適用核心限制。

## 5. 帳號占用的交易

採用 PostgreSQL 正式帳號資料列鎖，所有相關操作遵循同一流程：

1. 驗證工作階段及工作區資料關聯。
2. 開啟交易，取得該帳號固定鎖定列的 `SELECT … FOR UPDATE` 鎖。
3. **取得鎖後**重新讀取目前有效預約及占用；不能使用取得鎖前的快取判斷。
4. 驗證目標資源與最新狀態、卡片版本及是否已封存。
5. 寫入預約／占用／解除、相關狀態、版本與 AuditEvent。
6. 提交交易；成功後才發送更新通知。

所有需要多種鎖的使用案例統一鎖定順序：帳號、卡片，再到相關數值或活動列；同類多筆按 ID 排序。交易內不做網路推播、長時間計算或等待使用者。

### 必須防範的競態

兩個不同卡片各自有不同版本，不能靠卡片版本阻止它們同時選 A／B。必須競爭同一個帳號鎖定列；後執行者看到新狀態後拒絕異區要求。

第一版使用 READ COMMITTED 與上述共享鎖流程即可。資料表唯一鍵、外鍵與檢查約束作為補充；應用程式資料庫寫入入口統一，不宣稱單一資料列版本本身能保證跨列限制。

有正式占用時拒絕搬移卡片到其他帳號、刪除正式帳號或封存卡片。帳號關聯變更需要同時檢查新舊帳號並遵守鎖定順序。

## 6. 欄位級並行與數值命令

### 一般欄位

- 每個 RecordFieldValue 有應用程式管理的版本值。
- 寫入使用原版本作條件；不同欄位分開寫入，相同欄位舊版本回傳衝突。
- 新值列的競爭由唯一鍵檢查；不能把兩筆首次新增都保存。
- 衝突回應提供已授權的最新值／版本，前端保留草稿。
- 欄位定義變更也有版本；保存時重新檢查型別與定義是否仍有效。
- 共用值只寫 SharedFieldValue 一筆，遵守相同並行邏輯。

### 兌換與累積所得

- `RecordConversion` 命令處理資格檢查、可用功勳扣除、累積金幣增加及收入／操作紀錄。
- 以固定小數精度或整數表示金額，不用浮點數計算 1:1。
- 以同一卡片的數值狀態鎖或等價的條件式更新防止透支；相關更新在同一交易。
- 客戶端在首次送出前產生 RequestId；同次操作的重試沿用該 ID。
- `CommandReceipt` 有工作區／請求唯一鍵；同 ID、相同 payload 回傳已提交結果，同 ID、不同 payload 拒絕。
- 關鍵命令成功但通知失敗時，不能回應為未保存而誘導再次計算；回傳已提交狀態，通知走重試／版本校對。
- 兩個不同請求若各自回報同一次真實遊戲收入，系統無法自動知道；提供可追蹤更正，而不宣稱能自動辨識遊戲事件。
- 扣除功勳後不執行「低於門檻就撤銷已啟用資格」；資格失效走明確使用案例。

## 7. 換日計算

使用可注入的 Clock 與 GameDayPolicy，所有規則在後端執行。

1. 將 QualifiedAt 轉至該輪快照時區。
2. 找出嚴格晚於該時間的下一個本地 04:00。
3. 轉回 UTC 保存 EligibleFrom。
4. 在任何開始活動要求中，以伺服器時間重新驗證 `Now >= EligibleFrom`。

資格時間只在該輪正式達標時設定，重複保存相同功勳或重新開網頁不能延後／重新建立資格。一般資料更正如要撤銷未投入的達標結果，透過明確更正命令處理並記錄原因。

預設 Asia/Taipei 沒有夏令時間問題；若未來允許其他時區，政策需明確處理不存在或重複的本地時間，再加入測試。

## 8. SignalR、存在感與版本校對

### 傳輸與分組

- Vue 使用 SignalR JavaScript client；桌面重用同一 Vue 程式。
- 以工作區及必要的資料集訂閱分組；加入群組由後端檢查工作階段關聯。
- 一般命令透過 HTTP API，成功後推播資料版本與異動摘要。Hub 主要處理在線／焦點及訂閱。
- 不直接將 EF 實體或含完整操作者資訊的領域事件廣播給所有連線。

### 兩種生命週期

| 種類 | 保存位置 | 逾時結果 |
| --- | --- | --- |
| 在線、查看、編輯焦點 | 第一版單一實例記憶體，介面可替換 | 清除可見提示 |
| 預約、占用、活動、資格、數值 | PostgreSQL | 不因連線逾時釋放 |

多分頁以參與者／工作階段彙總，多個 ConnectionId 不能造成重複在線人數。WebView2 與普通瀏覽器如未配對則視為不同工作階段，不按暱稱自動合併。

### 更新可靠性

- 提交時增加工作區／資料實體版本，通知只在提交後發送。
- 連線後先建立有效訂閱，再取得含版本的快照，期間先暫存事件；丟棄不比快照新的事件並套用較新的事件。或採等價的雙重版本核對流程。
- 重連、視窗重新聚焦與定期校對均檢查最新版本；有遺漏時讀取最新快照。
- 單一實例第一版不強制建立外部事件平台。若未來要求跨重啟必達通知，再加入 transactional outbox；資料正確性不依賴通知必達。
- 編輯心跳／版本校對的初始間隔見 DECISIONS；背景分頁節流只影響提示，不影響保存驗證。

## 9. 訪客工作階段、日誌與隱身投影

### 工作階段

伺服器簽發 HttpOnly 工作階段 Cookie；HTTPS 使用 Secure，寫入端點採同源與適當的防偽請求檢查。系統沒有登入密碼。能力從伺服器所保存的目前暱稱與政策產生，不接收客戶端自訂 `IsAdmin`。

### 回應投影

建立單一 AudienceProjection／VisibilityPolicy，用於 REST、SignalR、在線快照、統計、搜尋、匯出與最後編輯者等投影。

- 普通受眾的在線與編輯快照排除 Admin；計數亦排除。
- Admin 作為資料操作者時，普通受眾取得必要業務狀態，但 actor／operator 中繼資料為不可識別的空值或一般狀態文字。
- 不透過 participant 清單、raw actor ID、錯誤訊息或變更歷史端點洩漏 Admin 身分。
- Admin 造成的衝突仍正常拒絕；訊息不揭露其暱稱。
- 普通受眾的事件序列即使有版本變化，也不附上 Admin 活動描述；資料版本可用於正確同步。
- 日誌端點以 Admin 能力驗證，不能把日誌先送到前端再隱藏。

角色模式改變時需更新已存在連線的群組及能力。日誌取得與訂閱均以最新伺服器工作階段判定，防止改離 Admin 後仍留在日誌群組。每筆事件保存當時的 actor／可見性快照，避免日後改暱稱重寫過去紀錄。

## 10. 明文帳密與執行資料

依已確認需求，帳密是普通文字欄位，直接顯示與保存。不要添加 secret-field UI、欄位密文、主密碼或揭露步驟。

日誌的密碼欄位只記錄更新動作，不記錄前後密碼。一般 DTO 按當前視圖取需要的資料，不把整張帳密表塞入每個看板或面板快照。

正式資料與備份放在 repository 外；Git 只保存虛構種子與設定範本。訪客 Cookie 的簽章金鑰需跨服務重啟保存，這與帳密欄位的明文政策是不同用途。

## 11. Desktop 邊界

WebView2 顯示服務上的 `/panel` 視圖，沿用同源 API、工作階段及 SignalR。WPF 只提供視窗控制；預設限制導航到配置的網站，僅暴露需要的原生操作。

- 網站名稱、欄位及布局修改不需重建 WPF 程式。
- 原生快捷鍵／系統匣／視窗功能修改才發布 Desktop 新版本。
- 主機無法連線時顯示斷線，不以本機副本取得新的占用成功結果。
- Windows CI 建置／打包，實機驗收焦點、Topmost、DPI、螢幕移除與遊戲模式。

## 12. Migration、預覽及正式發布

- 使用 Code-First，migration 原始檔與 ModelSnapshot 納入 Git。
- 一般更名／新增自訂欄位只改定義資料；改程式實體 schema 才產生 migration。
- 部署先產生可檢查的 migration script／bundle；驗證新庫及前一版本升級。
- 預覽與正式使用不同資料庫及執行設定。預覽用虛構資料。
- 家中可選 Docker Compose 管理 Web＋PostgreSQL；也可使用 Windows Service＋原生 PostgreSQL。M0 選定一條可重建路線後記錄。
- Tailscale Serve 提供私人 HTTPS 入口，WebSocket 路徑需要在實際路徑測試。
- CI 建置成果以版本或 commit SHA 標記；部署代理取得指定成果，不要求使用者保持 Visual Studio 開啟。
- 正式更新前備份並驗證還原流程。程式回退能否直接使用新 schema 必須獨立評估，不預設破壞性 migration 可自動回復。

## 13. 官方技術參考

以下來源支援技術選擇；遊戲規則以使用者描述為準。

- [ASP.NET Core 與 Vue 整合發布](https://learn.microsoft.com/en-us/visualstudio/javascript/tutorial-asp-net-core-with-vue?view=visualstudio)
- [SignalR 概觀及傳輸方式](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)
- [SignalR JavaScript client 與重新連線](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0)
- [PostgreSQL 資料列鎖](https://www.postgresql.org/docs/current/explicit-locking.html)
- [EF Core 並行處理](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [EF Core migrations 發布](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [Npgsql JSON mapping](https://www.npgsql.org/efcore/mapping/json.html)
- [WPF 與 WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf)
- [GitHub Actions PostgreSQL service container](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers)

文件引用於本次規劃期間核對；套件實際版本與環境命令在 M0 再驗證並鎖定。
