using System.Diagnostics;

using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;
using DoubleSlitPhysics.Services;

namespace DoubleSlitWeb.Services;

/// <summary>One electron impact the UI still has to animate.</summary>
public readonly record struct ElectronShot(double ImpactX, SlitMode Slit, bool Observed);

/// <summary>
///     UI-side state machine driving the double-slit experiment.
///     Unobserved mode: the wave evolution for the current geometry is
///     deterministic, so its screen distribution is computed once (with the
///     animation playing) and cached; every electron samples from it.
///     Observed mode: which-path measurement destroys the wave picture, so no
///     wave is shown at all — each electron passes one slit (50/50) and lands
///     in a sharp stripe behind it (classical trajectory with a small blur),
///     the pedagogical "electrons as little balls" outcome.
/// </summary>
public sealed class ExperimentRunner
{
    /// <summary>Blur (px) of the classical stripes in observed mode.</summary>
    private const double ClassicalBlurSigma = 2.0;

    /// <summary>
    ///     Expected spurious (undetected) companions drawn per real observed
    ///     shot, to show that a wide source mostly hits the solid wall — only a
    ///     narrow sliver of it lines up with either slit. Not the true ~9:1
    ///     physical ratio (the gun's width is much larger than the apertures),
    ///     which would clutter the animation; tapered as the firing rate grows
    ///     so a 50-electron burst doesn't dump hundreds of grey dots at once —
    ///     but never all the way to zero, so the wall is still visibly taking
    ///     hits even at the fastest auto-fire rate. Fractional values are
    ///     resolved probabilistically in <see cref="EmitObserved" />.
    /// </summary>
    private static double ExpectedBlockedElectronsFor(int batchSize) => batchSize switch
    {
        <= 1 => 3.0,
        <= 5 => 2.0,
        <= 15 => 1.0,
        _ => 0.3,
    };

    private readonly Random _rng = new();

    private DoubleSlitExperiment _experiment;
    private CachedDistribution? _both;
    private double[]? _classicalTheory;
    private double _fireAccumulator;
    private int _pendingShotsAfterAnimation;

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

    /// <summary>Electrons per second in auto-fire and per burst; minimum 1.</summary>
    public double FireRatePerSecond { get; set; } = 2.0;

    /// <summary>Wave-animation speed multiplier (steps per frame scale).</summary>
    public double WaveSpeed { get; set; } = 1.0;

    /// <summary>Show the dashed quantum-mechanical / classical prediction curve.</summary>
    public bool ShowTheoryCurve { get; set; } = true;

    /// <summary>Show the solid measured-histogram curve of actual impacts.</summary>
    public bool ShowMeasuredCurve { get; set; }

    /// <summary>Keep the time-averaged wave visible after a run completes.</summary>
    public bool PersistCloud { get; set; } = true;

    public bool IsAnimating { get; private set; }

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

    /// <summary>
    ///     Spurious observed-mode electrons fired this frame that never reach a
    ///     slit — sampled across the gun's whole width and absorbed at the
    ///     barrier. Purely illustrative: they are not counted in
    ///     <see cref="DotCount" /> or the histogram, since nothing was detected.
    /// </summary>
    public List<double> BlockedLaunchXs { get; } = new();

    /// <summary>Expected P(x) for the current mode; null if not known yet.</summary>
    public double[]? TheoryCurve() =>
        Observe ? _classicalTheory ??= BuildClassicalTheory() : _both?.Probability;

    public void ConfigureSlits(int slitWidth, int slitSeparation)
    {
        Geometry = Geometry with { SlitWidth = slitWidth, SlitSeparation = slitSeparation };
        _experiment = new DoubleSlitExperiment(Geometry, SlitMode.Both);
        _both = null;
        _classicalTheory = null;
        IsAnimating = false;
        _pendingShotsAfterAnimation = 0;
        HasCloud = false;
        Array.Clear(Cloud);
        WaveDirty = true;
    }

    /// <summary>
    ///     Fires one electron from auto-fire's schedule: reuses the cached wave
    ///     distribution whenever possible so the rate is not throttled by
    ///     replaying the animation on every shot.
    /// </summary>
    public void Fire() => FireMany(1);

