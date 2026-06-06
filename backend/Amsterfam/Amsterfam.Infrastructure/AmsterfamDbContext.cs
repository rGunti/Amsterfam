using Amsterfam.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Infrastructure;

public class AmsterfamDbContext(DbContextOptions<AmsterfamDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();
    public DbSet<AvailabilityEntry> AvailabilityEntries => Set<AvailabilityEntry>();
    public DbSet<Accommodation> Accommodations => Set<Accommodation>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<BedAssignment> BedAssignments => Set<BedAssignment>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityVote> ActivityVotes => Set<ActivityVote>();
    public DbSet<ItineraryEntry> ItineraryEntries => Set<ItineraryEntry>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();
    public DbSet<ComfortQuestionTemplate> ComfortQuestionTemplates => Set<ComfortQuestionTemplate>();
    public DbSet<EventComfortQuestion> EventComfortQuestions => Set<EventComfortQuestion>();
    public DbSet<ComfortAnswer> ComfortAnswers => Set<ComfortAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.ExternalId).IsUnique();
            e.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.Property(ev => ev.Status).HasConversion<string>();
            e.Property(ev => ev.CreatedAt).HasDefaultValueSql("now()");
            e.HasOne(ev => ev.CreatedBy).WithMany().HasForeignKey(ev => ev.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventAttendance>(e =>
        {
            e.HasIndex(a => new { a.EventId, a.UserId }).IsUnique();
            e.Property(a => a.Role).HasConversion<string>();
        });

        modelBuilder.Entity<AvailabilityEntry>(e =>
        {
            e.HasIndex(a => new { a.EventId, a.UserId, a.Date }).IsUnique();
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
    }
}
