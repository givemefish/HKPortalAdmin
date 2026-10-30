# HKPortalAdmin

ASP.NET Core MVC (.NET 10)、Kendo UI for jQuery 2026.3.1015、Bootstrap 5、Dapper、SQL Server 2022。
部署 IIS 10，登入使用 Windows 驗證；所有通過驗證的使用者都可管理文章，沒有角色授權或審核流程。

## 啟動

本機若 `dotnet` 仍指向舊版 SDK，可直接使用專案提供的腳本（優先選取 Program Files 下的 .NET 10，不改全機 PATH）：

```powershell
Set-Location D:/project/HKPortalAdmin
./scripts/Manage-Admin.ps1 -Action Build
./scripts/Manage-Admin.ps1 -Action Run
./scripts/Manage-Admin.ps1 -Action Publish
```

Run 使用 `https://localhost:7190`。Publish 輸出至根目錄 `.tools/publish/HKPortalAdmin`（未納入版本控制）。
本機 HTTPS 使用開發憑證；實際 IIS 部署請使用可信任的網站憑證。

1. 安裝 .NET 10 SDK。此專案獨立放置於 `D:\project\HKPortalAdmin`，方案檔為 `HKPortalAdmin.slnx`，不依賴原 HKECPW 專案。
2. 複製授權 Kendo 資源至 `wwwroot/lib/kendo/2026.3.1015/`，完整檔案清單見該目錄 README。
3. 在本專案建立 `appsettings.Local.json`（已 gitignore）或設定 User Secrets／環境變數：

   ```json
   {
     "ConnectionStrings": {
       "Portal": "Server=HKPortalDB;Database=EPCHK;User ID=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connect Timeout=10;"
     }
   }
   ```

   請使用可信任的 SQL Server 憑證。範本帳密為 CHANGE_ME，未設定前 API 回傳設定提示，不會連線嘗試寫入。
   `appsettings.Local.json` 是最後載入的設定來源；若部署使用環境變數，請不要部署此本機檔案。
4. 執行 `dotnet restore HKPortalAdmin.csproj`、`dotnet build HKPortalAdmin.csproj`、`dotnet run --project HKPortalAdmin.csproj`。
   亦可使用 Visual Studio 的 IIS Express profile，已設定啟用 Windows 驗證、停用匿名。
5. 開啟 `https://localhost:7190`，用 Windows 帳號登入。帳號取 `DOMAIN\username` 的 username，必須可存入既有 `varchar(8)`，不截斷。

本專案不自動建立／更改資料庫結構，不使用既有 KM_GET_LATEST_ARTICLE，不包含假登入或可寫入的模擬資料庫。

## 功能及資料規則

- 分類來自 `dbo.kmCategory`；`CateId=0` 為根，其他以 ParentCateId 組樹。可選任意層級，查詢只比對選取的 CateId。
- 同層先依可轉數字的 SortOrder 排序，再依原值與 CateId 穩定排序。分類資料缺父節點或有循環時顯示錯誤。
- 後台以 Raw 表為準，伺服器端分頁、標題關鍵字（繁中／簡中／英文）、狀態篩選與排序；不載入清單的內文及附件二進位內容。
- 新增／編輯使用三語頁籤與 Kendo Editor；Subject／Body 同步 ZHTW，CorpId=8800、Importance=0、Summary=空字串。
- 新文章無屬性，編輯保留既有屬性。遇到同文章重複 AttrName 時不發布，提示資料異常。
- 單篇選單一分類，既有多分類文章可檢視，但拒絕編輯儲存，避免默默刪除既有關聯。
- 預定日期必填，開始預設資料庫目前時間，結束預設 2099-12-31 23:59:59；UI 可選到分鐘，儲存開始秒數 00、結束秒數 59。
- 所有商業日期使用 SQL Server GETDATE()，與既有 datetime 一致；部署時確認 SQL Server 時區。

| 操作 | Raw | 正式表 |
| --- | --- | --- |
| 儲存文章 | 更新內容／附件，發布日期均清空，既有 PostDate 不變 | 僅清空文章發布日期，保留舊內容及關聯 |
| 待審 | 發布日期均清空 | 若存在該文章，同步清空發布日期 |
| 上架 | 發布日期由預定日期帶入，PostDate=資料庫目前時間 | 同一交易同步文章、分類、屬性、附件四表 |
| 下架 | 兩個發布日期均為同一個目前時間減一天 | 若存在該文章，同步更新日期 |
| 刪除 | 實體刪除文章及三種關聯資料 | 同一交易實體刪除文章及三種關聯資料 |

