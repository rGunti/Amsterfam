using Amsterfam.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Infrastructure;

public class AmsterfamDbContext(DbContextOptions<AmsterfamDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();
    public DbSet<AvailabilityEntry> AvailabilityEntries => Set<AvailabilityEntry>();
    public DbSet<DatePollEntry> DatePollEntries => Set<DatePollEntry>();
    public DbSet<Accommodation> Accommodations => Set<Accommodation>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<BedAssignment> BedAssignments => Set<BedAssignment>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityVote> ActivityVotes => Set<ActivityVote>();
    public DbSet<ItineraryEntry> ItineraryEntries => Set<ItineraryEntry>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();
    public DbSet<ComfortQuestionTemplate> ComfortQuestionTemplates =>
        Set<ComfortQuestionTemplate>();
    public DbSet<EventComfortQuestion> EventComfortQuestions => Set<EventComfortQuestion>();
    public DbSet<ComfortAnswer> ComfortAnswers => Set<ComfortAnswer>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<EventJoinLink> EventJoinLinks => Set<EventJoinLink>();
    public DbSet<EventFile> EventFiles => Set<EventFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.ExternalId).IsUnique();
            e.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.Property(ev => ev.Id).ValueGeneratedNever();
            e.Property(ev => ev.Status).HasConversion<string>();
            e.Property(ev => ev.CreatedAt).HasDefaultValueSql("now()");
            e.HasOne(ev => ev.CreatedBy)
                .WithMany()
                .HasForeignKey(ev => ev.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
            // Points at a file the event itself owns; files cascade away with the event.
            e.HasOne<EventFile>()
                .WithMany()
                .HasForeignKey(ev => ev.BannerFileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EventFile>(e =>
        {
            e.HasIndex(f => f.EventId);
            e.Property(f => f.FileName).HasMaxLength(EventFile.MaxFileNameLength);
            e.Property(f => f.ContentType).HasMaxLength(100);
            e.HasOne(f => f.Event)
                .WithMany(ev => ev.Files)
                .HasForeignKey(f => f.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.UploadedBy)
                .WithMany()
                .HasForeignKey(f => f.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventAttendance>(e =>
        {
            e.HasIndex(a => new { a.EventId, a.UserId }).IsUnique();
            e.Property(a => a.Role).HasConversion<string>();
            e.HasOne(a => a.JoinLink)
                .WithMany()
                .HasForeignKey(a => a.JoinLinkId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EventJoinLink>(e =>
        {
            e.HasIndex(l => l.Token).IsUnique();
            e.HasIndex(l => l.EventId);
            e.Property(l => l.Token).HasMaxLength(64);
            e.Property(l => l.Label).HasMaxLength(EventJoinLink.MaxLabelLength);
            e.Property(l => l.Kind).HasConversion<string>();
            e.HasOne(l => l.Event)
                .WithMany()
                .HasForeignKey(l => l.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.CreatedBy)
                .WithMany()
                .HasForeignKey(l => l.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AvailabilityEntry>(e =>
        {
            e.HasIndex(a => new
                {
                    a.EventId,
                    a.UserId,
                    a.Date,
                })
                .IsUnique();
            e.Property(a => a.Status).HasConversion<string>();
        });

        modelBuilder.Entity<DatePollEntry>(e =>
        {
            e.HasIndex(a => new
                {
                    a.EventId,
                    a.UserId,
                    a.WeekStart,
                })
                .IsUnique();
            e.Property(a => a.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Bed>(e =>
        {
            e.Property(b => b.Type).HasConversion<string>();
        });

        modelBuilder.Entity<ActivityVote>(e =>
        {
            e.HasIndex(v => new { v.ActivityId, v.UserId }).IsUnique();
            e.Property(v => v.Score).HasAnnotation("Range", new[] { 1, 5 });
        });

        modelBuilder.Entity<ComfortQuestionTemplate>(e =>
        {
            e.Property(t => t.Type).HasConversion<string>();
            e.Property(t => t.Options).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EventComfortQuestion>(e =>
        {
            e.HasIndex(q => new { q.EventId, q.TemplateId }).IsUnique();
        });

        modelBuilder.Entity<ComfortAnswer>(e =>
        {
            e.HasIndex(a => new { a.EventComfortQuestionId, a.UserId }).IsUnique();
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<ShoppingItem>(e =>
        {
            e.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<PaymentMethod>(e =>
        {
            e.HasOne(p => p.User)
                .WithMany(u => u.PaymentMethods)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
