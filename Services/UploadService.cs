using HKPortalAdmin.Models;

namespace HKPortalAdmin.Services;

public sealed class UploadService(IWebHostEnvironment environment)
{
    public const long MaximumFileSize = 10L * 1024 * 1024;
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".dll", ".sys", ".scr", ".cpl", ".msi", ".msp", ".mst", ".bat", ".cmd", ".ps1", ".psm1", ".psd1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".url", ".reg", ".sh", ".bash", ".app", ".jar",
        ".asp", ".aspx", ".ashx", ".asmx", ".php", ".cgi", ".pl", ".py", ".rb", ".html", ".htm", ".svg", ".iso", ".img"
    };

    private static void ValidateFile(IFormFile file)
    {
        if (file.Length is <= 0 or > MaximumFileSize) throw new PortalException("FileSize");
        var name = file.FileName;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name != Path.GetFileName(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') ||
            name.EndsWith('.') || name.EndsWith(' ') || name.Any(char.IsControl) || name.StartsWith('.'))
            throw new PortalException("InvalidFilename");
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new PortalException("InvalidFilename");
    }

    public async Task<byte[]> ReadAttachmentAsync(IFormFile file, CancellationToken ct)
    {
        ValidateFile(file);
        var extension = Path.GetExtension(file.FileName);
        if (extension.Length is < 2 or > 11 || file.FileName.Split('.').Skip(1).Any(x => BlockedExtensions.Contains("." + x)))
            throw new PortalException("FileType");
        var bytes = await ReadBytesAsync(file, ct);
        // Reject renamed native executables as well as known executable extensions.
        if ((bytes.Length >= 2 && bytes[0] == 'M' && bytes[1] == 'Z') ||
            (bytes.Length >= 4 && bytes[0] == 0x7f && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'F') ||
            (bytes.Length >= 2 && bytes[0] == '#' && bytes[1] == '!')) throw new PortalException("FileType");
        return bytes;
    }

    private static async Task<byte[]> ReadBytesAsync(IFormFile file, CancellationToken ct)
    {
        await using var source = file.OpenReadStream();
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        int length;
        while ((length = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (target.Length + length > MaximumFileSize) throw new PortalException("FileSize");
            await target.WriteAsync(buffer.AsMemory(0, length), ct);
        }
        return target.ToArray();
    }

    public async Task<string> SaveImageAsync(IFormFile file, CancellationToken ct)
    {
        ValidateFile(file);
        var bytes = await ReadBytesAsync(file, ct);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var valid = extension switch
        {
            ".jpg" or ".jpeg" => bytes.Length > 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".gif" => bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8),
            ".webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new PortalException("ImageType");
        var folder = Path.Combine(environment.WebRootPath, "Content", "User", "Image");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, file.FileName);
        FileStream output;
        // Atomic creation: a concurrent upload can never overwrite the original name.
        try { output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true); }
        catch (IOException) when (File.Exists(path)) { throw new PortalException("ImageExists", 409); }
        try
        {
            await using (output) await output.WriteAsync(bytes, ct);
        }
        catch
        {
            File.Delete(path); // Only the file successfully created by this request.
            throw;
        }
        return file.FileName;
    }
}
