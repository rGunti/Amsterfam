using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Amsterfam.Api.Services;

/// <summary>An uploaded image whose type was sniffed from its bytes.</summary>
public record UploadedImage(byte[] Data, string ContentType);

/// <summary>
/// Reading and serving the images stored as <see cref="EventFile"/>s (event banner, news
/// images). Uploads are a raw request body; the client downscales before sending.
/// </summary>
public static class ImageUpload
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Reads the request body as a JPEG, PNG or WebP image of at most <see cref="MaxBytes"/>.
    /// <paramref name="what"/> names the image in error messages, e.g. "Banner".
    /// </summary>
    public static async Task<(UploadedImage? Image, IResult? Error)> ReadAsync(
        HttpRequest request,
        string what
    )
    {
        if (request.ContentLength > MaxBytes)
            return (null, TooLarge(what));

        // Content-Length can be absent (chunked), so check the real size too. Kestrel's own body limit bounds the buffer.
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        if (buffer.Length > MaxBytes)
            return (null, TooLarge(what));
        var data = buffer.ToArray();

        // Trust the bytes, not the client's Content-Type: the sniffed type is what we serve back.
        var contentType = ImageSniffer.Detect(data);
        if (contentType is null)
            return (
                null,
                TypedResults.BadRequest(
                    new { error = $"{what} must be a JPEG, PNG or WebP image." }
                )
            );

        return (new UploadedImage(data, contentType), null);
    }

    /// <summary>
    /// Streams a stored file, or 404 when <paramref name="fileId"/> is null or gone. Callers
    /// must have checked access already. The file id doubles as the ETag, so revalidation
    /// never loads the content.
    /// </summary>
    public static async Task<IResult> ServeAsync(
        AmsterfamDbContext db,
        Guid? fileId,
        HttpRequest request
    )
    {
        request.HttpContext.Response.Headers.CacheControl = "private, no-cache";
        if (fileId is null)
            return TypedResults.NotFound();

        var etag = new EntityTagHeaderValue($"\"{fileId:N}\"");
        if (
            request
                .GetTypedHeaders()
                .IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false))
        )
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);

        var file = await db
            .EventFiles.Where(f => f.Id == fileId)
            .Select(f => new { f.Data, f.ContentType })
            .FirstOrDefaultAsync();
        if (file is null)
            return TypedResults.NotFound();

        return TypedResults.File(file.Data, file.ContentType, entityTag: etag);
    }

    /// <summary>The client's file name without any path, or <paramref name="fallback"/>.</summary>
    public static string CleanFileName(string? fileName, string fallback)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? "");
        if (string.IsNullOrEmpty(name))
            return fallback;
        return name.Length > EventFile.MaxFileNameLength
            ? name[..EventFile.MaxFileNameLength]
            : name;
    }

    private static IResult TooLarge(string what) =>
        TypedResults.Json(
            new { error = $"{what} can be at most {MaxBytes / (1024 * 1024)} MB." },
            statusCode: StatusCodes.Status413PayloadTooLarge
        );
}
