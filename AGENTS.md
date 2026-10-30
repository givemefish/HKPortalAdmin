# 專案開發指引

## 開發者背景

- 開發者是一名工程師，主要使用 .NET Core 建立網站。

## 主要技術

- 後端：ASP.NET Core MVC（.NET 10）
- 前端：jQuery、Kendo UI、Bootstrap 5
- 資料庫：Microsoft SQL Server、Oracle
- 資料存取：優先使用 Dapper

## 開發偏好

- 新增功能時，應優先沿用上述技術與專案既有架構。
- 前端介面使用 Bootstrap 5 進行版面配置，並依需求搭配 jQuery 與 Kendo UI。
- 資料庫操作預設使用 Dapper；除非專案既有設計或需求明確指定，否則不要自行改用其他 ORM。
- 撰寫 SQL 時，需留意 SQL Server 與 Oracle 在語法、資料型別及參數處理上的差異。
- 程式碼應保持清楚、易於維護，並遵循專案現有的命名與格式規範。

## 多語系

- 介面文字使用 `IStringLocalizer<SharedResource>`；英文 fallback 放在 `Resources/SharedResource.resx`，繁體與簡體中文分別放在 `SharedResource.zh-TW.resx`、`SharedResource.zh-CN.resx`，三份資源須維護相同的資源鍵。
- 目前支援 `zh-TW`、`zh-CN`、`en-US`，預設為 `zh-TW`；增減語系時，需同步更新請求本地化設定、語言切換白名單與資源檔。

