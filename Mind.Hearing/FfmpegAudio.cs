using System.Diagnostics;
using System.Globalization;

namespace Mind.Hearing;

/// <summary>
/// Pulls the audio track out of a media file (an MP4, say) and leaves it as a mono
/// WAV at a chosen sample rate, using ffmpeg. The picture is discarded — for this
/// piece the Mind only hears. ffmpeg must be on PATH.
/// </summary>
/// <remarks>
/// This is a fixed, dumb front-of-the-front-end: it does no analysis, it just gets
/// sound off disk in a shape the cochlea can read. We shell out to ffmpeg rather
/// than decode MP4/AAC ourselves — that is a whole codec we have no reason to own.
/// </remarks>
public static class FfmpegAudio
{
    /// <summary>
    /// Extract <paramref name="mediaPath"/>'s audio to a mono WAV at <paramref name="sampleRate"/> Hz,
    /// writing <paramref name="wavPath"/>. If <paramref name="seconds"/> is given, only that many
    /// seconds from the start are taken (handy for tuning on a slice). Throws if ffmpeg is
    /// missing or fails — for a tuning tool we want a loud failure, not a silent skip.
    /// </summary>
    public static void ExtractToWav(string mediaPath, string wavPath, int sampleRate, double? seconds = null)
    {
        // -vn drops the video; -ac 1 mono; -ar resamples. -t (before -i) limits the take.
        var limit = seconds is > 0
            ? $"-t {seconds.Value.ToString(CultureInfo.InvariantCulture)} "
            : "";
        var args = $"-y -v error {limit}-i \"{mediaPath}\" -vn -ac 1 -ar {sampleRate} \"{wavPath}\"";

        var psi = new ProcessStartInfo("ffmpeg", args)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not start ffmpeg. Is it installed and on PATH? (winget install ffmpeg)", ex);
        }

        if (process is null)
        {
            throw new InvalidOperationException("ffmpeg did not start.");
        }

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || !File.Exists(wavPath))
        {
            throw new InvalidOperationException(
                $"ffmpeg failed to extract audio from '{mediaPath}' (exit {process.ExitCode}). {stderr}".Trim());
        }
    }
}
