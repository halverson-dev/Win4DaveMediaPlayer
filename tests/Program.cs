using System.Diagnostics;
using Win4DaveMediaPlayer;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Check(Timecode.TryParse("01:02:03.125", out var parsed) && parsed == 3723.125, "Parse hours");
            Check(Timecode.TryParse("90.125", out parsed) && parsed == 90.125, "Parse seconds");
            Check(Timecode.TryParse("02:03.125", out parsed) && parsed == 123.125, "Parse minutes");
            foreach (var invalid in new[] { "", "-1", "NaN", "1:60", "1.5:20", "0:0:60", "1e2", "1:2:3:4" })
                Check(!Timecode.TryParse(invalid, out _), $"Reject time '{invalid}'");
            Check(Timecode.Format(59.9996) == "00:01:00.000", "Time formatting carries milliseconds");
            Throws(() => PlaybackInterval.Create(2, 1, 3), "Reject inverted interval");
            Throws(() => PlaybackInterval.Create(0, 4, 3), "Reject interval beyond video");
            Throws(() => PlaybackInterval.Create(double.NaN, 2, 3), "Reject non-finite interval");
            Throws(() => PlaybackInterval.Create(1, 1, 3), "Reject empty interval");
            if (args.Length == 0)
                throw new ArgumentException("Provide a 4+ second, 25fps MP4 fixture to run playback tests.");

            using var engine = new MpvEngine(headless: true);
            using var player = new PlaybackController(engine);
            player.Error += message => throw new InvalidOperationException(message);
            Throws(() => player.Open(Path.ChangeExtension(args[0], ".avi")), "Reject non-MP4");
            player.Open(args[0]);
            Wait(player, () => player.IsLoaded, "Load real MP4");
            Check(player.Duration >= 4, "Read video duration");
            Check(player.Paused, "Open paused");
            Seek(player, engine, 1);
            var original = player.Position;
            player.Step(1);
            Wait(player, () => player.Paused && engine.Get("seeking") != "yes"
                && player.Position > original + 0.02, "Forward frame");
            Check(Math.Abs(player.Position - original - 0.04) < 0.005, "Forward exactly one 25fps frame");
            var forward = player.Position;
            player.Step(-1);
            Wait(player, () => engine.Get("seeking") != "yes" && player.Position < forward - 0.02, "Backward frame");
            Check(Math.Abs(player.Position - original) < 0.005, "Backward returns to original frame");

            player.SetSpeed(0.1);
            player.TogglePlayback();
            Pump(player, 600);
            var slowStart = player.Position;
            Pump(player, 1200);
            player.Pause();
            var slowDelta = player.Position - slowStart;
            Check(slowDelta is > 0.05 and < 0.25,
                $"10% playback advances about 0.12 seconds in 1.2s (actual {slowDelta:F3}, speed {engine.Get("speed")})");
            player.SetSpeed(1);
            player.SetInterval(1, 1.8, false);
            Wait(player, () => engine.Get("seeking") != "yes" && Math.Abs(player.Position - 1) < 0.05, "Apply interval");
            player.TogglePlayback();
            Wait(player, () => player.Paused && engine.Get("eof-reached") == "yes", "Native interval stops playback");
            Check(player.Position is >= 1.7 and <= 1.801, "No playback beyond interval end");
            player.TogglePlayback();
            Wait(player, () => !player.Paused && player.Position < 1.4, "Replay interval from start");
            player.Pause();
            Seek(player, engine, -10);
            Check(player.Position >= 0.999, "Seek clamped to interval start");
            player.Step(-1);
            Pump(player, 200);
            Check(player.Position >= 0.999, "Backward step clamped to interval start");
            Seek(player, engine, 100);
            Check(player.Position <= 1.801, "Seek clamped to interval end");

            player.SetInterval(1, 1.8, true);
            Wait(player, () => engine.Get("seeking") != "yes" && player.Position < 1.1, "Prepare repeat interval");
            player.TogglePlayback();
            var sawEnd = false;
            var sawLoop = false;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 2000)
            {
                player.Tick();
                var time = player.Position;
                if (time > 1.65)
                    sawEnd = true;
                if (sawEnd && time < 1.25)
                    sawLoop = true;
                CheckSilent(time >= 0.99 && time <= 1.81, $"Loop escaped interval: {time}");
                Thread.Sleep(10);
            }
            Check(sawLoop && !player.Paused, "Interval repeats without leaving bounds");
            player.ClearInterval();
            Seek(player, engine, 2.5);
            Check(player.Position > 2.4, "Clear interval restores full-video seek");

            player.SetInterval(1.01, 1.79, false);
            Pump(player, 150);
            player.Step(-1);
            Pump(player, 300);
            Check(player.Position >= 1.009, $"Non-frame-aligned start bounds backward step ({player.Position})");
            Seek(player, engine, 1.75);
            player.Step(1);
            Pump(player, 300);
            Check(player.Position <= 1.791 && player.Paused, "Frame step respects non-frame-aligned end");
            player.SetInterval(1.01, 1.79, true);
            Seek(player, engine, 100);
            Check(player.Position <= 1.791, "Repeat mode also bounds non-aligned end seeks");
            player.Step(1);
            Pump(player, 300);
            Check(player.Position <= 1.791 && player.Position >= 1.009 && player.Paused,
                "Repeat mode bounds frame stepping");
            player.SetSpeed(0.1);
            player.SetInterval(1, 1.2, false);
            Pump(player, 150);
            player.TogglePlayback();
            Wait(player, () => player.Paused && engine.Get("eof-reached") == "yes", "Slow-motion interval stops at native end");
            Check(player.Position <= 1.201, "Slow-motion remains inside interval");
            player.ClearInterval();
            player.SetSpeed(1);

            player.Open(args[0]);
            Wait(player, () => player.IsLoaded, "Reopen resets interval");
            Check(player.Interval is null && !player.Repeat && player.Paused, "New file resets bounds and pause");
            if (args.Length > 1)
            {
                var rejected = false;
                using var badEngine = new MpvEngine(headless: true);
                using var badPlayer = new PlaybackController(badEngine);
                badPlayer.Error += _ => rejected = true;
                badPlayer.Open(args[1]);
                Wait(badPlayer, () => rejected, "Reject non-MP4 container renamed .mp4");
                Check(!badPlayer.IsLoaded, "Invalid container stays unloaded");
            }
            if (args.Length > 2)
            {
                player.Open(args[2]);
                Wait(player, () => player.IsLoaded, "Load variable-frame-rate MP4");
                Seek(player, engine, 1.2);
                var first = player.Position;
                player.Step(1);
                Wait(player, () => player.Paused && player.Position > first + 0.02, "VFR forward frame");
                var second = player.Position;
                player.Step(1);
                Wait(player, () => player.Paused && player.Position > second + 0.02, "VFR second frame");
                var third = player.Position;
                Check(Math.Abs((second - first) - (third - second)) > 0.02, "VFR steps follow actual unequal frame timestamps");
                player.Step(-1);
                Wait(player, () => engine.Get("seeking") != "yes" && player.Position < third - 0.02, "VFR backward frame");
                Check(Math.Abs(player.Position - second) < 0.005, "VFR backward returns to preceding displayed frame");
            }
            if (args.Contains("--ui"))
                TestWindow(args[0]);
            Console.WriteLine($"PASS: {passed} checks, including real mpv decoding and playback.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void TestWindow(string fixture)
    {
        using var form = new MainForm(fixture);
        form.Show();
        var controls = Descendants(form).ToArray();
        var next = controls.OfType<Button>().Single(button => button.Text == "Next frame");
        var previous = controls.OfType<Button>().Single(button => button.Text == "Previous frame");
        var timestamp = controls.OfType<Label>().Single(label => label.Text.Contains(" / "));
        WaitUi(() => next.Enabled, "Window loads MP4 with embedded renderer");
        var initial = timestamp.Text;
        next.PerformClick();
        WaitUi(() => timestamp.Text != initial, "Next-frame button advances visible timestamp");
        previous.PerformClick();
        WaitUi(() => timestamp.Text == initial, "Previous-frame button returns visible timestamp");
        var boxes = controls.OfType<TextBox>().ToArray();
        boxes[0].Text = "1";
        boxes[1].Text = "1.8";
        controls.OfType<Button>().Single(button => button.Text == "Apply interval").PerformClick();
        WaitUi(() => timestamp.Text.StartsWith("00:00:01."), "Interval inputs seek the visible player");

        using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using (var graphics = Graphics.FromImage(image))
            graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, image.Size);
        var colors = 0;
        for (var y = 80; y < image.Height / 2; y += 10)
            for (var x = 30; x < image.Width - 30; x += 10)
            {
                var pixel = image.GetPixel(x, y);
                if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) -
                    Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) > 60)
                    colors++;
            }
        Check(colors > 30, $"Embedded video actually renders colored MP4 pixels ({colors} samples)");
        var screenshot = Path.Combine(Path.GetDirectoryName(fixture)!, "..", "ui.png");
        image.Save(screenshot);
        form.Size = form.MinimumSize;
        Application.DoEvents();
        var clear = controls.OfType<Button>().Single(button => button.Text == "Clear interval");
        Check(clear.Parent!.ClientRectangle.Contains(clear.Bounds), "Interval controls remain visible at minimum window size");
        Check(controls.Where(control => control.Parent is FlowLayoutPanel)
            .All(control => control.Parent!.ClientRectangle.Contains(control.Bounds)),
            "All toolbar, transport, and interval content fits the minimum window");
        form.Close();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void WaitUi(Func<bool> condition, string name)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            Application.DoEvents();
            if (condition())
            {
                Check(true, name);
                return;
            }
            Thread.Sleep(10);
        }
        throw new Exception($"Timed out: {name}");
    }

    private static void Seek(PlaybackController player, MpvEngine engine, double target)
    {
        player.Seek(target);
        Pump(player, 100);
        Wait(player, () => engine.Get("seeking") != "yes", "Seek completes");
    }

    private static void Wait(PlaybackController player, Func<bool> condition, string name)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            player.Tick();
            if (condition())
            {
                Check(true, name);
                return;
            }
            Thread.Sleep(10);
        }
        throw new Exception($"Timed out: {name} (position {player.Position}, paused {player.Paused})");
    }

    private static void Pump(PlaybackController player, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            player.Tick();
            Thread.Sleep(10);
        }
    }

    private static void Check(bool condition, string name)
    {
        CheckSilent(condition, name);
        passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void CheckSilent(bool condition, string name)
    {
        if (!condition)
            throw new Exception($"FAIL: {name}");
    }

    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, name); return; }
        throw new Exception($"FAIL: {name}");
    }
}
