namespace DoubleSlitPhysics.Helpers;

/// <summary>
///     In-place iterative radix-2 complex FFT on interleaved single-precision
///     re/im data. One instance per transform size; bit-reversal indices and
///     twiddle factors are precomputed (twiddles in double, stored as float).
/// </summary>
public sealed class Fft
{
    private readonly int _size;
    private readonly int[] _bitReversal;
    private readonly float[] _twiddleCos;
    private readonly float[] _twiddleSin;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two ≥ 2.", nameof(size));
        }

        _size = size;
        _bitReversal = BuildBitReversal(size);

        // Twiddles for the largest stage cover all smaller stages: index k
        // holds exp(-2πi k / size) for k < size/2.
        _twiddleCos = new float[size / 2];
        _twiddleSin = new float[size / 2];
        for (var k = 0; k < size / 2; k++)
        {
            var angle = -2.0 * Math.PI * k / size;
            _twiddleCos[k] = (float)Math.Cos(angle);
            _twiddleSin[k] = (float)Math.Sin(angle);
        }
    }

    public int Size => _size;

    /// <summary>Forward transform, sign convention exp(-2πi kn/N), no scaling.</summary>
    public void Forward(Span<float> interleaved) => Transform(interleaved, inverse: false);

    /// <summary>Inverse transform including the 1/N scaling.</summary>
    public void Inverse(Span<float> interleaved)
    {
        Transform(interleaved, inverse: true);
        var scale = 1f / _size;
        for (var i = 0; i < 2 * _size; i++)
        {
            interleaved[i] *= scale;
        }
    }

    private unsafe void Transform(Span<float> data, bool inverse)
    {
        if (data.Length < 2 * _size)
        {
            throw new ArgumentException("Buffer too small for FFT size.", nameof(data));
        }

        // Pointer arithmetic keeps bounds checks out of the butterfly loops,
        // the hottest code in the whole simulation.
        fixed (float* p = data)
        fixed (float* twiddleCos = _twiddleCos)
        fixed (float* twiddleSin = _twiddleSin)
        fixed (int* bitReversal = _bitReversal)
        {
            // Bit-reversal permutation.
            for (var i = 0; i < _size; i++)
            {
                var j = bitReversal[i];
                if (j > i)
                {
                    var a = p + 2 * i;
                    var b = p + 2 * j;
                    (a[0], b[0]) = (b[0], a[0]);
                    (a[1], b[1]) = (b[1], a[1]);
                }
            }

            var sign = inverse ? -1f : 1f;

            // Butterflies. Stage with half-size m uses twiddle stride size/(2m).
            for (var m = 1; m < _size; m <<= 1)
            {
                var stride = _size / (2 * m);
                for (var blockStart = 0; blockStart < _size; blockStart += 2 * m)
                {
                    var top = p + 2 * blockStart;
                    var bottom = top + 2 * m;
                    for (var k = 0; k < m; k++)
                    {
                        var wRe = twiddleCos[k * stride];
                        var wIm = sign * twiddleSin[k * stride];

                        var bRe = bottom[0];
                        var bIm = bottom[1];
                        var tRe = wRe * bRe - wIm * bIm;
                        var tIm = wRe * bIm + wIm * bRe;

                        var aRe = top[0];
                        var aIm = top[1];
                        bottom[0] = aRe - tRe;
                        bottom[1] = aIm - tIm;
                        top[0] = aRe + tRe;
                        top[1] = aIm + tIm;

                        top += 2;
                        bottom += 2;
                    }
                }
            }
        }
    }

    private static int[] BuildBitReversal(int size)
    {
        var bits = int.TrailingZeroCount(size);
        var table = new int[size];
        for (var i = 0; i < size; i++)
        {
            var reversed = 0;
            for (var b = 0; b < bits; b++)
            {
                reversed |= ((i >> b) & 1) << (bits - 1 - b);
            }

            table[i] = reversed;
        }

        return table;
    }
}