狀態依日期衍生：雙 NULL=待審、結束早於現在=下架、開始晚於現在=預約上架、有效區間含邊界=上架。
單邊 NULL 或結束早於開始顯示日期異常，避免把不完整舊資料當成已上架。
新建 PostDate 先填資料庫目前時間以符合 NOT NULL，後續待審／下架不變；每次上架重新設定。

儲存與批次操作採交易、文章鎖及 bdModDate 樂觀衝突檢查。批次最多 100 筆，任何一筆失敗整批回復。
鎖不取代其他舊系統的更新規則；若其他程式更新文章，須同步更新 bdModDate 才能偵測跨系統衝突。
Raw→正式同步保留 UID／AID／AttID，使用明確欄位映射。正式附件若有跨文章 AttID 唯一鍵衝突，整筆發布會回復。

## 附件與圖片

- 附件每個 10 MiB，單次儲存最多新增 20 個；拒絕執行檔／腳本副檔名及常見執行檔檔頭。附件只透過強制下載端點提供。
- 附件內容存入 kmAttachmentRaw.FileContent，FileStatus=OK，FileLang=上傳頁籤的 ZHTW／ZHCN／EN。
- 移除先在編輯畫面標記，按儲存時刪除 Raw；取消編輯不改資料。正式附件在再次上架時同步移除。
- 既有 DEL 值原樣保留，不將它作為刪除判斷；未知／空 FileLang 的舊附件顯示在繁中頁籤但不更改值。
- 圖片僅 JPG/JPEG、PNG、GIF、WebP，每張 10 MiB，檢查副檔名與檔頭格式；不寫附件表。
- 圖片存 `wwwroot/Content/User/Image`，保留原檔名，拒絕路徑／不合法檔名。同名以原子建立拒絕，絕不覆蓋。
- 圖片上傳即寫入；取消文章／刪除文章不刪圖片，避免其他文章引用失效。需定期人工清查未引用圖片。
- HTML 儲存及載入編輯器時經 HtmlSanitizer 清理。原始碼模式不繞過後端清理。

## IIS 10

1. 安裝 .NET 10 Hosting Bundle 與 IIS Windows Authentication 功能。
2. 建立獨立網站或應用程式、獨立 App Pool，使用 No Managed Code；啟用 Windows Authentication、停用 Anonymous Authentication。
   驗證設定請在 IIS 管理員／伺服器層設定，避免鎖定 section 造成 HTTP 500.19。
3. `dotnet publish HKPortalAdmin.csproj -c Release -o ./publish`；發布輸出可部署至 IIS，使用 HTTPS。
4. 以本機設定或安全的部署環境變數 `ConnectionStrings__Portal` 提供真實 SQL 帳密。
5. SQL 帳號需具有這九張表的必要 SELECT／INSERT／UPDATE／DELETE 權限與使用 sp_getapplock 的能力；不需要 DDL 權限。
6. 僅授予 App Pool 身分對 `wwwroot/Content/User/Image` 的寫入權限。部署不得清除／覆蓋既有圖片目錄，並安排備份。
7. `web.config` 已設定 ASP.NET Core Module 與 220 MiB request limit；後端仍限制每個檔案 10 MiB。
8. 若前台網站在另一個網域／應用程式，須讓它可讀取 HTML 內 `/Content/User/Image/...` 所指向的相同圖片位置（例如 IIS 共用虛擬目錄），否則後台上傳圖片在前台會失效。

## 驗收

2026-10-30：已用 .NET SDK 10.0.403 完成 Release 建置（0 警告、0 錯誤）。本機 HTTPS 實際驗證已通過：匿名 401、Windows 驗證頁面 200、假帳密提示 503、缺少防偽權杖 400、圖片上傳 200、同名拒絕 409 且原檔未變、錯誤圖片格式 400、空批次拒絕 400，以及英文錯誤訊息。

Kendo 商用檔案尚未放入指定目錄，資料庫仍是假帳密，因此完整 Kendo 畫面操作與 SQL Server CRUD／發布交易整合驗收尚未執行。

請使用獨立的測試資料庫或已備份的測試資料執行 `docs/acceptance.md`；假帳密環境不宣稱資料庫 CRUD 已通過整合測試。

參考：
- https://learn.microsoft.com/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0
- https://www.telerik.com/kendo-jquery-ui/documentation/controls/editor/imagebrowser
