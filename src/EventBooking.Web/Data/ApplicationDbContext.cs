using EventBooking.Core;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<OrganiserApplication> OrganiserApplications => Set<OrganiserApplication>();
    public DbSet<Booking> Bookings => Set<Booking>();

    // Maps the Core classes to tables. Core has no EF attributes, so all database details live here.
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

            // A save fails if someone else changed the row since it was loaded.
            ev.Property(e => e.RowVersion).IsRowVersion();

            // Restrict: a user who organises events can't be deleted.
            ev.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.OrganiserId)
                .OnDelete(DeleteBehavior.Restrict);

            // EF finds the private _images field by name.
            ev.HasMany(e => e.Images)
                .WithOne()
                .HasForeignKey(i => i.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // For browsing published events by start date.
            ev.HasIndex(e => new { e.Status, e.StartsAt });

            // Hides deleted events from every query (opt out with .IgnoreQueryFilters()).
            ev.HasQueryFilter(e => e.Status != EventStatus.Deleted);
        });

        builder.Entity<EventImage>(image =>
        {
            // Without a DbSet, EF would name the table "EventImage".
            image.ToTable("EventImages");

            // A GUID plus an extension, about 37 characters.
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

            // A save fails if someone else changed the row since it was loaded.
            application.Property(a => a.RowVersion).IsRowVersion();

            // SQL Server allows only one cascading foreign key to the same table.
            // Cascade: deleting an account deletes its applications.
            application.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict: an admin who has reviewed applications can't be deleted.
            application.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.ReviewedById)
                .OnDelete(DeleteBehavior.Restrict);

            // At most one Pending application per user, even if two submits arrive at once.
            // The filter means the index only covers Pending rows.
            // Both indexes are on UserId, so each needs its own name.
            application.HasIndex(a => a.UserId, "IX_OrganiserApplications_UserId_Pending")
                .IsUnique()
                .HasFilter($"[Status] = {(int)ApplicationStatus.Pending}");

            // EF would normally add this foreign key index itself, but skips it because of the one above.
            application.HasIndex(a => a.UserId, "IX_OrganiserApplications_UserId");

            // For listing pending applications, oldest first.
            application.HasIndex(a => new { a.Status, a.SubmittedAt });
        });

        builder.Entity<Booking>(booking =>
        {
            // Restrict: published events are cancelled, never deleted, so bookings always have an event.
            booking.HasOne<Event>()
                .WithMany()
                .HasForeignKey(b => b.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cascade: deleting an account deletes its bookings, freeing the places.
            booking.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // At most one Confirmed booking per user per event, even if two requests arrive at once.
            // Cancelled bookings aren't in the index, so a user can book again after cancelling.
            booking.HasIndex(b => new { b.EventId, b.UserId }, "IX_Bookings_EventId_UserId_Confirmed")
                .IsUnique()
                .HasFilter($"[Status] = {(int)BookingStatus.Confirmed}");

            // For counting an event's bookings and listing a user's bookings.
            booking.HasIndex(b => new { b.EventId, b.Status });
            booking.HasIndex(b => b.UserId);
        });
    }
}
