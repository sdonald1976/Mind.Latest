using System.Text;

namespace Mind.Hearing;

/// <summary>
/// Writes mono float samples to a 16-bit PCM WAV — the counterpart to <see cref="WavReader"/>, used
/// to dump short listenable clips (e.g. a sound-unit's members) so a human can judge by ear whether
/// they really are the same sound.
/// </summary>
public static class WavWriter
{
    public static void WriteMono(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dataBytes = samples.Length * 2;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream); // BinaryWriter is little-endian, as WAV wants

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);            // PCM fmt chunk size
        writer.Write((short)1);      // PCM
        writer.Write((short)1);      // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // byte rate (1 channel * 2 bytes)
        writer.Write((short)2);      // block align
        writer.Write((short)16);     // bits per sample

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            var value = (int)Math.Round(Math.Clamp(sample, -1f, 1f) * 32767f);
            writer.Write((short)value);
        }
    }
}
