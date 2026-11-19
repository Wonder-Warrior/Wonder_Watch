using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("WonderWatch Desktop Installer")]
[assembly: AssemblyDescription("WonderWatch Desktop Setup")]
[assembly: AssemblyProduct("WonderWatch Desktop")]
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]

namespace WonderWatchInstaller
{
    internal static class NativeMethods
    {
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public static void ApplyDarkRoundedWindow(IntPtr handle)
        {
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
                }
                int cornerPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
            }
            catch { }
        }
    }

    internal static class DrawUtils
    {
        public static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
        {
            float d = Math.Min(bounds.Width, Math.Min(bounds.Height, radius * 2f));
            GraphicsPath path = new GraphicsPath();
            if (d <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180f, 90f);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270f, 90f);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class NeonProgressBar : Control
    {
        private int progressValue;
        public int Value
        {
            get { return progressValue; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (progressValue != clamped)
                {
                    progressValue = clamped;
                    Invalidate();
                }
            }
        }

        public NeonProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(255, 5, 6, 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(BackColor);

            RectangleF track = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            float radius = track.Height / 2f;
            using (GraphicsPath trackPath = DrawUtils.CreateRoundedRect(track, radius))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(255, 23, 24, 33)))
            using (Pen borderPen = new Pen(Color.FromArgb(255, 36, 39, 52), 1f))
            {
                g.FillPath(trackBrush, trackPath);
                g.DrawPath(borderPen, trackPath);
            }

            if (progressValue > 0)
            {
                float fillW = Math.Max(track.Height, (track.Width * progressValue) / 100f);
                RectangleF fillRect = new RectangleF(0.5f, 0.5f, Math.Min(track.Width, fillW), track.Height);
                using (GraphicsPath fillPath = DrawUtils.CreateRoundedRect(fillRect, radius))
                using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                    new PointF(0f, 0f),
                    new PointF(Math.Max(1f, fillRect.Width), 0f),
                    Color.FromArgb(255, 34, 204, 55),
                    Color.FromArgb(255, 158, 255, 171)))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }
    }

    internal sealed class RoundedButton : Control
    {
        private bool isHovered;
        private bool isPressed;

        public Color FillColor { get; set; }
        public Color HoverFillColor { get; set; }
        public Color BorderColor { get; set; }
        public float CornerRadius { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            CornerRadius = 12f;
            FillColor = Color.FromArgb(255, 59, 255, 83);
            HoverFillColor = Color.FromArgb(255, 106, 255, 127);
            BorderColor = Color.Transparent;
            ForeColor = Color.Black;
            BackColor = Color.FromArgb(255, 5, 6, 8);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(BackColor);

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            Color fill = !Enabled ? Color.FromArgb(255, 40, 44, 54) : (isPressed ? FillColor : (isHovered ? HoverFillColor : FillColor));
            using (GraphicsPath path = DrawUtils.CreateRoundedRect(rect, CornerRadius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (BorderColor.A > 0)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            Color textCol = !Enabled ? Color.FromArgb(255, 120, 124, 138) : ForeColor;
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            );
        }
    }

    internal sealed class RoundedCardPanel : Panel
    {
        public RoundedCardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(255, 13, 14, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.FromArgb(255, 5, 6, 8));

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            using (GraphicsPath path = DrawUtils.CreateRoundedRect(rect, 18f))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(255, 13, 14, 20)))
            using (Pen border = new Pen(Color.FromArgb(255, 34, 37, 48), 1f))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }
        }
    }

    internal sealed class InstallerForm : Form
    {
        private static readonly Color BgDark = Color.FromArgb(255, 5, 6, 8);
        private static readonly Color CardDark = Color.FromArgb(255, 13, 14, 20);
        private static readonly Color SurfaceDark = Color.FromArgb(255, 23, 24, 33);
        private static readonly Color BrandLime = Color.FromArgb(255, 59, 255, 83);
        private static readonly Color TextMuted = Color.FromArgb(255, 161, 161, 170);

        private readonly CheckBox desktopShortcutBox;
        private readonly CheckBox startMenuShortcutBox;
        private readonly CheckBox launchAfterBox;
        private readonly NeonProgressBar progressBar;
        private readonly Label statusLabel;
        private readonly Label percentLabel;
        private readonly RoundedButton installBtn;
        private readonly RoundedButton cancelBtn;
        private readonly RoundedButton closeXBtn;
        private RoundedButton browseBtn;
        private string installDir;
        private bool isInstalling;

        public InstallerForm()
        {
            installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WonderWatch");

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Text = "WonderWatch Desktop Setup";
            ClientSize = new Size(504, 392);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = Color.White;

            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            MouseDown += OnDragMouseDown;

            Panel headerPanel = new Panel();
            headerPanel.SetBounds(1, 1, 502, 102);
            headerPanel.BackColor = Color.FromArgb(255, 10, 11, 15);
            headerPanel.MouseDown += OnDragMouseDown;
            Controls.Add(headerPanel);

            closeXBtn = new RoundedButton();
            closeXBtn.Text = "×";
            closeXBtn.SetBounds(460, 14, 28, 28);
            closeXBtn.CornerRadius = 14f;
            closeXBtn.BackColor = Color.FromArgb(255, 10, 11, 15);
            closeXBtn.FillColor = Color.FromArgb(255, 23, 24, 33);
            closeXBtn.HoverFillColor = Color.FromArgb(255, 38, 41, 54);
            closeXBtn.BorderColor = Color.FromArgb(255, 42, 46, 58);
            closeXBtn.ForeColor = TextMuted;
            closeXBtn.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            closeXBtn.Click += delegate { if (!isInstalling) Close(); };
            headerPanel.Controls.Add(closeXBtn);

            PictureBox badge = new PictureBox();
            badge.SetBounds(28, 24, 52, 52);
            badge.BackColor = Color.Transparent;
            badge.Image = CreatePlayBadgeBitmap(52);
            badge.SizeMode = PictureBoxSizeMode.Zoom;
            badge.MouseDown += OnDragMouseDown;
            headerPanel.Controls.Add(badge);

            Label wonderLbl = new Label();
            wonderLbl.Text = "WONDER";
            wonderLbl.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            wonderLbl.ForeColor = Color.White;
            wonderLbl.AutoSize = true;
            wonderLbl.Location = new Point(92, 24);
            wonderLbl.MouseDown += OnDragMouseDown;
            headerPanel.Controls.Add(wonderLbl);

            Label watchLbl = new Label();
            watchLbl.Text = "WATCH";
            watchLbl.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            watchLbl.ForeColor = BrandLime;
            watchLbl.AutoSize = true;
            watchLbl.Location = new Point(wonderLbl.Right + 2, 24);
            watchLbl.MouseDown += OnDragMouseDown;
            headerPanel.Controls.Add(watchLbl);

            Label subTitle = new Label();
            subTitle.Text = "Desktop Installer • v2.4.0 (Windows x64) • Built-in Shield";
            subTitle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            subTitle.ForeColor = TextMuted;
            subTitle.SetBounds(94, 54, 355, 20);
            subTitle.MouseDown += OnDragMouseDown;
            headerPanel.Controls.Add(subTitle);

            RoundedCardPanel optionsCard = new RoundedCardPanel();
            optionsCard.SetBounds(26, 118, 452, 144);
            Controls.Add(optionsCard);

            Label pathLabel = new Label();
            pathLabel.Text = "Destination:  " + installDir;
            pathLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            pathLabel.ForeColor = TextMuted;
            pathLabel.BackColor = CardDark;
            pathLabel.AutoEllipsis = true;
            pathLabel.SetBounds(20, 18, 324, 20);
            optionsCard.Controls.Add(pathLabel);

            browseBtn = new RoundedButton();
            browseBtn.Text = "Browse...";
            browseBtn.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            browseBtn.FillColor = Color.FromArgb(255, 34, 42, 38);
            browseBtn.ForeColor = Color.White;
            browseBtn.CornerRadius = 8;
            browseBtn.SetBounds(352, 14, 80, 26);
            browseBtn.Click += delegate
            {
                if (isInstalling) return;
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Choose where to install WonderWatch";
                    dlg.SelectedPath = Directory.Exists(installDir) ? installDir : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    {
                        string chosen = dlg.SelectedPath.TrimEnd('\\', '/');
                        if (!chosen.EndsWith("WonderWatch", StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = Path.Combine(chosen, "WonderWatch");
                        }
                        installDir = chosen;
                        pathLabel.Text = "Destination:  " + installDir;
                    }
                }
            };
            optionsCard.Controls.Add(browseBtn);

            desktopShortcutBox = new CheckBox();
            desktopShortcutBox.Text = "Create Desktop shortcut";
            desktopShortcutBox.Checked = true;
            desktopShortcutBox.BackColor = CardDark;
            desktopShortcutBox.ForeColor = Color.White;
            desktopShortcutBox.Font = new Font("Segoe UI", 9.5f);
            desktopShortcutBox.SetBounds(20, 46, 360, 26);
            optionsCard.Controls.Add(desktopShortcutBox);

            startMenuShortcutBox = new CheckBox();
            startMenuShortcutBox.Text = "Add to Windows Start Menu";
            startMenuShortcutBox.Checked = true;
            startMenuShortcutBox.BackColor = CardDark;
            startMenuShortcutBox.ForeColor = Color.White;
            startMenuShortcutBox.Font = new Font("Segoe UI", 9.5f);
            startMenuShortcutBox.SetBounds(20, 74, 360, 26);
            optionsCard.Controls.Add(startMenuShortcutBox);

            launchAfterBox = new CheckBox();
            launchAfterBox.Text = "Launch WonderWatch after installation";
            launchAfterBox.Checked = true;
            launchAfterBox.BackColor = CardDark;
            launchAfterBox.ForeColor = Color.White;
            launchAfterBox.Font = new Font("Segoe UI", 9.5f);
            launchAfterBox.SetBounds(20, 102, 360, 26);
            optionsCard.Controls.Add(launchAfterBox);

            statusLabel = new Label();
            statusLabel.Text = "Ready to install WonderWatch Desktop.";
            statusLabel.ForeColor = TextMuted;
            statusLabel.Font = new Font("Segoe UI", 8.5f);
            statusLabel.SetBounds(28, 278, 360, 20);
            Controls.Add(statusLabel);

            percentLabel = new Label();
            percentLabel.Text = "0%";
            percentLabel.ForeColor = BrandLime;
            percentLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            percentLabel.TextAlign = ContentAlignment.MiddleRight;
            percentLabel.SetBounds(404, 278, 72, 20);
            Controls.Add(percentLabel);

            progressBar = new NeonProgressBar();
            progressBar.SetBounds(28, 302, 448, 12);
            Controls.Add(progressBar);

            cancelBtn = new RoundedButton();
            cancelBtn.Text = "Cancel";
            cancelBtn.CornerRadius = 14f;
            cancelBtn.FillColor = SurfaceDark;
            cancelBtn.HoverFillColor = Color.FromArgb(255, 32, 34, 44);
            cancelBtn.BorderColor = Color.FromArgb(255, 42, 46, 58);
            cancelBtn.ForeColor = TextMuted;
            cancelBtn.SetBounds(244, 332, 102, 40);
            cancelBtn.Click += delegate { if (!isInstalling) Close(); };
            Controls.Add(cancelBtn);

            installBtn = new RoundedButton();
            installBtn.Text = "Install";
            installBtn.CornerRadius = 14f;
            installBtn.FillColor = BrandLime;
            installBtn.HoverFillColor = Color.FromArgb(255, 106, 255, 127);
            installBtn.ForeColor = Color.Black;
            installBtn.SetBounds(356, 332, 122, 40);
            installBtn.Click += async delegate { await RunInstallAsync(); };
            Controls.Add(installBtn);

            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            using (GraphicsPath path = DrawUtils.CreateRoundedRect(new RectangleF(0f, 0f, Width, Height), 20f))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath borderPath = DrawUtils.CreateRoundedRect(new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f), 20f))
            using (Pen borderPen = new Pen(Color.FromArgb(255, 38, 42, 54), 1.5f))
            {
                g.DrawPath(borderPen, borderPath);
            }
        }

        private void OnDragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkRoundedWindow(Handle);
        }

        private async Task RunInstallAsync()
        {
            if (isInstalling) return;
            isInstalling = true;
            installBtn.Enabled = false;
            cancelBtn.Enabled = false;
            closeXBtn.Enabled = false;
            desktopShortcutBox.Enabled = false;
            startMenuShortcutBox.Enabled = false;
            launchAfterBox.Enabled = false;

            bool createDesktop = desktopShortcutBox.Checked;
            bool createStartMenu = startMenuShortcutBox.Checked;
            bool launchAfter = launchAfterBox.Checked;

            try
            {
                statusLabel.Text = "Preparing installation folder...";
                progressBar.Value = 15;
                percentLabel.Text = "15%";

                await Task.Run(delegate
                {
                    InstallFiles(installDir, createDesktop, createStartMenu, delegate(int pct, string msg)
                    {
                        BeginInvoke(new Action(delegate
                        {
                            int clamped = Math.Min(100, Math.Max(0, pct));
                            progressBar.Value = clamped;
                            percentLabel.Text = clamped + "%";
                            statusLabel.Text = msg;
                        }));
                    });
                });

                progressBar.Value = 100;
                percentLabel.Text = "100%";
                statusLabel.Text = "Installation complete!";

                if (launchAfter)
                {
                    string exePath = Path.Combine(installDir, "WonderWatch.exe");
                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = installDir, UseShellExecute = true });
                    }
                }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installation failed:\n\n" + ex.Message, "WonderWatch Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                isInstalling = false;
                installBtn.Enabled = true;
                cancelBtn.Enabled = true;
                closeXBtn.Enabled = true;
            }
        }

        public static void InstallFiles(string targetDir, bool createDesktop, bool createStartMenu, Action<int, string> progress)
        {
            if (progress != null) progress(20, "Closing any running instances...");
            foreach (Process p in Process.GetProcessesByName("WonderWatch"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }

            Directory.CreateDirectory(targetDir);
            if (progress != null) progress(35, "Extracting WonderWatch Desktop...");

            string tempZip = Path.Combine(Path.GetTempPath(), "ww_install_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("WonderWatchPayload.zip"))
                {
                    if (res == null) throw new InvalidOperationException("Embedded installer payload not found.");
                    using (FileStream fs = File.Create(tempZip))
                    {
                        res.CopyTo(fs);
                    }
                }

                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    int total = Math.Max(1, archive.Entries.Count);
                    int done = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(Path.Combine(targetDir, rel));
                        }
                        else
                        {
                            string destPath = Path.Combine(targetDir, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, true);
                        }
                        done++;
                        if (progress != null && (done % 10 == 0 || done == total))
                        {
                            progress(35 + (done * 45 / total), "Installing files...");
                        }
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }

            string exePath = Path.Combine(targetDir, "WonderWatch.exe");
            string uninstallerPath = Path.Combine(targetDir, "Uninstall.exe");
            try
            {
                File.Copy(Application.ExecutablePath, uninstallerPath, true);
            }
            catch { }

            if (progress != null) progress(85, "Creating shortcuts...");
            if (createDesktop)
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WonderWatch.lnk");
                CreateShortcut(desktopLnk, exePath, targetDir, "Stream Anime Free in HD");
            }
            if (createStartMenu)
            {
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WonderWatch");
                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(Path.Combine(startMenuDir, "WonderWatch.lnk"), exePath, targetDir, "Stream Anime Free in HD");
                CreateShortcut(Path.Combine(startMenuDir, "Uninstall WonderWatch.lnk"), uninstallerPath, targetDir, "Uninstall WonderWatch", "--uninstall");
            }

            if (progress != null) progress(94, "Registering application...");
            RegisterUninstallEntry(targetDir, exePath, uninstallerPath);
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string arguments = "")
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
                if (!string.IsNullOrEmpty(arguments))
                {
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                }
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        private static void RegisterUninstallEntry(string installDir, string exePath, string uninstallerPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\WonderWatch"))
                {
                    if (key == null) return;
                    key.SetValue("DisplayName", "WonderWatch Desktop");
                    key.SetValue("DisplayVersion", "2.4.0");
                    key.SetValue("Publisher", "WonderWatch");
                    key.SetValue("DisplayIcon", exePath);
                    key.SetValue("InstallLocation", installDir);
                    key.SetValue("UninstallString", "\"" + uninstallerPath + "\" --uninstall");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", 14500, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public static void Uninstall(bool silent)
        {
            if (!silent)
            {
                DialogResult res = MessageBox.Show(
                    "Remove WonderWatch Desktop from your computer?",
                    "Uninstall WonderWatch",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );
                if (res != DialogResult.Yes) return;
            }

            foreach (Process p in Process.GetProcessesByName("WonderWatch"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }

            try
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WonderWatch.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
            }
            catch { }

            try
            {
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WonderWatch");
                if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);
            }
            catch { }

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\WonderWatch", false);
            }
            catch { }

            string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (string.IsNullOrWhiteSpace(installDir) || !File.Exists(Path.Combine(installDir, "WonderWatch.exe")))
            {
                installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WonderWatch");
            }
            try
            {
                if (Directory.Exists(installDir))
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 2 > nul & rd /s /q \"" + installDir + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
            }
            catch { }

            if (!silent)
            {
                MessageBox.Show("WonderWatch Desktop has been uninstalled.", "Uninstall Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static Bitmap CreatePlayBadgeBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                RectangleF rect = new RectangleF(2, 2, size - 4, size - 4);
                float r = size * 0.26f;
                float d = r * 2f;
                using (GraphicsPath path = DrawUtils.CreateRoundedRect(rect, r))
                using (LinearGradientBrush brush = new LinearGradientBrush(new Point(0, size), new Point(size, 0), Color.FromArgb(255, 34, 204, 55), Color.FromArgb(255, 158, 255, 171)))
                {
                    g.FillPath(brush, path);
                }
                PointF[] tri = new PointF[]
                {
                    new PointF(size * 0.38f, size * 0.30f),
                    new PointF(size * 0.72f, size * 0.50f),
                    new PointF(size * 0.38f, size * 0.70f)
                };
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, 8, 10, 12)))
                {
                    g.FillPolygon(dark, tri);
                }
            }
            return bmp;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool uninstall = false;
            bool silent = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                    if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                }
            }

            if (uninstall)
            {
                InstallerForm.Uninstall(silent);
                return;
            }

            if (silent)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WonderWatch");
                InstallerForm.InstallFiles(dir, true, true, null);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }
}
