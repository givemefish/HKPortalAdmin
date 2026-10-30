using Ganss.Xss;
using HKPortalAdmin.Data;
using HKPortalAdmin.Models;
using System.Security.Claims;

namespace HKPortalAdmin.Services;

public sealed class ArticleService(PortalRepository repository, UploadService uploads)
{
    public static string GetUsername(ClaimsPrincipal principal)
    {
        var identity = principal.Identity;
        if (identity?.IsAuthenticated != true) throw new PortalException("Unauthenticated", 401);
        var user = (identity.Name ?? "").Split('\\').Last();
        // Existing columns are varchar(8); never truncate an identity.
        if (user.Length is < 1 or > 8 || user.Any(c => c > 127 || char.IsControl(c)))
            throw new PortalException("UsernameTooLong");
        return user;
    }

    public async Task<List<CategoryNode>> GetCategoryTreeAsync(CancellationToken ct)
    {
        var categories = await repository.GetCategoriesAsync(ct);
        var index = categories.ToDictionary(x => x.CateId);
        if (!index.ContainsKey(0)) throw new PortalException("InvalidCategories", 409);
        // Validate every branch, including unreachable cycles and missing parents.
        foreach (var category in categories)
        {
            var visited = new HashSet<int>();
            var current = category;
            while (current.CateId != 0)
            {
                if (!visited.Add(current.CateId) || !index.TryGetValue(current.ParentCateId, out var parent))
                    throw new PortalException("InvalidCategories", 409);
                current = parent;
            }
        }
        var nodes = categories.ToDictionary(x => x.CateId, x => new CategoryNode { Id = x.CateId, Text = x.CateName, Expanded = x.CateId == 0 });
        foreach (var category in categories.Where(x => x.CateId != 0)) nodes[category.ParentCateId].Items.Add(nodes[category.CateId]);
        return [nodes[0]];
    }

    public async Task<Article> GetAsync(Guid id, CancellationToken ct)
    {
        var article = await repository.GetAsync(id, ct);
        // Legacy HTML is also sanitized before being passed to the editor.
        var sanitizer = new HtmlSanitizer();
        article.Body = sanitizer.Sanitize(article.Body);
        article.BodyZHTW = sanitizer.Sanitize(article.BodyZHTW ?? article.Body);
        article.BodyZHCN = sanitizer.Sanitize(article.BodyZHCN ?? "");
        article.BodyEN = sanitizer.Sanitize(article.BodyEN ?? "");
        return article;
    }

    public async Task<Guid> SaveAsync(SaveArticleRequest request, IFormFileCollection files, ClaimsPrincipal principal, CancellationToken ct)
    {
        var user = GetUsername(principal);
        if (request.RemovedAttachments is null) throw new PortalException("InvalidInput");
        if (request.CateId is null) throw new PortalException("CategoryRequired");
        if (string.IsNullOrWhiteSpace(request.SubjectZHTW) || request.SubjectZHTW.Length > 256 ||
            (request.SubjectZHCN?.Length ?? 0) > 256 || (request.SubjectEN?.Length ?? 0) > 256)
            throw new PortalException("InvalidTitle");
        if (request.PrePublishBgnDate is null || request.PrePublishEndDate is null ||
            request.PrePublishBgnDate < new DateTime(1753, 1, 1) || request.PrePublishEndDate < new DateTime(1753, 1, 1) ||
            request.PrePublishBgnDate > request.PrePublishEndDate)
            throw new PortalException("InvalidDates");
        if (new[] { request.BodyZHTW, request.BodyZHCN, request.BodyEN }.Any(x => (x?.Length ?? 0) > 2_000_000) ||
            request.RemovedAttachments.Count > 1000 || files.Count > 20) throw new PortalException("InvalidInput");
        var sanitizer = new HtmlSanitizer();
        request.BodyZHTW = sanitizer.Sanitize(request.BodyZHTW ?? "");
        request.BodyZHCN = sanitizer.Sanitize(request.BodyZHCN ?? "");
        request.BodyEN = sanitizer.Sanitize(request.BodyEN ?? "");
        var attachments = new List<Attachment>();
        foreach (var file in files)
        {
            if (file.Name is not ("ZHTW" or "ZHCN" or "EN")) throw new PortalException("InvalidInput");
            var bytes = await uploads.ReadAttachmentAsync(file, ct);
            attachments.Add(new Attachment
            {
                AttID = Guid.NewGuid(), FileName = file.FileName, Title = file.FileName,
                ExtensionName = Path.GetExtension(file.FileName).TrimStart('.'), FileContent = bytes, FileLang = file.Name, FileStatus = "OK"
            });
        }
        return await repository.SaveAsync(request, attachments, user, ct);
    }

    public Task BatchAsync(BatchRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (request.Articles is null || request.Articles.Any(x => x is null) ||
            request.Action is not ("Pending" or "Publish" or "Offline" or "Delete") || request.Articles.Count is < 1 or > 100 ||
            request.Articles.Select(x => x.UID).Distinct().Count() != request.Articles.Count)
            throw new PortalException("InvalidInput");
        return repository.BatchAsync(request, GetUsername(principal), ct);
    }
}
