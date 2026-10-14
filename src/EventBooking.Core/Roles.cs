namespace EventBooking.Core;

/// <summary>
/// Normal users have no role. The names are stored in the database, so don't rename them.
/// </summary>
public static class Roles
{
    public const string Organiser = "Organiser";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Organiser, Admin];
}
