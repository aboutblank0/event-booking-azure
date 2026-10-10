namespace EventBooking.Core;

/// <summary>An image of an event. Created and removed only through <see cref="Event"/>.</summary>
public class EventImage
{
    // Used by EF Core when loading from the database.
    private EventImage() { }

    internal EventImage(string blobName, int sortOrder)
    {
        BlobName = blobName;
        SortOrder = sortOrder;
    }

    public int Id { get; private set; }
    public int EventId { get; private set; }

    /// <summary>The blob's name in storage, not its full URL.</summary>
    public string BlobName { get; private set; } = "";

    /// <summary>Display order. The image with the lowest value is the cover.</summary>
    public int SortOrder { get; private set; }
}
