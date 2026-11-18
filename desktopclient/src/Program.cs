using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("WonderWatch Desktop")]
[assembly: AssemblyDescription("WonderWatch Desktop Client")]
[assembly: AssemblyProduct("WonderWatch Desktop")]
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]

namespace WonderWatchDesktop
{
    internal static class NativeMethods
    {
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_CAPTION_COLOR = 35;

        public const int WM_SYSCOMMAND = 0x0112;
        public const int MF_SEPARATOR = 0x0800;
        public const int MF_STRING = 0x0000;

        public const int CMD_SHIELD_SETTINGS = 0x1001;
        public const int CMD_HOME = 0x1002;
        public const int CMD_RELOAD = 0x1003;
        public const int CMD_FULLSCREEN = 0x1004;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

        public static void ApplyDarkTitleBar(IntPtr handle)
        {
            try
            {
                int darkMode = 1;
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref darkMode, sizeof(int));
                }
                // Match #050608 surface-950 caption color (Win32 COLORREF is 0x00BBGGRR)
                int captionColor = 0x00080605;
                DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                // Match #171821 surface-800 border color
                int borderColor = 0x00211817;
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }
            catch
            {
                // Non-critical on older Windows 10 builds
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const string DefaultHomeUrl = "https://wonder-warrior.github.io/Wonder_Watch/";
        private static readonly Color BgDark = Color.FromArgb(255, 5, 6, 8);       // #050608
        private static readonly Color BrandLime = Color.FromArgb(255, 59, 255, 83); // #3BFF53

        private readonly WebView2 webView;
        private readonly Panel splashPanel;
        private readonly Label splashStatusLabel;
        private readonly string startUrl;
        private readonly string appDataDir;
        private readonly string userDataDir;
        private readonly string smokeTestLogPath;

        private CoreWebView2Environment sharedEnvironment;
        private string ublockExtensionId;
        private bool isInitialized;
        private bool isFullscreen;
        private FormWindowState prevWindowState = FormWindowState.Normal;
        private FormBorderStyle prevBorderStyle = FormBorderStyle.Sizable;
        private Rectangle prevBounds;

        public MainForm(string initialUrl, string customProfileDir, string smokeLogPath)
        {
            startUrl = string.IsNullOrWhiteSpace(initialUrl) ? DefaultHomeUrl : initialUrl;
            appDataDir = string.IsNullOrWhiteSpace(customProfileDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WonderWatchDesktop")
                : customProfileDir;
            userDataDir = Path.Combine(appDataDir, "UserData");
            smokeTestLogPath = smokeLogPath;

            if (!string.IsNullOrEmpty(smokeTestLogPath))
            {
                ShowInTaskbar = false;
                Opacity = 0;
            }

            Text = "WonderWatch";
            BackColor = BgDark;
            MinimumSize = new Size(480, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1340, 840);
            KeyPreview = true;

            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null)
                {
                    Icon = exeIcon;
                }
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(smokeTestLogPath))
            {
                RestoreWindowState();
            }

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = BgDark;
            Controls.Add(webView);

            // Dark startup splash shown while engine initializes
            splashPanel = new Panel();
            splashPanel.Dock = DockStyle.Fill;
            splashPanel.BackColor = BgDark;

            Panel centerBox = new Panel();
            centerBox.Size = new Size(340, 180);
            centerBox.BackColor = BgDark;

            PictureBox logoBox = new PictureBox();
            logoBox.Size = new Size(64, 64);
            logoBox.Location = new Point(138, 12);
            logoBox.Image = CreatePlayBadgeBitmap(64);
            logoBox.SizeMode = PictureBoxSizeMode.Normal;

            Label titleLabel = new Label();
            titleLabel.Text = "WONDERWATCH";
            titleLabel.ForeColor = Color.White;
            titleLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            titleLabel.TextAlign = ContentAlignment.MiddleCenter;
            titleLabel.SetBounds(0, 88, 340, 32);

            splashStatusLabel = new Label();
            splashStatusLabel.Text = "Initializing WonderWatch...";
            splashStatusLabel.ForeColor = Color.FromArgb(255, 161, 161, 170);
            splashStatusLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            splashStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            splashStatusLabel.SetBounds(0, 124, 340, 24);

            centerBox.Controls.Add(logoBox);
            centerBox.Controls.Add(titleLabel);
            centerBox.Controls.Add(splashStatusLabel);

            splashPanel.Controls.Add(centerBox);
            splashPanel.Resize += delegate
            {
                centerBox.Location = new Point(
                    Math.Max(0, (splashPanel.Width - centerBox.Width) / 2),
                    Math.Max(0, (splashPanel.Height - centerBox.Height) / 2)
                );
            };

            Controls.Add(splashPanel);
            splashPanel.BringToFront();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkTitleBar(Handle);
            InstallSystemMenuItems();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!isInitialized)
            {
                isInitialized = true;
                await InitializeWebViewAsync();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveWindowState();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SYSCOMMAND)
            {
                int cmd = m.WParam.ToInt32() & 0xFFF0;
                if (cmd == NativeMethods.CMD_SHIELD_SETTINGS)
                {
                    OpenShieldDashboard();
                    return;
                }
                if (cmd == NativeMethods.CMD_HOME)
                {
                    NavigateHome();
                    return;
                }
                if (cmd == NativeMethods.CMD_RELOAD)
                {
                    ReloadPage();
                    return;
                }
                if (cmd == NativeMethods.CMD_FULLSCREEN)
                {
                    ToggleFullscreen();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void InstallSystemMenuItems()
        {
            try
            {
                IntPtr sysMenu = NativeMethods.GetSystemMenu(Handle, false);
                if (sysMenu == IntPtr.Zero) return;
                NativeMethods.AppendMenu(sysMenu, NativeMethods.MF_SEPARATOR, 0, null);
                NativeMethods.AppendMenu(sysMenu, NativeMethods.MF_STRING, NativeMethods.CMD_SHIELD_SETTINGS, "Shield Settings\tCtrl+Shift+U");
                NativeMethods.AppendMenu(sysMenu, NativeMethods.MF_STRING, NativeMethods.CMD_HOME, "WonderWatch Home\tAlt+H");
                NativeMethods.AppendMenu(sysMenu, NativeMethods.MF_STRING, NativeMethods.CMD_RELOAD, "Reload\tF5");
                NativeMethods.AppendMenu(sysMenu, NativeMethods.MF_STRING, NativeMethods.CMD_FULLSCREEN, "Toggle Fullscreen\tF11");
            }
            catch
            {
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                Directory.CreateDirectory(userDataDir);

                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
                options.AreBrowserExtensionsEnabled = true;
                options.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";

                sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataDir, options);
                await webView.EnsureCoreWebView2Async(sharedEnvironment);

                ConfigureWebViewSettings();
                WireWebViewEvents();

                await EnsureUBlockOriginActivatedAsync();

                // Identify Desktop Client to WonderWatch frontend & suppress web extension prompts
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "(function(){" +
                    "window.__WONDERWATCH_DESKTOP__=true;" +
                    "try{if(location.hostname.indexOf('github.io')!==-1||location.hostname==='localhost'||location.hostname==='127.0.0.1'||location.protocol==='file:'){" +
                    "localStorage.setItem('wonderwatch_ublock_dismissed','true');}}catch(e){}" +
                    "})();"
                );

                UpdateSplashStatus("Loading WonderWatch...");
                if (!string.IsNullOrEmpty(smokeTestLogPath))
                {
                    await RunSmokeTestAsync();
                    Close();
                    return;
                }
                webView.CoreWebView2.Navigate(startUrl);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                DialogResult res = MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime is required to run WonderWatch Desktop.\n\nWould you like to open the official Microsoft download page now?",
                    "WonderWatch Desktop - Runtime Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information
                );
                if (res == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
                    }
                    catch { }
                }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to initialize WonderWatch Desktop:\n\n" + ex.Message,
                    "WonderWatch Desktop Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Close();
            }
        }

        private void ConfigureWebViewSettings()
        {
            CoreWebView2Settings settings = webView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = true;
            settings.IsZoomControlEnabled = true;
            settings.IsBuiltInErrorPageEnabled = true;
        }

        private void WireWebViewEvents()
        {
            webView.CoreWebView2.DocumentTitleChanged += delegate
            {
                string title = webView.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    Text = title.IndexOf("WonderWatch", StringComparison.OrdinalIgnoreCase) >= 0
                        ? title
                        : title + " - WonderWatch";
                }
                else
                {
                    Text = "WonderWatch";
                }
            };

            webView.CoreWebView2.NavigationCompleted += delegate
            {
                if (splashPanel.Visible)
                {
                    splashPanel.Visible = false;
                }
            };

            webView.CoreWebView2.ContainsFullScreenElementChanged += delegate
            {
                SetFullscreen(webView.CoreWebView2.ContainsFullScreenElement);
            };

            webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                ToggleFullscreen();
                return true;
            }
            if (keyData == Keys.Escape && isFullscreen && (webView.CoreWebView2 == null || !webView.CoreWebView2.ContainsFullScreenElement))
            {
                SetFullscreen(false);
                return true;
            }
            if (keyData == Keys.F5 || keyData == (Keys.Control | Keys.R))
            {
                ReloadPage();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.U))
            {
                OpenShieldDashboard();
                return true;
            }
            if (keyData == (Keys.Alt | Keys.H))
            {
                NavigateHome();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            string uri = e.Uri ?? string.Empty;

            // Allow legitimate OAuth / Firebase Authentication popups inside a dark WebView2 popup window
            if (IsTrustedAuthPopup(uri))
            {
                e.Handled = true;
                OpenAuthPopupWindow(uri);
                return;
            }

            // If a user explicitly initiated a navigation to GitHub / uBlock / docs, open in system browser
            if (e.IsUserInitiated && IsSafeExternalLink(uri))
            {
                e.Handled = true;
                try
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                catch { }
                return;
            }

            // Block ad-network popups / redirect tabs triggered by embedded video players
            e.Handled = true;
        }

        private static bool IsTrustedAuthPopup(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return false;
            try
            {
                Uri parsed = new Uri(uri);
                string host = parsed.Host.ToLowerInvariant();
                return host.EndsWith("accounts.google.com") ||
                       host.EndsWith("firebaseapp.com") ||
                       (host.EndsWith("google.com") && parsed.AbsolutePath.IndexOf("oauth", StringComparison.OrdinalIgnoreCase) >= 0) ||
                       host.EndsWith("wonder-warrior.github.io");
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSafeExternalLink(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return false;
            try
            {
                Uri parsed = new Uri(uri);
                if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
                string host = parsed.Host.ToLowerInvariant();
                return host == "github.com" ||
                       host.EndsWith(".github.com") ||
                       host == "ublockorigin.com" ||
                       host == "addons.mozilla.org" ||
                       host == "chromewebstore.google.com" ||
                       host == "microsoftedge.microsoft.com";
            }
            catch
            {
                return false;
            }
        }

        private void OpenAuthPopupWindow(string url)
        {
            if (sharedEnvironment == null) return;

            Form popup = new Form();
            popup.Text = "WonderWatch - Account Sign In";
            popup.Size = new Size(520, 680);
            popup.StartPosition = FormStartPosition.CenterParent;
            popup.BackColor = BgDark;
            if (Icon != null) popup.Icon = Icon;

            WebView2 popupWebView = new WebView2();
            popupWebView.Dock = DockStyle.Fill;
            popupWebView.DefaultBackgroundColor = BgDark;
            popup.Controls.Add(popupWebView);

            popup.HandleCreated += delegate
            {
                NativeMethods.ApplyDarkTitleBar(popup.Handle);
            };

            popup.Shown += async delegate
            {
                try
                {
                    await popupWebView.EnsureCoreWebView2Async(sharedEnvironment);
                    popupWebView.CoreWebView2.WindowCloseRequested += delegate { popup.Close(); };
                    popupWebView.CoreWebView2.Navigate(url);
                }
                catch
                {
                    popup.Close();
                }
            };

            popup.Show(this);
        }

        private async Task EnsureUBlockOriginActivatedAsync()
        {
            string ublockPath = ResolveUBlockExtensionPath();
            if (string.IsNullOrEmpty(ublockPath))
            {
                return;
            }

            string markerPath = Path.Combine(userDataDir, ".ublock_bootstrapped");
            bool firstBoot = !File.Exists(markerPath);
            string previousPath = null;
            try
            {
                if (!firstBoot) previousPath = File.ReadAllText(markerPath).Trim();
            }
            catch { }
            bool pathChanged = !string.IsNullOrEmpty(previousPath) &&
                !string.Equals(previousPath, ublockPath, StringComparison.OrdinalIgnoreCase);

            try
            {
                var existingExtensions = await webView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
                CoreWebView2BrowserExtension ublockExt = null;

                foreach (CoreWebView2BrowserExtension ext in existingExtensions)
                {
                    if (ext.Name != null && ext.Name.IndexOf("uBlock Origin", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ublockExt = ext;
                        break;
                    }
                }

                if (ublockExt != null && pathChanged)
                {
                    await ublockExt.RemoveAsync();
                    ublockExt = null;
                    firstBoot = true;
                }

                if (ublockExt == null)
                {
                    ublockExt = await webView.CoreWebView2.Profile.AddBrowserExtensionAsync(ublockPath);
                    firstBoot = true;
                }

                if (ublockExt != null)
                {
                    ublockExtensionId = ublockExt.Id;
                    if (!ublockExt.IsEnabled)
                    {
                        await ublockExt.EnableAsync(true);
                    }
                }

                // Static declarativeNetRequest filter rulesets activate immediately on install
                if (firstBoot || pathChanged)
                {
                    await Task.Delay(150);
                    try { File.WriteAllText(markerPath, ublockPath); } catch { }
                }
            }
            catch
            {
            }
        }

        private static string ResolveUBlockExtensionPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "extensions", "ublock-origin"),
                Path.Combine(baseDir, "..", "extensions", "ublock-origin"),
                Path.Combine(Environment.CurrentDirectory, "extensions", "ublock-origin")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(Path.Combine(full, "manifest.json")))
                    {
                        return full;
                    }
                }
                catch { }
            }
            return null;
        }

        private void OpenShieldDashboard()
        {
            if (sharedEnvironment == null || string.IsNullOrEmpty(ublockExtensionId))
            {
                MessageBox.Show(
                    "Shield is still initializing.",
                    "WonderWatch Shield",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            string dashboardUrl = "chrome-extension://" + ublockExtensionId + "/dashboard.html";

            Form dashForm = new Form();
            dashForm.Text = "WonderWatch Shield Settings";
            dashForm.Size = new Size(980, 680);
            dashForm.StartPosition = FormStartPosition.CenterParent;
            dashForm.BackColor = BgDark;
            if (Icon != null) dashForm.Icon = Icon;

            WebView2 dashView = new WebView2();
            dashView.Dock = DockStyle.Fill;
            dashView.DefaultBackgroundColor = BgDark;
            dashForm.Controls.Add(dashView);

            dashForm.HandleCreated += delegate
            {
                NativeMethods.ApplyDarkTitleBar(dashForm.Handle);
            };

            dashForm.Shown += async delegate
            {
                try
                {
                    await dashView.EnsureCoreWebView2Async(sharedEnvironment);
                    dashView.CoreWebView2.Navigate(dashboardUrl);
                }
                catch { }
            };

            dashForm.Show(this);
        }

        private void NavigateHome()
        {
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Navigate(DefaultHomeUrl);
            }
        }

        private void ReloadPage()
        {
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Reload();
            }
        }

        private void ToggleFullscreen()
        {
            SetFullscreen(!isFullscreen);
        }

        private void SetFullscreen(bool enable)
        {
            if (isFullscreen == enable) return;
            isFullscreen = enable;

            if (enable)
            {
                prevWindowState = WindowState;
                prevBorderStyle = FormBorderStyle;
                prevBounds = Bounds;

                if (WindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Normal;
                }
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = prevBorderStyle;
                WindowState = prevWindowState;
                if (prevWindowState == FormWindowState.Normal && prevBounds.Width > 0 && prevBounds.Height > 0)
                {
                    Bounds = prevBounds;
                }
                NativeMethods.ApplyDarkTitleBar(Handle);
            }
        }

        private void UpdateSplashStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateSplashStatus), text);
                return;
            }
            splashStatusLabel.Text = text;
        }

        private void RestoreWindowState()
        {
            try
            {
                string cfg = Path.Combine(appDataDir, "window.cfg");
                if (!File.Exists(cfg)) return;
                string[] parts = File.ReadAllText(cfg).Trim().Split(',');
                if (parts.Length < 5) return;

                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                int w = int.Parse(parts[2]);
                int h = int.Parse(parts[3]);
                bool maximized = parts[4] == "1";

                Rectangle rect = new Rectangle(x, y, Math.Max(480, w), Math.Max(360, h));
                foreach (Screen scr in Screen.AllScreens)
                {
                    if (scr.WorkingArea.IntersectsWith(rect))
                    {
                        StartPosition = FormStartPosition.Manual;
                        Bounds = rect;
                        break;
                    }
                }
                if (maximized)
                {
                    WindowState = FormWindowState.Maximized;
                }
            }
            catch
            {
            }
        }

        private void SaveWindowState()
        {
            try
            {
                Directory.CreateDirectory(appDataDir);
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                string data = string.Format(
                    "{0},{1},{2},{3},{4}",
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    WindowState == FormWindowState.Maximized ? "1" : "0"
                );
                File.WriteAllText(Path.Combine(appDataDir, "window.cfg"), data);
            }
            catch
            {
            }
        }

        private static Bitmap CreatePlayBadgeBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                int radius = (int)(size * 0.26f);
                Rectangle rect = new Rectangle(2, 2, size - 4, size - 4);
                using (GraphicsPath path = CreateRoundedRect(rect, radius))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Point(0, size),
                    new Point(size, 0),
                    Color.FromArgb(255, 34, 204, 55),
                    Color.FromArgb(255, 158, 255, 171)))
                {
                    g.FillPath(brush, path);
                }

                PointF[] triangle = new PointF[]
                {
                    new PointF(size * 0.38f, size * 0.30f),
                    new PointF(size * 0.72f, size * 0.50f),
                    new PointF(size * 0.38f, size * 0.70f)
                };
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, 8, 10, 12)))
                {
                    g.FillPolygon(dark, triangle);
                }
            }
            return bmp;
        }

        private async Task RunSmokeTestAsync()
        {
            var sb = new System.Text.StringBuilder();
            var list = await webView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
            sb.AppendLine("ExtensionCount=" + list.Count);
            foreach (CoreWebView2BrowserExtension ext in list)
            {
                sb.AppendLine("Ext=" + ext.Name + "|Id=" + ext.Id + "|Enabled=" + ext.IsEnabled);
            }
            sb.AppendLine("MarkerExists=" + File.Exists(Path.Combine(userDataDir, ".ublock_bootstrapped")));

            await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            var reqSent = webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            reqSent.DevToolsProtocolEventReceived += delegate(object s, CoreWebView2DevToolsProtocolEventReceivedEventArgs evt)
            {
                sb.AppendLine("Request=" + evt.ParameterObjectAsJson);
            };
            var receiver = webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
            receiver.DevToolsProtocolEventReceived += delegate(object s, CoreWebView2DevToolsProtocolEventReceivedEventArgs evt)
            {
                sb.AppendLine("NetworkLoadingFailed=" + evt.ParameterObjectAsJson);
            };

            webView.CoreWebView2.NavigateToString(
                "<html><body><script src='https://doubleclick.net/test.js'></script><script src='https://popads.net/ad.js'></script></body></html>"
            );
            await Task.Delay(2500);
            File.WriteAllText(smokeTestLogPath, sb.ToString());
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string targetUrl = null;
            string customProfile = null;
            string smokeLog = null;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (arg == null) continue;
                    if (arg.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
                    {
                        targetUrl = arg.Substring("--url=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
                    {
                        customProfile = arg.Substring("--profile=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--smoke-test=", StringComparison.OrdinalIgnoreCase))
                    {
                        smokeLog = arg.Substring("--smoke-test=".Length).Trim('"');
                    }
                }
            }

            Application.Run(new MainForm(targetUrl, customProfile, smokeLog));
        }
    }
}
