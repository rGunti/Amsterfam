namespace Amsterfam.Core.Entities;

/// <summary>
/// A binary file (e.g. the banner image) stored in the database and owned by an event.
/// What a file is used for is decided by whatever references it, such as
/// <see cref="Event.BannerFileId"/>. Queries that don't need the content should project
/// away <see cref="Data"/>.
/// </summary>
public class EventFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public required byte[] Data { get; set; }
    public int UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Event Event { get; set; } = null!;
    public User UploadedBy { get; set; } = null!;

    public const int MaxFileNameLength = 255;
}
