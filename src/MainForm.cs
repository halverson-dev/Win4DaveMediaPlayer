namespace Win4DaveMediaPlayer;

public sealed class MainForm : Form
{
    private readonly Panel video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly Label empty = new()
    {
        Text = "Open or drop an MP4 video",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.Silver,
        BackColor = Color.Black
    };
    private readonly Button play = new() { Text = "Play", AutoSize = true };
    private readonly TrackBar timeline = new()
    {
        Dock = DockStyle.Fill, Minimum = 0, Maximum = 10000, TickStyle = TickStyle.None
    };
    private readonly Label position = new() { AutoSize = true, Text = "00:00:00.000 / 00:00:00.000" };
    private readonly TextBox start = new() { Width = 115, Text = "00:00:00.000" };
    private readonly TextBox end = new() { Width = 115, Text = "00:00:00.000" };
    private readonly CheckBox repeat = new() { Text = "Repeat interval", AutoSize = true };
    private readonly CheckBox slow = new() { Text = "10% speed (0.1x)", AutoSize = true };
    private readonly CheckBox mute = new() { Text = "Mute", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "MP4 only. Ready.", AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    private readonly List<Control> mediaControls = [];
    private readonly PlaybackController player;
    private readonly MpvEngine engine;
    private bool draggingTimeline;
    private bool updatingTimeline;

    public MainForm(string? initialPath = null)
    {
        Text = "Win4DaveMediaPlayer";
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        AllowDrop = true;
        KeyPreview = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(layout);

        var toolbar = Row();
        toolbar.Controls.Add(Button("Open MP4...", OpenDialog));
        toolbar.Controls.Add(new Label
        {
            AutoSize = true, Text = "Space: play/pause   Left/Right or ,/.: previous/next frame",
            Margin = new Padding(14, 9, 0, 0)
        });
        layout.Controls.Add(toolbar, 0, 0);
        video.Controls.Add(empty);
        layout.Controls.Add(video, 0, 1);
        layout.Controls.Add(timeline, 0, 2);
        engine = new MpvEngine(video.Handle);
        player = new PlaybackController(engine);
        engine.FileDropped += Open;

        var transport = Row();
        play.Click += (_, _) => Run(player.TogglePlayback);
        transport.Controls.Add(play);
        transport.Controls.Add(Button("Previous frame", () => player.Step(-1), true));
        transport.Controls.Add(Button("Next frame", () => player.Step(1), true));
        transport.Controls.Add(slow);
        transport.Controls.Add(mute);
        position.Margin = new Padding(14, 9, 0, 0);
        transport.Controls.Add(position);
        layout.Controls.Add(transport, 0, 3);
        mediaControls.AddRange([play, timeline, start, end, repeat]);

        var interval = Row();
        interval.Controls.Add(new Label { Text = "Start", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        interval.Controls.Add(start);
        interval.Controls.Add(Button("Set start here", () => start.Text = Timecode.Format(player.Position), true));
        interval.Controls.Add(new Label { Text = "End", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
        interval.Controls.Add(end);
        interval.Controls.Add(Button("Set end here", () => end.Text = Timecode.Format(player.Position), true));
        interval.Controls.Add(repeat);
        interval.Controls.Add(Button("Apply interval", ApplyInterval, true));
        interval.Controls.Add(Button("Clear interval", () =>
        {
            player.ClearInterval();
            repeat.Checked = false;
            status.Text = "Full video. Paused.";
        }, true));
        interval.Controls.Add(new Label
        {
            Text = "Times: seconds, mm:ss.mmm, or hh:mm:ss.mmm. Apply pauses at the start; Play begins the interval.",
            AutoSize = true
        });
        layout.Controls.Add(interval, 0, 4);
        layout.Controls.Add(status, 0, 5);
        SetMediaEnabled(false);

        player.Loaded += () =>
        {
            SetMediaEnabled(true);
            empty.Visible = false;
            Text = $"{Path.GetFileName(player.FilePath)} - Win4DaveMediaPlayer";
            start.Text = Timecode.Format(0);
            // Truncation avoids entering a rounded time beyond the exact duration.
            end.Text = Timecode.Format(Math.Floor(player.Duration * 1000) / 1000);
            status.Text = "Full video. Paused. Use Play or step through individual frames.";
        };
        player.Error += message =>
        {
            SetMediaEnabled(false);
            empty.Visible = true;
            status.Text = message;
            MessageBox.Show(this, message, "Unable to open video", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        slow.CheckedChanged += (_, _) => Run(() => player.SetSpeed(slow.Checked ? 0.1 : 1));
        mute.CheckedChanged += (_, _) => Run(() => engine.Set("mute", mute.Checked ? "yes" : "no"));
        repeat.CheckedChanged += (_, _) =>
        {
            if (player.Interval is { } selected && repeat.Checked != player.Repeat)
                Run(() => player.SetInterval(selected.Start, selected.End, repeat.Checked));
        };

        timeline.MouseDown += (_, _) => draggingTimeline = true;
        timeline.MouseUp += (_, _) =>
        {
            draggingTimeline = false;
            Run(() => player.Seek(timeline.Value / 10000.0 * player.Duration));
        };
        timeline.ValueChanged += (_, _) =>
        {
            if (!updatingTimeline && !draggingTimeline && player.IsLoaded)
                Run(() => player.Seek(timeline.Value / 10000.0 * player.Duration));
        };
        video.MouseDown += (_, _) => Focus();
        DragEnter += (_, args) =>
        {
            if (args.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
                && Path.GetExtension(files[0]).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                args.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, args) =>
        {
            if (args.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files)
                Open(files[0]);
        };
        timer.Tick += (_, _) => Run(UpdatePlayback);
        Shown += (_, _) =>
        {
            timer.Start();
            if (initialPath is not null)
                Open(initialPath);
        };
    }

    private static FlowLayoutPanel Row()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        row.SizeChanged += (_, _) =>
        {
            foreach (var label in row.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(1, row.ClientSize.Width - 6), 0);
        };
        return row;
    }

    private Button Button(string text, Action action, bool requiresVideo = false)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => Run(action);
        if (requiresVideo)
            mediaControls.Add(button);
        return button;
    }

    private void OpenDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open MP4 video", Filter = "MP4 video (*.mp4)|*.mp4", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Open(dialog.FileName);
    }

    private void Open(string path) => Run(() =>
    {
        player.Open(path);
        SetMediaEnabled(false);
        empty.Visible = true;
        repeat.Checked = false;
        status.Text = "Loading MP4...";
    });

    private void ApplyInterval()
    {
        if (!Timecode.TryParse(start.Text, out var startTime) || !Timecode.TryParse(end.Text, out var endTime))
            throw new ArgumentException("Enter valid times using seconds, mm:ss.mmm, or hh:mm:ss.mmm.");
        player.SetInterval(startTime, endTime, repeat.Checked);
        status.Text = $"Interval {Timecode.Format(startTime)} - {Timecode.Format(endTime)}" +
            (repeat.Checked ? " (repeating). Paused." : " (stops at end). Paused.");
    }

    private void UpdatePlayback()
    {
        player.Tick();
        if (!player.IsLoaded)
            return;
        play.Text = player.Paused ? "Play" : "Pause";
        position.Text = $"{Timecode.Format(player.Position)} / {Timecode.Format(player.Duration)}";
        if (!draggingTimeline)
        {
            updatingTimeline = true;
            timeline.Value = (int)Math.Clamp(player.Position / player.Duration * 10000, 0, 10000);
            updatingTimeline = false;
        }
    }

    private void SetMediaEnabled(bool enabled)
    {
        foreach (var control in mediaControls)
            control.Enabled = enabled;
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
            MessageBox.Show(this, exception.Message, "Win4DaveMediaPlayer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.O))
        {
            OpenDialog();
            return true;
        }
        if (ActiveControl is TextBox || !player.IsLoaded)
            return base.ProcessCmdKey(ref msg, keyData);
        Action? action = keyData switch
        {
            Keys.Space => player.TogglePlayback,
            Keys.Left or Keys.Oemcomma => () => player.Step(-1),
            Keys.Right or Keys.OemPeriod => () => player.Step(1),
            Keys.Home => () => player.Seek(player.Interval?.Start ?? 0),
            _ => null
        };
        if (action is null)
            return base.ProcessCmdKey(ref msg, keyData);
        Run(action);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            timer.Dispose();
            player?.Dispose();
        }
        base.Dispose(disposing);
    }
}
