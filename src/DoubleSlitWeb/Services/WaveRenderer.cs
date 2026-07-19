using DoubleSlitPhysics.Representations;

namespace DoubleSlitWeb.Services;

/// <summary>
///     Converts |ψ|² into a semi-transparent RGBA buffer (straight alpha, as
///     putImageData expects) so the wave layer composits over the scene canvas.
///     Brightness is normalised against the running peak of the current run to
///     avoid frame-to-frame brightness pumping.
/// </summary>
public sealed class WaveRenderer
{
    // Electron glow colour (matches the --series-1/--accent blue closely
    // enough in both light and dark themes).
    private const byte Red = 47;
    private const byte Green = 127;
    private const byte Blue = 222;

    private byte[] _buffer = Array.Empty<byte>();
    private float _peak;

    public byte[] Buffer => _buffer;

    public void Configure(int width, int height)
    {
        _buffer = new byte[4 * width * height];
        _peak = 0f;
    }

    /// <summary>Resets the brightness normalisation at the start of a run.</summary>
    public void StartRun() => _peak = 0f;

    public void RenderLive(WaveFunction psi)
    {
        var data = psi.Data;
        var siteCount = psi.Width * psi.Height;

        var frameMax = 0f;
        for (var i = 0; i < siteCount; i++)
        {
            var re = data[2 * i];
            var im = data[2 * i + 1];
            var p = re * re + im * im;
            if (p > frameMax)
            {
                frameMax = p;
            }
        }

        _peak = Math.Max(_peak, frameMax);
        FillBuffer(i =>
        {
            var re = data[2 * i];
            var im = data[2 * i + 1];
            return re * re + im * im;
        }, siteCount, _peak, alphaScale: 0.9f);
    }

    /// <summary>Renders the time-averaged |ψ|² cloud of a completed run.</summary>
    public void RenderCloud(float[] cloud)
    {
        var max = 0f;
        for (var i = 0; i < cloud.Length; i++)
        {
            if (cloud[i] > max)
            {
                max = cloud[i];
            }
        }

        FillBuffer(i => cloud[i], cloud.Length, max, alphaScale: 0.65f);
    }

    private void FillBuffer(Func<int, float> probabilityAt, int siteCount, float peak, float alphaScale)
    {
        if (peak <= 0f)
        {
            Array.Clear(_buffer);
            return;
        }

        var invPeak = 1f / peak;
        for (var i = 0; i < siteCount; i++)
        {
            // γ = 0.5 lifts the faint outer wave into visibility.
            var normalised = MathF.Sqrt(Math.Min(1f, probabilityAt(i) * invPeak));
            var alpha = (byte)(255f * alphaScale * normalised);
            var offset = 4 * i;
            _buffer[offset] = Red;
            _buffer[offset + 1] = Green;
            _buffer[offset + 2] = Blue;
            _buffer[offset + 3] = alpha;
        }
    }
}
