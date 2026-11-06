using I76.Formats.Audio;
using Xunit;

namespace I76.Formats.Tests;

public class MovieClipTests
{
    [Fact]
    public void Window_cuts_converts_8bit_to_16bit_and_fades()
    {
        var data = Enumerable.Repeat((byte)228, 1000).ToArray();   // +100 in unsigned 8-bit
        var w = MovieClip.Window(new Pcm(1, 1000, 8, data), 0.1, 0.6, 0.1, 0.1);
        Assert.Equal(16, w.Bits);
        Assert.Equal(500 * 2, w.Data.Length);
        short S(int i) => BitConverter.ToInt16(w.Data, i * 2);
        Assert.Equal(0, S(0));                       // faded in from silence
        Assert.Equal(100 * 256, S(250));             // full level in the middle
        Assert.Equal(0, S(499));                     // faded out to silence
        Assert.True(S(50) > 0 && S(50) < S(250));
    }

    [Fact]
    public void Window_clips_to_the_data()
    {
        var w = MovieClip.Window(new Pcm(2, 100, 16, new byte[100 * 4]), 0.5, 5, 0, 0);
        Assert.Equal(50 * 4, w.Data.Length);
    }

    [InstallFact]
    public void StartUp_is_the_INT03_engine_catching()
    {
        var p = MovieClip.StartUp(TestInstall.Install);
        Assert.NotNull(p);
        Assert.Equal(22050, p!.Rate);
        Assert.InRange(p.Seconds, 3.3, 3.4);
        double Rms(double a, double b)
        {
            int i0 = (int)(a * p.Rate), i1 = (int)(b * p.Rate); double s = 0;
            for (int i = i0; i < i1; i++) { double v = BitConverter.ToInt16(p.Data, i * 2); s += v * v; }
            return Math.Sqrt(s / (i1 - i0));
        }
        // quiet before the catch, loud after it
        Assert.True(Rms(0.4, 1.0) > 8 * Rms(0.02, 0.2), $"{Rms(0.02, 0.2)} vs {Rms(0.4, 1.0)}");
    }
}
