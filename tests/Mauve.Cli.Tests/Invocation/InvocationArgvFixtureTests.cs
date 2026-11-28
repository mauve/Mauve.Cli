using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class InvocationArgvFixtureTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public async Task Run_GeneratedSyncHostDefaultsToProcessArgumentsAndExplicitArgumentsOverrideThem()
    {
        await AssertFixtureExitAsync("sync-default", 17).ConfigureAwait(false);
        await AssertFixtureExitAsync("sync-explicit", 29).ConfigureAwait(false);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public async Task RunAsync_GeneratedAsyncHostDefaultsToProcessArgumentsAndExplicitArgumentsOverrideThem()
    {
        await AssertFixtureExitAsync("async-default", 17).ConfigureAwait(false);
        await AssertFixtureExitAsync("async-explicit", 29).ConfigureAwait(false);
    }

    private async Task AssertFixtureExitAsync(string mode, int expectedExitCode)
    {
        string hostName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string runtimeHost = Path.GetFullPath(Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", hostName));
        string? configuredHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        string host = configuredHost is not null && File.Exists(configuredHost)
            ? Path.GetFullPath(configuredHost)
            : runtimeHost;
        string fixture = Path.Combine(AppContext.BaseDirectory, "argv-fixture", "InvocationArgvFixture.dll");
        Assert.IsTrue(File.Exists(host), "The .NET host for the fixture was not found.");
        Assert.IsTrue(File.Exists(fixture), "The invocation fixture was not copied to the test output.");

        ProcessStartInfo start = new(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add("--number=17");
        start.Environment.Clear();
        start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(host);
        start.Environment["MAUVE_ARGV_MODE"] = mode;
        if (OperatingSystem.IsWindows())
        {
            start.Environment["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The invocation fixture could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            Assert.AreEqual(expectedExitCode, process.ExitCode, mode);
            Assert.AreEqual(0, (await output.ConfigureAwait(false)).Length, mode);
            Assert.AreEqual(0, (await errors.ConfigureAwait(false)).Length, mode);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
