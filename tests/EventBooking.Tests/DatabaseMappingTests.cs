using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Tests;

/// <summary>
/// Checks the EF Core mapping in ApplicationDbContext. EF can build its model and generate SQL
/// without connecting, so these run without a database; the connection string is never used.
/// </summary>
public class DatabaseMappingTests
{
    private static ApplicationDbContext CreateContext() => TestDbContextFactory.Create("Server=unused");

    [Fact]
    public void Migrations_AreUpToDateWithTheModel()
    {
        using var db = CreateContext();

        // Compares the model with the latest migration snapshot. If this fails, a mapping or
        // entity changed without a migration: run `dotnet ef migrations add <Name>`.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Event_TextColumnSizesMatchTheCoreRules()
    {
        using var db = CreateContext();
        var ev = db.Model.FindEntityType(typeof(Event))!;

        Assert.Equal(Event.NameMaxLength, ev.FindProperty(nameof(Event.Name))!.GetMaxLength());
        Assert.Equal(Event.DescriptionMaxLength, ev.FindProperty(nameof(Event.Description))!.GetMaxLength());
        Assert.Equal(Event.VenueNameMaxLength, ev.FindProperty(nameof(Event.VenueName))!.GetMaxLength());
        Assert.Equal(Event.AddressMaxLength, ev.FindProperty(nameof(Event.Address))!.GetMaxLength());
    }

    [Fact]
    public void Event_RowVersionIsAConcurrencyToken()
    {
        using var db = CreateContext();
        var rowVersion = db.Model.FindEntityType(typeof(Event))!.FindProperty(nameof(Event.RowVersion))!;

        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
    }

    [Fact]
    public void Event_OrganiserCannotBeDeletedWhileTheyHaveEvents()
    {
        using var db = CreateContext();
        var organiserKey = db.Model.FindEntityType(typeof(Event))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(ApplicationUser));

        Assert.Equal(DeleteBehavior.Restrict, organiserKey.DeleteBehavior);
    }

    [Fact]
    public void EventImage_SortOrderIsUniquePerEvent()
    {
        using var db = CreateContext();
        var index = db.Model.FindEntityType(typeof(EventImage))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(EventImage.EventId), nameof(EventImage.SortOrder)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Events_QueriesLeaveOutDeletedEvents()
    {
        using var db = CreateContext();

        // ToQueryString shows the SQL EF would send, without running it.
        var sql = db.Events.ToQueryString();

        Assert.Contains($"[e].[Status] <> {(int)EventStatus.Deleted}", sql);
    }
}
