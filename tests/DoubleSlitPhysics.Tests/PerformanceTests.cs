using System.Diagnostics;

using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;
using DoubleSlitPhysics.Services;

using Xunit.Abstractions;

namespace DoubleSlitPhysics.Tests;

public class PerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ReportMillisecondsPerStep()
    {
        var solver = new SplitStepSolver(new ExperimentGeometry(), SlitMode.Both);

        // Warm-up.
        for (var i = 0; i < 5; i++)
        {
            solver.Step();
        }

        const int steps = 50;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < steps; i++)
        {
            solver.Step();
        }

        stopwatch.Stop();
        var msPerStep = stopwatch.Elapsed.TotalMilliseconds / steps;
        _output.WriteLine($"{msPerStep:F2} ms per split-step on a 512×256 grid (native).");

        // Native should be far faster than the WASM budget; this is an early
        // warning only, not a strict performance contract.
        Assert.True(msPerStep < 50.0, $"Suspiciously slow: {msPerStep:F1} ms/step.");
    }
}
