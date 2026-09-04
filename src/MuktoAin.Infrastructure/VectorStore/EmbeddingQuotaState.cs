namespace MuktoAin.Infrastructure.VectorStore;

/// <summary>
/// Adaptive free-tier quota pacing for the EmbeddingBatchJob (AIMD:
/// additive-increase, multiplicative-decrease over a 60s rolling window).
///
/// FIX-EMB-5: the window is now SUB-REQUEST WEIGHTED. Google's quota metric
/// (generativelanguage.googleapis.com/embed_content_free_tier_requests,
/// 100/min per project) counts EACH TEXT inside a batchEmbedContents batch as
/// one request — a 100-text batch spends 100 quota units, not 1. The old
/// tracker counted HTTP calls, so its gate could never engage at real
/// throughput (~12,000 texts/min) and the job 429-stormed on every pool.
///
/// The window tracks (items, tokens) per recorded request; WaitAsync gates on
/// BOTH the item budget and the token budget (the lesser headroom binds);
/// RecordQuotaExceeded now resets the running counters when it drops the
/// window (the old code cleared the queues but left _tokensUsedThisWindow
/// drifting upward forever).
///
/// Thread-safe: workers share one instance; progress endpoints read snapshots.
/// </summary>
public class EmbeddingQuotaState
{
    private readonly object _lock = new();

    private readonly int _maxItemsPerMinute;
    private readonly int _maxTokensPerMinute;

    private double _itemBudgetPerMinute;
    private readonly Queue<(DateTime Timestamp, int Items, int Tokens)> _window = new();
    private int _itemsInWindow;
    private int _tokensInWindow;

    private DateTime? _frozenUntil;
    private int _consecutiveFourTwentyNines;

