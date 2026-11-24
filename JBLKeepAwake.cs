using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("JBL Keep Awake")]
[assembly: AssemblyDescription("A quiet audio keep-alive for Windows")]
[assembly: AssemblyProduct("JBL Keep Awake")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal sealed class MainWindow : Form
{
    private const string AppName = "JBL Keep Awake";
    private readonly KeepAwakeSignal signal = new KeepAwakeSignal();
    private readonly Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolStripMenuItem trayToggle = new ToolStripMenuItem();
    private readonly ToolStripMenuItem levels = new ToolStripMenuItem("Signal level");
    private readonly Button toggle = new Button();
    private readonly Label status = new Label();
    private int level = -54;
    private bool exiting;

    public MainWindow()
    {
        Text = AppName;
        Icon = appIcon;
        ClientSize = new Size(300, 154);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Window;

        status.Location = new Point(20, 21);
        status.Size = new Size(260, 24);
        status.TextAlign = ContentAlignment.MiddleCenter;

        toggle.Location = new Point(20, 52);
        toggle.Size = new Size(260, 52);
        toggle.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 14, FontStyle.Bold);
        toggle.FlatStyle = FlatStyle.Flat;
        toggle.FlatAppearance.BorderColor = SystemColors.ControlDark;
        toggle.UseVisualStyleBackColor = false;
        toggle.Click += delegate { ToggleSignal(); };
        AcceptButton = toggle;

        var hint = new Label
        {
            Text = "Closing this window keeps the app in the tray.",
            Location = new Point(12, 118),
            Size = new Size(276, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText
        };
        Controls.AddRange(new Control[] { status, toggle, hint });

        menu.Items.Add("Open", null, delegate { ShowWindow(); });
        trayToggle.Click += delegate { ToggleSignal(); };
        menu.Items.Add(trayToggle);
        foreach (int value in new[] { -72, -60, -54, -48, -42, -36, -30 })
        {
            var item = new ToolStripMenuItem(value + " dBFS") { Tag = value };
            item.Click += delegate { ChangeLevel((int)item.Tag); };
            levels.DropDownItems.Add(item);
        }
        menu.Items.Add(levels);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate
        {
            exiting = true;
            Close();
        });

        tray.Icon = appIcon;
        tray.ContextMenuStrip = menu;
        tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ShowWindow();
        };
        UpdateState();
        tray.Visible = true;
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
            if (signal.IsPlaying)
                signal.Stop();
            else
                signal.Start(level);
        }
        catch (InvalidOperationException error)
        {
            ShowAudioError(error);
        }
        UpdateState();
    }

    private void ChangeLevel(int value)
    {
        if (value == level)
            return;

        try
        {
            if (signal.IsPlaying)
            {
                signal.Stop();
                signal.Start(value);
            }
            level = value;
        }
        catch (InvalidOperationException error)
        {
            ShowAudioError(error);
        }
        UpdateState();
    }

    private void ShowAudioError(InvalidOperationException error)
    {
        ShowWindow();
        MessageBox.Show(this, error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void UpdateState()
    {
        bool active = signal.IsPlaying;
        string state = active ? "ON" : "OFF";
        status.Text = active ? "Keep-alive is playing" : "Keep-alive is off";
        toggle.Text = state;
        toggle.AccessibleName = active ? "ON. Turn keep-alive off" : "OFF. Turn keep-alive on";
        toggle.BackColor = active && !SystemInformation.HighContrast
            ? Color.FromArgb(20, 104, 75) : SystemColors.Control;
        toggle.ForeColor = active && !SystemInformation.HighContrast
            ? Color.White : SystemColors.ControlText;
        trayToggle.Text = active ? "Turn Off" : "Turn On";
        tray.Text = AppName + " (" + state + ")";
        foreach (ToolStripMenuItem item in levels.DropDownItems)
            item.Checked = (int)item.Tag == level;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
            Application.Run(new MainWindow());
        }
    }
}
