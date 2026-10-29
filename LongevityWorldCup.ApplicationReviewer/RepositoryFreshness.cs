using System.Diagnostics;

namespace LongevityWorldCup.ApplicationReviewer;

internal static class RepositoryFreshness
{
    internal static async Task<string> EnsureCurrentAsync(string solutionRoot)
    {
        var root = await GitAsync(solutionRoot, "rev-parse", "--show-toplevel");
        if (root.ExitCode != 0 || !string.Equals(Path.GetFullPath(root.Output.Trim()),
                Path.GetFullPath(solutionRoot), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw Failure("the reviewer is not running from the repository root");

        var conflicts = await GitAsync(solutionRoot, "ls-files", "--unmerged");
        if (conflicts.ExitCode != 0 || conflicts.Output.Length != 0)
            throw Failure("the checkout has unresolved merge conflicts");

        // Fetch the actual remote branch. A stale origin/master ref is not evidence of freshness.
        var fetch = await GitAsync(solutionRoot, "fetch", "--no-tags", "origin", "refs/heads/master:refs/remotes/origin/master");
        if (fetch.ExitCode != 0)
            throw Failure("origin/master could not be fetched; current code cannot be verified");
        var upstream = await GitAsync(solutionRoot, "rev-parse", "refs/remotes/origin/master");
        var head = await GitAsync(solutionRoot, "rev-parse", "HEAD");
        if (upstream.ExitCode != 0 || head.ExitCode != 0)
            throw Failure("the local and fetched revisions could not be read");
        var contains = await GitAsync(solutionRoot, "merge-base", "--is-ancestor", upstream.Output.Trim(), head.Output.Trim());
        if (contains.ExitCode != 0)
            throw Failure($"checkout {head.Output.Trim()} does not contain current origin/master {upstream.Output.Trim()}. Reconcile master while preserving local work, then rerun");
        Console.WriteLine($"Verified checkout {head.Output.Trim()} contains origin/master {upstream.Output.Trim()}.");
        return head.Output.Trim();
    }

    internal static async Task<(int ExitCode, string Output)> GitAsync(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw Failure("Git could not be started");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw Failure("Git timed out; current code cannot be verified");
        }
        await stderr; // Drain diagnostics without exposing remote credentials in exception text.
        return (process.ExitCode, await stdout);
    }

    private static InvalidOperationException Failure(string reason)
        => new($"Application review stopped: {reason}. No application files have been processed.");
}
