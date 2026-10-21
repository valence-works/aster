namespace Aster.Persistence.Relational;

/// <summary>
/// The schema-bootstrap pattern the relational providers share (#99, #202): a provider's pending infrastructure is
/// applied once, before its stores' first statement, by one shared run that every caller awaits.
/// </summary>
/// <param name="apply">Applies the provider's pending infrastructure steps.</param>
internal sealed class RelationalInfrastructureStartup(Func<CancellationToken, ValueTask> apply)
{
    private readonly object gate = new();
    private Task? startup;

    /// <summary>
    /// Applies pending steps once. The stores await it before their first statement rather than blocking in their
    /// constructors: a blocked thread can't help finish the run, and a burst of first requests would otherwise wait
    /// for the pool to grow past all of them. A failed run isn't kept: the next caller tries again. Cancelling a
    /// caller stops its wait, not the shared run.
    /// </summary>
    public ValueTask EnsureAppliedAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (gate)
        {
            // Checked here rather than by the waiters: every waiter may have stopped waiting before the run failed.
            if (startup is { IsFaulted: true } or { IsCanceled: true })
                startup = null;

            // Shared, so it takes nothing from the first caller: no synchronization context to post to, and no
            // execution context, which could carry an ambient TransactionScope the run's connection would enlist in.
            // SuppressFlow throws when the caller has already suppressed flow, which leaves nothing to suppress.
            if (startup is null && ExecutionContext.IsFlowSuppressed())
            {
                startup = StartRun();
            }
            else if (startup is null)
            {
                using (ExecutionContext.SuppressFlow())
                    startup = StartRun();
            }

            task = startup;
        }

        return task.IsCompletedSuccessfully ? ValueTask.CompletedTask : new(task.WaitAsync(cancellationToken));
    }

    private Task StartRun() => Task.Run(async () => await apply(CancellationToken.None).ConfigureAwait(false));
}
