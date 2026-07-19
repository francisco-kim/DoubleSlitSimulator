using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;

namespace DoubleSlitPhysics.Services;

/// <summary>
///     One full "electron shot": evolves the wavefunction from the gun until
///     the transmitted pulse has been absorbed past the screen, accumulating
///     the arrival distribution at the screen row along the way.
/// </summary>
public sealed class DoubleSlitExperiment
{
    // The run is complete once the transmitted probability has decayed to this
    // fraction of its running maximum (the pulse has passed the screen).
    private const double CompletionFraction = 0.05;

    private readonly SplitStepSolver _solver;
    private readonly float[] _screenRow;
    private double _maxNormBeyondBarrier;

    public DoubleSlitExperiment(ExperimentGeometry geometry, SlitMode mode)
    {
        Geometry = geometry;
        Mode = mode;
        _solver = new SplitStepSolver(geometry, mode);
        _screenRow = new float[geometry.Width];
        Screen = new ScreenAccumulator(geometry.Width);
    }

    public ExperimentGeometry Geometry { get; }

    public SlitMode Mode { get; private set; }

    public ScreenAccumulator Screen { get; }

    public WaveFunction Psi => _solver.Psi;

    public int StepCount { get; private set; }

    public bool IsComplete { get; private set; }

    public void Reset(SlitMode mode)
    {
        Mode = mode;
        _solver.Reset(mode);
        Screen.Clear();
        StepCount = 0;
        IsComplete = false;
        _maxNormBeyondBarrier = 0.0;
    }

    /// <summary>Advances one time step; returns false once the run is complete.</summary>
    public bool Step()
    {
        if (IsComplete)
        {
            return false;
        }

        _solver.Step();
        StepCount++;

        Psi.ProbabilityOfRow(Geometry.ScreenY, _screenRow);
        Screen.Add(_screenRow, Geometry.Dt);

        var beyond = _solver.NormBeyondBarrier();
        _maxNormBeyondBarrier = Math.Max(_maxNormBeyondBarrier, beyond);

        // Guard with a minimum step count so the initial Gaussian tail beyond
        // the barrier cannot trigger completion before the pulse arrives.
        var pulseAbsorbed = StepCount > 100
            && _maxNormBeyondBarrier > 0.0
            && beyond < CompletionFraction * _maxNormBeyondBarrier;
        if (StepCount >= Geometry.MaxSteps || pulseAbsorbed)
        {
            IsComplete = true;
            Screen.Finalise();
        }

        return !IsComplete;
    }

    /// <summary>Runs headless until completion (used for caching and tests).</summary>
    public void RunToCompletion()
    {
        while (Step())
        {
        }
    }
}
