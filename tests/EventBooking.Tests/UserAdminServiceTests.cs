using EventBooking.Core;
using EventBooking.Web.Data;
using EventBooking.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

/// <summary>Runs the service against a real SQL Server, each call in its own scope like a web request.</summary>
[Collection("SqlServer")]
public class UserAdminServiceTests(SqlServerFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <param name="configAdminEmail">The Admin:Email setting.</param>
    private async Task<T> WithServiceAsync<T>(Func<UserAdminService, Task<T>> action, string? configAdminEmail = null)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Email"] = configAdminEmail })
            .Build();
        var service = ActivatorUtilities.CreateInstance<UserAdminService>(
            scope.ServiceProvider, configuration, new FixedTimeProvider(Now));
        return await action(service);
    }

    private Task WithServiceAsync(Func<UserAdminService, Task> action, string? configAdminEmail = null) =>
        WithServiceAsync(async s => { await action(s); return true; }, configAdminEmail);

    private async Task<(ApplicationUser User, IList<string> Roles)> LoadAsync(string userId)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId))!;
        return (user, await userManager.GetRolesAsync(user));
    }

    private async Task<string> EmailOfAsync(string userId) => (await LoadAsync(userId)).User.Email!;

    // ---------- Who can use it ----------

    [Fact]
    public async Task NonAdmins_CantManageUsers()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var userId = await database.CreateUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.GetUsersAsync(organiserId, null)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.LockAsync(organiserId, userId)));
    }

    [Fact]
    public async Task LockedAdmins_CantManageUsers()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var otherAdminId = await database.CreateUserAsync(Roles.Admin);
        await WithServiceAsync(s => s.LockAsync(otherAdminId, adminId));

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.UnlockAsync(adminId, adminId)));
    }

    // ---------- Listing ----------

    [Fact]
    public async Task GetUser_ShowsRolesLockAndConfigAdmin()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var lockedId = await database.CreateUserAsync();
        await WithServiceAsync(s => s.LockAsync(adminId, lockedId));
        var adminEmail = await EmailOfAsync(adminId);
        // Upper case, to check letter case doesn't matter.
        Task<UserForAdmin?> GetAsync(string id) =>
            WithServiceAsync(s => s.GetUserAsync(adminId, id), adminEmail.ToUpperInvariant());

        Assert.Equal(new UserForAdmin(adminId, adminEmail, false, true, false, true), await GetAsync(adminId));
        Assert.Equal(new UserForAdmin(organiserId, await EmailOfAsync(organiserId), true, false, false, false),
            await GetAsync(organiserId));
        Assert.True((await GetAsync(lockedId))!.IsLocked);
    }

    [Fact]
    public async Task GetUsers_SearchMatchesPartOfTheEmail()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync();
        var part = (await EmailOfAsync(userId))[..8];

        var users = await WithServiceAsync(s => s.GetUsersAsync(adminId, $"  {part} "));

        Assert.Equal([userId], users.Select(u => u.Id));
    }

    [Fact]
    public async Task GetUser_MissingUser_ReturnsNull()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);

        Assert.Null(await WithServiceAsync(s => s.GetUserAsync(adminId, "no-such-user")));
    }

    // ---------- Organiser role ----------

    [Fact]
    public async Task RemoveOrganiser_RemovesTheRoleAndSignsThemOut()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var stampBefore = (await LoadAsync(organiserId)).User.SecurityStamp;

        await WithServiceAsync(s => s.RemoveOrganiserAsync(adminId, organiserId));

        var (user, roles) = await LoadAsync(organiserId);
        Assert.DoesNotContain(Roles.Organiser, roles);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
    }

    [Fact]
    public async Task RemoveOrganiser_NotAnOrganiser_Throws()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.RemoveOrganiserAsync(adminId, userId)));
    }

    // ---------- Admin role ----------

    [Fact]
    public async Task GiveAdmin_AddsTheRoleWithoutSigningThemOut()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync();
        var stampBefore = (await LoadAsync(userId)).User.SecurityStamp;

        await WithServiceAsync(s => s.GiveAdminAsync(adminId, userId));

        var (user, roles) = await LoadAsync(userId);
        Assert.Contains(Roles.Admin, roles);
        Assert.Equal(stampBefore, user.SecurityStamp);
    }

    [Theory]
    [InlineData(Roles.Organiser)]
    [InlineData(Roles.Admin)]
    public async Task GiveAdmin_OrganiserOrAlreadyAdmin_Throws(string role)
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync(role);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.GiveAdminAsync(adminId, userId)));
    }

    [Fact]
    public async Task RemoveAdmin_RemovesTheRoleAndSignsThemOut()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var otherAdminId = await database.CreateUserAsync(Roles.Admin);
        var stampBefore = (await LoadAsync(otherAdminId)).User.SecurityStamp;

        await WithServiceAsync(s => s.RemoveAdminAsync(adminId, otherAdminId));

        var (user, roles) = await LoadAsync(otherAdminId);
        Assert.DoesNotContain(Roles.Admin, roles);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
    }

    [Fact]
    public async Task RemoveAdmin_Themselves_Throws()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.RemoveAdminAsync(adminId, adminId)));
        Assert.Contains("yourself", ex.Message);
    }

    [Fact]
    public async Task RemoveAdmin_ConfigAdmin_Throws()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var configAdminId = await database.CreateUserAsync(Roles.Admin);
        var configAdminEmail = await EmailOfAsync(configAdminId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.RemoveAdminAsync(adminId, configAdminId), configAdminEmail));
        Assert.Contains("configuration", ex.Message);
        Assert.Contains(Roles.Admin, (await LoadAsync(configAdminId)).Roles);
    }

    // ---------- Locking ----------

    [Fact]
    public async Task Lock_LocksTheAccountAndSignsThemOut()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync(Roles.Organiser);
        var stampBefore = (await LoadAsync(userId)).User.SecurityStamp;

        await WithServiceAsync(s => s.LockAsync(adminId, userId));

        var (user, roles) = await LoadAsync(userId);
        Assert.True(user.LockoutEnd > Now.AddYears(100));
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        // Locking doesn't remove roles, so unlocking brings everything back.
        Assert.Contains(Roles.Organiser, roles);
    }

    [Fact]
    public async Task Lock_ThemselvesOrConfigAdmin_Throws()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var configAdminId = await database.CreateUserAsync(Roles.Admin);
        var configAdminEmail = await EmailOfAsync(configAdminId);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.LockAsync(adminId, adminId), configAdminEmail));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.LockAsync(adminId, configAdminId), configAdminEmail));
        Assert.Null((await LoadAsync(configAdminId)).User.LockoutEnd);
    }

    [Fact]
    public async Task Lock_AlreadyLocked_Throws()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync();
        await WithServiceAsync(s => s.LockAsync(adminId, userId));

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.LockAsync(adminId, userId)));
    }

    [Fact]
    public async Task Unlock_ClearsTheLock()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync();
        await WithServiceAsync(s => s.LockAsync(adminId, userId));

        await WithServiceAsync(s => s.UnlockAsync(adminId, userId));

        Assert.Null((await LoadAsync(userId)).User.LockoutEnd);
    }

    [Fact]
    public async Task Unlock_NotLocked_Throws()
    {
        var adminId = await database.CreateUserAsync(Roles.Admin);
        var userId = await database.CreateUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.UnlockAsync(adminId, userId)));
    }
}
