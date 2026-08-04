using NAudio.Wave;

namespace Mind.Hearing;

/// <summary>An input device the system reports: its index and product name.</summary>
/// <remarks>
/// The name comes from the legacy WaveIn (MME) API, which caps product names at 31 characters — so a
/// long name may arrive truncated. Substring matching still works against the truncated form.
/// </remarks>
public readonly record struct MicDeviceInfo(int Index, string Name);

/// <summary>
/// A live microphone as an <see cref="IAudioSource"/> — the same seam a file uses, so everything
/// downstream (cochlea, ear, place-baseline) is identical whether the Mind hears a recording or the
/// room around it. NAudio captures on its own thread and pushes buffers; <see cref="Read"/> pulls
/// from a small queue, blocking until samples arrive (a live source blocks, by contract). It drops
/// the oldest samples if the reader ever falls behind, so latency stays bounded. Windows only.
/// </summary>
public sealed class MicAudioSource : IAudioSource, IDisposable
{
    private readonly WaveInEvent _waveIn;
    private readonly Queue<float> _buffer = new();
    private readonly object _gate = new();
    private readonly int _maxBuffered;
    private bool _stopped;

    public int SampleRate { get; }

    /// <summary>The input devices the system reports, by index and product name — so a config can name one.</summary>
    public static IReadOnlyList<MicDeviceInfo> ListDevices()
    {
        var devices = new List<MicDeviceInfo>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            devices.Add(new MicDeviceInfo(i, WaveInEvent.GetCapabilities(i).ProductName));
        }
        return devices;
    }

    /// <summary>
    /// Pick which device to open. A non-empty <paramref name="name"/> wins — the first device whose
    /// product name contains it (case-insensitive) — otherwise the explicit <paramref name="index"/>.
    /// Returns <c>null</c> when a name was asked for but nothing matched, or the index is out of range,
    /// so the caller can fall back and report it rather than silently opening the wrong microphone.
    /// </summary>
    public static MicDeviceInfo? Resolve(IReadOnlyList<MicDeviceInfo> devices, string? name, int index)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var device in devices)
            {
                if (device.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            return null; // asked by name, none matched
        }

        foreach (var device in devices)
        {
            if (device.Index == index)
            {
                return device;
            }
        }

        return null; // index out of range
    }

    public MicAudioSource(int sampleRate = 16_000, int deviceNumber = 0)
    {
        SampleRate = sampleRate;
        _maxBuffered = sampleRate * 2; // ~2s cap so a stalled reader can't grow this without bound

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(sampleRate, 16, 1), // mono 16-bit at our rate
            BufferMilliseconds = 20,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += (_, _) =>
        {
            lock (_gate)
            {
                _stopped = true;
                Monitor.PulseAll(_gate);
            }
        };
        _waveIn.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                _buffer.Enqueue(BitConverter.ToInt16(e.Buffer, i) / 32768f);
            }

            while (_buffer.Count > _maxBuffered)
            {
                _buffer.Dequeue();
            }

            Monitor.PulseAll(_gate);
        }
    }

    public int Read(Span<float> buffer)
    {
        lock (_gate)
        {
            while (_buffer.Count == 0 && !_stopped)
            {
                Monitor.Wait(_gate);
            }

            var count = 0;
            while (count < buffer.Length && _buffer.Count > 0)
            {
                buffer[count++] = _buffer.Dequeue();
            }
            return count; // 0 only once stopped and drained
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            Monitor.PulseAll(_gate);
        }

        try
        {
            _waveIn.StopRecording();
        }
        catch
        {
            // best-effort stop on shutdown
        }
        _waveIn.Dispose();
    }
}
