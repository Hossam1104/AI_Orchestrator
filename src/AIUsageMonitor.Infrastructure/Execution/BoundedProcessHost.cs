using System.Collections;
using System.Diagnostics;
using System.Text;

namespace AIUsageMonitor.Infrastructure.Execution;

public enum BoundedProcessOutcome
{
    ExitedSuccessfully,
    NonZeroExit,
    TimedOut,
    Cancelled,
    StartFailed,
    TerminationFailure
}

public sealed class BoundedProcessRequest
{
    public BoundedProcessRequest(
        string executablePath,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        int maxStdoutBytes = 32 * 1024,
        int maxStderrBytes = 32 * 1024,
        TimeSpan? drainTimeout = null)
    {
        ExecutablePath = Required(executablePath, nameof(executablePath), 2_000);
        if (IsShellExecutable(ExecutablePath))
        {
            throw new ArgumentException("Shell executables are not supported by the bounded process host.", nameof(executablePath));
        }

        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments.Select(value => value ?? throw new ArgumentException("Arguments cannot contain null values.", nameof(arguments))).ToArray();
        if (Arguments.Count > 128 || Arguments.Any(value => value.Length > 8_000))
        {
            throw new ArgumentException("Process arguments exceed the supported bound.", nameof(arguments));
        }

        WorkingDirectory = Required(workingDirectory, nameof(workingDirectory), 2_000);
        if (!Path.IsPathFullyQualified(WorkingDirectory))
        {
            throw new ArgumentException("The process working directory must be absolute.", nameof(workingDirectory));
        }

        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Process timeout is outside the supported bound.");
        }

