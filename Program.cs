using HKPortalAdmin;
using HKPortalAdmin.Data;
using HKPortalAdmin.Services;
using HKPortalAdmin.Infrastructure;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<PortalExceptionFilter>();
}).AddViewLocalization().AddDataAnnotationsLocalization(options =>
    options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
// At most 20 new attachments per save; every individual file is limited to 10 MiB.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 220L * 1024 * 1024;
    options.ValueLengthLimit = 8 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 220L * 1024 * 1024);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = 220L * 1024 * 1024);
builder.Services.AddScoped<PortalRepository>();
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<UploadService>();
builder.Services.AddScoped<PortalExceptionFilter>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("zh-TW")
    .AddSupportedCultures("zh-TW", "zh-CN", "en-US")
    .AddSupportedUICultures("zh-TW", "zh-CN", "en-US"));
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Articles}/{action=Index}/{id?}");
app.Run();
