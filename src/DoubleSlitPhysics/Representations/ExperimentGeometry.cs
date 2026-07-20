using DoubleSlitPhysics.Models;

namespace DoubleSlitPhysics.Representations;

/// <summary>
///     Grid layout and physical parameters of the double-slit experiment, in
///     dimensionless units (ħ = m = 1, dx = 1). The electron travels in +y:
///     gun near y = 0, barrier in the middle, detection screen near y = Height.
/// </summary>
public sealed record ExperimentGeometry
{
    /// <summary>Transverse extent (fringes run along x).</summary>
    public int Width { get; init; } = 512;

    /// <summary>Propagation extent.</summary>
    public int Height { get; init; } = 256;

    /// <summary>Mean momentum along +y; λ = 2π/k₀.</summary>
    public double K0 { get; init; } = 1.0;

    public double Wavelength => 2.0 * Math.PI / K0;

    public int PacketX => Width / 2;

    public int PacketY { get; init; } = 40;

    /// <summary>Transverse std dev of |ψ|²; must illuminate both slits.</summary>
    public double SigmaX { get; init; } = 64.0;

    /// <summary>
    ///     Practical half-width of the source for illustration purposes (blocked
    ///     -electron sampling, gun icon width): ±2σ, ~95% of the Gaussian's mass.
    /// </summary>
    public double SourceHalfWidth => 2.0 * SigmaX;

    /// <summary>Longitudinal std dev of |ψ|²; large ⇒ quasi-monochromatic.</summary>
    public double SigmaY { get; init; } = 16.0;

    public int BarrierY { get; init; } = 96;

    public int BarrierThickness { get; init; } = 3;

    public int SlitWidth { get; init; } = 8;

    /// <summary>Centre-to-centre slit separation.</summary>
    public int SlitSeparation { get; init; } = 48;

    public int ScreenY { get; init; } = 224;

    /// <summary>Width of the absorbing cos²-ramp at the domain edges.</summary>
    public int AbsorberWidth { get; init; } = 32;

    /// <summary>Absorber strength per mask application (amplitude e-folding).</summary>
    public double AbsorberStrength { get; init; } = 0.2;

    public double Dt { get; init; } = 0.4;

    public int MaxSteps { get; init; } = 700;

    /// <summary>Distance from barrier to screen.</summary>
    public int BarrierToScreen => ScreenY - BarrierY;

    /// <summary>Expected fringe period on the screen, Δx ≈ λL/d.</summary>
    public double FringeSpacing => Wavelength * BarrierToScreen / SlitSeparation;

    public int LeftSlitCentre => Width / 2 - SlitSeparation / 2;

    public int RightSlitCentre => Width / 2 + SlitSeparation / 2;

    /// <summary>
    ///     Amplitude mask applied twice per split-step: 0 inside the barrier
    ///     wall, 1 in open slits and free space, smoothly decaying inside the
    ///     absorbing boundary layers (kills periodic-FFT wraparound).
    /// </summary>
    public float[] BuildMask(SlitMode mode)
    {
        var mask = new float[Width * Height];

        // Absorber profile per axis: 1 in the interior, cos²-ramped decay
        // exp(-strength · ramp) towards each edge.
        var absorbX = BuildAbsorberProfile(Width);
        var absorbY = BuildAbsorberProfile(Height);

        for (var y = 0; y < Height; y++)
        {
            var inBarrier = y >= BarrierY && y < BarrierY + BarrierThickness;
            for (var x = 0; x < Width; x++)
            {
                float value = absorbX[x] * absorbY[y];
                if (inBarrier && !IsSlitOpenAt(x, mode))
                {
                    value = 0f;
                }

                mask[y * Width + x] = value;
            }
        }

        return mask;
    }

    public bool IsSlitOpenAt(int x, SlitMode mode)
    {
        var halfWidth = SlitWidth / 2.0;
        var inLeft = Math.Abs(x - LeftSlitCentre) < halfWidth;
        var inRight = Math.Abs(x - RightSlitCentre) < halfWidth;
        return mode switch
        {
            SlitMode.LeftOnly => inLeft,
            SlitMode.RightOnly => inRight,
            _ => inLeft || inRight,
        };
    }

    private float[] BuildAbsorberProfile(int size)
    {
        var profile = new float[size];
        for (var i = 0; i < size; i++)
        {
            var distanceToEdge = Math.Min(i, size - 1 - i);
            if (distanceToEdge >= AbsorberWidth)
            {
                profile[i] = 1f;
                continue;
            }

            // ramp: 0 at the interior boundary of the layer, 1 at the edge.
            var t = 1.0 - distanceToEdge / (double)AbsorberWidth;
            var ramp = Math.Sin(Math.PI * t / 2.0);
            profile[i] = (float)Math.Exp(-AbsorberStrength * ramp * ramp);
        }

        return profile;
    }
}
