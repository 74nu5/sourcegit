using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     What a lookup found, and where it came from.
    ///
    ///     <see cref="FromCache"/> is not a curiosity: a view that knows an answer is stale
    ///     can show it greyed while a fresh one is on its way, instead of blanking.
    /// </summary>
    public record ForgeCached<T>(ForgeResult<T> Result, bool FromCache, DateTime FetchedAt)
    {
        public bool IsOk => Result.IsOk;
    }

    /// <summary>
    ///     Keeps forge answers so that asking about two hundred branches costs one request.
    ///
    ///     Three things make that true, and all three matter:
    ///
    ///     A fresh answer is reused. A failed one is reused too, for a shorter while — this
    ///     is the part people forget, and it is what stops a forge that is refusing a token
    ///     from being asked again once per branch, which is how a quota dies in a minute.
    ///
    ///     And a request already in flight is joined rather than duplicated. Callers arrive
    ///     together, because a view lays out its rows all at once; without this, the first
    ///     paint of a busy history would fire a request per row before any of them returned.
    ///
    ///     Cancelling a caller abandons that caller, never the shared request: the others are
    ///     still waiting for it, and the answer still belongs in the cache.
    /// </summary>
    public sealed class ForgeCache<T>
    {
        /// <summary>
        ///     How long a good answer is reused, and how long a bad one is. The second is
        ///     short enough that fixing a token feels responsive, long enough that a wall of
        ///     rows does not become a wall of requests.
        /// </summary>
        public ForgeCache(TimeSpan freshFor, TimeSpan retryFailureAfter)
        {
            _freshFor = freshFor;
            _retryFailureAfter = retryFailureAfter;
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return _entries.Count;
            }
        }

        /// <summary>
        ///     The answer for a key, fetching it only if nothing usable is held.
        /// </summary>
        public async Task<ForgeCached<T>> GetAsync(
            string key,
            Func<CancellationToken, Task<ForgeResult<T>>> fetch,
            CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(key) || fetch == null)
                return new ForgeCached<T>(ForgeResult<T>.Failure(ForgeStatus.Unexpected), false, DateTime.UtcNow);

            Task<ForgeCached<T>> shared;
            TaskCompletionSource<ForgeCached<T>> mine = null;

            lock (_lock)
            {
                if (_entries.TryGetValue(key, out var held) && IsUsable(held))
                    return held with { FromCache = true };

                if (!_inFlight.TryGetValue(key, out shared))
                {
                    // Published before the fetch starts, and started outside this lock. A
                    // fetch that completes synchronously would otherwise clear its own
                    // in-flight slot before it was ever filled, leaving a finished task
                    // behind that answers every later question without asking anyone.
                    mine = new TaskCompletionSource<ForgeCached<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shared = mine.Task;
                    _inFlight[key] = shared;
                }
            }

            if (mine != null)
                _ = RunAsync(key, fetch, mine);

            try
            {
                // WaitAsync abandons this caller, not the request: whoever else is waiting
                // still gets the answer, and it still lands in the cache.
                return await shared.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ForgeCached<T>(ForgeResult<T>.Failure(ForgeStatus.Cancelled), false, DateTime.UtcNow);
            }
        }

        /// <summary>
        ///     What is held for a key without asking anyone, or null. For a view that wants to
        ///     paint immediately and refresh afterwards.
        /// </summary>
        public ForgeCached<T> Peek(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            lock (_lock)
                return _entries.TryGetValue(key, out var held) ? held with { FromCache = true } : null;
        }

        /// <summary>
        ///     Forgets one key, so the next question reaches the forge. What a refresh is for.
        /// </summary>
        public void Invalidate(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            lock (_lock)
                _entries.Remove(key);
        }

        /// <summary>
        ///     Forgets everything under a prefix — every repository of an account whose token
        ///     just changed, for instance.
        /// </summary>
        public void InvalidateWhere(Func<string, bool> predicate)
        {
            if (predicate == null)
                return;

            lock (_lock)
            {
                var doomed = new List<string>();
                foreach (var key in _entries.Keys)
                {
                    if (predicate(key))
                        doomed.Add(key);
                }

                foreach (var key in doomed)
                    _entries.Remove(key);
            }
        }

        public void Clear()
        {
            lock (_lock)
                _entries.Clear();
        }

        private async Task RunAsync(string key, Func<CancellationToken, Task<ForgeResult<T>>> fetch, TaskCompletionSource<ForgeCached<T>> completion)
        {
            ForgeResult<T> result;
            try
            {
                result = await fetch(CancellationToken.None).ConfigureAwait(false) ??
                         ForgeResult<T>.Failure(ForgeStatus.Unexpected);
            }
            catch (Exception e)
            {
                // A connector that throws is a bug, but not one worth taking the window down
                // for. It is recorded like any other failure and retried like one.
                result = ForgeResult<T>.Failure(ForgeStatus.Unexpected, e.Message);
            }

            ForgeLog.Line($"fetched {key} -> {result.Status}");

            var entry = new ForgeCached<T>(result, false, DateTime.UtcNow);

            lock (_lock)
            {
                // A cancelled answer says nothing about the forge, so it is not remembered.
                if (result.Status != ForgeStatus.Cancelled)
                    _entries[key] = entry;

                _inFlight.Remove(key);
            }

            completion.TrySetResult(entry);
        }

        private bool IsUsable(ForgeCached<T> entry)
        {
            var age = DateTime.UtcNow - entry.FetchedAt;
            return age < (entry.IsOk ? _freshFor : _retryFailureAfter);
        }

        private readonly TimeSpan _freshFor;
        private readonly TimeSpan _retryFailureAfter;
        private readonly Lock _lock = new();
        private readonly Dictionary<string, ForgeCached<T>> _entries = [];
        private readonly Dictionary<string, Task<ForgeCached<T>>> _inFlight = [];
    }
}
