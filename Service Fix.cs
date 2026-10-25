// Service Fix.cs - WinForms port of "Service Fix.ps1".
// Reads service lists from "Service Fix.ini" (next to the EXE, or on the Desktop).
// Build: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe "/out:Service Fix.exe"
//        /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll
//        /r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll
//        "/win32manifest:Service Fix.manifest" "/win32icon:Service Fix.ico" "Service Fix.cs"
// (The manifest is optional; it requests admin rights (UAC prompt) and DPI awareness.)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

// ======================================================================
// Service logic (same behavior as the PowerShell script)
// ======================================================================
class ServiceRow
{
    public string DisplayName, Name, Status, StartType, Target;
    public bool Ok;
}

static class ServiceLogic
{
    static readonly string[] SectionNames = { "ManualDisplayNames", "ManualNames", "DisabledDisplayNames", "DisabledNames" };
    static List<string> manualDisplayNames, manualNames, disabledDisplayNames, disabledNames;
    public static string IniPath;

    public static string FindIni()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Service Fix.ini");
        if (File.Exists(path)) return path;
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Service Fix.ini");
        return File.Exists(path) ? path : null;
    }

    public static void LoadIni()
    {
        IniPath = FindIni();
        if (IniPath == null)
            throw new FileNotFoundException("Service Fix.ini was not found next to the EXE or on the Desktop.");

        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string s in SectionNames) sections[s] = new List<string>();
        List<string> current = null;
        foreach (string raw in File.ReadAllLines(IniPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            // Only the known section names are headers, so an entry like "[a-c]*" stays an entry.
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                List<string> section;
                if (sections.TryGetValue(line.Substring(1, line.Length - 2).Trim(), out section))
                {
                    current = section;
                    continue;
                }
            }
            // Quotes are allowed (e.g. pasted from the .ps1: *"Epic "* or "Haptic Service").
            string entry = line.Replace("\"", "");
            if (current != null && entry.Length > 0) current.Add(entry);
        }

        manualDisplayNames = Section(sections, "ManualDisplayNames");
        manualNames = Section(sections, "ManualNames");
        disabledDisplayNames = Section(sections, "DisabledDisplayNames");
        disabledNames = Section(sections, "DisabledNames");
    }

    static List<string> Section(Dictionary<string, List<string>> sections, string name)
    {
        List<string> list;
        return sections.TryGetValue(name, out list) ? list : new List<string>();
    }

    // Uses PowerShell's own wildcard engine (* ? [a-c] and ` escapes), so matching is identical to Get-Service.
    static Func<string, bool> Matcher(string pattern)
    {
        var wp = new WildcardPattern(pattern, WildcardOptions.IgnoreCase);
        return s =>
        {
            try { return wp.IsMatch(s); }
            catch (WildcardPatternException) { return string.Equals(s, pattern, StringComparison.OrdinalIgnoreCase); }
        };
    }

    static List<ServiceController> Find(List<string> displayPatterns, List<string> namePatterns)
    {
        var dMatch = displayPatterns.Select(Matcher).ToList();
        var nMatch = namePatterns.Select(Matcher).ToList();
        // Like Get-Service -Name: a literal (non-wildcard) name also matches the display name.
        var literals = new HashSet<string>(namePatterns.Where(n => !WildcardPattern.ContainsWildcardCharacters(n)),
                                           StringComparer.OrdinalIgnoreCase);
        var result = new List<ServiceController>();
        foreach (ServiceController sc in ServiceController.GetServices())
        {
            if (dMatch.Any(m => m(sc.DisplayName)) || nMatch.Any(m => m(sc.ServiceName)) ||
                literals.Contains(sc.DisplayName))
                result.Add(sc);
        }
        return result;
    }

    public static List<ServiceController> GetDemServices() { return Find(manualDisplayNames, manualNames); }
    public static List<ServiceController> GetDisabledServices() { return Find(disabledDisplayNames, disabledNames); }

    public static List<ServiceRow> GetDeList()
    {
        var rows = new List<ServiceRow>();
        AddRows(rows, GetDemServices(), ServiceStartMode.Manual);
        AddRows(rows, GetDisabledServices(), ServiceStartMode.Disabled);
        return rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void AddRows(List<ServiceRow> rows, List<ServiceController> services, ServiceStartMode target)
    {
        foreach (ServiceController s in services)
        {
            var row = new ServiceRow { DisplayName = s.DisplayName, Name = s.ServiceName, Target = target.ToString() };
            try
            {
                row.Status = s.Status.ToString();
                row.StartType = s.StartType.ToString();
                row.Ok = s.Status == ServiceControllerStatus.Stopped && s.StartType == target;
            }
            catch { row.Status = "?"; row.StartType = "?"; row.Ok = false; }
            rows.Add(row);
        }
    }

    // Equivalent of Stop-Service -Force: stops dependent services first.
    static void StopForce(ServiceController sc)
    {
        try
        {
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Stopped) return;
            foreach (ServiceController dep in sc.DependentServices) StopForce(dep);
            if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        catch { }
    }

    const uint SC_MANAGER_CONNECT = 0x0001;
    const uint SERVICE_CHANGE_CONFIG = 0x0002;
    const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    const uint SERVICE_DEMAND_START = 3;
    const uint SERVICE_DISABLED = 4;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenSCManager(string machine, string db, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenService(IntPtr scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ChangeServiceConfig(IntPtr svc, uint type, uint start, uint error, string path,
        string group, IntPtr tag, string deps, string user, string pwd, string display);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool CloseServiceHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryServiceConfig2(IntPtr svc, uint infoLevel, IntPtr buffer, int size, out int needed);

    const uint SERVICE_QUERY_CONFIG = 0x0001;
    const uint SERVICE_CONFIG_DESCRIPTION = 1;

    // The description shown in services.msc (resolved from resource strings by Windows).
    public static string GetDescription(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return null;
        IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_CONFIG);
        string result = null;
        if (svc != IntPtr.Zero)
        {
            int needed;
            QueryServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, IntPtr.Zero, 0, out needed);
            if (needed > 0)
            {
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (QueryServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, buf, needed, out needed))
                    {
                        IntPtr text = Marshal.ReadIntPtr(buf); // SERVICE_DESCRIPTION.lpDescription
                        if (text != IntPtr.Zero) result = Marshal.PtrToStringUni(text);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            CloseServiceHandle(svc);
        }
        CloseServiceHandle(scm);
        return result;
    }

    // Equivalent of Set-Service -StartupType.
    static void SetStartType(string serviceName, uint startType)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return;
        IntPtr svc = OpenService(scm, serviceName, SERVICE_CHANGE_CONFIG);
        if (svc != IntPtr.Zero)
        {
            ChangeServiceConfig(svc, SERVICE_NO_CHANGE, startType, SERVICE_NO_CHANGE,
                null, null, IntPtr.Zero, null, null, null, null);
            CloseServiceHandle(svc);
        }
        CloseServiceHandle(scm);
    }

    public static void SetAllDemServices(IProgress<string> progress)
    {
        var dem = GetDemServices();
        var dis = GetDisabledServices();

        progress.Report("All superfluous services are being STOPPED...");
        foreach (var s in dem.Concat(dis))
        {
            progress.Report("Stopping " + s.DisplayName + "...");
            StopForce(s);
        }
        Thread.Sleep(1000);

        progress.Report("Setting startup of all superfluous services to MANUAL...");
        foreach (var s in GetDemServices())
            try { if (s.StartType != ServiceStartMode.Manual) SetStartType(s.ServiceName, SERVICE_DEMAND_START); } catch { }

        progress.Report("Setting startup of critical services to DISABLED...");
        foreach (var s in GetDisabledServices())
            try { if (s.StartType != ServiceStartMode.Disabled) SetStartType(s.ServiceName, SERVICE_DISABLED); } catch { }
        Thread.Sleep(1000);
    }
}

