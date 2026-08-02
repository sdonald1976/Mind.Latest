using NAudio.Wave;

namespace Mind.Hearing;

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
