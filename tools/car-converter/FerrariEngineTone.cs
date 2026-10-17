using System.Numerics;

namespace DiRT2VR.CustomCars;

// Preserve a recording's engine-order spectrum, without replaying its changes
// in revs, beats or recording envelope. The native engine controller supplies
// RPM and load; all bands use the same continuous engine-cycle phase.
internal static class FerrariEngineTone
{
    internal const int CycleSamples = 2048;
    const int Window = 16384, Harmonics = 64;

    internal static double[] FromRecording(double[] pcm, int rate, double cycleHz, CancellationToken cancel)
    {
        if (pcm.Length < Window || !double.IsFinite(cycleHz) || cycleHz <= 0)
            throw new InvalidDataException("Unsupported engine tone recording.");
        var power = new double[Harmonics + 1];
        double energy = 0;
        const int windows = 8;
        for (int w = 0; w < windows; w++)
        {
            cancel.ThrowIfCancellationRequested();
            int start = (pcm.Length - Window) * w / (windows - 1);
            var bins = new Complex[Window];
            double mean = 0;
            for (int i = 0; i < Window; i++) mean += pcm[start + i] / Window;
            for (int i = 0; i < Window; i++)
            {
                double value = pcm[start + i] - mean;
                energy += value * value / (Window * windows);
                bins[i] = value * (.5 - .5 * Math.Cos(2 * Math.PI * i / (Window - 1)));
            }
            Fft(bins);
            for (int h = 1; h <= Harmonics; h++)
            {
                double centre = h * cycleHz * Window / rate;
                // Include source drift around each order, without allowing
                // adjacent orders to claim the same spectral energy.
                double radius = Math.Min(cycleHz * Window / rate * .4, Math.Max(2, centre * .025));
                int low = Math.Max(1, (int)Math.Ceiling(centre - radius));
                int high = Math.Min(Window / 2 - 1, (int)Math.Floor(centre + radius));
                for (int i = low; i <= high; i++) power[h] += bins[i].Magnitude * bins[i].Magnitude;
            }
        }
        double total = power.Sum();
        if (!double.IsFinite(total) || total <= 0 || energy <= 0)
            throw new InvalidDataException("Engine recording contains no usable tone.");
        var cycle = new double[CycleSamples];
        for (int h = 1; h <= Harmonics; h++)
        {
            double amplitude = Math.Sqrt(2 * energy * power[h] / total);
            // Fixed phases avoid coherent impulse peaks and remain identical
            // across RPM bands, so blends cannot cancel the firing frequency.
            double phase = Math.PI * h * (h - 1) / Harmonics;
            for (int i = 0; i < cycle.Length; i++)
                cycle[i] += amplitude * Math.Cos(2 * Math.PI * h * i / cycle.Length + phase);
        }
        double peak = cycle.Max(v => Math.Abs(v));
        if (peak > 30000) for (int i = 0; i < cycle.Length; i++) cycle[i] *= 30000 / peak;
        return cycle;
    }

    static void Fft(Complex[] data)
    {
        for (int i = 1, j = 0; i < data.Length; i++)
        {
            int bit = data.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (int size = 2; size <= data.Length; size <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (int start = 0; start < data.Length; start += size)
            {
                Complex phase = Complex.One;
                for (int i = 0; i < size / 2; i++)
                {
                    var even = data[start + i]; var odd = data[start + i + size / 2] * phase;
                    data[start + i] = even + odd; data[start + i + size / 2] = even - odd;
                    phase *= step;
                }
            }
        }
    }
}
