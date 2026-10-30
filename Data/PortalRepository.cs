using System.Data;
using System.Globalization;
using Dapper;
using HKPortalAdmin.Models;
using Microsoft.Data.SqlClient;

namespace HKPortalAdmin.Data;

public sealed class PortalRepository(IConfiguration configuration)
{
    private const string ArticleColumns = "bdAddUserId,bdAddDate,bdModUserId,bdModDate,UID,CorpId,UserId,Importance,Subject,Summary,Body,PostDate,PublishBgnDate,PublishEndDate,bodyRtf,prePublishBgnDate,prePublishEndDate,SubjectEN,SubjectZHTW,SubjectZHCN,BodyEN,BodyZHTW,BodyZHCN";
    private const string AttachmentColumns = "bdAddUserID,bdAddDate,bdModUserID,bdModDate,AttID,UID,FileName,Title,ExtensionName,FileContent,UploaderUserID,UploadDate,FileStatus,FileLang";
    // GETDATE() and UI defaults both use the database server's local clock.
    private const string StateSql = "CASE WHEN a.PublishBgnDate IS NULL AND a.PublishEndDate IS NULL THEN 'Pending' WHEN a.PublishBgnDate IS NULL OR a.PublishEndDate IS NULL OR a.PublishEndDate<a.PublishBgnDate THEN 'Invalid' WHEN a.PublishEndDate<@Now THEN 'Offline' WHEN a.PublishBgnDate>@Now THEN 'Scheduled' ELSE 'Published' END";

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("Portal");
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("CHANGE_ME", StringComparison.Ordinal))
            throw new PortalException("ConfigureDatabase", 503);
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<DateTime> GetNowAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT GETDATE()", cancellationToken: ct));
    }

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return (await db.QueryAsync<Category>(new CommandDefinition("SELECT CateId,CateName,ParentCateId,SortOrder FROM dbo.kmCategory ORDER BY TRY_CONVERT(int,SortOrder),SortOrder,CateId", cancellationToken: ct))).ToList();
    }

    public async Task<ArticlePage> SearchAsync(ArticleQuery query, CancellationToken ct)
    {
        query.Page = Math.Max(query.Page, 1);
        query.PageSize = Math.Clamp(query.PageSize, 1, 100);
        if (query.Page > 1000000 || (query.Keyword?.Length ?? 0) > 256) throw new PortalException("InvalidInput");
        var sort = query.SortField switch
        {
            "subject" => "a.Subject", "postDate" => "a.PostDate", "bdModDate" => "a.bdModDate",
            "publishBgnDate" => "a.PublishBgnDate", "publishEndDate" => "a.PublishEndDate", _ => "a.bdModDate"
        };
        var direction = query.SortDirection == "asc" ? "ASC" : "DESC";
        var keyword = query.Keyword?.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        var where = $"""
            WHERE (@CateId IS NULL OR EXISTS(SELECT 1 FROM dbo.kmAnnounceCategoryRaw c WHERE c.UID=a.UID AND c.CateId=@CateId))
            AND (@Keyword IS NULL OR a.Subject LIKE @Keyword ESCAPE '~' OR a.SubjectEN LIKE @Keyword ESCAPE '~' OR a.SubjectZHCN LIKE @Keyword ESCAPE '~')
            AND (@Status IS NULL OR {StateSql}=@Status)
            """;
        await using var db = await OpenAsync(ct);
        // A single snapshot of the clock is used for the count and page.
        var sql = $"""
            DECLARE @Now datetime=GETDATE();
            SELECT COUNT(*) FROM dbo.kmAnnounceRaw a {where};
            SELECT a.UID,a.Subject,a.PostDate,a.bdModDate,a.bdModUserId,a.PublishBgnDate,a.PublishEndDate,
              {StateSql} AS Status,
              (SELECT STRING_AGG(CONVERT(nvarchar(max),c.CateName),N' / ')
               FROM dbo.kmAnnounceCategoryRaw ac JOIN dbo.kmCategory c ON c.CateId=ac.CateId WHERE ac.UID=a.UID) AS CategoryNames
            FROM dbo.kmAnnounceRaw a {where}
            ORDER BY {sort} {direction},a.UID
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        using var result = await db.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            query.CateId, Keyword = string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword}%",
            Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
            Skip = (query.Page - 1) * query.PageSize, Take = query.PageSize
        }, cancellationToken: ct));
        var total = await result.ReadSingleAsync<int>();
        return new ArticlePage(await result.ReadAsync<Article>(), total);
    }

    public async Task<Article> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        // Serializable read prevents mixing article and attachment versions during a concurrent save.
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockAsync(db, tx, id, ct);
        var sql = $"""
            DECLARE @Now datetime=GETDATE();
            SELECT a.UID,a.Subject,a.Body,a.SubjectZHTW,a.SubjectZHCN,a.SubjectEN,a.BodyZHTW,a.BodyZHCN,a.BodyEN,
              a.prePublishBgnDate,a.prePublishEndDate,a.PublishBgnDate,a.PublishEndDate,a.PostDate,a.bdModDate,a.bdModUserId,
              {StateSql} AS Status FROM dbo.kmAnnounceRaw a WHERE a.UID=@id;
            SELECT CateId FROM dbo.kmAnnounceCategoryRaw WHERE UID=@id ORDER BY CateId;
            SELECT AttID,UID,FileName,Title,ExtensionName,FileLang,FileStatus FROM dbo.kmAttachmentRaw WHERE UID=@id ORDER BY UploadDate,AttID;
            """;
        Article article;
        using (var result = await db.QueryMultipleAsync(new CommandDefinition(sql, new { id }, tx, cancellationToken: ct)))
        {
            article = await result.ReadSingleOrDefaultAsync<Article>() ?? throw new PortalException("NotFound", 404);
            article.CategoryIds = (await result.ReadAsync<int>()).ToList();
            article.Attachments = (await result.ReadAsync<Attachment>()).ToList();
        }
        await tx.CommitAsync(ct);
        return article;
    }

    public async Task<Attachment> GetAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<Attachment>(new CommandDefinition(
            "SELECT AttID,UID,FileName,FileContent FROM dbo.kmAttachmentRaw WHERE UID=@id AND AttID=@attachmentId",
            new { id, attachmentId }, cancellationToken: ct)) ?? throw new PortalException("NotFound", 404);
    }

    private static async Task LockAsync(SqlConnection db, SqlTransaction tx, Guid id, CancellationToken ct)
    {
        // Serialize this application's mutations even when the article has no row yet.
        var code = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @result;
            """, new { Resource = $"HKPortalAdmin:Article:{id:D}" }, tx, cancellationToken: ct));
        if (code < 0) throw new PortalException("Conflict", 409);
    }

    private static async Task<DateTime> CheckVersionAsync(SqlConnection db, SqlTransaction tx, Guid id, string? token, CancellationToken ct)
    {
        var actual = await db.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT bdModDate FROM dbo.kmAnnounceRaw WITH (UPDLOCK,HOLDLOCK) WHERE UID=@id", new { id }, tx, cancellationToken: ct));
        if (actual is null) throw new PortalException("NotFound", 404);
        if (!DateTime.TryParseExact(token, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expected) || actual.Value.Ticks != expected.Ticks)
            throw new PortalException("Conflict", 409);
        return actual.Value;
    }

    public async Task<Guid> SaveAsync(SaveArticleRequest request, List<Attachment> uploads, string user, CancellationToken ct)
    {
        var id = request.UID ?? Guid.NewGuid();
        await using var db = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockAsync(db, tx, id, ct);
        DateTime? previous = null;
        if (request.UID.HasValue) previous = await CheckVersionAsync(db, tx, id, request.ModifiedToken, ct);
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.kmCategory WHERE CateId=@CateId", new { request.CateId }, tx, cancellationToken: ct)) != 1)
            throw new PortalException("CategoryRequired");
        // Never silently discard unexpected existing multi-category relationships.
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.kmAnnounceCategoryRaw WHERE UID=@id", new { id }, tx, cancellationToken: ct)) > 1)
            throw new PortalException("MultipleCategories", 409);
        var now = await db.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT GETDATE()", transaction: tx, cancellationToken: ct));
        var modified = previous.HasValue && now <= previous.Value ? previous.Value.AddMilliseconds(10) : now;
        var values = new
        {
            UID = id, User = user, Now = now, Modified = modified, request.CateId,
            request.SubjectZHTW, request.SubjectZHCN, request.SubjectEN, request.BodyZHTW, request.BodyZHCN, request.BodyEN,
            request.PrePublishBgnDate, request.PrePublishEndDate
        };
        var sql = request.UID.HasValue ? """
            UPDATE dbo.kmAnnounceRaw SET bdModUserId=@User,bdModDate=@Modified,CorpId='8800',UserId=@User,Importance='0',Summary=N'',
              Subject=@SubjectZHTW,Body=@BodyZHTW,SubjectZHTW=@SubjectZHTW,SubjectZHCN=@SubjectZHCN,SubjectEN=@SubjectEN,
              BodyZHTW=@BodyZHTW,BodyZHCN=@BodyZHCN,BodyEN=@BodyEN,prePublishBgnDate=@PrePublishBgnDate,prePublishEndDate=@PrePublishEndDate,
              PublishBgnDate=NULL,PublishEndDate=NULL WHERE UID=@UID;
            """ : """
            INSERT dbo.kmAnnounceRaw
              (UID,bdAddUserId,bdAddDate,bdModUserId,bdModDate,CorpId,UserId,Importance,Subject,Summary,Body,PostDate,
               PublishBgnDate,PublishEndDate,prePublishBgnDate,prePublishEndDate,SubjectZHTW,SubjectZHCN,SubjectEN,BodyZHTW,BodyZHCN,BodyEN)
            VALUES (@UID,@User,@Now,@User,@Modified,'8800',@User,'0',@SubjectZHTW,N'',@BodyZHTW,@Now,
               NULL,NULL,@PrePublishBgnDate,@PrePublishEndDate,@SubjectZHTW,@SubjectZHCN,@SubjectEN,@BodyZHTW,@BodyZHCN,@BodyEN);
            """;
        await db.ExecuteAsync(new CommandDefinition(sql, values, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM dbo.kmAnnounceCategoryRaw WHERE UID=@UID;
            INSERT dbo.kmAnnounceCategoryRaw(UID,CateId) VALUES(@UID,@CateId);
            UPDATE dbo.kmAnnounce SET PublishBgnDate=NULL,PublishEndDate=NULL WHERE UID=@UID;
            """, values, tx, cancellationToken: ct));
        if (request.RemovedAttachments.Count > 0)
        {
            var removed = await db.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.kmAttachmentRaw WHERE UID=@id AND AttID IN @Ids",
                new { id, Ids = request.RemovedAttachments.Distinct().ToArray() }, tx, cancellationToken: ct));
            if (removed != request.RemovedAttachments.Distinct().Count()) throw new PortalException("Conflict", 409);
        }
        foreach (var attachment in uploads)
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT dbo.kmAttachmentRaw
                (bdAddUserID,bdAddDate,bdModUserID,bdModDate,AttID,UID,FileName,Title,ExtensionName,FileContent,UploaderUserID,UploadDate,FileStatus,FileLang)
                VALUES(@User,@Now,@User,@Now,@AttID,@UID,@FileName,@Title,@ExtensionName,@FileContent,@User,@Now,'OK',@FileLang);
                """, new { User = user, Now = now, UID = id, attachment.AttID, attachment.FileName, attachment.Title,
                    attachment.ExtensionName, attachment.FileContent, attachment.FileLang }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task BatchAsync(BatchRequest request, string user, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = await db.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT GETDATE()", transaction: tx, cancellationToken: ct));
        foreach (var item in request.Articles.OrderBy(x => x.UID))
        {
            await LockAsync(db, tx, item.UID, ct);
            var previous = await CheckVersionAsync(db, tx, item.UID, item.ModifiedToken, ct);
            var args = new { item.UID, User = user, Now = now, Modified = now <= previous ? previous.AddMilliseconds(10) : now };
            if (request.Action == "Delete")
            {
                await db.ExecuteAsync(new CommandDefinition("""
                    DELETE FROM dbo.kmAttachment WHERE UID=@UID;
                    DELETE FROM dbo.kmAnnounceAttribute WHERE UID=@UID;
                    DELETE FROM dbo.kmAnnounceCategory WHERE UID=@UID;
                    DELETE FROM dbo.kmAnnounce WHERE UID=@UID;
                    DELETE FROM dbo.kmAttachmentRaw WHERE UID=@UID;
                    DELETE FROM dbo.kmAnnounceAttributeRaw WHERE UID=@UID;
                    DELETE FROM dbo.kmAnnounceCategoryRaw WHERE UID=@UID;
                    DELETE FROM dbo.kmAnnounceRaw WHERE UID=@UID;
                    """, args, tx, cancellationToken: ct));
                continue;
            }
            if (request.Action == "Publish")
            {
                var invalid = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM dbo.kmAnnounceRaw WHERE UID=@UID AND
                    (prePublishBgnDate IS NULL OR prePublishEndDate IS NULL OR prePublishBgnDate>prePublishEndDate);
                    """, args, tx, cancellationToken: ct));
                if (invalid != 0) throw new PortalException("InvalidDates");
                if (await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM (SELECT AttrName FROM dbo.kmAnnounceAttributeRaw WHERE UID=@UID GROUP BY AttrName HAVING COUNT(*)>1) d;
                    """, args, tx, cancellationToken: ct)) > 0) throw new PortalException("DuplicateAttributes", 409);
                await db.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.kmAnnounceRaw SET PublishBgnDate=prePublishBgnDate,PublishEndDate=prePublishEndDate,PostDate=@Now,
                    bdModUserId=@User,bdModDate=@Modified WHERE UID=@UID;
                    """, args, tx, cancellationToken: ct));
                await PublishAsync(db, tx, item.UID, ct);
            }
            else
            {
                var dates = request.Action == "Pending" ? "PublishBgnDate=NULL,PublishEndDate=NULL" : "PublishBgnDate=DATEADD(day,-1,@Now),PublishEndDate=DATEADD(day,-1,@Now)";
                await db.ExecuteAsync(new CommandDefinition($"""
                    UPDATE dbo.kmAnnounceRaw SET {dates},bdModUserId=@User,bdModDate=@Modified WHERE UID=@UID;
                    UPDATE dbo.kmAnnounce SET {dates} WHERE UID=@UID;
                    """, args, tx, cancellationToken: ct));
            }
        }
        await tx.CommitAsync(ct);
    }

    private static async Task PublishAsync(SqlConnection db, SqlTransaction tx, Guid id, CancellationToken ct)
    {
        // Identifiers below are compile-time constants, never request input. No SELECT * schema dependency.
        var assignments = string.Join(",", ArticleColumns.Split(',').Where(x => x != "UID").Select(x => $"p.[{x}]=r.[{x}]"));
        var sql = $"""
            UPDATE p SET {assignments} FROM dbo.kmAnnounce p JOIN dbo.kmAnnounceRaw r ON r.UID=p.UID WHERE p.UID=@id;
            IF @@ROWCOUNT=0 INSERT dbo.kmAnnounce({ArticleColumns}) SELECT {ArticleColumns} FROM dbo.kmAnnounceRaw WHERE UID=@id;
            DELETE FROM dbo.kmAnnounceCategory WHERE UID=@id;
            INSERT dbo.kmAnnounceCategory(UID,CateId) SELECT UID,CateId FROM dbo.kmAnnounceCategoryRaw WHERE UID=@id;
            DELETE FROM dbo.kmAnnounceAttribute WHERE UID=@id;
            INSERT dbo.kmAnnounceAttribute(UID,AID,AttrName,AttrValue) SELECT UID,AID,AttrName,AttrValue FROM dbo.kmAnnounceAttributeRaw WHERE UID=@id;
            DELETE FROM dbo.kmAttachment WHERE UID=@id;
            INSERT dbo.kmAttachment({AttachmentColumns}) SELECT {AttachmentColumns} FROM dbo.kmAttachmentRaw WHERE UID=@id;
            """;
        await db.ExecuteAsync(new CommandDefinition(sql, new { id }, tx, cancellationToken: ct));
    }
}
