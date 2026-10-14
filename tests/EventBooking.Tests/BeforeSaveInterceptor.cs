using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventBooking.Tests;

/// <summary>
/// Runs an action once, just before the context's first save, to act like another request that
/// saves in between this one loading and saving.
/// </summary>
internal class BeforeSaveInterceptor(Func<Task> action) : SaveChangesInterceptor
{
    private bool _done;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_done)
        {
            _done = true;
            await action();
        }
        return result;
    }
}
