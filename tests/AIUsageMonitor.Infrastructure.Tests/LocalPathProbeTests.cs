using System.Diagnostics;
using AIUsageMonitor.Infrastructure.Git;

namespace AIUsageMonitor.Infrastructure.Tests;

[CollectionDefinition("SystemLocalPathProbe", DisableParallelization = true)]
public sealed class SystemLocalPathProbeCollection
{
}

[Collection("SystemLocalPathProbe")]
public sealed class LocalPathProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "apo-path-probe-tests-" + Guid.NewGuid().ToString("N"));

    public LocalPathProbeTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RepeatedTimeouts_DoNotStartAdditionalUnderlyingProbe()
    {
        var invocationCount = 0;
        using var release = new ManualResetEventSlim(initialState: false);
        var started = NewSignal();
        var completed = NewSignal();
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromMilliseconds(100),
            _ =>
            {
                Interlocked.Increment(ref invocationCount);
                started.TrySetResult(true);
                release.Wait();
                completed.TrySetResult(true);
                return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
            });

        try
        {
            var first = probe.ProbeAsync(_root);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var firstResult = await first;
            Assert.Equal(LocalPathProbeStatus.Unavailable, firstResult.Status);
            Assert.True(firstResult.TimedOut);

            for (var attempt = 0; attempt < 4; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                var retryResult = await probe.ProbeAsync(_root);
                stopwatch.Stop();

                Assert.Equal(LocalPathProbeStatus.Unavailable, retryResult.Status);
                Assert.True(retryResult.TimedOut);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
            }

            Assert.Equal(1, Volatile.Read(ref invocationCount));
        }
        finally
        {
            release.Set();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ConcurrentCallers_DoNotCreateMultipleUnderlyingProbes()
    {
        var invocationCount = 0;
        using var release = new ManualResetEventSlim(initialState: false);
        var started = NewSignal();
        var completed = NewSignal();
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromMilliseconds(150),
            _ =>
            {
                Interlocked.Increment(ref invocationCount);
                started.TrySetResult(true);
                release.Wait();
                completed.TrySetResult(true);
                return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
            });

        try
        {
            var callers = Enumerable.Range(0, 8)
                .Select(_ => probe.ProbeAsync(_root))
                .ToArray();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var stopwatch = Stopwatch.StartNew();
            var results = await Task.WhenAll(callers);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
            Assert.Equal(1, Volatile.Read(ref invocationCount));
            Assert.All(results, result =>
            {
                Assert.Equal(LocalPathProbeStatus.Unavailable, result.Status);
                Assert.True(result.TimedOut);
            });
        }
        finally
        {
            release.Set();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task CancellationDoesNotSpawnReplacementProbe()
    {
        var invocationCount = 0;
        using var release = new ManualResetEventSlim(initialState: false);
        var started = NewSignal();
        var completed = NewSignal();
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromSeconds(10),
            _ =>
            {
                Interlocked.Increment(ref invocationCount);
                started.TrySetResult(true);
                release.Wait();
                completed.TrySetResult(true);
                return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
            });
        using var cancellation = new CancellationTokenSource();

        try
        {
            var cancelledCall = probe.ProbeAsync(_root, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledCall);

            var retry = await probe.ProbeAsync(_root);

            Assert.Equal(LocalPathProbeStatus.Unavailable, retry.Status);
            Assert.True(retry.TimedOut);
            Assert.Equal(1, Volatile.Read(ref invocationCount));
        }
        finally
        {
            release.Set();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ProbeMayRestartAfterOriginalUnderlyingOperationCompletes()
    {
        var invocationCount = 0;
        using var release = new ManualResetEventSlim(initialState: false);
        var firstStarted = NewSignal();
        var firstCompleted = NewSignal();
        var secondStarted = NewSignal();
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromMilliseconds(100),
            _ =>
            {
                var invocation = Interlocked.Increment(ref invocationCount);
                if (invocation == 1)
                {
                    firstStarted.TrySetResult(true);
                    release.Wait();
                    firstCompleted.TrySetResult(true);
                    return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
                }

                secondStarted.TrySetResult(true);
                return new LocalPathProbeResult(LocalPathProbeStatus.Missing);
            });

        try
        {
            var firstResultTask = probe.ProbeAsync(_root);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var firstResult = await firstResultTask;
            Assert.True(firstResult.TimedOut);

            release.Set();
            await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // The first call's timeout must not evict the slot. Once the actual core has
            // completed, the next call is allowed to start a fresh underlying probe.
            //
            // firstCompleted is signalled from inside the core, so it only proves the core body
            // ran: the slot is not free until that task is marked complete. Retrying until the
            // second core actually starts races that handover rather than assuming it has already
            // happened, so a slow machine only makes the healthy path take longer; it cannot turn
            // a genuine regression green. A retry that finds the slot still occupied returns the
            // first probe's own result without invoking the core, so the invocation count below
            // still pins the number of underlying probes at exactly two.
            var deadline = Stopwatch.GetTimestamp() + (long)(5 * Stopwatch.Frequency);
            LocalPathProbeResult secondResult;
            do
            {
                secondResult = await probe.ProbeAsync(_root);
            }
            while (!secondStarted.Task.IsCompleted && Stopwatch.GetTimestamp() < deadline);

            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, Volatile.Read(ref invocationCount));
            Assert.Equal(LocalPathProbeStatus.Missing, secondResult.Status);
        }
        finally
        {
            release.Set();
            await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task DifferentPathDoesNotStartAnotherProbeWhileGlobalProbeIsBlocked()
    {
        var invocationCount = 0;
        var pathA = Path.Combine(_root, "path-a");
        var pathB = Path.Combine(_root, "path-b");
        using var release = new ManualResetEventSlim(initialState: false);
        var started = NewSignal();
        var completed = NewSignal();
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromMilliseconds(100),
            path =>
            {
                Interlocked.Increment(ref invocationCount);
                started.TrySetResult(true);
                release.Wait();
                completed.TrySetResult(true);
                return new LocalPathProbeResult(
                    string.Equals(path, pathA, StringComparison.Ordinal)
                        ? LocalPathProbeStatus.AvailableDirectory
                        : LocalPathProbeStatus.Missing);
            });

        try
        {
            var pathAResultTask = probe.ProbeAsync(pathA);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var pathAResult = await pathAResultTask;
            Assert.True(pathAResult.TimedOut);

            var stopwatch = Stopwatch.StartNew();
            var pathBResult = await probe.ProbeAsync(pathB);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(LocalPathProbeStatus.Unavailable, pathBResult.Status);
            Assert.True(pathBResult.TimedOut);
            Assert.Equal(1, Volatile.Read(ref invocationCount));
        }
        finally
        {
            release.Set();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ProbeExceptionIsObservedAndSlotIsReleased()
    {
        var invocationCount = 0;
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromSeconds(1),
            _ =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    throw new InvalidOperationException("controlled probe failure");
                }

                return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
            });

        var firstResult = await probe.ProbeAsync(_root);
        var secondResult = await probe.ProbeAsync(_root);

        Assert.Equal(LocalPathProbeStatus.Unavailable, firstResult.Status);
        Assert.Equal(LocalPathProbeStatus.AvailableDirectory, secondResult.Status);
        Assert.Equal(2, Volatile.Read(ref invocationCount));
    }

    [Fact]
    public async Task ProbeAsync_AvailableDirectory_ForARealDirectory()
    {
        var probe = new SystemLocalPathProbe(TimeSpan.FromSeconds(4));

        var result = await probe.ProbeAsync(_root);

        Assert.Equal(LocalPathProbeStatus.AvailableDirectory, result.Status);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ProbeAsync_Missing_ForANonexistentPath()
    {
        var probe = new SystemLocalPathProbe(TimeSpan.FromSeconds(4));
        var missingPath = Path.Combine(_root, "does-not-exist");

        var result = await probe.ProbeAsync(missingPath);

        Assert.Equal(LocalPathProbeStatus.Missing, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_NotADirectory_ForAFile()
    {
        var probe = new SystemLocalPathProbe(TimeSpan.FromSeconds(4));
        var filePath = Path.Combine(_root, "file.txt");
        File.WriteAllText(filePath, "content");

        var result = await probe.ProbeAsync(filePath);

        Assert.Equal(LocalPathProbeStatus.NotADirectory, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_MissingAndUnavailable_AreDistinctOutcomes()
    {
        // Mirrors production ProbeCore's contract: access-denial is caught internally and
        // surfaced as Unavailable, never as an exception crossing the seam boundary.
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromSeconds(4),
            _ => new LocalPathProbeResult(LocalPathProbeStatus.Unavailable));

        var result = await probe.ProbeAsync(_root);

        Assert.Equal(LocalPathProbeStatus.Unavailable, result.Status);
        Assert.NotEqual(LocalPathProbeStatus.Missing, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_IOFailure_IsUnavailable()
    {
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromSeconds(4),
            _ => new LocalPathProbeResult(LocalPathProbeStatus.Unavailable));

        var result = await probe.ProbeAsync(_root);

        Assert.Equal(LocalPathProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_MalformedPath_IsUnavailableRatherThanThrowing()
    {
        var probe = new SystemLocalPathProbe(TimeSpan.FromSeconds(4));
        var malformedPath = _root + "\0invalid";

        var result = await probe.ProbeAsync(malformedPath);

        Assert.Equal(LocalPathProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsBoundedTimeout_WhenUnderlyingProbeNeverCompletes()
    {
        var neverReturns = new ManualResetEventSlim(initialState: false);
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromMilliseconds(100),
            path =>
            {
                neverReturns.Wait(TimeSpan.FromSeconds(10));
                return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
            });
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await probe.ProbeAsync(_root);

            stopwatch.Stop();
            Assert.Equal(LocalPathProbeStatus.Unavailable, result.Status);
            Assert.True(result.TimedOut);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Expected a bounded return; took {stopwatch.Elapsed}.");
        }
        finally
        {
            neverReturns.Set();
        }
    }

    [Fact]
    public async Task ProbeAsync_HonorsExternalCancellation_WithoutWaitingForTheUnderlyingProbe()
    {
        var neverReturns = new ManualResetEventSlim(initialState: false);
        var probe = new SystemLocalPathProbe(
            TimeSpan.FromSeconds(10),
            path =>
            {
                neverReturns.Wait(TimeSpan.FromSeconds(10));
                return new LocalPathProbeResult(LocalPathProbeStatus.AvailableDirectory);
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => probe.ProbeAsync(_root, cancellation.Token));

            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Expected a bounded return; took {stopwatch.Elapsed}.");
        }
        finally
        {
            neverReturns.Set();
        }
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
