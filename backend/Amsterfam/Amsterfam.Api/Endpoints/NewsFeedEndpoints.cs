using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// Private per-member Atom feeds of an event's news (ADR-016). Members manage their own feed
/// link; the feed itself is the API's only anonymous data endpoint, authorised by the token.
/// </summary>
public static class NewsFeedEndpoints
{
    public const int MaxEntries = 50;

    /// <summary>How stale <see cref="NewsFeedToken.LastUsedAt"/> may get before a fetch updates it.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromHours(1);

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public static IEndpointRouteBuilder MapNewsFeedEndpoints(this IEndpointRouteBuilder app)
    {
        var manage = app.MapGroup("/api/v1/events/{eventId:guid}/news/feed-token")
            .RequireAuthorization();
        manage.MapGet("/", GetToken);
        manage.MapPost("/", CreateToken).NotLoggedToTimeline("personal feed token");
        manage.MapDelete("/", RevokeToken).NotLoggedToTimeline("personal feed token");

        // Readers and podcast-style clients may check with HEAD first; Kestrel drops the body.
        string[] getOrHead = [HttpMethods.Get, HttpMethods.Head];
        app.MapMethods("/api/v1/feeds/{token}/news.atom", getOrHead, GetFeed).AllowAnonymous();
        app.MapMethods("/api/v1/feeds/{token}/images/{postId:int}.{ext:alpha}", getOrHead, GetImage)
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Same rules as reading the news: non-members get 404, pending members 403, and cancelled
    /// events are hidden from everyone but organisers.
    /// </summary>
    private static async Task<(int? UserId, IResult? Error)> AuthorizeAsync(
        AmsterfamDbContext db,
        Guid eventId,
        ICurrentUserService currentUser
    )
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId))
            return (null, TypedResults.NotFound());

        var user = await currentUser.GetOrCreateAsync();
        var role = await EventGuards.RoleOf(db, eventId, user.Id);
        if (role is null)
            return (null, TypedResults.NotFound());
        if (!EventGuards.IsConfirmed(role))
            return (null, TypedResults.Forbid());
        if (await EventGuards.EnsureVisibleAsync(db, eventId, user.Id) is { } hidden)
            return (null, hidden);

        return (user.Id, null);
    }

    private static Task<NewsFeedToken?> ActiveTokenAsync(
        AmsterfamDbContext db,
        Guid eventId,
        int userId
    ) =>
        db.NewsFeedTokens.FirstOrDefaultAsync(t =>
            t.EventId == eventId && t.UserId == userId && t.RevokedAt == null
        );

    private static async Task<IResult> GetToken(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (userId, error) = await AuthorizeAsync(db, eventId, currentUser);
        if (userId is null)
            return error!;

        var token = await ActiveTokenAsync(db, eventId, userId.Value);
        return TypedResults.Ok(
            new NewsFeedTokenStatus(token is not null, token?.CreatedAt, token?.LastUsedAt)
        );
    }

    /// <summary>Creates the caller's feed link, revoking any previous one. The URL is only returned here.</summary>
    private static async Task<IResult> CreateToken(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        IOptions<AppOptions> app
    )
    {
        var (userId, error) = await AuthorizeAsync(db, eventId, currentUser);
        if (userId is null)
            return error!;

        var now = time.GetUtcNow();
        var token = SecureToken.New();

        // Revoke first, in its own save, so the "one active token" index never sees two.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await ActiveTokenAsync(db, eventId, userId.Value) is { } previous)
        {
            previous.RevokedAt = now;
            await db.SaveChangesAsync();
        }

        db.NewsFeedTokens.Add(
            new NewsFeedToken
            {
                UserId = userId.Value,
                EventId = eventId,
                TokenHash = SecureToken.Hash(token),
                CreatedAt = now,
            }
        );
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another request created a link at the same moment.
            return TypedResults.Conflict(
                new { error = "A feed link was just created. Try again." }
            );
        }
        await transaction.CommitAsync();

        return TypedResults.Ok(new NewsFeedTokenCreated(FeedUrl(app.Value, token), now));
    }

    private static async Task<IResult> RevokeToken(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var (userId, error) = await AuthorizeAsync(db, eventId, currentUser);
        if (userId is null)
            return error!;

        if (await ActiveTokenAsync(db, eventId, userId.Value) is { } token)
        {
            token.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static string FeedBase(AppOptions app, string token) =>
        $"{app.BaseUrl}/api/v1/feeds/{token}";

    private static string FeedUrl(AppOptions app, string token) =>
        $"{FeedBase(app, token)}/news.atom";

    /// <summary>
    /// The active token, with its event, if its owner may still read the event's news:
    /// a confirmed member, and the event not hidden from them. Null otherwise.
    /// </summary>
    private static async Task<NewsFeedToken?> ResolveAsync(AmsterfamDbContext db, string token)
    {
        var hash = SecureToken.Hash(token);
        var feedToken = await db
            .NewsFeedTokens.Include(t => t.Event)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);
        if (feedToken is null)
            return null;

        var role = await EventGuards.RoleOf(db, feedToken.EventId, feedToken.UserId);
        return
            EventGuards.IsConfirmed(role)
            && !EventStateMachine.IsHiddenFrom(
                feedToken.Event.Status,
                role == AttendanceRole.Organiser
            )
            ? feedToken
            : null;
    }

    /// <summary>
    /// The Atom feed. Every check runs on each request against the token's owner, and every
    /// failure is a plain 404 so the endpoint gives nothing away.
    /// </summary>
    private static async Task<IResult> GetFeed(
        string token,
        HttpRequest request,
        AmsterfamDbContext db,
        TimeProvider time,
        IOptions<AppOptions> app
    )
    {
        var feedToken = await ResolveAsync(db, token);
        if (feedToken is null)
            return TypedResults.NotFound();

        var ev = feedToken.Event;

        var now = time.GetUtcNow();
        if (feedToken.LastUsedAt is null || now - feedToken.LastUsedAt >= LastUsedResolution)
        {
            feedToken.LastUsedAt = now;
            await db.SaveChangesAsync();
        }

        var published = db.NewsPosts.Where(p => p.EventId == ev.Id && p.PublishedAt != null);
        var stamp = await published
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                MaxId = g.Max(p => p.Id),
                LastPublished = g.Max(p => p.PublishedAt),
                LastEdited = g.Max(p => p.EditedAt),
            })
            .FirstOrDefaultAsync();

        // Count and highest id change when a post is deleted, which the dates alone wouldn't.
        var etag = new EntityTagHeaderValue(
            $"\"{SecureToken.Hash($"{ev.Name}|{stamp?.Count}|{stamp?.MaxId}|{stamp?.LastPublished:O}|{stamp?.LastEdited:O}")[..32]}\""
        );
        var response = request.HttpContext.Response;
        response.Headers.CacheControl = "private, max-age=300";
        response.Headers.ETag = etag.ToString();
        if (
            request
                .GetTypedHeaders()
                .IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false))
        )
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);

        var posts = await published
            .OrderByDescending(p => p.PublishedAt)
            .ThenByDescending(p => p.Id)
            .Take(MaxEntries)
            .Include(p => p.Author)
            .ToListAsync();

        var updated =
            Latest(stamp?.LastPublished, stamp?.LastEdited)
            ?? new DateTimeOffset(DateTime.SpecifyKind(ev.CreatedAt, DateTimeKind.Utc));
        var imageIds = posts.Where(p => p.ImageFileId != null).Select(p => p.ImageFileId);
        var images = await db
            .EventFiles.Where(f => imageIds.Contains(f.Id))
            .Select(f => new FeedImage(f.Id, f.ContentType, f.Size))
            .ToDictionaryAsync(f => f.Id);

        var feed = BuildFeed(app.Value, FeedBase(app.Value, token), ev, posts, images, updated);
        return TypedResults.Bytes(feed, "application/atom+xml; charset=utf-8");
    }

    private record FeedImage(Guid Id, string ContentType, long Size);

    /// <summary>A file extension for the image URL; some readers go by it rather than the type.</summary>
    private static string Extension(string contentType) =>
        contentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "img",
        };

    /// <summary>
    /// A published post's image, for the feed. The extension in the URL is only for readers. Guarded by the same token checks as the feed,
    /// so it exposes nothing the feed link doesn't already (ADR-016).
    /// </summary>
    private static async Task<IResult> GetImage(
        string token,
        int postId,
        HttpRequest request,
        AmsterfamDbContext db
    )
    {
        var feedToken = await ResolveAsync(db, token);
        if (feedToken is null)
            return TypedResults.NotFound();

        var imageFileId = await db
            .NewsPosts.Where(p =>
                p.Id == postId && p.EventId == feedToken.EventId && p.PublishedAt != null
            )
            .Select(p => p.ImageFileId)
            .FirstOrDefaultAsync();
        return await ImageUpload.ServeAsync(db, imageFileId, request);
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b
        : b is null ? a
        : a > b ? a
        : b;

    private static byte[] BuildFeed(
        AppOptions app,
        string feedBase,
        Event ev,
        IReadOnlyList<NewsPost> posts,
        IReadOnlyDictionary<Guid, FeedImage> images,
        DateTimeOffset updated
    )
    {
        var newsUrl = app.NewsUrl(ev.Id);
        var feed = new XElement(
            Atom + "feed",
            new XElement(Atom + "id", newsUrl),
            new XElement(Atom + "title", $"{ev.Name} — News"),
            new XElement(Atom + "updated", Timestamp(updated)),
            new XElement(
                Atom + "link",
                new XAttribute("rel", "alternate"),
                new XAttribute("href", newsUrl)
            ),
            new XElement(Atom + "generator", "Amsterfam")
        );

        foreach (var post in posts)
        {
            var url = app.NewsPostUrl(ev.Id, post.Id);
            var title = post.Title ?? NewsText.Excerpt(post.Body, 60).Text;
            var html = NewsText.Html(post.Body, app.BaseUrl);

            // The file id in the query makes readers fetch a replaced image again.
            XElement? enclosure = null;
            if (post.ImageFileId is { } fileId && images.TryGetValue(fileId, out var image))
            {
                var imageUrl =
                    $"{feedBase}/images/{post.Id}.{Extension(image.ContentType)}?v={fileId:N}";
                html =
                    $"<p><img src=\"{WebUtility.HtmlEncode(imageUrl)}\" alt=\"{WebUtility.HtmlEncode(title)}\" /></p>\n{html}";
                enclosure = new XElement(
                    Atom + "link",
                    new XAttribute("rel", "enclosure"),
                    new XAttribute("type", image.ContentType),
                    new XAttribute("length", image.Size),
                    new XAttribute("href", imageUrl)
                );
            }

            feed.Add(
                new XElement(
                    Atom + "entry",
                    new XElement(Atom + "id", url),
                    new XElement(Atom + "title", title),
                    new XElement(
                        Atom + "link",
                        new XAttribute("rel", "alternate"),
                        new XAttribute("href", url)
                    ),
                    enclosure,
                    new XElement(Atom + "published", Timestamp(post.PublishedAt!.Value)),
                    new XElement(
                        Atom + "updated",
                        Timestamp(post.EditedAt ?? post.PublishedAt!.Value)
                    ),
                    new XElement(
                        Atom + "author",
                        new XElement(Atom + "name", post.Author.DisplayName ?? post.Author.Handle)
                    ),
                    new XElement(Atom + "summary", NewsText.Excerpt(post.Body).Text),
                    new XElement(Atom + "content", new XAttribute("type", "html"), html)
                )
            );
        }

        using var stream = new MemoryStream();
        using (
            var writer = XmlWriter.Create(
                stream,
                new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }
            )
        )
        {
            new XDocument(new XDeclaration("1.0", "utf-8", null), feed).Save(writer);
        }
        return stream.ToArray();
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
