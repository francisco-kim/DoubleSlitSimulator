using DoubleSlitPhysics.Services;

namespace DoubleSlitPhysics.Tests;

public class ScreenSamplerTests
{
    [Fact]
    public void SamplesFollowTheAccumulatedDistribution()
    {
        const int bins = 16;
        var accumulator = new ScreenAccumulator(bins);

        // Triangular distribution: weight proportional to bin index + 1.
        var row = new float[bins];
        for (var x = 0; x < bins; x++)
        {
            row[x] = x + 1;
        }

        accumulator.Add(row, dt: 1.0);
        accumulator.Finalise();

        const int samples = 50_000;
        var rng = new Random(123);
        var counts = new int[bins];
        for (var i = 0; i < samples; i++)
        {
            counts[(int)accumulator.Sample(rng)]++;
        }

        var totalWeight = bins * (bins + 1) / 2.0;
        for (var x = 0; x < bins; x++)
        {
            var expected = samples * (x + 1) / totalWeight;
            var tolerance = 4.0 * Math.Sqrt(expected);   // ~4σ Poisson
            Assert.True(
                Math.Abs(counts[x] - expected) < tolerance,
                $"Bin {x}: got {counts[x]}, expected {expected:F0} ± {tolerance:F0}.");
        }
    }

    [Fact]
    public void SamplingIsDeterministicWithSeededRng()
    {
        var accumulator = new ScreenAccumulator(8);
        accumulator.Add(new float[] { 1, 2, 3, 4, 4, 3, 2, 1 }, dt: 1.0);
        accumulator.Finalise();

        var first = Draw(accumulator, new Random(99), 100);
        var second = Draw(accumulator, new Random(99), 100);
        Assert.Equal(first, second);
    }

    [Fact]
    public void SamplingBeforeFinaliseThrows()
    {
        var accumulator = new ScreenAccumulator(4);
        accumulator.Add(new float[] { 1, 1, 1, 1 }, dt: 1.0);
        Assert.Throws<InvalidOperationException>(() => accumulator.Sample(new Random(1)));
    }

    private static double[] Draw(ScreenAccumulator accumulator, Random rng, int count)
    {
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = accumulator.Sample(rng);
        }

        return result;
    }
}
