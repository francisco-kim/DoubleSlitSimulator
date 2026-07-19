using DoubleSlitPhysics.Helpers;

namespace DoubleSlitPhysics.Tests;

public class FftTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(256)]
    [InlineData(512)]
    public void ForwardThenInverseIsIdentity(int size)
    {
        var rng = new Random(42);
        var data = new float[2 * size];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        var original = (float[])data.Clone();
        var fft = new Fft(size);
        fft.Forward(data);
        fft.Inverse(data);

        for (var i = 0; i < data.Length; i++)
        {
            Assert.Equal(original[i], data[i], 1e-4);
        }
    }

    [Fact]
    public void PureSinusoidTransformsToSingleSpike()
    {
        const int size = 256;
        const int frequency = 17;
        var data = new float[2 * size];
        for (var n = 0; n < size; n++)
        {
            var angle = 2.0 * Math.PI * frequency * n / size;
            data[2 * n] = (float)Math.Cos(angle);
            data[2 * n + 1] = (float)Math.Sin(angle);
        }

        new Fft(size).Forward(data);

        for (var k = 0; k < size; k++)
        {
            var magnitude = Math.Sqrt(
                (double)data[2 * k] * data[2 * k] + (double)data[2 * k + 1] * data[2 * k + 1]);
            var expected = k == frequency ? size : 0.0;
            Assert.Equal(expected, magnitude, 1e-2);
        }
    }

    [Fact]
    public void ParsevalHolds()
    {
        const int size = 512;
        var rng = new Random(7);
        var data = new float[2 * size];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        var timeEnergy = SumOfSquares(data);
        new Fft(size).Forward(data);
        var frequencyEnergy = SumOfSquares(data) / size;

        Assert.Equal(timeEnergy, frequencyEnergy, timeEnergy * 1e-5);
    }

    [Fact]
    public void NonPowerOfTwoSizeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Fft(96));
    }

    private static double SumOfSquares(float[] data)
    {
        double sum = 0.0;
        foreach (var value in data)
        {
            sum += (double)value * value;
        }

        return sum;
    }
}