    /// <summary>
    ///     Fires several electrons in one burst (or auto-fire's schedule).
    ///     Unobserved shots need the cached wave distribution; if missing, the
    ///     wave animation runs first and the whole burst lands when it completes.
    /// </summary>
    public void FireMany(int count)
    {
        if (Observe)
        {
            // Auto-fire calls this with count=1 on every tick (so the rate
            // isn't throttled), which would otherwise always look like a lone
            // solo shot; use the configured rate instead so scaling reflects
            // how busy the screen actually gets.
            var effectiveBatch = AutoFire ? Math.Max(count, (int)FireRatePerSecond) : count;

            // A manual single-electron burst (button, not auto-fire) should
            // show just that one electron threading a slit — the spurious
            // wall hits are there to convey a busy source, which a lone shot
            // isn't.
            var showBlocked = AutoFire || count > 1;
            for (var i = 0; i < count; i++)
            {
                EmitObserved(effectiveBatch, showBlocked);
            }

            return;
        }

        if (_both is null)
        {
            StartAnimation(pendingShots: count);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            EmitInterference();
        }
    }

    /// <summary>
    ///     The explicit "Fire one electron" button: unlike <see cref="Fire" />,
    ///     this always replays the wave animation (even once cached) so a single
    ///     manual shot is never silent — the whole point of firing "just one".
    /// </summary>
    public void FireOneVisibly()
    {
        if (Observe)
        {
            EmitObserved(batchSize: 1, showBlocked: false);
            return;
        }

        StartAnimation(pendingShots: 1);
    }

    /// <summary>Replays the wave animation (interference mode only).</summary>
    public void Replay()
    {
        if (!Observe)
        {
            StartAnimation(pendingShots: 0);
        }
    }

    /// <summary>Stops any wave run and hides the cloud (e.g. observation switched on).</summary>
    public void CancelWave()
    {
        IsAnimating = false;
        _pendingShotsAfterAnimation = 0;
        HasCloud = false;
        WaveDirty = true;
    }

    /// <summary>
    ///     Applies the "keep wave visible" toggle. While a run is in progress
    ///     the live animation already shows regardless, so this only affects
    ///     display once the run has completed: turning it off hides the cloud
    ///     immediately, and turning it back on re-reveals the last completed
    ///     run's accumulated cloud (still held in <see cref="Cloud" />) without
    ///     needing to re-run anything.
    /// </summary>
    public void SetPersistCloud(bool value)
    {
        PersistCloud = value;
        if (IsAnimating)
        {
            return;
        }

        HasCloud = value && _both is not null;
        WaveDirty = true;
    }

    public void ResetScreen()
    {
        DotCount = 0;
        LeftSlitCount = 0;
        RightSlitCount = 0;
        Array.Clear(Histogram);
        ShotsToAnimate.Clear();
        BlockedLaunchXs.Clear();
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
        // WaveSpeed must control how many steps run per frame; a per-frame wall
        // -clock budget close to a single step's cost (as under WASM) would
        // silently cap every speed setting at ~1 step/frame. So targetSteps is
        // always run in full — budgetMilliseconds only guards against a
        // pathological stall (e.g. a slow first JIT frame), at a much larger
        // multiple of the normal per-frame cost.
        const double SafetyCapMultiplier = 25.0;
        var stopwatch = Stopwatch.StartNew();
        var targetSteps = Math.Max(1, (int)Math.Round(2.0 * WaveSpeed));
        for (var i = 0; i < targetSteps; i++)
        {
            if (!_experiment.Step())
            {
                break;
            }

            AccumulateCloud();
            if (stopwatch.Elapsed.TotalMilliseconds > budgetMilliseconds * SafetyCapMultiplier)
            {
                break;
            }
        }

        WaveDirty = true;
        if (!_experiment.IsComplete)
        {
            return;
        }

        _both = new CachedDistribution(_experiment.Screen.Distribution());
        IsAnimating = false;
        HasCloud = PersistCloud;

        var pending = _pendingShotsAfterAnimation;
        _pendingShotsAfterAnimation = 0;
        for (var i = 0; i < pending; i++)
        {
            EmitInterference();
        }
    }

