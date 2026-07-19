namespace DoubleSlitPhysics.Representations;

/// <summary>
///     A discretised 2D complex wavefunction ψ(x, y) on a Width × Height grid
///     (dx = 1), stored as interleaved single-precision re/im pairs in row-major
///     order: Data[2 * (y * Width + x)] = Re ψ, Data[2 * (y * Width + x) + 1] = Im ψ.
/// </summary>
public sealed class WaveFunction
{
    public WaveFunction(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new float[2 * width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Data { get; }

    /// <summary>Total probability ∑|ψ|² (dx = dy = 1).</summary>
    public double Norm()
    {
        double sum = 0.0;
        var data = Data;
        for (var i = 0; i < data.Length; i += 2)
        {
            sum += (double)data[i] * data[i] + (double)data[i + 1] * data[i + 1];
        }

        return sum;
    }

    /// <summary>Probability contained in rows [yStart, yEnd).</summary>
    public double NormInRows(int yStart, int yEnd)
    {
        double sum = 0.0;
        var data = Data;
        var start = 2 * yStart * Width;
        var end = 2 * yEnd * Width;
        for (var i = start; i < end; i += 2)
        {
            sum += (double)data[i] * data[i] + (double)data[i + 1] * data[i + 1];
        }

        return sum;
    }

    /// <summary>Scales ψ so that the total probability is 1.</summary>
    public void Normalise()
    {
        var norm = Norm();
        if (norm <= 0.0)
        {
            return;
        }

        var scale = (float)(1.0 / Math.Sqrt(norm));
        var data = Data;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] *= scale;
        }
    }

    /// <summary>Writes |ψ|² for each site of row y into the target span.</summary>
    public void ProbabilityOfRow(int y, Span<float> target)
    {
        var data = Data;
        var offset = 2 * y * Width;
        for (var x = 0; x < Width; x++)
        {
            var re = data[offset + 2 * x];
            var im = data[offset + 2 * x + 1];
            target[x] = re * re + im * im;
        }
    }

    public void Clear() => Array.Clear(Data);
}
