using System.Buffers.Binary;
using System.Text;

namespace Mind.Hearing;

/// <summary>
/// Reads a WAV file into mono float samples in [-1, 1]. Deliberately small: it
/// understands PCM 16-bit and IEEE float 32-bit, mono or multi-channel (down-mixed
/// by averaging), and linearly resamples to a requested rate when they differ.
/// Good enough to feed the cochlea from a file; not a hi-fi decoder.
/// </summary>
public static class WavReader
{
    private const int FormatPcm = 1;
    private const int FormatFloat = 3;

    /// <summary>Read <paramref name="path"/> as mono samples at <paramref name="targetRate"/> Hz.</summary>
    public static float[] ReadMono(string path, int targetRate)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 12 ||
            bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
            bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
        {
            throw new InvalidDataException($"'{path}' is not a RIFF/WAVE file.");
        }

        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        var dataOffset = -1;
        var dataLength = 0;

        // Walk the chunks: each is a 4-byte id, a 4-byte little-endian size, then a
        // body padded to an even length.
        var p = 12;
        while (p + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, p, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 4, 4));
            var body = p + 8;

            if (id == "fmt " && body + 16 <= bytes.Length)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14, 2));
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = Math.Min(size, bytes.Length - body);
            }

            p = body + size + (size & 1);
        }

        if (dataOffset < 0 || channels == 0 || sampleRate == 0)
        {
            throw new InvalidDataException($"'{path}' is missing a fmt or data chunk.");
        }

        var mono = Decode(bytes.AsSpan(dataOffset, dataLength), format, channels, bits, path);
        return sampleRate == targetRate ? mono : Resample(mono, sampleRate, targetRate);
    }

    private static float[] Decode(ReadOnlySpan<byte> data, int format, int channels, int bits, string path)
    {
        if (format == FormatPcm && bits == 16)
        {
            var frames = data.Length / (2 * channels);
            var mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    var sample = BinaryPrimitives.ReadInt16LittleEndian(data.Slice((i * channels + c) * 2, 2));
                    sum += sample / 32768f;
                }
                mono[i] = sum / channels;
            }
            return mono;
        }

        if (format == FormatFloat && bits == 32)
        {
            var frames = data.Length / (4 * channels);
            var mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    sum += BinaryPrimitives.ReadSingleLittleEndian(data.Slice((i * channels + c) * 4, 4));
                }
                mono[i] = sum / channels;
            }
            return mono;
        }

        throw new NotSupportedException(
            $"'{path}': WAV format {format} at {bits}-bit is not supported (PCM 16-bit or float 32-bit only).");
    }

    /// <summary>Linear resample. Crude but faithful enough to audition a file.</summary>
    private static float[] Resample(float[] input, int from, int to)
    {
        if (input.Length == 0 || from == to)
        {
            return input;
        }

        var ratio = (double)to / from;
        var outLength = (int)(input.Length * ratio);
        var output = new float[outLength];
        for (var i = 0; i < outLength; i++)
        {
            var source = i / ratio;
            var index = (int)source;
            var frac = (float)(source - index);
            var a = input[index];
            var b = index + 1 < input.Length ? input[index + 1] : a;
            output[i] = a + (b - a) * frac;
        }
        return output;
    }
}