// ======================================================================
// UI
// ======================================================================
class Palette
{
    public Color Bg, PanelBg, HeaderBg, AltRow, GridLine, Fg, Muted, Good, Bad,
                 Accent, AccentHover, ButtonBg, ButtonHover, Selection;

    public static readonly Palette Dark = new Palette
    {
        Bg = Color.FromArgb(30, 30, 30), PanelBg = Color.FromArgb(37, 37, 38),
        HeaderBg = Color.FromArgb(45, 45, 48), AltRow = Color.FromArgb(35, 35, 36),
        GridLine = Color.FromArgb(55, 55, 58), Fg = Color.FromArgb(230, 230, 230),
        Muted = Color.FromArgb(160, 160, 160), Good = Color.FromArgb(108, 203, 95),
        Bad = Color.FromArgb(241, 112, 123), Accent = Color.FromArgb(46, 125, 50),
        AccentHover = Color.FromArgb(56, 142, 60), ButtonBg = Color.FromArgb(45, 45, 48),
        ButtonHover = Color.FromArgb(62, 62, 66), Selection = Color.FromArgb(9, 71, 113)
    };

    public static readonly Palette Light = new Palette
    {
        Bg = Color.White, PanelBg = Color.FromArgb(243, 243, 243),
        HeaderBg = Color.FromArgb(229, 229, 229), AltRow = Color.FromArgb(248, 248, 248),
        GridLine = Color.FromArgb(221, 221, 221), Fg = Color.FromArgb(30, 30, 30),
        Muted = Color.FromArgb(96, 96, 96), Good = Color.FromArgb(16, 124, 16),
        Bad = Color.FromArgb(196, 43, 28), Accent = Color.FromArgb(46, 125, 50),
        AccentHover = Color.FromArgb(56, 142, 60), ButtonBg = Color.FromArgb(251, 251, 251),
        ButtonHover = Color.FromArgb(235, 235, 235), Selection = Color.FromArgb(204, 228, 247)
    };

