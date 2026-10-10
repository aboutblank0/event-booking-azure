using EventBooking.Core;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Event> Events => Set<Event>();

    // Describes how the Core classes map to tables. Core has no EF attributes, so all
    // database details (column sizes, keys, indexes) live here instead.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Sets up the Identity tables (AspNetUsers etc.). Must stay first.
        base.OnModelCreating(builder);

        builder.Entity<Event>(ev =>
        {
            ev.Property(e => e.Name).HasMaxLength(Event.NameMaxLength);
            ev.Property(e => e.Description).HasMaxLength(Event.DescriptionMaxLength);
            ev.Property(e => e.VenueName).HasMaxLength(Event.VenueNameMaxLength);
            ev.Property(e => e.Address).HasMaxLength(Event.AddressMaxLength);

            // The longest IANA time zone names are about 30 characters.
            ev.Property(e => e.TimeZoneId).HasMaxLength(64);

            // SQL Server changes a rowversion column on every update. EF includes it in the
            // WHERE clause of UPDATEs, so a save fails if someone else changed the row first.
            ev.Property(e => e.RowVersion).IsRowVersion();

            // Event has no ApplicationUser property (Core can't know about Identity), so the
            // relationship is declared from this side only. Restrict: a user who organises
            // events can't be deleted, because published events must never be deleted.
            ev.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.OrganiserId)
                .OnDelete(DeleteBehavior.Restrict);

            // EF finds the private _images field by its name and uses it to load and save images.
            ev.HasMany(e => e.Images)
                .WithOne()
                .HasForeignKey(i => i.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // Speeds up the main browsing query: published events, ordered by start date.
            ev.HasIndex(e => new { e.Status, e.StartsAt });

            // Deleted events are left out of every query automatically (rule 15).
            // A query can opt back in with .IgnoreQueryFilters() if ever needed.
            ev.HasQueryFilter(e => e.Status != EventStatus.Deleted);
        });

        builder.Entity<EventImage>(image =>
        {
            // There's no DbSet for images (they're only reached through Event), so EF would
            // name the table "EventImage". Name it explicitly to match "Events".
            image.ToTable("EventImages");

            // Blob names are a GUID plus an extension, e.g. "3f2a...9c.jpg" (about 37 characters).
            image.Property(i => i.BlobName).HasMaxLength(100);

            // No two images of an event share a SortOrder.
            image.HasIndex(i => new { i.EventId, i.SortOrder }).IsUnique();
        });
    }
}
