using System.Text.Json;
using HKPortalAdmin.Data;
using HKPortalAdmin.Models;
using HKPortalAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HKPortalAdmin.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ArticlesController(PortalRepository repository, ArticleService service, UploadService uploads) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Categories(CancellationToken ct) => Json(await service.GetCategoryTreeAsync(ct));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ArticleQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) throw new PortalException("InvalidInput");
        return Json(await repository.SearchAsync(query, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Defaults(CancellationToken ct)
    {
        var now = await repository.GetNowAsync(ct);
        return Json(new { begin = now.ToString("yyyy-MM-ddTHH:mm:00"), end = "2099-12-31T23:59:59" });
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Json(await service.GetAsync(id, ct));

    [HttpPost]
    [RequestSizeLimit(220L * 1024 * 1024)]
    public async Task<IActionResult> Save([FromForm] string? payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payload)) throw new PortalException("InvalidInput");
        SaveArticleRequest request;
        try { request = JsonSerializer.Deserialize<SaveArticleRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException(); }
        catch (JsonException) { throw new PortalException("InvalidInput"); }
        var id = await service.SaveAsync(request, Request.Form.Files, User, ct);
        return Json(new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Batch([FromBody] BatchRequest? request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) throw new PortalException("InvalidInput");
        await service.BatchAsync(request, User, ct);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await repository.GetAttachmentAsync(id, attachmentId, ct);
        return File(attachment.FileContent, "application/octet-stream", attachment.FileName);
    }

    [HttpPost]
    [RequestSizeLimit(11L * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken ct)
    {
        if (file is null) throw new PortalException("InvalidInput");
        var name = await uploads.SaveImageAsync(file, ct);
        return Json(new { url = Url.Content("~/Content/User/Image/" + Uri.EscapeDataString(name)) });
    }
}