    // Windows "Choose your app mode" setting. Missing value = light (Windows default).
    public static bool SystemIsDark()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                return v is int && (int)v == 0;
            }
        }
        catch { return false; }
    }
}

class MainForm : Form
{
    Palette p;
    bool isDark;
    float k = 1f; // DPI scale factor

    Panel top;
    TableLayoutPanel bottom;
    FlowLayoutPanel buttons;
    DataGridView grid;
    Label title, summary, status;
    Button applyBtn, refreshBtn, iniBtn, exitBtn;
    bool busy, allOk;

    int S(int px) { return (int)Math.Round(px * k); }

    public MainForm()
    {
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;

        Text = "Service Fix";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Shield; }
        Font = new Font("Segoe UI", 9.75f);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(1000), S(680));
        MinimumSize = new Size(S(700), S(400));
        WindowState = FormWindowState.Maximized;

        // ---- Header (auto-sizes to its text) ----
        top = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(16), S(10), S(16), S(12)) };
        var headerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false
        };
        title = new Label { Text = "Service Fix", AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), Margin = new Padding(0) };
        summary = new Label { AutoSize = true, Margin = new Padding(S(2), S(2), 0, 0) };
        headerFlow.Controls.Add(title);
        headerFlow.Controls.Add(summary);
        top.Controls.Add(headerFlow);

        // ---- Grid ----
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            StandardTab = true
        };
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.75f);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(6), 0, 0, 0);
        grid.DefaultCellStyle.Padding = new Padding(S(6), 0, 0, 0);
        grid.ColumnHeadersHeight = Font.Height + S(16);
        grid.RowTemplate.Height = Font.Height + S(12);
        AddColumn("", 4, "State");
        AddColumn("Display Name", 40, "DisplayName");
        AddColumn("Name", 22, "Name");
        AddColumn("Status", 12, "Status");
        AddColumn("Startup Type", 12, "StartType");
        AddColumn("Target", 12, "Target");
        grid.Columns["State"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Columns["State"].DefaultCellStyle.Font = new Font("Segoe UI Symbol", 10f);
        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(grid, true, null);

        // ---- Footer (auto-sizes to its buttons) ----
        bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Padding = new Padding(S(16), S(12), S(16), S(12))
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right
        };
        exitBtn = MakeButton("Exit", false);
        applyBtn = MakeButton("Apply", true);
        refreshBtn = MakeButton("Refresh", false);
        iniBtn = MakeButton("Edit INI", false);
        buttons.Controls.AddRange(new Control[] { exitBtn, applyBtn, refreshBtn, iniBtn });
        status = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            Margin = new Padding(0), Text = "Enter / Space = Apply    Esc = Exit"
        };
        bottom.Controls.Add(status, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(bottom);
        grid.BringToFront();

        ApplyTheme(Palette.SystemIsDark());

        applyBtn.Click += delegate { Apply(); };
        refreshBtn.Click += delegate { RefreshList(); };
        iniBtn.Click += delegate { EditIni(); };
        exitBtn.Click += delegate { Close(); };
        grid.SelectionChanged += delegate { grid.ClearSelection(); };
        grid.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            string displayName = (string)row.Cells["DisplayName"].Value, name = (string)row.Cells["Name"].Value;
            using (var dlg = new DescriptionDialog(displayName, name, ServiceLogic.GetDescription(name), p, isDark, k))
                dlg.ShowDialog(this);
        };
        Shown += delegate { RefreshList(); };

        // Follow light/dark changes live.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        FormClosed += delegate { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; };
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        bool dark = Palette.SystemIsDark();
        if (dark != isDark && IsHandleCreated) BeginInvoke((Action)(() => ApplyTheme(dark)));
    }

    void ApplyTheme(bool dark)
    {
        isDark = dark;
        p = dark ? Palette.Dark : Palette.Light;

        BackColor = p.Bg;
        ForeColor = p.Fg;
        top.BackColor = bottom.BackColor = buttons.BackColor = p.PanelBg;
        title.ForeColor = p.Fg;
        status.ForeColor = p.Muted;
        summary.ForeColor = allOk ? p.Good : p.Muted;

        grid.BackgroundColor = p.Bg;
        grid.GridColor = p.GridLine;
        grid.ColumnHeadersDefaultCellStyle.BackColor = p.HeaderBg;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Fg;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.HeaderBg;
        grid.DefaultCellStyle.BackColor = p.Bg;
        grid.DefaultCellStyle.ForeColor = p.Fg;
        grid.DefaultCellStyle.SelectionBackColor = p.Selection;
        grid.AlternatingRowsDefaultCellStyle.BackColor = p.AltRow;
        foreach (DataGridViewRow row in grid.Rows) ColorRow(row);

        StyleButton(applyBtn, true);
        StyleButton(refreshBtn, false);
        StyleButton(iniBtn, false);
        StyleButton(exitBtn, false);

        SetTitleBarDark(dark);
        Invalidate(true);
    }

    void ColorRow(DataGridViewRow row)
    {
        bool ok = row.Tag is bool && (bool)row.Tag;
        row.DefaultCellStyle.ForeColor = ok ? p.Good : p.Bad;
        row.DefaultCellStyle.SelectionForeColor = row.DefaultCellStyle.ForeColor;
    }

    void AddColumn(string header, float weight, string name)
    {
        var col = new DataGridViewTextBoxColumn { HeaderText = header, Name = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
        grid.Columns.Add(col);
    }

    Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(S(110), Font.Height + S(16)),
            Padding = new Padding(S(8), 0, S(8), 0), FlatStyle = FlatStyle.Flat,
            Margin = new Padding(S(8), 0, 0, 0), Cursor = Cursors.Hand, UseVisualStyleBackColor = false
        };
        if (primary) b.Font = new Font("Segoe UI Semibold", 9.75f);
        return b;
    }

    void StyleButton(Button b, bool primary)
    {
        b.BackColor = primary ? p.Accent : p.ButtonBg;
        b.ForeColor = primary ? Color.White : p.Fg;
        b.FlatAppearance.BorderColor = primary ? p.Accent : p.GridLine;
        b.FlatAppearance.MouseOverBackColor = primary ? p.AccentHover : p.ButtonHover;
    }

    // Enter / Space = Apply, Esc = Exit (same keys as the script).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if ((keyData == Keys.Enter || keyData == Keys.Space) && !busy) { Apply(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Dark/light title bar (DWMWA_USE_IMMERSIVE_DARK_MODE), Windows 10 20H1+ / Windows 11.
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void SetTitleBarDark(bool dark)
    {
        if (!IsHandleCreated) return;
        int v = dark ? 1 : 0;
        try { DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetTitleBarDark(isDark);
    }

    void SetBusy(bool value)
    {
        busy = value;
        applyBtn.Enabled = refreshBtn.Enabled = iniBtn.Enabled = !value;
        UseWaitCursor = value;
    }

    async void RefreshList()
    {
        if (busy) return;
        SetBusy(true);
        status.Text = "Reading services...";
        try
        {
            var rows = await Task.Run(() => { ServiceLogic.LoadIni(); return ServiceLogic.GetDeList(); });
            Populate(rows);
            status.Text = "Enter / Space = Apply    Esc = Exit";
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(this, ex.Message, "Service Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
        catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        finally { SetBusy(false); }
    }

    void Populate(List<ServiceRow> rows)
    {
        grid.SuspendLayout();
        grid.Rows.Clear();
        foreach (var r in rows)
        {
            int i = grid.Rows.Add(r.Ok ? "✔" : "✖", r.DisplayName, r.Name, r.Status, r.StartType, r.Target);
            grid.Rows[i].Tag = r.Ok;
            ColorRow(grid.Rows[i]);
        }
        grid.ResumeLayout();
        grid.ClearSelection();

        int ok = rows.Count(r => r.Ok);
        allOk = ok == rows.Count;
        summary.Text = ok + " of " + rows.Count + " services are in the desired state" +
                       (allOk ? "." : "  •  " + (rows.Count - ok) + " need changes.");
        summary.ForeColor = allOk ? p.Good : p.Muted;
    }

    async void Apply()
    {
        if (busy) return;
        SetBusy(true);
        var progress = new Progress<string>(msg => status.Text = msg);
        try
        {
            await Task.Run(() => { ServiceLogic.LoadIni(); ServiceLogic.SetAllDemServices(progress); });
        }
        catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        SetBusy(false);
        RefreshList();
    }

    void EditIni()
    {
        string path = ServiceLogic.IniPath ?? ServiceLogic.FindIni();
        if (path == null) return;
        try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
        status.Text = "Save the INI in Notepad, then click Refresh.";
    }
}

// Small themed pop-up with a service's description, plus OK / Web Search.
class DescriptionDialog : Form
{
    readonly string query;
    readonly bool dark;

    public DescriptionDialog(string displayName, string serviceName, string description, Palette p, bool dark, float k)
    {
        this.dark = dark;
        Func<int, int> S = px => (int)Math.Round(px * k);
        query = string.Equals(displayName, serviceName, StringComparison.OrdinalIgnoreCase)
            ? serviceName + " Windows service"
            : displayName + " " + serviceName + " Windows service";

        Text = displayName;
        Font = new Font("Segoe UI", 9.75f);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = p.Bg;
        ForeColor = p.Fg;

        int textWidth = S(440);
        var layout = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
            Padding = new Padding(S(20), S(16), S(20), S(16)), BackColor = p.Bg
        };
        var title = new Label
        {
            Text = displayName, AutoSize = true, MaximumSize = new Size(textWidth, 0),
            Font = new Font("Segoe UI Semibold", 12f), ForeColor = p.Fg, Margin = new Padding(0)
        };
        var name = new Label
        {
            Text = serviceName, AutoSize = true, MaximumSize = new Size(textWidth, 0),
            ForeColor = p.Muted, Margin = new Padding(0, S(2), 0, S(12))
        };
        bool none = string.IsNullOrWhiteSpace(description);
        var desc = new Label
        {
            Text = none ? "This service has no description." : description.Trim(),
            AutoSize = true, MaximumSize = new Size(textWidth, 0), MinimumSize = new Size(textWidth, 0),
            ForeColor = none ? p.Muted : p.Fg, Margin = new Padding(0, 0, 0, S(18))
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = p.Bg
        };
        var ok = MakeButton("OK", true, p, S);
        var search = MakeButton("Web Search", false, p, S);
        ok.DialogResult = DialogResult.OK;
        search.Click += delegate { WebSearch(); Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(search);

        layout.Controls.Add(title);
        layout.Controls.Add(name);
        layout.Controls.Add(desc);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = ok;
        ActiveControl = ok;
    }

    static Button MakeButton(string text, bool primary, Palette p, Func<int, int> S)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(S(100), S(32)), Padding = new Padding(S(8), 0, S(8), 0),
            FlatStyle = FlatStyle.Flat, Margin = new Padding(S(8), 0, 0, 0), Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            BackColor = primary ? p.Accent : p.ButtonBg, ForeColor = primary ? Color.White : p.Fg
        };
        b.FlatAppearance.BorderColor = primary ? p.Accent : p.GridLine;
        b.FlatAppearance.MouseOverBackColor = primary ? p.AccentHover : p.ButtonHover;
        if (primary) b.Font = new Font("Segoe UI Semibold", 9.75f);
        return b;
    }

    // Google search in the default browser. Launched through Explorer so the browser
    // does not inherit this app's administrator rights.
    void WebSearch()
    {
        string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
        try { Process.Start("explorer.exe", "\"" + url + "\""); }
        catch { try { Process.Start(url); } catch { } }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int v = dark ? 1 : 0;
        try { MainForm.DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
    }
}