        if (maxStdoutBytes <= 0 || maxStdoutBytes > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStdoutBytes));
        }

        if (maxStderrBytes <= 0 || maxStderrBytes > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStderrBytes));
        }

        var effectiveDrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(2);
        if (effectiveDrainTimeout <= TimeSpan.Zero || effectiveDrainTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout));
        }

        Timeout = timeout;
        MaxStdoutBytes = maxStdoutBytes;
        MaxStderrBytes = maxStderrBytes;
        DrainTimeout = effectiveDrainTimeout;
    }

    public string ExecutablePath { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string WorkingDirectory { get; }
    public TimeSpan Timeout { get; }
    public int MaxStdoutBytes { get; }
    public int MaxStderrBytes { get; }
    public TimeSpan DrainTimeout { get; }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded process value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static bool IsShellExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("command.com", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sh", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record BoundedProcessResult(
    BoundedProcessOutcome Outcome,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    bool ProcessWasKilled,
    bool ProcessTerminationConfirmed,
    TimeSpan Elapsed,
    string? ErrorMessage = null)
{
    public bool Succeeded => Outcome == BoundedProcessOutcome.ExitedSuccessfully;
}

public interface IBoundedProcessHost
{
    Task<BoundedProcessResult> RunAsync(
        BoundedProcessRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Deterministic ordinary-environment allowlist for future reviewed CLI adapters.</summary>
public static class BoundedProcessEnvironment
{
    private static readonly HashSet<string> AllowedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH",
        "PATHEXT",
        "SystemRoot",
        "WINDIR",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "HOME",
        "LOCALAPPDATA",
        "APPDATA"
    };

    public static IReadOnlyDictionary<string, string> BuildAllowlisted(
        IReadOnlyDictionary<string, string?>? ambient = null)
    {
        var source = ambient ?? ReadAmbient();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (AllowedNames.Contains(pair.Key) && !IsSecretLike(pair.Key) && pair.Value is not null)
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string?> ReadAmbient()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                result[key] = entry.Value as string;
            }
        }

        return result;
    }

    private static bool IsSecretLike(string name) =>
        name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("APIKEY", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PAT", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Infrastructure-only bounded process execution. Callers provide structured arguments and an
/// explicit directory; no shell command string or planning text is ever interpreted.
/// </summary>
public sealed class BoundedProcessHost : IBoundedProcessHost
{
    public async Task<BoundedProcessResult> RunAsync(
        BoundedProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return new(
                BoundedProcessOutcome.Cancelled,
                null,
                string.Empty,
                string.Empty,
                false,
                false,
                false,
                true,
                TimeSpan.Zero,
                "Process cancellation was requested before start.");
        }

        var start = Stopwatch.GetTimestamp();
        using var process = new Process
        {
            StartInfo = CreateStartInfo(request),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return Failure(BoundedProcessOutcome.StartFailed, start, "The process could not be started.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Failure(BoundedProcessOutcome.StartFailed, start, "The process could not be started.");
        }

        using var outputCancellation = new CancellationTokenSource();
        var stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream, request.MaxStdoutBytes, outputCancellation.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError.BaseStream, request.MaxStderrBytes, outputCancellation.Token);
        var exitTask = process.WaitForExitAsync();

        // The timer is cancelled on every exit path, so a short-lived process does not leave a
        // pending timer alive for the whole configured timeout.
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(request.Timeout, timeoutCancellation.Token);
        Observe(timeoutTask);
        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationSignal);

        var completed = await Task.WhenAny(exitTask, timeoutTask, cancellationSignal.Task).ConfigureAwait(false);
        timeoutCancellation.Cancel();
        if (completed == exitTask)
        {
            await exitTask.ConfigureAwait(false);
            var output = await DrainOutputAsync(stdoutTask, stderrTask, request.DrainTimeout).ConfigureAwait(false);

            // A grandchild that inherited the pipe keeps the readers alive past the parent's exit.
            // Without this the readers outlive the host, and the process handle they are reading
            // through is disposed underneath them the moment this method returns.
            outputCancellation.Cancel();
            Observe(stdoutTask, stderrTask);
            return new(
                process.ExitCode == 0 ? BoundedProcessOutcome.ExitedSuccessfully : BoundedProcessOutcome.NonZeroExit,
                process.ExitCode,
                output.Stdout,
                output.Stderr,
                output.StdoutTruncated,
                output.StderrTruncated,
                false,
                true,
                ElapsedSince(start),
                process.ExitCode == 0 ? null : "The process exited with a non-zero code.");
        }

        var timedOut = completed == timeoutTask;
        outputCancellation.Cancel();
        var killed = false;
        var terminationConfirmed = false;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                killed = true;
            }

            terminationConfirmed = await WaitForExitBoundedAsync(process, request.DrainTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or IOException)
        {
            terminationConfirmed = false;
        }

        if (!terminationConfirmed && process.HasExited)
        {
            terminationConfirmed = true;
        }

        var drained = await DrainOutputAsync(stdoutTask, stderrTask, request.DrainTimeout).ConfigureAwait(false);
        Observe(stdoutTask, stderrTask);
        var outcome = terminationConfirmed
            ? timedOut ? BoundedProcessOutcome.TimedOut : BoundedProcessOutcome.Cancelled
            : BoundedProcessOutcome.TerminationFailure;
        return new(
            outcome,
            terminationConfirmed && process.HasExited ? process.ExitCode : null,
            drained.Stdout,
            drained.Stderr,
            drained.StdoutTruncated,
            drained.StderrTruncated,
            killed,
            terminationConfirmed,
            ElapsedSince(start),
            timedOut ? "The bounded process timeout elapsed." : "Process cancellation was requested.");
    }

    private static ProcessStartInfo CreateStartInfo(BoundedProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var pair in BoundedProcessEnvironment.BuildAllowlisted())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static async Task<BoundedOutput> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        await using (stream.ConfigureAwait(false))
        {
            using var output = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
            var buffer = new byte[8 * 1024];
            var truncated = false;
            try
            {
                while (true)
                {
                    var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    var remaining = maximumBytes - output.Length;
                    if (remaining > 0)
                    {
                        var retained = (int)Math.Min(remaining, count);
                        output.Write(buffer, 0, retained);
                        if (retained < count)
                        {
                            truncated = true;
                        }
                    }
                    else
                    {
                        truncated = true;
                    }
                }
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // The process was terminated, the bounded drain elapsed, or the pipe was torn down
                // with the process. The retained prefix remains safe in memory and is never logged
                // or persisted by this host. A broken pipe is an ordinary end of output here, not a
                // host failure: faulting would turn a truthful bounded result into an exception.
            }

            return new(Encoding.UTF8.GetString(output.ToArray()), truncated);
        }
    }

    /// <summary>
    /// Marks abandoned reader tasks as observed. A reader that faults after the bounded drain gave
    /// up would otherwise surface as an unobserved task exception on the finalizer thread, long
    /// after this host returned and attributed to unrelated code.
    /// </summary>
    private static void Observe(params Task[] tasks)
    {
        foreach (var task in tasks)
        {
            _ = task.ContinueWith(
                static completed => { _ = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task<BoundedOutputPair> DrainOutputAsync(
        Task<BoundedOutput> stdoutTask,
        Task<BoundedOutput> stderrTask,
        TimeSpan timeout)
    {
        var all = Task.WhenAll(stdoutTask, stderrTask);
        var finished = await Task.WhenAny(all, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished == all)
        {
            var values = await all.ConfigureAwait(false);
            return new(values[0].Text, values[1].Text, values[0].Truncated, values[1].Truncated);
        }

        return new(
            stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result.Text : string.Empty,
            stderrTask.IsCompletedSuccessfully ? stderrTask.Result.Text : string.Empty,
            stdoutTask.IsCompletedSuccessfully && stdoutTask.Result.Truncated,
            stderrTask.IsCompletedSuccessfully && stderrTask.Result.Truncated);
    }

    private static async Task<bool> WaitForExitBoundedAsync(Process process, TimeSpan timeout)
    {
        var exitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != exitTask)
        {
            return process.HasExited;
        }

        await exitTask.ConfigureAwait(false);
        return process.HasExited;
    }

    private static BoundedProcessResult Failure(BoundedProcessOutcome outcome, long start, string message) =>
        new(outcome, null, string.Empty, string.Empty, false, false, false, false, ElapsedSince(start), message);

    private static TimeSpan ElapsedSince(long start) => Stopwatch.GetElapsedTime(start);

    private sealed record BoundedOutput(string Text, bool Truncated);

    private sealed record BoundedOutputPair(
        string Stdout,
        string Stderr,
        bool StdoutTruncated,
        bool StderrTruncated);
}
