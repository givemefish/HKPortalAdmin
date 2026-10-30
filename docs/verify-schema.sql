-- Read-only preflight. Run against a test copy of EPCHK before enabling writes.
SET NOCOUNT ON;

SELECT DB_NAME() AS DatabaseName, SERVERPROPERTY('ProductVersion') AS SqlServerVersion,
       GETDATE() AS DatabaseLocalTime;

SELECT t.name AS TableName,c.column_id,c.name AS ColumnName,ty.name AS DataType,
       c.max_length,c.is_nullable
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE s.name=N'dbo' AND t.name IN
 (N'kmCategory',N'kmAnnounce',N'kmAnnounceRaw',N'kmAnnounceCategory',N'kmAnnounceCategoryRaw',
  N'kmAnnounceAttribute',N'kmAnnounceAttributeRaw',N'kmAttachment',N'kmAttachmentRaw')
ORDER BY t.name,c.column_id;

-- A root must exist. A self-reference for CateId=0 is intentional.
SELECT CateId,ParentCateId FROM dbo.kmCategory WHERE CateId=0;
SELECT c.CateId,c.ParentCateId FROM dbo.kmCategory c
LEFT JOIN dbo.kmCategory p ON p.CateId=c.ParentCateId
WHERE c.CateId<>0 AND (p.CateId IS NULL OR c.CateId=c.ParentCateId);

-- The UI is single-category, but the database still supports multiple categories.
SELECT UID,COUNT(*) AS CategoryCount FROM dbo.kmAnnounceCategoryRaw
GROUP BY UID HAVING COUNT(*)>1;

-- Attribute names are unique within each article by business rule.
SELECT UID,AttrName,COUNT(*) AS DuplicateCount FROM dbo.kmAnnounceAttributeRaw
GROUP BY UID,AttrName HAVING COUNT(*)>1;

-- Formal attachments additionally enforce AttID uniqueness.
SELECT AttID,COUNT(*) AS DuplicateCount FROM dbo.kmAttachmentRaw
GROUP BY AttID HAVING COUNT(*)>1;

SELECT UID,PublishBgnDate,PublishEndDate FROM dbo.kmAnnounceRaw
WHERE (PublishBgnDate IS NULL AND PublishEndDate IS NOT NULL)
   OR (PublishBgnDate IS NOT NULL AND PublishEndDate IS NULL)
   OR PublishEndDate<PublishBgnDate;

SELECT FileLang,FileStatus,COUNT(*) AS AttachmentCount
FROM dbo.kmAttachmentRaw GROUP BY FileLang,FileStatus;
