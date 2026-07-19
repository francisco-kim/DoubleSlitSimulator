using System.Diagnostics;

using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;
using DoubleSlitPhysics.Services;

namespace DoubleSlitWeb.Services;

/// <summary>One electron impact the UI still has to animate.</summary>
public readonly record struct ElectronShot(double ImpactX, SlitMode Slit, bool Observed);

/// <summary>
///     UI-side state machine driving the double-slit experiment. The wave
///     evolution for a given geometry + slit mode is deterministic, so its
///     screen distribution is computed once (with the animation playing) and
///     cached; every subsequent electron just samples an impact position from
///     the cached distribution.
/// </summary>
public sealed class ExperimentRunner
{
    private readonly Random _rng = new();

    private DoubleSlitExperiment _experiment;
    private CachedDistribution? _both;
    private CachedDistribution? _left;
    private CachedDistribution? _right;
    private double _fireAccumulator;
    private bool _shotPendingAfterAnimation;

    public ExperimentRunner()
    {
        Geometry = new ExperimentGeometry();
        _experiment = new DoubleSlitExperiment(Geometry, SlitMode.Both);
        Histogram = new double[Geometry.Width];
        Cloud = new float[Geometry.Width * Geometry.Height];
    }

    public ExperimentGeometry Geometry { get; private set; }

    /// <summary>Which-slit measurement on: every shot collapses to one slit.</summary>
    public bool Observe { get; set; }

    public bool AutoFire { get; set; }

    /// <summary>Electrons per second in auto-fire; minimum 1 (one at a time).</summary>
    public double FireRatePerSecond { get; set; } = 2.0;

    /// <summary>Wave-animation speed multiplier (steps per frame scale).</summary>
    public double WaveSpeed { get; set; } = 1.0;

    public bool ShowCurve { get; set; } = true;

    public bool IsAnimating { get; private set; }

    public SlitMode AnimatingMode { get; private set; } = SlitMode.Both;

    public WaveFunction Psi => _experiment.Psi;

    /// <summary>Time-averaged |ψ|² of the latest animated run.</summary>
    public float[] Cloud { get; private set; }

    public bool HasCloud { get; private set; }

    /// <summary>Set whenever the wave layer needs re-blitting; page clears it.</summary>
    public bool WaveDirty { get; set; } = true;

    public int DotCount { get; private set; }

    public int LeftSlitCount { get; private set; }

    public int RightSlitCount { get; private set; }

    /// <summary>Accumulated impact counts per screen column (for the curve).</summary>
    public double[] Histogram { get; private set; }

    /// <summary>Shots fired this frame; the page consumes and animates them.</summary>
    public List<ElectronShot> ShotsToAnimate { get; } = new();

    /// <summary>Expected P(x) for the current mode, from cached runs; null if unknown.</summary>
    public double[]? TheoryCurve()
    {
        if (!Observe)
        {
            return _both?.Probability;
        }

        if (_left is null && _right is null)
        {
            return null;
        }

        var left = _left?.Probability;
        var right = _right?.Probability;
        var result = new double[Geometry.Width];
        for (var x = 0; x < result.Length; x++)
        {
            var l = left?[x] ?? right![x];
            var r = right?[x] ?? left![x];
            result[x] = (l + r) / 2.0;
        }

        return result;
    }

    public void ConfigureSlits(int slitWidth, int slitSeparation)
    {
        Geometry = Geometry with { SlitWidth = slitWidth, SlitSeparation = slitSeparation };
        _experiment = new DoubleSlitExperiment(Geometry, SlitMode.Both);
        _both = _left = _right = null;
        IsAnimating = false;
        _shotPendingAfterAnimation = false;
        HasCloud = false;
        Array.Clear(Cloud);
        WaveDirty = true;
    }

    /// <summary>
    ///     Fires one electron. If the screen distribution for the required slit
    ///     mode is not cached yet, the wave evolution is animated first and the
    ///     impact happens when it completes.
    /// </summary>
    public void Fire()
    {
        var mode = ChooseMode();
        var distribution = DistributionFor(mode);
        if (distribution is null)
        {
            StartAnimation(mode, shotPending: true);
            return;
        }

        Emit(mode, distribution);
    }

    /// <summary>Replays the wave animation for the current mode (no impact).</summary>
    public void Replay() => StartAnimation(ChooseMode(), shotPending: false);

    public void ResetScreen()
    {
        DotCount = 0;
        LeftSlitCount = 0;
        RightSlitCount = 0;
        Array.Clear(Histogram);
        ShotsToAnimate.Clear();
    }

