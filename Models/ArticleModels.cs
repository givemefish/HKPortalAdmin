namespace HKPortalAdmin.Models;

public sealed class Category
{
    public int CateId { get; set; }
    public string CateName { get; set; } = "";
    public int ParentCateId { get; set; }
    public string SortOrder { get; set; } = "";
}

public sealed class CategoryNode
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public List<CategoryNode> Items { get; set; } = [];
    public bool Expanded { get; set; }
}

public sealed class Article
{
    public Guid UID { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? SubjectZHTW { get; set; }
    public string? SubjectZHCN { get; set; }
    public string? SubjectEN { get; set; }
    public string? BodyZHTW { get; set; }
    public string? BodyZHCN { get; set; }
    public string? BodyEN { get; set; }
    public DateTime? PrePublishBgnDate { get; set; }
    public DateTime? PrePublishEndDate { get; set; }
    public DateTime? PublishBgnDate { get; set; }
    public DateTime? PublishEndDate { get; set; }
    public DateTime PostDate { get; set; }
    public DateTime BdModDate { get; set; }
    public string ModifiedToken => BdModDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    public string BdModUserId { get; set; } = "";
    public string CategoryNames { get; set; } = "";
    public string Status { get; set; } = "";
    public List<int> CategoryIds { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
}

public sealed class Attachment
{
    public Guid AttID { get; set; }
    public Guid UID { get; set; }
    public string FileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string ExtensionName { get; set; } = "";
    public string? FileLang { get; set; }
    public string? FileStatus { get; set; }
    public byte[] FileContent { get; set; } = [];
}

public sealed class ArticleQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int? CateId { get; set; }
    public string? Keyword { get; set; }
    public string? Status { get; set; }
    public string? SortField { get; set; }
    public string? SortDirection { get; set; }
}

public sealed record ArticlePage(IEnumerable<Article> Data, int Total);

public sealed class SaveArticleRequest
{
    public Guid? UID { get; set; }
    public string? ModifiedToken { get; set; }
    public int? CateId { get; set; }
    public string SubjectZHTW { get; set; } = "";
    public string? SubjectZHCN { get; set; }
    public string? SubjectEN { get; set; }
    public string BodyZHTW { get; set; } = "";
    public string? BodyZHCN { get; set; }
    public string? BodyEN { get; set; }
    public DateTime? PrePublishBgnDate { get; set; }
    public DateTime? PrePublishEndDate { get; set; }
    public List<Guid> RemovedAttachments { get; set; } = [];
}

public sealed record ArticleReference(Guid UID, string ModifiedToken);
public sealed class BatchRequest
{
    public List<ArticleReference> Articles { get; set; } = [];
    public string Action { get; set; } = "";
}

public sealed class PortalException(string key, int status = 400) : Exception(key)
{
    public string Key { get; } = key;
    public int Status { get; } = status;
}
