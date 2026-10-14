using EventBooking.Core;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<OrganiserApplication> OrganiserApplications => Set<OrganiserApplication>();

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
        builder.Entity<OrganiserApplication>(application =>
        {
            application.Property(a => a.OrganisationName).HasMaxLength(OrganiserApplication.OrganisationNameMaxLength);
            application.Property(a => a.ContactPhone).HasMaxLength(OrganiserApplication.ContactPhoneMaxLength);
            application.Property(a => a.Website).HasMaxLength(OrganiserApplication.WebsiteMaxLength);
            application.Property(a => a.Description).HasMaxLength(OrganiserApplication.DescriptionMaxLength);
            application.Property(a => a.RejectionReason).HasMaxLength(OrganiserApplication.RejectionReasonMaxLength);

            // Same as Event: a save fails if someone else changed the row first.
            application.Property(a => a.RowVersion).IsRowVersion();

            // Two foreign keys point at AspNetUsers. SQL Server refuses a table with two
            // cascading paths from the same parent, so only one of them can cascade.
            // Cascade: deleting your account (personal data) deletes your applications too.
            application.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict: an admin who has reviewed applications can't be deleted, which keeps
            // the history of who approved whom.
            application.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.ReviewedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Rule 1, enforced by the database as well: at most one Pending application per user.
            // A filtered index only covers the rows matching the filter, so a user can still
            // have any number of Approved and Rejected ones. This also catches a double click
            // that submits twice at the same moment, which the check in Core can't see.
            // Both indexes are on UserId, so each needs its own name or EF would treat them as one.
            application.HasIndex(a => a.UserId, "IX_OrganiserApplications_UserId_Pending")
                .IsUnique()
                .HasFilter($"[Status] = {(int)ApplicationStatus.Pending}");

            // For loading one user's applications (account page, rules 1 and 3). EF normally adds
            // this for a foreign key itself, but skips it here because of the filtered index above.
            application.HasIndex(a => a.UserId, "IX_OrganiserApplications_UserId");

            // For the admin review page: pending applications, oldest first.
            application.HasIndex(a => new { a.Status, a.SubmittedAt });
        });
    }
}