    /// <summary>
    ///     Advances the runner by one animation frame: steps the wave evolution
    ///     under the wall-time budget while animating, and schedules auto-fire
    ///     shots otherwise.
    /// </summary>
    public void Tick(double budgetMilliseconds, double deltaSeconds)
    {
        if (IsAnimating)
        {
            TickAnimation(budgetMilliseconds);
            return;
        }

        if (!AutoFire)
        {
            _fireAccumulator = 0.0;
            return;
        }

        _fireAccumulator = Math.Min(_fireAccumulator + deltaSeconds * FireRatePerSecond, 2.0);
        while (_fireAccumulator >= 1.0)
        {
            _fireAccumulator -= 1.0;
            Fire();
            if (IsAnimating)
            {
                // The shot triggered a wave run; pause the schedule until done.
                _fireAccumulator = 0.0;
                break;
            }
        }
    }

    private void TickAnimation(double budgetMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        var targetSteps = Math.Max(1, (int)Math.Round(2.0 * WaveSpeed));
        for (var i = 0; i < targetSteps; i++)
        {
            if (!_experiment.Step())
            {
                break;
            }

            AccumulateCloud();
            if (stopwatch.Elapsed.TotalMilliseconds > budgetMilliseconds)
            {
                break;
            }
        }

        WaveDirty = true;
        if (!_experiment.IsComplete)
        {
            return;
        }

        var cached = new CachedDistribution(_experiment.Screen.Distribution());
        switch (_experiment.Mode)
        {
            case SlitMode.LeftOnly:
                _left = cached;
                break;
            case SlitMode.RightOnly:
                _right = cached;
                break;
            default:
                _both = cached;
                break;
        }

        IsAnimating = false;
        HasCloud = true;

        if (_shotPendingAfterAnimation)
        {
            _shotPendingAfterAnimation = false;
            Emit(_experiment.Mode, cached);
        }
    }

    private void StartAnimation(SlitMode mode, bool shotPending)
    {
        if (IsAnimating)
        {
            _shotPendingAfterAnimation |= shotPending;
            return;
        }

        _experiment.Reset(mode);
        AnimatingMode = mode;
        IsAnimating = true;
        _shotPendingAfterAnimation = shotPending;
        HasCloud = false;
        Array.Clear(Cloud);
        WaveDirty = true;
    }

    private SlitMode ChooseMode() =>
        !Observe ? SlitMode.Both
            : _rng.Next(2) == 0 ? SlitMode.LeftOnly
            : SlitMode.RightOnly;

    private CachedDistribution? DistributionFor(SlitMode mode) => mode switch
    {
        SlitMode.LeftOnly => _left,
        SlitMode.RightOnly => _right,
        _ => _both,
    };

    private void Emit(SlitMode mode, CachedDistribution distribution)
    {
        var x = distribution.Sample(_rng);
        DotCount++;
        Histogram[Math.Clamp((int)x, 0, Histogram.Length - 1)]++;
        if (mode is SlitMode.LeftOnly)
        {
            LeftSlitCount++;
        }
        else if (mode is SlitMode.RightOnly)
        {
            RightSlitCount++;
        }

        ShotsToAnimate.Add(new ElectronShot(x, mode, Observe));
    }

    private void AccumulateCloud()
    {
        var data = _experiment.Psi.Data;
        var cloud = Cloud;
        for (var i = 0; i < cloud.Length; i++)
        {
            var re = data[2 * i];
            var im = data[2 * i + 1];
            cloud[i] += re * re + im * im;
        }
    }

    /// <summary>Normalised P(x) plus its CDF for inverse-transform sampling.</summary>
    private sealed class CachedDistribution
    {
        private readonly double[] _cdf;

        public CachedDistribution(double[] probability)
        {
            Probability = probability;
            _cdf = new double[probability.Length];
            var running = 0.0;
            for (var x = 0; x < probability.Length; x++)
            {
                running += probability[x];
                _cdf[x] = running;
            }

            // Guard against runs where nothing reached the screen.
            if (running > 0.0)
            {
                for (var x = 0; x < _cdf.Length; x++)
                {
                    _cdf[x] /= running;
                }
            }
        }

        public double[] Probability { get; }

        public double Sample(Random rng)
        {
            var u = rng.NextDouble();
            var low = 0;
            var high = _cdf.Length - 1;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (_cdf[mid] < u)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            var previous = low > 0 ? _cdf[low - 1] : 0.0;
            var binMass = _cdf[low] - previous;
            var fraction = binMass > 0.0 ? (u - previous) / binMass : 0.5;
            return low + fraction;
        }
    }
}
