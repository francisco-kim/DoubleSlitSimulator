namespace DoubleSlitPhysics.Services;

/// <summary>
///     Time-integrated arrival distribution P(x) at the detection screen row.
///     After an evolution completes, the distribution is normalised into a CDF
///     from which individual electron impact positions are sampled.
/// </summary>
public sealed class ScreenAccumulator
{
    private readonly double[] _bins;
    private double[]? _cdf;

    public ScreenAccumulator(int binCount)
    {
        _bins = new double[binCount];
    }

    public int BinCount => _bins.Length;

    public bool IsFinalised => _cdf is not null;

    /// <summary>Adds |ψ(x, screenRow)|²·dt for the current time step.</summary>
    public void Add(ReadOnlySpan<float> screenRowProbability, double dt)
    {
        _cdf = null;
        for (var x = 0; x < _bins.Length; x++)
        {
            _bins[x] += screenRowProbability[x] * dt;
        }
    }

    public void Clear()
    {
        Array.Clear(_bins);
        _cdf = null;
    }

    /// <summary>Normalised P(x); zero everywhere if nothing has arrived.</summary>
    public double[] Distribution()
    {
        var total = _bins.Sum();
        var result = new double[_bins.Length];
        if (total <= 0.0)
        {
            return result;
        }

        for (var x = 0; x < _bins.Length; x++)
        {
            result[x] = _bins[x] / total;
        }

        return result;
    }

    /// <summary>Builds the CDF; must be called after the evolution completes.</summary>
    public void Finalise()
    {
        var cdf = new double[_bins.Length];
        double running = 0.0;
        for (var x = 0; x < _bins.Length; x++)
        {
            running += _bins[x];
            cdf[x] = running;
        }

        if (running <= 0.0)
        {
            throw new InvalidOperationException("Screen distribution is empty; run the evolution first.");
        }

        for (var x = 0; x < _bins.Length; x++)
        {
            cdf[x] /= running;
        }

        _cdf = cdf;
    }

    /// <summary>
    ///     Samples one impact position (continuous, in bin units) by inverting
    ///     the CDF with linear interpolation inside the chosen bin.
    /// </summary>
    public double Sample(Random rng)
    {
        var cdf = _cdf ?? throw new InvalidOperationException("Call Finalise() before sampling.");
        var u = rng.NextDouble();

        var low = 0;
        var high = cdf.Length - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (cdf[mid] < u)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        var previous = low > 0 ? cdf[low - 1] : 0.0;
        var binMass = cdf[low] - previous;
        var fraction = binMass > 0.0 ? (u - previous) / binMass : 0.5;
        return low + fraction;
    }
}
