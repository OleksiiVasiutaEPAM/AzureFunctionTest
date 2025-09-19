using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Epam.TestFunction.Options;
using Epam.TestFunction.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Epam.TestFunction.Services;

public class BlobFileService(BlobServiceClient svc, IOptions<BlobReaderOptions> opt, ILogger<BlobFileService> log) 
    : IBlobFileService
{
    private readonly BlobServiceClient _svc = svc;
    private readonly BlobReaderOptions _opt = opt.Value;
    private readonly ILogger<BlobFileService> _log = log;


    public async Task<(string? Text, string ContentType, long Length, string FileName)> ReadTextAsync(
        string container, string blobName, CancellationToken ct = default)
    {
        var (stream, ctType, length, fileName) = await OpenReadAsync(container, blobName, ct);

        // Попытаемся определить кодировку по BOM, иначе UTF-8
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        ms.Position = 0;

        // Лимит: если сильно большой файл — не читаем в память
        if (ms.Length > _opt.MaxBytes)
            throw new InvalidOperationException($"Blob too large: {ms.Length} > {_opt.MaxBytes}");

        var enc = DetectEncoding(ms) ?? Encoding.UTF8;
        ms.Position = 0;
        using var sr = new StreamReader(ms, enc, detectEncodingFromByteOrderMarks: true);
        var text = await sr.ReadToEndAsync();

        // Только для текстовых типов, иначе вернем null (пусть вызывающий решает, что делать)
        if (!IsTextLike(ctType)) return (null, ctType, length, fileName);
        return (text, ctType, length, fileName);
    }

    public async Task<(Stream Content, string ContentType, long Length, string FileName)> OpenReadAsync(
        string container, string blobName, CancellationToken ct = default)
    {
        container = container ?? throw new ArgumentNullException(nameof(container));
        blobName  = blobName ?? throw new ArgumentNullException(nameof(blobName));

        var containerClient = _svc.GetBlobContainerClient(container);
        var blob = containerClient.GetBlobClient(blobName);

        BlobProperties props;
        try
        {
            var propsResp = await blob.GetPropertiesAsync(cancellationToken: ct);
            props = propsResp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"Blob not found: {container}/{blobName}");
        }

        if (props.ContentLength > _opt.MaxBytes)
            _log.LogWarning("Blob {Blob} is {Len} bytes (> {Max}), consider streaming/chunking", blobName, props.ContentLength, _opt.MaxBytes);

        var dl = await blob.DownloadStreamingAsync(cancellationToken: ct);
        var contentType = dl.Value.Details.ContentType ?? "application/octet-stream";
        var name = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        // Скопируем в память, если размер умеренный; иначе можно вернуть dl.Value.Content напрямую (но это NetworkStream)
        var ms = new MemoryStream();
        await dl.Value.Content.CopyToAsync(ms, ct);
        ms.Position = 0;

        return (ms, contentType, props.ContentLength, name);
    }

    private static bool IsTextLike(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        contentType = contentType.ToLowerInvariant();
        return contentType.StartsWith("text/")
            || contentType.Contains("json")
            || contentType.Contains("xml")
            || contentType.Contains("yaml")
            || contentType.Contains("csv")
            || contentType.Contains("markdown");
    }

    private static Encoding? DetectEncoding(Stream s)
    {
        // простая проверка BOM
        var pre = new byte[4];
        var read = s.Read(pre, 0, 4);
        s.Position = 0;

        if (read >= 3 && pre[0] == 0xEF && pre[1] == 0xBB && pre[2] == 0xBF) return Encoding.UTF8;
        if (read >= 2 && pre[0] == 0xFF && pre[1] == 0xFE) return Encoding.Unicode;          // UTF-16 LE
        if (read >= 2 && pre[0] == 0xFE && pre[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE
        if (read >= 4 && pre[0] == 0xFF && pre[1] == 0xFE && pre[2] == 0x00 && pre[3] == 0x00) return Encoding.UTF32;
        if (read >= 4 && pre[0] == 0x00 && pre[1] == 0x00 && pre[2] == 0xFE && pre[3] == 0xFF) return Encoding.GetEncoding(12001); // UTF-32 BE
        return null;
    }
}