    private void StartAnimation(int pendingShots)
    {
        if (IsAnimating)
        {
            _pendingShotsAfterAnimation += pendingShots;
            return;
        }

        _experiment.Reset(SlitMode.Both);
        IsAnimating = true;
        _pendingShotsAfterAnimation = pendingShots;
        HasCloud = false;
        Array.Clear(Cloud);
        WaveDirty = true;
    }

    private void EmitInterference() =>
        RecordShot(_both!.Sample(_rng), SlitMode.Both);

    private void EmitObserved(int batchSize, bool showBlocked = true)
    {
        var mode = _rng.Next(2) == 0 ? SlitMode.LeftOnly : SlitMode.RightOnly;
        var centre = mode is SlitMode.LeftOnly ? Geometry.LeftSlitCentre : Geometry.RightSlitCentre;

        // Classical trajectory: measured at the slit, so the electron lands in
        // a sharp stripe behind it — uniform across the aperture plus a small
        // Gaussian blur.
        var x = centre
            + (_rng.NextDouble() - 0.5) * Geometry.SlitWidth
            + SampleStandardNormal() * ClassicalBlurSigma;

        RecordShot(Math.Clamp(x, 0.0, Geometry.Width - 1.0), mode);

        if (!showBlocked)
        {
            return;
        }

        // Most of the wide source actually hits the solid wall, not a slit —
        // show a few of those undetected electrons too, sampled across the
        // gun's real width (clamped to the source's ±2σ extent, not the whole
        // grid — the source is wide, not literally wall-to-wall). Below one
        // expected companion, resolve the fraction as a probability so
        // blocked electrons still appear now and then instead of vanishing
        // entirely at high firing rates.
        var expectedBlocked = ExpectedBlockedElectronsFor(batchSize);
        var blockedCount = (int)expectedBlocked;
        if (_rng.NextDouble() < expectedBlocked - blockedCount)
        {
            blockedCount++;
        }

        var half = Geometry.SourceHalfWidth;
        for (var i = 0; i < blockedCount; i++)
        {
            var sampled = Geometry.PacketX + SampleStandardNormal() * Geometry.SigmaX;
            var bounded = Math.Clamp(sampled, Geometry.PacketX - half, Geometry.PacketX + half);
            var blockedX = PushOutsideSlits(bounded);
            BlockedLaunchXs.Add(Math.Clamp(blockedX, 0.0, Geometry.Width - 1.0));
        }
    }

    /// <summary>
    ///     Nudges a sampled position just clear of either slit aperture, so a
    ///     "blocked" electron never visually threads the gap it was meant to miss.
    /// </summary>
    private double PushOutsideSlits(double x)
    {
        var margin = Geometry.SlitWidth / 2.0 + 2.0;
        foreach (var centre in new double[] { Geometry.LeftSlitCentre, Geometry.RightSlitCentre })
        {
            if (Math.Abs(x - centre) < margin)
            {
                var offset = x - centre;
                return centre + (offset == 0 ? 1.0 : Math.Sign(offset)) * margin;
            }
        }

        return x;
    }

    private void RecordShot(double x, SlitMode mode)
    {
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

    private double[] BuildClassicalTheory()
    {
        // Two slit apertures convolved with the classical blur.
        var result = new double[Geometry.Width];
        var halfWidth = Geometry.SlitWidth / 2.0;
        foreach (var centre in new[] { Geometry.LeftSlitCentre, Geometry.RightSlitCentre })
        {
            for (var x = 0; x < result.Length; x++)
            {
                var sum = 0.0;
                for (var offset = -halfWidth; offset <= halfWidth; offset += 0.5)
                {
                    var d = x - (centre + offset);
                    sum += Math.Exp(-d * d / (2.0 * ClassicalBlurSigma * ClassicalBlurSigma));
                }

                result[x] += sum;
            }
        }

        var total = result.Sum();
        for (var x = 0; x < result.Length; x++)
        {
            result[x] /= total;
        }

        return result;
    }

    private double SampleStandardNormal()
    {
        // Box–Muller.
        var u = 1.0 - _rng.NextDouble();
        var v = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
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
