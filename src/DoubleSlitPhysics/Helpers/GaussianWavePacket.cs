using DoubleSlitPhysics.Representations;

namespace DoubleSlitPhysics.Helpers;

/// <summary>
///     Fills a wavefunction with a normalised Gaussian packet
///     ψ ∝ exp(-(x-x₀)²/4σₓ² - (y-y₀)²/4σᵧ²) · exp(i k₀ y), so that σₓ, σᵧ are
///     the position-space standard deviations of |ψ|².
/// </summary>
public static class GaussianWavePacket
{
    public static void Fill(WaveFunction psi, ExperimentGeometry geometry)
    {
        var data = psi.Data;
        var width = psi.Width;
        var invFourSigmaX2 = 1.0 / (4.0 * geometry.SigmaX * geometry.SigmaX);
        var invFourSigmaY2 = 1.0 / (4.0 * geometry.SigmaY * geometry.SigmaY);

        for (var y = 0; y < psi.Height; y++)
        {
            var dy = y - geometry.PacketY;
            var phase = geometry.K0 * y;
            var (sin, cos) = Math.SinCos(phase);
            var envelopeY = Math.Exp(-dy * dy * invFourSigmaY2);
            for (var x = 0; x < width; x++)
            {
                var dx = x - geometry.PacketX;
                var envelope = envelopeY * Math.Exp(-dx * dx * invFourSigmaX2);
                var index = 2 * (y * width + x);
                data[index] = (float)(envelope * cos);
                data[index + 1] = (float)(envelope * sin);
            }
        }

        psi.Normalise();
    }
}
