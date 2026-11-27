using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiningGame.ErrorChecker;

internal sealed class CheckerForm : Form
{
    private readonly string _gamePath = Path.Combine(AppContext.BaseDirectory, "BlockCraft.exe");
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiningGame", "Logs");
    private readonly string _logPath;
    private readonly Label _status;
    private readonly Label _detail;
    private readonly Label _logPathLabel;
    private readonly ProgressBar _progress;
    private readonly Button _openLog;
    private readonly Button _copyReport;
    private readonly Button _closeButton;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 3500 };
    private readonly object _logLock = new();
    private Process? _gameProcess;
    private DateTimeOffset _startedAt;
    private bool _finished;
    private bool _closingFromMonitor;

    public CheckerForm(string[] args)
    {
        _ = args;
        Directory.CreateDirectory(_logDirectory);
        _logPath = Path.Combine(_logDirectory, $"Mining-Game-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        Text = "Mining Game — Launch Guard";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        ClientSize = new Size(620, 360);
        BackColor = Color.FromArgb(13, 21, 25);
        ForeColor = Color.FromArgb(232, 243, 232);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
            if (icon is not null) Icon = icon;
        }
        catch { /* The monitor remains usable if Windows cannot extract the app icon. */ }

        var header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.FromArgb(24, 42, 31) };
        Controls.Add(header);
        var logo = new Label
        {
            Text = "⛏",
            Font = new Font("Segoe UI Symbol", 31, FontStyle.Bold),
            ForeColor = Color.FromArgb(157, 245, 102),
            Location = new Point(22, 13),
            Size = new Size(54, 56),
            TextAlign = ContentAlignment.MiddleCenter
        };
        header.Controls.Add(logo);
        header.Controls.Add(new Label
        {
            Text = "MINING GAME",
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Color.FromArgb(230, 248, 220),
            Location = new Point(82, 15),
            AutoSize = true
        });
        header.Controls.Add(new Label
        {
            Text = "STARTUP CHECK  •  CRASH MONITOR",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(151, 207, 133),
            Location = new Point(85, 52),
            AutoSize = true
        });

        var card = new Panel
        {
            Location = new Point(22, 108),
            Size = new Size(576, 176),
            BackColor = Color.FromArgb(23, 34, 37)
        };
        Controls.Add(card);
        card.Controls.Add(new Label
        {
            Text = "LAUNCH PROTECTION",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(139, 188, 150),
            Location = new Point(20, 17),
            AutoSize = true
        });
        _status = new Label
        {
            Text = "Checking your game installation…",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 245, 229),
            Location = new Point(20, 45),
            Size = new Size(530, 32)
        };
        card.Controls.Add(_status);
        _detail = new Label
        {
            Text = "Verifying core files and starting the game in a monitored process.",
            ForeColor = Color.FromArgb(178, 197, 187),
            Location = new Point(22, 83),
            Size = new Size(530, 38)
        };
        card.Controls.Add(_detail);
        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Location = new Point(22, 132),
            Size = new Size(530, 12),
            ForeColor = Color.FromArgb(114, 222, 75),
            BackColor = Color.FromArgb(47, 62, 54)
        };
        card.Controls.Add(_progress);

        _logPathLabel = new Label
        {
            Text = "Diagnostic log: " + _logPath,
            ForeColor = Color.FromArgb(122, 145, 135),
            Location = new Point(24, 296),
            Size = new Size(570, 24),
            AutoEllipsis = true
        };
        Controls.Add(_logPathLabel);

        _openLog = CreateButton("OPEN LOG", 22, 326, Color.FromArgb(42, 68, 49));
        _copyReport = CreateButton("COPY REPORT", 174, 326, Color.FromArgb(42, 68, 49));
        _closeButton = CreateButton("CLOSE", 466, 326, Color.FromArgb(57, 73, 66));
        _openLog.Enabled = false;
        _copyReport.Enabled = false;
        _openLog.Click += (_, _) => OpenLog();
        _copyReport.Click += (_, _) => CopyReport();
        _closeButton.Click += (_, _) => Close();
        Controls.AddRange([_openLog, _copyReport, _closeButton]);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Mining Game monitor", null, (_, _) => RestoreWindow());
        menu.Items.Add("Open diagnostic log", null, (_, _) => OpenLog());
        _tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "Mining Game — crash monitor",
            ContextMenuStrip = menu,
            Visible = false
        };
        _tray.DoubleClick += (_, _) => RestoreWindow();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_gameProcess is { HasExited: false } && !_finished)
            {
                _tray.Visible = true;
                Hide();
            }
        };
        Shown += (_, _) => BeginLaunch();
        FormClosing += HandleFormClosing;
    }

    private static Button CreateButton(string text, int x, int y, Color color)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(136, 29),
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.FromArgb(231, 244, 228),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(77, 115, 75);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 103, 55);
        return button;
    }

    private void BeginLaunch()
    {
        WriteLog("Mining Game launch monitor started.");
        WriteLog($"OS: {Environment.OSVersion}");
        WriteLog($"Runtime: {RuntimeInformation.FrameworkDescription}");
        WriteLog($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        WriteLog($"Install directory: {AppContext.BaseDirectory}");

        var required = new[]
        {
            _gamePath,
            Path.Combine(AppContext.BaseDirectory, "assets", "data", "blocks.json"),
            Path.Combine(AppContext.BaseDirectory, "assets", "data", "items.json"),
            Path.Combine(AppContext.BaseDirectory, "assets", "generated", "block_atlas.png"),
            Path.Combine(AppContext.BaseDirectory, "assets", "generated", "music", "music_manifest.json")
        };
        var missing = required.Where(path => !File.Exists(path)).ToArray();
        if (missing.Length > 0)
        {
            ShowFailure("Game files are missing", "The installation is incomplete. Reinstall the game or send the diagnostic log to the developer.",
                "Missing files:\n" + string.Join("\n", missing));
            return;
        }

        try
        {
            _startedAt = DateTimeOffset.Now;
            var start = new ProcessStartInfo
            {
                FileName = _gamePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _gameProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
            _gameProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLog("[GAME OUT] " + e.Data); };
            _gameProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) WriteLog("[GAME ERR] " + e.Data); };
            _gameProcess.Exited += (_, _) =>
            {
                int code;
                try { code = _gameProcess.ExitCode; }
                catch { code = -1; }
                try
                {
                    BeginInvoke(new Action(() => HandleGameExit(code)));
                }
                catch (InvalidOperationException) { /* Window is already shutting down. */ }
            };

            if (!_gameProcess.Start())
            {
                ShowFailure("The game process did not start", "Windows did not create the game process.", "Process.Start returned false.");
                return;
            }
            _gameProcess.BeginOutputReadLine();
            _gameProcess.BeginErrorReadLine();
            WriteLog($"Started BlockCraft.exe (PID {_gameProcess.Id}).");
            _status.Text = "Game launched successfully";
            _detail.Text = "Mining Game is running. The monitor will collect a report if it closes with an error.";
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            _hideTimer.Start();
        }
        catch (Exception exception)
        {
            WriteLog("Failed to start the core game process:");
            WriteLog(exception.ToString());
            ShowFailure("Could not launch the game", "The launch monitor caught an error before the game window opened.", exception.ToString());
        }
    }

    private void HandleGameExit(int exitCode)
    {
        if (_finished || IsDisposed) return;
        _finished = true;
        var elapsed = DateTimeOffset.Now - _startedAt;
        WriteLog($"Game process exited with code {exitCode} after {elapsed.TotalSeconds:0.0} seconds.");
        AppendRecentGameCrashReports();

        if (exitCode == 0)
        {
            WriteLog("The game closed normally. No crash report is required.");
            _tray.Visible = false;
            _hideTimer.Stop();
            _closingFromMonitor = true;
            Close();
            return;
        }

        ShowFailure("Mining Game closed unexpectedly",
            $"The game exited with error code {exitCode}. Please send this log file to the developer.",
            "The game process ended with a non-zero exit code. The report below includes the system details, game output and any crash report written by the game.");
    }

    private void AppendRecentGameCrashReports()
    {
        var gameLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockCraft", "Logs");
        if (!Directory.Exists(gameLogs)) return;
        foreach (var path in Directory.GetFiles(gameLogs, "crash-*.txt")
                     .Select(path => new FileInfo(path))
                     .Where(file => file.LastWriteTime >= _startedAt.LocalDateTime.AddSeconds(-5))
                     .OrderByDescending(file => file.LastWriteTime)
                     .Take(3)
                     .Select(file => file.FullName))
        {
            WriteLog("----- GAME CRASH REPORT: " + path + " -----");
            try { WriteLog(File.ReadAllText(path)); }
            catch (Exception exception) { WriteLog("Could not read game crash report: " + exception.Message); }
        }
    }

    private void ShowFailure(string title, string detail, string technicalDetails)
    {
        _finished = true;
        _hideTimer.Stop();
        WriteLog("ERROR: " + title);
        WriteLog(detail);
        WriteLog(technicalDetails);
        WriteLog("Support instruction: send this complete log file to the Mining Game developer.");

        _tray.Visible = false;
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 100;
        _progress.ForeColor = Color.FromArgb(236, 91, 75);
        _status.Text = title;
        _status.ForeColor = Color.FromArgb(255, 142, 120);
        _detail.Text = detail;
        _logPathLabel.Text = "SEND THIS LOG TO THE DEVELOPER: " + _logPath;
        _openLog.Enabled = true;
        _copyReport.Enabled = true;
        _closeButton.Text = "CLOSE";
    }

    private void WriteLog(string message)
    {
        lock (_logLock)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { /* A logger failure must never block or crash the monitored game. */ }
        }
    }

    private void OpenLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_logPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Could not open log", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void CopyReport()
    {
        try
        {
            Clipboard.SetText(File.ReadAllText(_logPath));
            _detail.Text = "Diagnostic report copied. Paste it into your message to the developer.";
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Could not copy report", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void RestoreWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_closingFromMonitor && !_finished && _gameProcess is { HasExited: false } && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.Visible = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _gameProcess?.Dispose();
        }
        base.Dispose(disposing);
    }
}
