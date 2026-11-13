using System.Diagnostics;

namespace BoostRoot.Platform.Windows;

/// <summary>Runs Windows PowerShell scripts/winget hidden and returns stdout (used by the Update Center).</summary>
public static class PowerShellRunner
{
    public static async Task<string> RunAsync(string script, CancellationToken ct, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "-" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell");
        await p.StandardInput.WriteAsync("[Console]::OutputEncoding=[Text.Encoding]::UTF8\n" + script).ConfigureAwait(false);
        p.StandardInput.Close();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(30));
        var output = p.StandardOutput.ReadToEndAsync(cts.Token);
        try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        return await output.ConfigureAwait(false);
    }

    /// <summary>Runs winget with arguments; returns (exit code, stdout). Throws FileNotFoundException when winget is missing.</summary>
    public static async Task<(int Code, string Output)> WingetAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("winget.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process? p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { throw new FileNotFoundException("winget"); }
        using (p)
        {
            if (p is null) throw new FileNotFoundException("winget");
            var output = p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return (p.ExitCode, await output.ConfigureAwait(false));
        }
    }
}
