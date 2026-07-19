using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;
using DoubleSlitPhysics.Services;

namespace DoubleSlitPhysics.Tests;

public class InterferenceTests
{
    private static readonly ExperimentGeometry Geometry = new();

    private static double[] RunDistribution(SlitMode mode)
    {
        var experiment = new DoubleSlitExperiment(Geometry, mode);
        experiment.Reset(mode);
        experiment.RunToCompletion();
        return experiment.Screen.Distribution();
    }

    [Fact]
    public void DoubleSlitFringeSpacingMatchesTheory()
    {
        var distribution = RunDistribution(SlitMode.Both);
        var maxima = FindLocalMaxima(distribution, Geometry.Width / 2, 80);

        Assert.True(maxima.Count >= 3, $"Expected several fringes, found {maxima.Count}.");

        var spacings = new List<double>();
        for (var i = 1; i < maxima.Count; i++)
        {
            spacings.Add(maxima[i] - maxima[i - 1]);
        }

        // Δx = λL/d is the far-field limit; at this geometry the Fresnel
        // number d²/λL ≈ 3, so near-field corrections widen the fringes by
        // ~15-20%. The formula still sets the scale.
        var meanSpacing = spacings.Average();
        Assert.Equal(Geometry.FringeSpacing, meanSpacing, Geometry.FringeSpacing * 0.25);
    }

    [Fact]
    public void DoubleSlitVisibilityIsHigh()
    {
        var distribution = RunDistribution(SlitMode.Both);
        var visibility = CentralVisibility(distribution);
        Assert.True(visibility > 0.5, $"Double-slit visibility {visibility:F2} ≤ 0.5.");
    }

    [Fact]
    public void SingleSlitHasNoFringes()
    {
        var distribution = RunDistribution(SlitMode.LeftOnly);

        // A single slit produces a smooth diffraction envelope: its central
        // region must not oscillate like the double-slit pattern does.
        var visibility = CentralVisibility(distribution);
        Assert.True(visibility < 0.3, $"Single-slit central visibility {visibility:F2} ≥ 0.3.");
    }

    [Fact]
    public void MixtureOfSingleSlitsShowsNoInterference()
    {
        var left = RunDistribution(SlitMode.LeftOnly);
        var right = RunDistribution(SlitMode.RightOnly);
        var mixture = left.Zip(right, (l, r) => (l + r) / 2.0).ToArray();

        var doubleSlitVisibility = CentralVisibility(RunDistribution(SlitMode.Both));
        var mixtureVisibility = CentralVisibility(mixture);

        Assert.True(
            mixtureVisibility < doubleSlitVisibility / 2.0,
            $"Mixture visibility {mixtureVisibility:F2} not ≪ double-slit {doubleSlitVisibility:F2}.");
    }

    /// <summary>
    ///     (I_max − I_min)/(I_max + I_min) over a window of ± one expected
    ///     fringe period around the pattern centre.
    /// </summary>
    private static double CentralVisibility(double[] distribution)
    {
        var centre = Geometry.Width / 2;
        var window = (int)Math.Ceiling(Geometry.FringeSpacing);
        double max = double.MinValue, min = double.MaxValue;
        for (var x = centre - window; x <= centre + window; x++)
        {
            max = Math.Max(max, distribution[x]);
            min = Math.Min(min, distribution[x]);
        }

        return (max - min) / (max + min);
    }

    private static List<double> FindLocalMaxima(double[] distribution, int centre, int halfWindow)
    {
        var maxima = new List<double>();
        var threshold = distribution.Max() * 0.1;
        for (var x = centre - halfWindow + 1; x < centre + halfWindow - 1; x++)
        {
            if (distribution[x] > threshold
                && distribution[x] > distribution[x - 1]
                && distribution[x] >= distribution[x + 1])
            {
                maxima.Add(x);
            }
        }

        return maxima;
    }
}
