using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;
using DoubleSlitPhysics.Services;

namespace DoubleSlitPhysics.Tests;

public class SolverTests
{
    /// <summary>Geometry with no barrier and no absorbers: free propagation.</summary>
    private static ExperimentGeometry FreeGeometry(int width = 256, int height = 256) => new()
    {
        Width = width,
        Height = height,
        BarrierThickness = 0,
        AbsorberStrength = 0.0,
        PacketY = height / 2,
        SigmaX = 24.0,
        SigmaY = 24.0,
        K0 = 0.5,
    };

    [Fact]
    public void FreeEvolutionConservesNorm()
    {
        var solver = new SplitStepSolver(FreeGeometry(), SlitMode.Both);
        for (var i = 0; i < 200; i++)
        {
            solver.Step();
        }

        Assert.Equal(1.0, solver.Psi.Norm(), 1e-3);
    }

    [Fact]
    public void FreePacketSpreadsAtAnalyticRate()
    {
        // σ(t)² = σ₀²(1 + (t/2σ₀²)²) for a Gaussian with position-space σ₀.
        var geometry = FreeGeometry() with { K0 = 0.0, SigmaX = 12.0, SigmaY = 12.0 };
        var solver = new SplitStepSolver(geometry, SlitMode.Both);

        const int steps = 150;
        for (var i = 0; i < steps; i++)
        {
            solver.Step();
        }

        var t = steps * geometry.Dt;
        var sigma0 = geometry.SigmaX;
        var expected = sigma0 * sigma0 * (1.0 + Math.Pow(t / (2.0 * sigma0 * sigma0), 2.0));
        var measured = MeasureVarianceX(solver.Psi);

        Assert.Equal(expected, measured, expected * 0.05);
    }

    [Fact]
    public void PacketDriftsAtGroupVelocity()
    {
        var geometry = FreeGeometry(height: 512) with { PacketY = 128, K0 = 0.5 };
        var solver = new SplitStepSolver(geometry, SlitMode.Both);

        const int steps = 200;
        for (var i = 0; i < steps; i++)
        {
            solver.Step();
        }

        // ⟨y⟩ = y₀ + k₀·t (group velocity = k₀ with m = 1).
        var expected = geometry.PacketY + geometry.K0 * steps * geometry.Dt;
        var measured = MeasureMeanY(solver.Psi);

        Assert.Equal(expected, measured, 1.0);
    }

    private static double MeasureVarianceX(WaveFunction psi)
    {
        double total = 0.0, mean = 0.0, meanSquare = 0.0;
        for (var y = 0; y < psi.Height; y++)
        {
            for (var x = 0; x < psi.Width; x++)
            {
                var index = 2 * (y * psi.Width + x);
                var p = (double)psi.Data[index] * psi.Data[index]
                    + (double)psi.Data[index + 1] * psi.Data[index + 1];
                total += p;
                mean += p * x;
                meanSquare += p * x * x;
            }
        }

        mean /= total;
        return meanSquare / total - mean * mean;
    }

    private static double MeasureMeanY(WaveFunction psi)
    {
        double total = 0.0, mean = 0.0;
        for (var y = 0; y < psi.Height; y++)
        {
            for (var x = 0; x < psi.Width; x++)
            {
                var index = 2 * (y * psi.Width + x);
                var p = (double)psi.Data[index] * psi.Data[index]
                    + (double)psi.Data[index + 1] * psi.Data[index + 1];
                total += p;
                mean += p * y;
            }
        }

        return mean / total;
    }
}