    public QuotaSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new QuotaSnapshot(
                _itemBudgetPerMinute,
                _maxItemsPerMinute,
                _tokensInWindow,
                _maxTokensPerMinute,
                _frozenUntil,
                _consecutiveFourTwentyNines);
        }
    }

    public EmbeddingQuotaState(int maxRequestsPerMinute = 100, int maxTokensPerMinute = 1_000_000)
    {
        _maxItemsPerMinute = maxRequestsPerMinute;
        _maxTokensPerMinute = maxTokensPerMinute;
        _itemBudgetPerMinute = maxRequestsPerMinute;
    }

    /// <summary>
    /// Waits until the tracker allows a request carrying
    /// <paramref name="itemCount"/> sub-requests (embedded texts). Honors 429
    /// freeze time first, then the rolling 60s item window, then the token
    /// window. A request bigger than the entire (possibly halved) budget is
    /// still admitted — refusing it would deadlock the job permanently.
    /// </summary>
    public async Task WaitAsync(int itemCount, CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_lock)
            {
                PruneWindow();

                var now = DateTime.UtcNow;

                if (_frozenUntil > now)
                {
                    wait = _frozenUntil!.Value - now;
                }
                else
                {
                    // Item cap: at least itemCount must be admissible, even when
                    // AIMD has halved the budget below the batch size (deadlock guard).
                    var itemCap = Math.Max(itemCount, Math.Ceiling(_itemBudgetPerMinute));
                    var itemHeadroom = itemCap - _itemsInWindow;

                    if (itemHeadroom < itemCount)
                    {
                        // Oldest entry exits the window in <= 60s.
                        wait = _window.Peek().Timestamp + TimeSpan.FromSeconds(60) - now;
                    }
                    else if (_tokensInWindow >= _maxTokensPerMinute)
                    {
                        wait = _window.Count > 0
                            ? _window.Peek().Timestamp + TimeSpan.FromSeconds(60) - now
                            : TimeSpan.FromSeconds(1);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            if (wait <= TimeSpan.Zero)
            {
                wait = TimeSpan.FromMilliseconds(250);
            }

            await Task.Delay(wait, ct);
        }
    }

    /// <summary>Records a successful request that carried the given sub-request (item) count and token cost.</summary>
    public void RecordSuccess(int itemCount, int tokenCost)
    {
        lock (_lock)
        {
            PruneWindow();
            _window.Enqueue((DateTime.UtcNow, itemCount, tokenCost));
            _itemsInWindow += itemCount;
            _tokensInWindow += tokenCost;

            // Additive increase: a clean window earns budget back.
            if (_consecutiveFourTwentyNines == 0 &&
                _itemBudgetPerMinute < _maxItemsPerMinute)
            {
                _itemBudgetPerMinute = Math.Min(
                    _maxItemsPerMinute,
                    _itemBudgetPerMinute + 1);
            }
        }
    }

    /// <summary>
    /// Records a 429. Halves the item budget, freezes sending for
    /// <paramref name="retryAfter"/> (Google's RetryInfo when available), and
    /// clears the current window so the next send starts from a clean slate.
    /// </summary>
    public void RecordQuotaExceeded(TimeSpan? retryAfter)
    {
        lock (_lock)
        {
            PruneWindow();

            _consecutiveFourTwentyNines++;

            // Multiplicative decrease — halves each 429, floor of 2 items/min.
            _itemBudgetPerMinute = Math.Max(2, _itemBudgetPerMinute * 0.5);

            var freeze = retryAfter ?? TimeSpan.FromSeconds(30 + (15 * Math.Min(_consecutiveFourTwentyNines, 6)));
            _frozenUntil = DateTime.UtcNow + freeze;

            // Drop the in-flight window: those items/tokens are spent quota.
            // Counters MUST reset with the queues or they drift upward forever
            // (FIX-EMB-5: the old code leaked _tokensUsedThisWindow here).
            _window.Clear();
            _itemsInWindow = 0;
            _tokensInWindow = 0;
        }
    }

    /// <summary>
    /// Approximate tokens still safely available in the current window —
    /// the batch packer shrinks the batch when this gets small.
    /// </summary>
    public int GetRemainingTokenBudget()
    {
        lock (_lock)
        {
            PruneWindow();
            return Math.Max(0, _maxTokensPerMinute - _tokensInWindow);
        }
    }

    /// <summary>
    /// Call when a fresh batch succeeded with zero 429s to reset the 429 streak
    /// (AI recovery after sustained clean operation). Unconditional by design:
    /// the only caller (EmbeddingBatchJob's success path) invokes this AFTER
    /// WaitAsync already gated out any active freeze, so an elapsed freeze is
    /// guaranteed there — the streak is "consecutive 429s" and one clean batch
    /// breaks it. Additive recovery then resumes on each subsequent success.
    /// </summary>
    public void ClearFourTwentyNineStreakIfClean()
    {
        lock (_lock)
        {
            _consecutiveFourTwentyNines = 0;
        }
    }

    /// <summary>
    /// Daily quota window reset (midnight Pacific): clears freeze, streak and
    /// window history, and restores full item budget for the fresh day.
    /// </summary>
    public void ResetForNewDay()
    {
        lock (_lock)
        {
            _frozenUntil = null;
            _consecutiveFourTwentyNines = 0;
            _window.Clear();
            _itemsInWindow = 0;
            _tokensInWindow = 0;
            _itemBudgetPerMinute = _maxItemsPerMinute;
        }
    }

    private void PruneWindow()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(60);
        while (_window.Count > 0 && _window.Peek().Timestamp <= cutoff)
        {
            var expired = _window.Dequeue();
            _itemsInWindow -= expired.Items;
            _tokensInWindow -= expired.Tokens;
        }

        if (_frozenUntil <= DateTime.UtcNow)
        {
            _frozenUntil = null;
        }
    }

    public sealed record QuotaSnapshot(
        double RequestBudgetPerMinute,
        int MaxRequestsPerMinute,
        int TokensUsedThisWindow,
        int MaxTokensPerMinute,
        DateTime? FrozenUntil,
        int ConsecutiveFourTwentyNines);
}
