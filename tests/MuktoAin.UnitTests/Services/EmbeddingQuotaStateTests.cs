using MuktoAin.Infrastructure.VectorStore;

namespace MuktoAin.UnitTests.Services;

public class EmbeddingQuotaStateTests
{
    [Fact]
    public async Task WaitAsync_WithinBudget_ReturnsImmediately()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 10, maxTokensPerMinute: 100_000);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await state.WaitAsync(1, CancellationToken.None);
        state.RecordSuccess(1, 1000);
        await state.WaitAsync(1, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, "Second request within a 10/min budget should not wait.");
    }

    [Fact]
    public async Task WaitAsync_WhenItemBudgetExhausted_WaitsForWindowToDrain()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 2, maxTokensPerMinute: 1_000_000);

        state.RecordSuccess(1, 100);
        state.RecordSuccess(1, 100);

        // Window is full: the next wait must block, not return immediately.
        var waitTask = state.WaitAsync(1, CancellationToken.None);
        Assert.False(waitTask.IsCompletedAfterShortWait(), "WaitAsync should block while the window is full.");
    }

    [Fact]
    public async Task WaitAsync_BatchFillingEntireBudget_BlocksNextBatch()
    {
        // FIX-EMB-5 regression: Google counts each TEXT in a batchEmbedContents
        // call as one quota request, so a batch consuming the whole minute
        // budget must gate the next batch. The old HTTP-call-counting tracker
        // let ~100 such batches through per minute -> guaranteed 429 storms.
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 1_000_000);

        await state.WaitAsync(100, CancellationToken.None);
        state.RecordSuccess(100, 10_000); // one 100-text batch = the entire budget

        var waitTask = state.WaitAsync(25, CancellationToken.None);
        Assert.False(waitTask.IsCompletedAfterShortWait(),
            "A second batch after a budget-filling batch must wait for the window to drain.");
    }

    [Fact]
    public async Task WaitAsync_BatchLargerThanHalvedBudget_StillAdmitted()
    {
        // Deadlock guard: AIMD can halve the budget BELOW the batch size (e.g.
        // 25-text batches, budget halved to 2). A batch must still be admissible
        // — refusing it would hang the job forever at the gate.
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 1_000_000);

        // Zero freeze so only the item-cap logic is under test (a 1s freeze
        // would delay the return and mask the deadlock-guard behavior).
        state.RecordQuotaExceeded(TimeSpan.Zero); // 100 -> 50
        state.RecordQuotaExceeded(TimeSpan.Zero); // 50 -> 25
        state.RecordQuotaExceeded(TimeSpan.Zero); // 25 -> 12

        // Budget is now 12 but a 25-text batch must still pass the gate
        // (freeze elapsed, itemCap = max(batch, budget)).
        var waitTask = state.WaitAsync(25, CancellationToken.None);
        Assert.True(waitTask.IsCompletedAfterShortWait(),
            "A batch larger than the halved budget must still be admitted, not deadlock.");
    }

    [Fact]
    public async Task WaitAsync_TokenWindowBinds_BlocksUntilOldestEntryExpires()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 10_000);

        state.RecordSuccess(10, 6_000);
        state.RecordSuccess(10, 5_000); // 11k tokens > 10k cap: token gate now binds

        var waitTask = state.WaitAsync(10, CancellationToken.None);
        Assert.False(waitTask.IsCompletedAfterShortWait(),
            "When the token window is over budget, the next batch must wait.");
    }

    [Fact]
    public void RecordQuotaExceeded_HalvesBudgetAndFreezes()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 1_000_000);
        state.RecordSuccess(1, 10);

        state.RecordQuotaExceeded(TimeSpan.FromSeconds(37));

        var snap = state.Snapshot();
        Assert.Equal(50, snap.RequestBudgetPerMinute, precision: 0);
        Assert.NotNull(snap.FrozenUntil);
        Assert.True(snap.FrozenUntil > DateTime.UtcNow.AddSeconds(30));
        Assert.Equal(1, snap.ConsecutiveFourTwentyNines);
    }

    [Fact]
    public void RecordQuotaExceeded_ResetsItemAndTokenCounters()
    {
        // FIX-EMB-5 regression: the old code cleared the window queues but never
        // zeroed the running token counter, so it drifted upward forever — the
        // moment GetRemainingTokenBudget() was wired into the packer, one 429
        // would permanently collapse the token budget to zero.
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 100_000);

        state.RecordSuccess(50, 60_000);
        Assert.Equal(40_000, state.GetRemainingTokenBudget());

        state.RecordQuotaExceeded(TimeSpan.FromSeconds(30));

        // Window dropped AND counters reset — full token headroom returns.
        Assert.Equal(100_000, state.GetRemainingTokenBudget());
    }

    [Fact]
    public void RecordSuccess_AfterCleanWindow_RestoresBudgetAdditively()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 1_000_000);

        state.RecordQuotaExceeded(null);         // 100 -> 50
        var halved = state.Snapshot().RequestBudgetPerMinute;

        state.ClearFourTwentyNineStreakIfClean();
        state.RecordSuccess(10, 100);           // clean => +1

        var snap = state.Snapshot();
        Assert.Equal(halved + 1, snap.RequestBudgetPerMinute, precision: 0);
        Assert.Equal(0, snap.ConsecutiveFourTwentyNines);
    }

    [Fact]
    public void ConsecutiveQuotaExceeded_BudgetFloorsAtTwoPerMinute()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 1_000_000);

        for (var i = 0; i < 10; i++)
        {
            state.RecordQuotaExceeded(null);
        }

        Assert.Equal(2, state.Snapshot().RequestBudgetPerMinute, precision: 0);
    }

    [Fact]
    public void GetRemainingTokenBudget_TracksWindowUsage()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 10_000);

        state.RecordSuccess(10, 3000);
        state.RecordSuccess(10, 2000);

        Assert.Equal(5000, state.GetRemainingTokenBudget());
    }

    [Fact]
    public void ResetForNewDay_RestoresFullBudgetAndClearsState()
    {
        var state = new EmbeddingQuotaState(maxRequestsPerMinute: 100, maxTokensPerMinute: 10_000);

        state.RecordQuotaExceeded(TimeSpan.FromMinutes(5)); // budget 50, frozen, streak 1
        state.RecordSuccess(40, 4000);                       // items/tokens spent in window

        state.ResetForNewDay();

        var snap = state.Snapshot();
        Assert.Equal(100, snap.RequestBudgetPerMinute, precision: 0);
        Assert.Null(snap.FrozenUntil);
        Assert.Equal(0, snap.ConsecutiveFourTwentyNines);
        Assert.Equal(0, snap.TokensUsedThisWindow);
        Assert.Equal(10_000, state.GetRemainingTokenBudget());
    }
}

file static class TaskExtensions
{
    public static bool IsCompletedAfterShortWait(this Task task)
    {
        try
        {
            return task.Wait(300);
        }
        catch (AggregateException)
        {
            return false;
        }
    }
}
