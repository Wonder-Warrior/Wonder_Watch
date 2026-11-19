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
        public const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void ApplyDarkTitleBar(IntPtr handle)
        {
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
                }
                int captionColor = 0x00080605;
                DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            }
            catch { }
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

            RectangleF track = new RectangleF(0f, 0f, Width - 1f, Height - 1f);
            float radius = track.Height / 2f;
            using (GraphicsPath trackPath = CreateCapsule(track, radius))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(255, 23, 24, 33)))
            using (Pen borderPen = new Pen(Color.FromArgb(255, 36, 39, 52), 1f))
            {
                g.FillPath(trackBrush, trackPath);
                g.DrawPath(borderPen, trackPath);
            }

            if (progressValue > 0)
            {
                float fillW = Math.Max(track.Height, (track.Width * progressValue) / 100f);
                RectangleF fillRect = new RectangleF(0f, 0f, Math.Min(track.Width, fillW), track.Height);
                using (GraphicsPath fillPath = CreateCapsule(fillRect, radius))
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

        private static GraphicsPath CreateCapsule(RectangleF bounds, float radius)
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

    internal sealed class RoundedCardPanel : Panel
    {
        public RoundedCardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(255, 11, 12, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(255, 5, 6, 8));

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            float d = 24f;
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(255, 13, 14, 20)))
            using (Pen border = new Pen(Color.FromArgb(255, 34, 37, 48), 1f))
            {
                path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
                path.CloseFigure();
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
        private readonly Button installBtn;
        private readonly Button cancelBtn;
        private readonly string installDir;
        private bool isInstalling;

        public InstallerForm()
        {
            installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WonderWatch");

            Text = "WonderWatch Desktop Setup";
            ClientSize = new Size(496, 376);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = Color.White;

            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            Panel accentBar = new Panel();
            accentBar.SetBounds(0, 0, 496, 3);
            accentBar.BackColor = BrandLime;
            Controls.Add(accentBar);

            Panel headerPanel = new Panel();
            headerPanel.SetBounds(0, 3, 496, 94);
            headerPanel.BackColor = Color.FromArgb(255, 10, 11, 15);
            Controls.Add(headerPanel);

            PictureBox badge = new PictureBox();
            badge.SetBounds(28, 22, 50, 50);
            badge.Image = CreatePlayBadgeBitmap(50);
            badge.SizeMode = PictureBoxSizeMode.Normal;
            headerPanel.Controls.Add(badge);

            Label wonderLbl = new Label();
            wonderLbl.Text = "WONDER";
            wonderLbl.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            wonderLbl.ForeColor = Color.White;
            wonderLbl.AutoSize = true;
            wonderLbl.Location = new Point(92, 22);
            headerPanel.Controls.Add(wonderLbl);

            Label watchLbl = new Label();
            watchLbl.Text = "WATCH";
            watchLbl.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            watchLbl.ForeColor = BrandLime;
            watchLbl.AutoSize = true;
            watchLbl.Location = new Point(188, 22);
            headerPanel.Controls.Add(watchLbl);

            Label subTitle = new Label();
            subTitle.Text = "Desktop Installer • v2.4.0 (Windows x64) • Built-in Shield";
            subTitle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            subTitle.ForeColor = TextMuted;
            subTitle.SetBounds(94, 51, 370, 20);
            headerPanel.Controls.Add(subTitle);

            RoundedCardPanel optionsCard = new RoundedCardPanel();
            optionsCard.SetBounds(24, 112, 448, 138);
            Controls.Add(optionsCard);

            Label pathLabel = new Label();
            pathLabel.Text = "Destination:  " + installDir;
            pathLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            pathLabel.ForeColor = TextMuted;
            pathLabel.BackColor = CardDark;
            pathLabel.AutoEllipsis = true;
            pathLabel.SetBounds(18, 16, 412, 20);
            optionsCard.Controls.Add(pathLabel);

            desktopShortcutBox = new CheckBox();
            desktopShortcutBox.Text = "Create Desktop shortcut";
            desktopShortcutBox.Checked = true;
            desktopShortcutBox.BackColor = CardDark;
            desktopShortcutBox.ForeColor = Color.White;
            desktopShortcutBox.Font = new Font("Segoe UI", 9.5f);
            desktopShortcutBox.SetBounds(18, 44, 340, 26);
            optionsCard.Controls.Add(desktopShortcutBox);

            startMenuShortcutBox = new CheckBox();
            startMenuShortcutBox.Text = "Add to Windows Start Menu";
            startMenuShortcutBox.Checked = true;
            startMenuShortcutBox.BackColor = CardDark;
            startMenuShortcutBox.ForeColor = Color.White;
            startMenuShortcutBox.Font = new Font("Segoe UI", 9.5f);
            startMenuShortcutBox.SetBounds(18, 72, 340, 26);
            optionsCard.Controls.Add(startMenuShortcutBox);

            launchAfterBox = new CheckBox();
            launchAfterBox.Text = "Launch WonderWatch after installation";
            launchAfterBox.Checked = true;
            launchAfterBox.BackColor = CardDark;
            launchAfterBox.ForeColor = Color.White;
            launchAfterBox.Font = new Font("Segoe UI", 9.5f);
            launchAfterBox.SetBounds(18, 100, 340, 26);
            optionsCard.Controls.Add(launchAfterBox);

            statusLabel = new Label();
            statusLabel.Text = "Ready to install WonderWatch Desktop.";
            statusLabel.ForeColor = TextMuted;
            statusLabel.Font = new Font("Segoe UI", 8.5f);
            statusLabel.SetBounds(26, 266, 360, 20);
            Controls.Add(statusLabel);

            percentLabel = new Label();
            percentLabel.Text = "0%";
            percentLabel.ForeColor = BrandLime;
            percentLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            percentLabel.TextAlign = ContentAlignment.MiddleRight;
            percentLabel.SetBounds(400, 266, 70, 20);
            Controls.Add(percentLabel);

            progressBar = new NeonProgressBar();
            progressBar.SetBounds(26, 290, 444, 10);
            Controls.Add(progressBar);

            cancelBtn = new Button();
            cancelBtn.Text = "Cancel";
            cancelBtn.FlatStyle = FlatStyle.Flat;
            cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 42, 46, 58);
            cancelBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 32, 34, 44);
            cancelBtn.BackColor = SurfaceDark;
            cancelBtn.ForeColor = TextMuted;
            cancelBtn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            cancelBtn.Cursor = Cursors.Hand;
            cancelBtn.SetBounds(244, 318, 98, 38);
            cancelBtn.Click += delegate { if (!isInstalling) Close(); };
            Controls.Add(cancelBtn);

            installBtn = new Button();
            installBtn.Text = "Install";
            installBtn.FlatStyle = FlatStyle.Flat;
            installBtn.FlatAppearance.BorderSize = 0;
            installBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 106, 255, 127);
            installBtn.BackColor = BrandLime;
            installBtn.ForeColor = Color.Black;
            installBtn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            installBtn.Cursor = Cursors.Hand;
            installBtn.SetBounds(352, 318, 118, 38);
            installBtn.Click += async delegate { await RunInstallAsync(); };
            Controls.Add(installBtn);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkTitleBar(Handle);
        }

        private async Task RunInstallAsync()
        {
            if (isInstalling) return;
            isInstalling = true;
            installBtn.Enabled = false;
            cancelBtn.Enabled = false;
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

            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WonderWatch");
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
                using (GraphicsPath path = new GraphicsPath())
                using (LinearGradientBrush brush = new LinearGradientBrush(new Point(0, size), new Point(size, 0), Color.FromArgb(255, 34, 204, 55), Color.FromArgb(255, 158, 255, 171)))
                {
                    path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
                    path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
                    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
                    path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
                    path.CloseFigure();
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
