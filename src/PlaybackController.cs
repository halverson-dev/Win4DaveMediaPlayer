using System.Globalization;

namespace Win4DaveMediaPlayer;

public sealed class PlaybackController : IDisposable
{
    private readonly MpvEngine engine;
    private double? pendingStepBoundary;

    public event Action? Loaded;
    public event Action<string>? Error;
    public bool IsLoaded { get; private set; }
    public string? FilePath { get; private set; }
    public double Duration { get; private set; }
    public double Position => engine.Number("time-pos") ?? 0;
    public bool Paused => engine.Get("pause") != "no";
    public double Speed { get; private set; } = 1;
    public PlaybackInterval? Interval { get; private set; }
    public bool Repeat { get; private set; }

    public PlaybackController(MpvEngine engine)
    {
        this.engine = engine;
        engine.FileLoaded += OnFileLoaded;
        engine.PlaybackError += OnPlaybackError;
    }

    public void Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only MP4 video files are supported.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The MP4 file was not found.", fullPath);
        IsLoaded = false;
        Interval = null;
        Repeat = false;
        pendingStepBoundary = null;
        Duration = 0;
        FilePath = fullPath;
        engine.Set("pause", "yes");
        engine.Set("end", "none");
        engine.Set("ab-loop-a", "no");
        engine.Set("ab-loop-b", "no");
        engine.Command("loadfile", fullPath, "replace");
    }

    private void OnFileLoaded()
    {
        var format = engine.Get("file-format") ?? "";
        Duration = engine.Number("duration") ?? 0;
        if (!format.Split(',').Contains("mp4", StringComparer.OrdinalIgnoreCase)
            && !format.StartsWith("mov", StringComparison.OrdinalIgnoreCase))
        {
            engine.Command("stop");
            OnPlaybackError("The file is not an MP4 container.");
            return;
        }
        if (Duration <= 0 || engine.Number("video-params/w") is not > 0)
        {
            engine.Command("stop");
            OnPlaybackError("This MP4 does not contain a playable, finite video.");
            return;
        }
        IsLoaded = true;
        engine.Set("speed", Speed);
        Loaded?.Invoke();
    }

    private void OnPlaybackError(string message)
    {
        IsLoaded = false;
        Duration = 0;
        Error?.Invoke(message);
    }

    public void Tick()
    {
        engine.PollEvents();
        if (!IsLoaded)
            return;
        // Frame steps complete asynchronously. Correct a step crossing a selected boundary.
        if (pendingStepBoundary is { } boundary && engine.Get("seeking") != "yes" && Paused)
        {
            if (Interval is { } interval && (Position < interval.Start || Position >= interval.End))
                Seek(boundary);
        }
    }

    public void TogglePlayback()
    {
        RequireLoaded();
        if (!Paused)
        {
            Pause();
            return;
        }
        var start = Interval?.Start ?? 0;
        var end = Interval?.End ?? Duration;
        if (Position < start || Position >= end - 0.001 || engine.Get("eof-reached") == "yes")
            Seek(start);
        pendingStepBoundary = null;
        engine.Set("pause", "no");
    }

    public void Pause() => engine.Set("pause", "yes");

    public void Seek(double seconds)
    {
        RequireLoaded();
        if (!double.IsFinite(seconds))
            throw new ArgumentException("The seek time must be finite.");
        var target = Math.Clamp(seconds, Interval?.Start ?? 0, Interval?.End ?? Duration);
        pendingStepBoundary = null;
        engine.Command("seek", target.ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
    }

    public void Step(int direction)
    {
        RequireLoaded();
        if (direction is not (1 or -1))
            throw new ArgumentException("Frame direction must be +1 or -1.");
        Pause();
        if (Interval is { } interval)
        {
            if (direction < 0 && Position <= interval.Start + 0.001)
            {
                Seek(interval.Start);
                return;
            }
            if (direction > 0 && engine.Get("eof-reached") == "yes")
                return;
            pendingStepBoundary = direction < 0 ? interval.Start : interval.End;
        }
        engine.Command(direction > 0 ? "frame-step" : "frame-back-step");
    }

    public void SetSpeed(double speed)
    {
        if (speed is not (1 or 0.1))
            throw new ArgumentException("Choose normal speed or 10% speed.");
        engine.Set("speed", speed);
        Speed = speed;
    }

    public void SetInterval(double start, double end, bool repeat)
    {
        RequireLoaded();
        var interval = PlaybackInterval.Create(start, end, Duration);
        Pause();
        pendingStepBoundary = null;
        engine.Set("ab-loop-a", "no");
        engine.Set("ab-loop-b", "no");
        engine.Set("end", end);
        if (repeat)
        {
            engine.Set("ab-loop-count", "inf");
            engine.Set("ab-loop-a", start);
            engine.Set("ab-loop-b", end);
        }
        Interval = interval;
        Repeat = repeat;
        Seek(start);
    }

    public void ClearInterval()
    {
        RequireLoaded();
        Pause();
        engine.Set("ab-loop-a", "no");
        engine.Set("ab-loop-b", "no");
        engine.Set("end", "none");
        Interval = null;
        Repeat = false;
        pendingStepBoundary = null;
        Seek(Position);
    }

    private void RequireLoaded()
    {
        if (!IsLoaded)
            throw new InvalidOperationException("Open an MP4 video first.");
    }

    public void Dispose()
    {
        engine.FileLoaded -= OnFileLoaded;
        engine.PlaybackError -= OnPlaybackError;
        engine.Dispose();
    }
}

public sealed record PlaybackInterval(double Start, double End)
{
    public static PlaybackInterval Create(double start, double end, double duration)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(duration)
            || start < 0 || start >= end || end > duration)
            throw new ArgumentException("Use an interval with 0 <= start < end <= video duration.");
        return new PlaybackInterval(start, end);
    }
}
