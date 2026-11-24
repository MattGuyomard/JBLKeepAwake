using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using FormsTimer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("JBL Keep Awake")]
[assembly: AssemblyDescription("A quiet audio keep-alive for Windows")]
[assembly: AssemblyProduct("JBL Keep Awake")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal sealed class MainWindow : Form
{
    private const string AppName = "JBL Keep Awake";
    private const int MaximumRetries = 4;
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "JBLKeepAwake";
    private const int ColorSteps = 8;
    private const uint GetClientAreaAnimation = 0x1042;
    private static readonly Color StoppedColor = Color.FromArgb(55, 65, 81);
    private static readonly Color RunningColor = Color.FromArgb(20, 104, 75);

    private readonly SignalSettings settings;
    private readonly KeepAwakeSignal signal = new KeepAwakeSignal();
    private readonly Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolStripMenuItem trayToggle = new ToolStripMenuItem();
    private readonly ToolStripMenuItem levels = new ToolStripMenuItem("Signal level");
    private readonly Button toggle = new Button();
    private readonly Label status = new Label();
    private readonly ComboBox outputPicker = new ComboBox();
    private readonly NumericUpDown frequency = new NumericUpDown();
    private readonly ComboBox level = new ComboBox();
    private readonly CheckBox startWithWindows = new CheckBox();
    private readonly Label resetFeedback = new Label();
    private readonly ToolTip toolTip = new ToolTip();
    private readonly FormsTimer retry = new FormsTimer { Interval = 750 };
    private readonly FormsTimer feedbackTimer = new FormsTimer { Interval = 2000 };
    private readonly FormsTimer colorTimer = new FormsTimer { Interval = 15 };

    private AudioOutputs outputs;
    private string outputId;
    private string outputName;
    private int retries;
    private int colorStep;
    private Color colorFrom;
    private Color colorTo;
    private bool enabled;
    private bool waiting;
    private bool exiting;
    private bool resettingSettings;

    public MainWindow(SignalSettings settings)
    {
        this.settings = settings;
        Text = AppName;
        Icon = appIcon;
        ClientSize = new Size(400, 386);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Window;

        status.Location = new Point(20, 16);
        status.Size = new Size(360, 24);
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.AutoEllipsis = true;

        toggle.Location = new Point(20, 46);
        toggle.Size = new Size(360, 48);
        toggle.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 14, FontStyle.Bold);
        toggle.FlatStyle = FlatStyle.Flat;
        toggle.FlatAppearance.BorderColor = SystemColors.ControlDark;
        toggle.UseVisualStyleBackColor = false;
        toggle.Click += delegate { ToggleSignal(); };
        toggle.TabIndex = 0;

        outputPicker.Location = new Point(20, 125);
        outputPicker.Size = new Size(360, 24);
        outputPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        outputPicker.AccessibleName = "Audio output";
        outputPicker.TabIndex = 2;
        outputPicker.SelectionChangeCommitted += delegate { ChangeOutput(); };

        frequency.Location = new Point(243, 165);
        frequency.Size = new Size(108, 24);
        frequency.Minimum = SignalSettings.MinimumFrequency;
        frequency.Maximum = SignalSettings.MaximumFrequency;
        frequency.Value = settings.Frequency;
        frequency.AccessibleName = "Frequency in hertz";
        frequency.TabIndex = 4;
        frequency.ValueChanged += delegate { ChangeFrequency(); };
        frequency.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ChangeFrequency();
                e.SuppressKeyPress = true;
            }
        };

        level.Location = new Point(243, 201);
        level.Size = new Size(137, 24);
        level.DropDownStyle = ComboBoxStyle.DropDownList;
        level.AccessibleName = "Signal level";
        level.TabIndex = 6;

        menu.Items.Add("Open", null, delegate { ShowWindow(); });
        trayToggle.Click += delegate { ToggleSignal(); };
        menu.Items.Add(trayToggle);
        foreach (int value in SignalSettings.Levels)
        {
            string text = value + " dBFS";
            level.Items.Add(text);
            var item = new ToolStripMenuItem(text) { Tag = value };
            item.Click += delegate { level.SelectedIndex = Array.IndexOf(SignalSettings.Levels, (int)item.Tag); };
            levels.DropDownItems.Add(item);
        }
        level.SelectedIndex = Array.IndexOf(SignalSettings.Levels, settings.Level);
        level.SelectedIndexChanged += delegate
        {
            if (resettingSettings)
                return;

            settings.Level = SignalSettings.Levels[level.SelectedIndex];
            ApplySettings();
        };
        menu.Items.Add(levels);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate
        {
            exiting = true;
            Close();
        });

        var defaults = new Button
        {
            Text = "Default settings",
            Location = new Point(243, 235),
            Size = new Size(137, 29),
            TabIndex = 7
        };
        defaults.Click += delegate { RestoreDefaults(); };

        resetFeedback.Location = new Point(20, 235);
        resetFeedback.Size = new Size(210, 29);
        resetFeedback.TextAlign = ContentAlignment.MiddleLeft;
        feedbackTimer.Tick += delegate
        {
            feedbackTimer.Stop();
            resetFeedback.Text = string.Empty;
        };

        startWithWindows.Text = "Start with &Windows";
        startWithWindows.AutoSize = true;
        startWithWindows.Location = new Point(20, 281);
        startWithWindows.TabIndex = 8;
        startWithWindows.Checked = ReadWindowsStartup();
        startWithWindows.Click += delegate { ChangeWindowsStartup(); };

        var startSignal = new CheckBox
        {
            Text = "Start signal on &launch",
            AutoSize = true,
            Location = new Point(20, 310),
            TabIndex = 9,
            Checked = settings.StartSignalOnLaunch
        };
        startSignal.CheckedChanged += delegate
        {
            settings.StartSignalOnLaunch = startSignal.Checked;
            settings.Save();
        };

        var hint = new Label
        {
            Text = "Closing this window keeps the app in the tray.",
            Location = new Point(20, 351),
            Size = new Size(360, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText
        };
        Controls.AddRange(new Control[]
        {
            status, toggle,
            new Label { Text = "&Output", AutoSize = true, Location = new Point(20, 106), TabIndex = 1 },
            outputPicker,
            new Label { Text = "&Frequency", AutoSize = true, Location = new Point(20, 168), TabIndex = 3 },
            frequency, new Label { Text = "Hz", AutoSize = true, Location = new Point(358, 168) },
            new Label { Text = "Signal l&evel", AutoSize = true, Location = new Point(20, 204), TabIndex = 5 },
            level, defaults, resetFeedback, startWithWindows, startSignal, hint
        });

        retry.Tick += delegate { RefreshPlayback(true); };
        colorTimer.Tick += delegate { AdvanceToggleColor(); };
        SystemColorsChanged += delegate { UpdateToggleColor(false); };
        tray.Icon = appIcon;
        tray.ContextMenuStrip = menu;
        tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ShowWindow();
        };
        UpdateState();
        UpdateToggleColor(false);
        tray.Visible = true;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            WatchOutputs();
        }
        catch (COMException) { } // Windows audio may still be starting at sign-in.
        RefreshOutputPicker();
        if (settings.StartSignalOnLaunch)
            ToggleSignal();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        toggle.Focus();
    }

    private void ToggleSignal()
    {
        try
        {
            if (!enabled)
            {
                enabled = true;
                outputId = settings.OutputId;
                outputName = settings.OutputName;
                retries = 0;
                RefreshPlayback(false);
            }
            else
            {
                signal.Stop();
                retry.Stop();
                enabled = false;
                outputId = null;
                outputName = null;
                waiting = false;
                UpdateState();
            }
            RefreshOutputPicker();
            UpdateToggleColor(true);
        }
        catch (Exception error)
        {
            if (!(error is InvalidOperationException) && !(error is COMException))
                throw;
            ShowWindow();
            MessageBox.Show(this, error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateState();
        }
    }

    private void WatchOutputs()
    {
        if (outputs == null)
            outputs = new AudioOutputs(QueueOutputChange);
    }

    private void QueueOutputChange(string id, bool invalidated)
    {
        // Core Audio callbacks must return without waiting for the UI thread.
        try
        {
            BeginInvoke((Action)delegate
            {
                if (outputs == null || exiting)
                    return;
                if (enabled && (outputId == null || SameOutput(id, outputId)))
                {
                    retries = 0;
                    RefreshPlayback(invalidated && SameOutput(id, outputId));
                }
                RefreshOutputPicker();
            });
        }
        catch (InvalidOperationException) { } // The window was closed.
    }

    private static bool SameOutput(string first, string second)
    {
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshOutputPicker()
    {
        var available = new List<AudioOutput>();
        string defaultId = null;
        try
        {
            if (outputs != null)
            {
                available = outputs.ListActive();
                defaultId = outputs.DefaultId();
            }
        }
        catch (COMException)
        {
            // Keep the last list when Windows is updating its audio devices.
            if (outputPicker.Items.Count != 0)
                return;
        }

        var playingOutput = available.Find(item => SameOutput(item.Id, outputId));
        if (playingOutput != null)
            outputName = playingOutput.Name;
        var windowsDefault = available.Find(item => SameOutput(item.Id, defaultId));
        string defaultName = enabled && settings.OutputId == null && outputId != null
            ? outputName ?? "Waiting for audio output"
            : windowsDefault == null ? "No available output" : windowsDefault.Name;
        var defaultChoice = new AudioOutput(null, "Windows default (" + defaultName + ")");
        var selected = available.Find(item => SameOutput(item.Id, settings.OutputId));

        outputPicker.BeginUpdate();
        try
        {
            outputPicker.Items.Clear();
            outputPicker.Items.Add(defaultChoice);
            foreach (var item in available)
                outputPicker.Items.Add(item);
            if (settings.OutputId != null && selected == null)
            {
                selected = new AudioOutput(settings.OutputId,
                    (settings.OutputName ?? "Selected output") + " (unavailable)");
                outputPicker.Items.Add(selected);
            }
            outputPicker.SelectedItem = selected ?? defaultChoice;
        }
        finally
        {
            outputPicker.EndUpdate();
        }
        toolTip.SetToolTip(outputPicker, outputPicker.Text);
        UpdateState();
    }

    private void ChangeOutput()
    {
        var selected = (AudioOutput)outputPicker.SelectedItem;
        if (selected == null || SameOutput(selected.Id, settings.OutputId))
            return;

        settings.OutputId = selected.Id;
        settings.OutputName = selected.Id == null ? null : selected.Name;
        if (enabled)
        {
            outputId = settings.OutputId;
            outputName = settings.OutputName;
        }
        ApplySettings();
        RefreshOutputPicker();
    }

    private void RefreshPlayback(bool restart)
    {
        retry.Stop();
        if (!enabled)
            return;

        string previousOutput = outputId;
        bool wasWatching = outputs != null;
        try
        {
            WatchOutputs();
            if (outputId == null)
                outputId = outputs.DefaultId();

            bool available = outputId != null && outputs.IsActive(outputId);
            if (restart || !available)
                signal.Stop();
            if (available && !signal.IsPlaying)
                signal.Start(outputId, settings.Frequency, settings.Level);

            waiting = !available;
        }
        catch (Exception error)
        {
            if (!(error is InvalidOperationException) && !(error is COMException))
                throw;
            waiting = true;
            // A Bluetooth endpoint can report active before waveOut can open it.
            if (retries++ < MaximumRetries)
                retry.Start();
        }
        if ((!wasWatching && outputs != null) || (previousOutput == null && outputId != null))
            RefreshOutputPicker();
        UpdateState();
    }

    private void ChangeFrequency()
    {
        if (resettingSettings)
            return;

        int value = (int)frequency.Value;
        if (settings.Frequency != value)
        {
            settings.Frequency = value;
            ApplySettings();
        }
    }

    private void RestoreDefaults()
    {
        resettingSettings = true;
        try
        {
            settings.Frequency = SignalSettings.DefaultFrequency;
            settings.Level = SignalSettings.DefaultLevel;
            frequency.Value = settings.Frequency;
            level.SelectedIndex = Array.IndexOf(SignalSettings.Levels, settings.Level);
        }
        finally
        {
            resettingSettings = false;
        }
        ApplySettings();
        resetFeedback.Text = "Defaults restored";
        feedbackTimer.Stop();
        feedbackTimer.Start();
    }

    private void ApplySettings()
    {
        settings.Save();
        retries = 0;
        if (!exiting && enabled)
            RefreshPlayback(true);
        else
            UpdateState();
    }

    private void UpdateState()
    {
        status.Text = !enabled ? "Keep-alive stopped"
            : (waiting ? "Waiting for " : "Playing on ") + (outputName ?? "audio output");
        toolTip.SetToolTip(status, status.Text);
        toggle.Text = enabled ? "Stop" : "Start";
        toggle.AccessibleName = enabled ? "Stop keep-alive" : "Start keep-alive";
        trayToggle.Text = enabled ? "Stop" : "Start";
        tray.Text = AppName + (enabled ? (waiting ? " (waiting)" : " (playing)") : " (stopped)");
        foreach (ToolStripMenuItem item in levels.DropDownItems)
            item.Checked = (int)item.Tag == settings.Level;
    }

    private void UpdateToggleColor(bool animate)
    {
        colorTimer.Stop();
        bool highContrast = SystemInformation.HighContrast;
        colorTo = highContrast ? SystemColors.Control : enabled ? RunningColor : StoppedColor;
        toggle.ForeColor = highContrast ? SystemColors.ControlText : Color.White;
        bool animationsEnabled;
        if (animate && !highContrast && toggle.BackColor != colorTo
            && SystemParametersInfo(GetClientAreaAnimation, 0, out animationsEnabled, 0) && animationsEnabled)
        {
            colorFrom = toggle.BackColor;
            colorStep = 0;
            colorTimer.Start();
        }
        else
        {
            toggle.BackColor = colorTo;
        }
    }

    private void AdvanceToggleColor()
    {
        colorStep++;
        toggle.BackColor = Color.FromArgb(
            colorFrom.R + (colorTo.R - colorFrom.R) * colorStep / ColorSteps,
            colorFrom.G + (colorTo.G - colorFrom.G) * colorStep / ColorSteps,
            colorFrom.B + (colorTo.B - colorFrom.B) * colorStep / ColorSteps);
        if (colorStep == ColorSteps)
            colorTimer.Stop();
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    private static string StartupCommand()
    {
        return "\"" + Application.ExecutablePath + "\"";
    }

    private static bool ReadWindowsStartup()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
                return key != null && string.Equals(key.GetValue(StartupValue) as string,
                    StartupCommand(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error)
        {
            if (!IsStartupError(error))
                throw;
            return false;
        }
    }

    private void ChangeWindowsStartup()
    {
        try
        {
            if (startWithWindows.Checked)
            {
                string command = StartupCommand();
                if (command.Length > 260)
                    throw new InvalidOperationException("Move JBLKeepAwake.exe to a shorter folder path and try again.");
                using (var key = Registry.CurrentUser.CreateSubKey(StartupKey))
                    key.SetValue(StartupValue, command, RegistryValueKind.String);
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupKey, true))
                {
                    if (key != null)
                        key.DeleteValue(StartupValue, false);
                }
            }
        }
        catch (Exception error)
        {
            if (!IsStartupError(error))
                throw;
            startWithWindows.Checked = ReadWindowsStartup();
            MessageBox.Show(this, "Could not change Windows startup. " + error.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsStartupError(Exception error)
    {
        return error is IOException || error is UnauthorizedAccessException
            || error is System.Security.SecurityException || error is InvalidOperationException;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ChangeFrequency();
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            if (outputs != null)
            {
                var previous = outputs;
                outputs = null;
                previous.Dispose();
            }
            retry.Dispose();
            feedbackTimer.Dispose();
            colorTimer.Dispose();
            toolTip.Dispose();
            signal.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            if (appIcon != null)
                appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    [STAThread]
    private static void Main()
    {
        bool firstInstance;
        using (var mutex = new Mutex(true, @"Local\JBLKeepAwake", out firstInstance))
        {
            if (!firstInstance)
            {
                MessageBox.Show("JBL Keep Awake is already running. Check the notification area by the clock.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JBLKeepAwake", "settings.txt");
            Application.Run(new MainWindow(new SignalSettings(path)));
        }
    }
}
