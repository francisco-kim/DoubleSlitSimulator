using DoubleSlitPhysics.Helpers;
using DoubleSlitPhysics.Models;
using DoubleSlitPhysics.Representations;

namespace DoubleSlitPhysics.Services;

/// <summary>
///     Split-step Fourier integrator for the 2D time-dependent Schrödinger
///     equation (ħ = m = 1). Per step:
///     ψ ← mask·ψ;  ψ ← IFFT₂( exp(-i(kₓ²+k_y²)·dt/2) · FFT₂(ψ) );  ψ ← mask·ψ.
///     The potential is a hard-wall barrier plus absorbing boundary layers,
///     both folded into a single multiplicative amplitude mask.
/// </summary>
public sealed class SplitStepSolver
{
    private readonly ExperimentGeometry _geometry;
    private readonly Fft _rowFft;
    private readonly Fft _columnFft;
    private readonly float[] _columnScratch;
    private float[] _mask;

    // Kinetic full-step phase factor exp(-i(kₓ²+k_y²)dt/2), separable into
    // per-axis factors; stored per axis to keep the tables small.
    private readonly float[] _phaseXCos;
    private readonly float[] _phaseXSin;
    private readonly float[] _phaseYCos;
    private readonly float[] _phaseYSin;

    public SplitStepSolver(ExperimentGeometry geometry, SlitMode mode)
    {
        _geometry = geometry;
        Psi = new WaveFunction(geometry.Width, geometry.Height);
        _rowFft = new Fft(geometry.Width);
        _columnFft = new Fft(geometry.Height);
        _columnScratch = new float[2 * geometry.Height];
        _mask = geometry.BuildMask(mode);

        (_phaseXCos, _phaseXSin) = BuildAxisPhase(geometry.Width, geometry.Dt);
        (_phaseYCos, _phaseYSin) = BuildAxisPhase(geometry.Height, geometry.Dt);

        Reset(mode);
    }

    public WaveFunction Psi { get; }

    public ExperimentGeometry Geometry => _geometry;

    /// <summary>Reinitialises the packet at the gun with the given slit mode.</summary>
    public void Reset(SlitMode mode)
    {
        _mask = _geometry.BuildMask(mode);
        GaussianWavePacket.Fill(Psi, _geometry);
    }

    public void Step()
    {
        ApplyMask();
        KineticStep();
        ApplyMask();
    }

    /// <summary>Probability remaining past the barrier (transmitted pulse).</summary>
    public double NormBeyondBarrier() =>
        Psi.NormInRows(_geometry.BarrierY + _geometry.BarrierThickness, _geometry.Height);

    private void ApplyMask()
    {
        var data = Psi.Data;
        var mask = _mask;
        for (var i = 0; i < mask.Length; i++)
        {
            var m = mask[i];
            if (m == 1f)
            {
                continue;
            }

            data[2 * i] *= m;
            data[2 * i + 1] *= m;
        }
    }

    private void KineticStep()
    {
        var data = Psi.Data;
        var width = _geometry.Width;
        var height = _geometry.Height;

        // Row FFTs (contiguous).
        for (var y = 0; y < height; y++)
        {
            _rowFft.Forward(data.AsSpan(2 * y * width, 2 * width));
        }

        // Column FFTs via gather/scatter, then apply the separable phase while
        // the column is still in the cache-friendly scratch buffer.
        for (var x = 0; x < width; x++)
        {
            GatherColumn(data, x, width, height);
            _columnFft.Forward(_columnScratch);
            ApplyPhaseToColumn(x, height);
            _columnFft.Inverse(_columnScratch);
            ScatterColumn(data, x, width, height);
        }

        for (var y = 0; y < height; y++)
        {
            _rowFft.Inverse(data.AsSpan(2 * y * width, 2 * width));
        }
    }

    private void GatherColumn(float[] data, int x, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var src = 2 * (y * width + x);
            _columnScratch[2 * y] = data[src];
            _columnScratch[2 * y + 1] = data[src + 1];
        }
    }

    private void ScatterColumn(float[] data, int x, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var dst = 2 * (y * width + x);
            data[dst] = _columnScratch[2 * y];
            data[dst + 1] = _columnScratch[2 * y + 1];
        }
    }

    private void ApplyPhaseToColumn(int x, int height)
    {
        var xCos = _phaseXCos[x];
        var xSin = _phaseXSin[x];
        for (var ky = 0; ky < height; ky++)
        {
            // Combined phase = phaseX(kₓ) · phaseY(k_y).
            var cos = xCos * _phaseYCos[ky] - xSin * _phaseYSin[ky];
            var sin = xCos * _phaseYSin[ky] + xSin * _phaseYCos[ky];

            var re = _columnScratch[2 * ky];
            var im = _columnScratch[2 * ky + 1];
            _columnScratch[2 * ky] = re * cos - im * sin;
            _columnScratch[2 * ky + 1] = re * sin + im * cos;
        }
    }

    private static (float[] Cos, float[] Sin) BuildAxisPhase(int size, double dt)
    {
        var cos = new float[size];
        var sin = new float[size];
        for (var n = 0; n < size; n++)
        {
            // FFT frequency layout: n < size/2 ⇒ k = 2πn/size, else k − 2π.
            var frequency = n <= size / 2 ? n : n - size;
            var k = 2.0 * Math.PI * frequency / size;
            var angle = -k * k * dt / 2.0;
            cos[n] = (float)Math.Cos(angle);
            sin[n] = (float)Math.Sin(angle);
        }

        return (cos, sin);
    }
}
