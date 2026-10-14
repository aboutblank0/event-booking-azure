namespace EventBooking.Core;

/// <summary>
/// The stored roles from docs/roles-design.md. A normal user has no role. The names are saved in
/// the AspNetRoles table, so renaming one would disconnect users from the role they already have.
/// </summary>
public static class Roles
{
    public const string Organiser = "Organiser";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Organiser, Admin];
}
