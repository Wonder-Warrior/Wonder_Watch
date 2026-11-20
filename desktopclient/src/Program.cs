using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("WonderWatch Desktop")]
[assembly: AssemblyDescription("WonderWatch Desktop Client")]
[assembly: AssemblyProduct("WonderWatch Desktop")]


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
        public const int CMD_CHECK_UPDATES = 0x1005;

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
        private readonly AnimatedEmblemSpinner splashSpinner;
        private readonly Label splashStatusLabel;
        private readonly string startUrl;
        private readonly string appDataDir;
        private readonly string userDataDir;
        private readonly string smokeTestLogPath;

        private CoreWebView2Environment sharedEnvironment;
        private string ublockExtensionId;
        private readonly DiscordRpcClient discordRpc = new DiscordRpcClient();
        private bool isInitialized;
        private bool isFullscreen;
        private bool isAppFullscreen;
        private bool isElementFullscreen;
        private bool isWebAutoFullscreen;
        private FormWindowState prevWindowState = FormWindowState.Normal;
        private FormBorderStyle prevBorderStyle = FormBorderStyle.Sizable;
        private Rectangle prevBounds;

        private static string SanitizeStartUrl(string initialUrl)
        {
            if (string.IsNullOrWhiteSpace(initialUrl)) return DefaultHomeUrl;
            try
            {
                Uri u = new Uri(initialUrl);
                if (u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("wonder-warrior.github.io", StringComparison.OrdinalIgnoreCase))
                {
                    return u.AbsoluteUri;
                }
                if ((u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) &&
                    (u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || u.Host == "127.0.0.1"))
                {
                    return u.AbsoluteUri;
                }
            }
            catch { }
            return DefaultHomeUrl;
        }

        private static void EnsureHighPerformanceGpuPreference()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                {
                    if (key != null)
                    {
                        key.SetValue(Application.ExecutablePath, "GpuPreference=2;", Microsoft.Win32.RegistryValueKind.String);
                    }
                }
            }
            catch { }
        }

        private void CheckForUpdatesAsync(bool manual)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                Version localVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                Version local3 = new Version(localVer.Major, localVer.Minor, Math.Max(0, localVer.Build));
                try
                {
                    try { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)12288; }
                    catch { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; }

                    string json = null;
                    Exception lastErr = null;
                    string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                    string[] urls = new string[]
                    {
                        "https://wonder-warrior.github.io/Wonder_Watch/version.json?t=" + ts,
                        "https://raw.githubusercontent.com/wonder-warrior/Wonder_Watch/main/version.json?t=" + ts
                    };
                    foreach (string url in urls)
                    {
                        try
                        {
                            using (var wc = new System.Net.WebClient())
                            {
                                wc.Encoding = System.Text.Encoding.UTF8;
                                wc.Headers.Add("User-Agent", "WonderWatchDesktop");
                                wc.Headers.Add("Cache-Control", "no-cache");
                                json = wc.DownloadString(url);
                                if (!string.IsNullOrWhiteSpace(json)) break;
                            }
                        }
                        catch (Exception ex)
                        {
                            lastErr = ex;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        if (manual && !IsDisposed)
                        {
                            string errText = lastErr != null ? lastErr.Message : "Network error";
                            BeginInvoke((Action)(() => MessageBox.Show(this, "Unable to check for updates right now.\n\nCurrent version: v" + local3.ToString(3) + "\nError: " + errText, "WonderWatch Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                        }
                        return;
                    }

                    var verMatch = System.Text.RegularExpressions.Regex.Match(json, @"""desktop""\s*:\s*\{[^}]*""version""\s*:\s*""([0-9]+\.[0-9]+\.[0-9]+)""");
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(json, @"""desktop""\s*:\s*\{[^}]*""exeUrl""\s*:\s*""([^""]+)""");
                    if (!verMatch.Success || !urlMatch.Success)
                    {
                        if (manual && !IsDisposed)
                        {
                            BeginInvoke((Action)(() => MessageBox.Show(this, "Current version: v" + local3.ToString(3) + "\nLatest version: Unable to read version.json", "WonderWatch Updates", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                        }
                        return;
                    }

                    Version remoteVer = new Version(verMatch.Groups[1].Value);
                    if (remoteVer <= local3)
                    {
                        if (manual && !IsDisposed)
                        {
                            BeginInvoke((Action)(() => MessageBox.Show(this, "You are up to date!\n\nCurrent version: v" + local3.ToString(3) + "\nLatest version: v" + remoteVer.ToString(3), "WonderWatch Updates", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                        }
                        return;
                    }

                    string exeUrl = urlMatch.Groups[1].Value;
                    if (!IsDisposed)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            var res = MessageBox.Show(
                                this,
                                "A new version of WonderWatch Desktop is available!\n\nCurrent version: v" + local3.ToString(3) + "\nLatest version: v" + remoteVer.ToString(3) + "\n\nWould you like to update now?",
                                "WonderWatch Update Available",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information
                            );
                            if (res != DialogResult.Yes) return;

                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {
                                    string currentExe = Application.ExecutablePath;
                                    string newExe = currentExe + ".new";
                                    using (var dl = new System.Net.WebClient())
                                    {
                                        dl.Headers.Add("User-Agent", "WonderWatchDesktop");
                                        dl.DownloadFile(exeUrl, newExe);
                                    }
                                    if (!File.Exists(newExe) || new FileInfo(newExe).Length < 50000)
                                    {
                                        if (!IsDisposed)
                                        {
                                            BeginInvoke((Action)(() => MessageBox.Show(this, "Downloaded update file was incomplete. Please try again.", "WonderWatch Update", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                                        }
                                        return;
                                    }

                                    string cmd = "/c ping 127.0.0.1 -n 2 > nul & move /y \"" + newExe + "\" \"" + currentExe + "\" & start \"\" \"" + currentExe + "\"";
                                    Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                                    {
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    });
                                    BeginInvoke((Action)(() => Application.Exit()));
                                }
                                catch (Exception ex)
                                {
                                    if (!IsDisposed)
                                    {
                                        BeginInvoke((Action)(() => MessageBox.Show(this, "Update failed: " + ex.Message, "WonderWatch Update", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                                    }
                                }
                            });
                        }));
                    }
                }
                catch (Exception ex)
                {
                    if (manual && !IsDisposed)
                    {
                        BeginInvoke((Action)(() => MessageBox.Show(this, "Unable to check for updates right now.\n\nCurrent version: v" + local3.ToString(3) + "\nError: " + ex.Message, "WonderWatch Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                    }
                }
            });
        }

        public MainForm(string initialUrl, string customProfileDir, string smokeLogPath)
        {
            startUrl = SanitizeStartUrl(initialUrl);
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
            else
            {
                EnsureHighPerformanceGpuPreference();
                Shown += (s, e) => CheckForUpdatesAsync(false);
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

            // Dark startup splash matching mobile animated loading emblem
            splashPanel = new Panel();
            splashPanel.Dock = DockStyle.Fill;
            splashPanel.BackColor = BgDark;

            Panel centerBox = new Panel();
            centerBox.Size = new Size(340, 210);
            centerBox.BackColor = BgDark;

            splashSpinner = new AnimatedEmblemSpinner();
            splashSpinner.Size = new Size(108, 108);
            splashSpinner.Location = new Point(116, 4);

            Panel brandRow = new Panel();
            brandRow.Size = new Size(340, 34);
            brandRow.Location = new Point(0, 124);
            brandRow.BackColor = BgDark;

            Label wonderLabel = new Label();
            wonderLabel.Text = "WONDER";
            wonderLabel.ForeColor = Color.White;
            wonderLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            wonderLabel.AutoSize = true;

            Label watchLabel = new Label();
            watchLabel.Text = "WATCH";
            watchLabel.ForeColor = BrandLime;
            watchLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            watchLabel.AutoSize = true;

            brandRow.Controls.Add(wonderLabel);
            brandRow.Controls.Add(watchLabel);
            brandRow.Layout += delegate
            {
                int totalW = wonderLabel.Width + watchLabel.Width - 4;
                int startX = Math.Max(0, (brandRow.Width - totalW) / 2);
                wonderLabel.Location = new Point(startX, 2);
                watchLabel.Location = new Point(startX + wonderLabel.Width - 4, 2);
            };

            splashStatusLabel = new Label();
            splashStatusLabel.Text = "Initializing WonderWatch...";
            splashStatusLabel.ForeColor = Color.FromArgb(255, 161, 161, 170);
            splashStatusLabel.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            splashStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            splashStatusLabel.SetBounds(0, 164, 340, 24);

            centerBox.Controls.Add(splashSpinner);
            centerBox.Controls.Add(brandRow);
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
            discordRpc.Dispose();
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
                if (cmd == NativeMethods.CMD_CHECK_UPDATES)
                {
                    CheckForUpdatesAsync(true);
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
                NativeMethods.AppendMenu(sysMenu, NativeMethods.MF_STRING, NativeMethods.CMD_CHECK_UPDATES, "Check for Updates...");
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
                options.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-features=OverscrollHistoryNavigation --force_high_performance_gpu";

                sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataDir, options);
                await webView.EnsureCoreWebView2Async(sharedEnvironment);

                ConfigureWebViewSettings();
                WireWebViewEvents();

                await EnsureUBlockOriginActivatedAsync();

                // Identify Desktop Client to WonderWatch frontend, enforce app-like selection, & hide Apps links
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "(function(){" +
                    "window.__WONDERWATCH_DESKTOP__=true;" +
                    "try{" +
                    "var g=document.createElement('style');" +
                    "g.textContent='html,body{overscroll-behavior-x:none!important;}';" +
                    "(document.head||document.documentElement).appendChild(g);" +
                    "}catch(e){}" +
                    "try{if(location.hostname.indexOf('github.io')!==-1||location.hostname==='localhost'||location.hostname==='127.0.0.1'||location.protocol==='file:'){" +
                    "localStorage.setItem('wonderwatch_ublock_dismissed','true');localStorage.setItem('uBlockPromptDismissed','true');" +
                    "var s=document.createElement('style');" +
                    "s.textContent='*,*::before,*::after{-webkit-user-select:none!important;user-select:none!important;}input,textarea,[contenteditable=\"true\"]{-webkit-user-select:text!important;user-select:text!important;}#navAppsLink,#mobileNavAppsLink,a[href=\"downloads.html\"]{display:none!important;}';" +
                    "(document.head||document.documentElement).appendChild(s);" +
                    "}}catch(e){}" +
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

        private static void ApplyAppWebViewSettings(CoreWebView2Settings settings)
        {
            if (settings == null) return;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.IsBuiltInErrorPageEnabled = true;
            settings.IsWebMessageEnabled = true;
        }

        private void ConfigureWebViewSettings()
        {
            CoreWebView2Settings settings = webView.CoreWebView2.Settings;
            ApplyAppWebViewSettings(settings);
            try
            {
                if (!string.IsNullOrEmpty(settings.UserAgent) && settings.UserAgent.IndexOf("WonderWatchDesktop", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    settings.UserAgent = settings.UserAgent + " WonderWatchDesktop/2.4";
                }
            }
            catch { }
        }

        private void WireWebViewEvents()
        {
            webView.CoreWebView2.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs args)
            {
                try
                {
                    string msg = null;
                    try { msg = args.TryGetWebMessageAsString(); } catch { }
                    string json = args.WebMessageAsJson ?? "";
                    if (msg == "ww:check_updates" || json.IndexOf("ww:check_updates", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        CheckForUpdatesAsync(true);
                    }
                    else if (msg == "ww:enter_fullscreen" || json.IndexOf("ww:enter_fullscreen", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isWebAutoFullscreen = true;
                        if (!IsDisposed)
                        {
                            BeginInvoke((Action)(() => SetFullscreen(isAppFullscreen || isElementFullscreen || isWebAutoFullscreen)));
                        }
                    }
                    else if (msg == "ww:exit_fullscreen" || json.IndexOf("ww:exit_fullscreen", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isWebAutoFullscreen = false;
                        if (!IsDisposed)
                        {
                            BeginInvoke((Action)(() => SetFullscreen(isAppFullscreen || isElementFullscreen)));
                        }
                    }
                }
                catch { }
            };
            webView.CoreWebView2.ContainsFullScreenElementChanged += delegate
            {
                isElementFullscreen = webView.CoreWebView2.ContainsFullScreenElement;
                if (!isElementFullscreen)
                {
                    SetFullscreen(isAppFullscreen || isWebAutoFullscreen);
                }
                else
                {
                    System.Threading.Tasks.Task.Delay(50).ContinueWith(_ =>
                    {
                        if (!IsDisposed)
                        {
                            BeginInvoke((Action)(() => SetFullscreen(isAppFullscreen || isElementFullscreen || isWebAutoFullscreen)));
                        }
                    });
                }
            };
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
                UpdateDiscordPresence();
            };

            webView.CoreWebView2.NavigationCompleted += delegate
            {
                if (splashPanel.Visible)
                {
                    splashSpinner.Stop();
                    splashPanel.Visible = false;
                }
                UpdateDiscordPresence();
            };



            webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F12 || keyData == (Keys.Control | Keys.Shift | Keys.I) || keyData == (Keys.Control | Keys.Shift | Keys.J))
            {
                return true;
            }
            if (keyData == Keys.F11)
            {
                ToggleFullscreen();
                return true;
            }
            if (keyData == Keys.Escape && isFullscreen && (webView.CoreWebView2 == null || !webView.CoreWebView2.ContainsFullScreenElement))
            {
                if (isWebAutoFullscreen)
                {
                    isWebAutoFullscreen = false;
                    try
                    {
                        if (webView.CoreWebView2 != null)
                        {
                            webView.CoreWebView2.ExecuteScriptAsync("if(typeof window.__wwOnExitFullscreen==='function')window.__wwOnExitFullscreen();");
                        }
                    }
                    catch { }
                    SetFullscreen(isAppFullscreen || isElementFullscreen);
                }
                else
                {
                    SetFullscreen(false);
                }
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

        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            string uri = e.Uri ?? string.Empty;

            // Allow legitimate OAuth / Firebase Authentication popups with real window linkage
            if (IsTrustedAuthPopup(uri))
            {
                CoreWebView2Deferral deferral = e.GetDeferral();
                try
                {
                    CoreWebView2 popupCore = await OpenAuthPopupWindowAsync();
                    if (popupCore != null)
                    {
                        e.NewWindow = popupCore;
                    }
                    e.Handled = true;
                }
                finally
                {
                    deferral.Complete();
                }
                return;
            }

            // If a user explicitly initiated a navigation to GitHub / docs, open in system browser
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

        private async Task<CoreWebView2> OpenAuthPopupWindowAsync()
        {
            if (sharedEnvironment == null) return null;

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

            popup.Show(this);

            try
            {
                await popupWebView.EnsureCoreWebView2Async(sharedEnvironment);
                ApplyAppWebViewSettings(popupWebView.CoreWebView2.Settings);
                popupWebView.CoreWebView2.WindowCloseRequested += delegate { popup.Close(); };
                return popupWebView.CoreWebView2;
            }
            catch
            {
                popup.Close();
                return null;
            }
        }

        private void UpdateDiscordPresence()
        {
            try
            {
                if (webView == null || webView.CoreWebView2 == null) return;
                string source = webView.CoreWebView2.Source ?? string.Empty;
                if (source.IndexOf("watch.html", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Uri parsed = new Uri(source);
                    string rawTitle = ExtractQueryParam(parsed.Query, "title");
                    string rawEp = ExtractQueryParam(parsed.Query, "ep");
                    string animeName = !string.IsNullOrEmpty(rawTitle) ? Uri.UnescapeDataString(rawTitle.Replace('+', ' ')) : "Anime";
                    string epLabel = !string.IsNullOrEmpty(rawEp) ? "Episode " + rawEp : "Streaming HD";
                    discordRpc.UpdatePresence("Watching " + animeName, epLabel);
                }
                else
                {
                    discordRpc.UpdatePresence("Browsing Anime Catalog", "WonderWatch Desktop");
                }
            }
            catch
            {
            }
        }

        private static string ExtractQueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            string trimmed = query.StartsWith("?") ? query.Substring(1) : query;
            string[] pairs = trimmed.Split('&');
            foreach (string pair in pairs)
            {
                int idx = pair.IndexOf('=');
                if (idx <= 0) continue;
                string k = pair.Substring(0, idx);
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Substring(idx + 1);
                }
            }
            return null;
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
                    ApplyAppWebViewSettings(dashView.CoreWebView2.Settings);
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
            isAppFullscreen = !isAppFullscreen;
            SetFullscreen(isAppFullscreen || isElementFullscreen || isWebAutoFullscreen);
        }

        private void SetFullscreen(bool enable)
        {
            if (isFullscreen == enable) return;
            isFullscreen = enable;

            SuspendLayout();
            if (enable)
            {
                prevWindowState = WindowState;
                prevBorderStyle = FormBorderStyle;
                prevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

                FormBorderStyle = FormBorderStyle.None;
                if (WindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Normal;
                }
                Bounds = Screen.FromControl(this).Bounds;
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
            ResumeLayout(true);
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

        private sealed class AnimatedEmblemSpinner : Control
        {
            private readonly System.Windows.Forms.Timer timer;
            private readonly Stopwatch clock = Stopwatch.StartNew();

            public AnimatedEmblemSpinner()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.SupportsTransparentBackColor,
                    true
                );
                BackColor = BgDark;
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 16;
                timer.Tick += delegate { Invalidate(); };
                timer.Start();
            }

            public void Stop()
            {
                timer.Stop();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    timer.Stop();
                    timer.Dispose();
                }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(BgDark);

                double secs = clock.Elapsed.TotalSeconds;
                float angle = (float)((secs * 327.0) % 360.0);
                float pulseScale = 1.0f + 0.04f * (float)Math.Sin(secs * Math.PI / 0.9);

                int w = Width;
                int h = Height;
                float cx = w / 2f;
                float cy = h / 2f;

                // Soft ambient lime glow
                float glowRadius = (w * 0.38f) * pulseScale;
                using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(28, 59, 255, 83)))
                {
                    g.FillEllipse(glowBrush, cx - glowRadius, cy - glowRadius, glowRadius * 2f, glowRadius * 2f);
                }

                // Track ring & rotating neon orbital arc
                float pad = 7f;
                RectangleF ringRect = new RectangleF(pad, pad, w - pad * 2f, h - pad * 2f);
                using (Pen trackPen = new Pen(Color.FromArgb(255, 29, 33, 43), 3f))
                {
                    g.DrawEllipse(trackPen, ringRect);
                }

                using (LinearGradientBrush arcBrush = new LinearGradientBrush(
                    new PointF(0f, h),
                    new PointF(w, 0f),
                    Color.FromArgb(255, 34, 204, 55),
                    Color.FromArgb(255, 158, 255, 171)))
                using (Pen arcPen = new Pen(arcBrush, 3f))
                {
                    arcPen.StartCap = LineCap.Round;
                    arcPen.EndCap = LineCap.Round;
                    g.DrawArc(arcPen, ringRect, angle - 90f, 250f);
                }

                // Pulsing WonderWatch lime badge (sub-pixel float geometry to prevent 1px snapping jitter)
                float badgeSize = (w * 0.58f) * pulseScale;
                float left = cx - badgeSize / 2f;
                float top = cy - badgeSize / 2f;
                RectangleF rect = new RectangleF(left, top, badgeSize, badgeSize);
                float cornerRadius = Math.Max(4f, badgeSize * 0.27f);

                using (GraphicsPath path = CreateRoundedRectF(rect, cornerRadius))
                using (LinearGradientBrush badgeBrush = new LinearGradientBrush(
                    new PointF(left, top + badgeSize),
                    new PointF(left + badgeSize, top),
                    Color.FromArgb(255, 34, 204, 55),
                    Color.FromArgb(255, 158, 255, 171)))
                {
                    g.FillPath(badgeBrush, path);
                }

                PointF[] triangle = new PointF[]
                {
                    new PointF(left + badgeSize * 0.39f, top + badgeSize * 0.30f),
                    new PointF(left + badgeSize * 0.72f, top + badgeSize * 0.50f),
                    new PointF(left + badgeSize * 0.39f, top + badgeSize * 0.70f)
                };
                using (SolidBrush innerBrush = new SolidBrush(Color.FromArgb(255, 8, 10, 12)))
                using (Pen innerPen = new Pen(Color.FromArgb(255, 8, 10, 12), 2.5f))
                {
                    innerPen.LineJoin = LineJoin.Round;
                    g.FillPolygon(innerBrush, triangle);
                    g.DrawPolygon(innerPen, triangle);
                }
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
            return CreateRoundedRectF(bounds, radius);
        }

        private static GraphicsPath CreateRoundedRectF(RectangleF bounds, float radius)
        {
            float d = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180f, 90f);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270f, 90f);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class DiscordRpcClient : IDisposable
    {
        private const string DefaultClientId = "1572722491514691694";
        private readonly string clientId;
        private readonly long sessionStartEpoch;
        private readonly object syncLock = new object();
        private NamedPipeClientStream pipe;
        private bool handshaken;
        private bool disposed;

        public DiscordRpcClient()
        {
            sessionStartEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string customId = Environment.GetEnvironmentVariable("WONDERWATCH_DISCORD_CLIENT_ID");
            if (string.IsNullOrWhiteSpace(customId))
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discord_client_id.txt");
                if (File.Exists(cfg))
                {
                    try { customId = File.ReadAllText(cfg).Trim(); } catch { }
                }
            }
            clientId = string.IsNullOrWhiteSpace(customId) ? DefaultClientId : customId.Trim();
        }

        public void UpdatePresence(string details, string state)
        {
            if (disposed) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (syncLock)
                {
                    if (disposed) return;
                    try
                    {
                        if (!EnsureConnected()) return;
                        int pid = Process.GetCurrentProcess().Id;
                        string json = string.Format(
                            "{{\"cmd\":\"SET_ACTIVITY\",\"args\":{{\"pid\":{0},\"activity\":{{\"details\":\"{1}\",\"state\":\"{2}\",\"timestamps\":{{\"start\":{3}}}}}}},\"nonce\":\"{4}\"}}",
                            pid,
                            EscapeJson(details),
                            EscapeJson(state),
                            sessionStartEpoch,
                            Guid.NewGuid().ToString("N")
                        );
                        WriteFrame(1, json);
                        // Read response or acknowledge frame
                        ReadFrameNonBlocking();
                    }
                    catch
                    {
                        ResetPipe();
                    }
                }
            });
        }

        private bool EnsureConnected()
        {
            if (pipe != null && pipe.IsConnected && handshaken) return true;
            ResetPipe();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    NamedPipeClientStream candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
                    candidate.Connect(150);
                    pipe = candidate;

                    // Handshake opcode 0
                    WriteFrame(0, string.Format("{{\"v\":1,\"client_id\":\"{0}\"}}", EscapeJson(clientId)));

                    // Read READY opcode 1 from Discord
                    byte[] header = new byte[8];
                    int read = ReadExact(header, 0, 8);
                    if (read == 8)
                    {
                        int opcode = BitConverter.ToInt32(header, 0);
                        int length = BitConverter.ToInt32(header, 4);
                        if (length > 0 && length < 65536)
                        {
                            byte[] payload = new byte[length];
                            ReadExact(payload, 0, length);
                        }
                        if (opcode == 1) // DISPATCH / READY
                        {
                            handshaken = true;
                            return true;
                        }
                    }
                    ResetPipe();
                }
                catch
                {
                    ResetPipe();
                }
            }
            return false;
        }

        private int ReadExact(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = pipe.Read(buffer, offset + total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private void ReadFrameNonBlocking()
        {
            try
            {
                if (pipe == null || !pipe.IsConnected) return;
                byte[] header = new byte[8];
                int read = ReadExact(header, 0, 8);
                if (read == 8)
                {
                    int length = BitConverter.ToInt32(header, 4);
                    if (length > 0 && length < 65536)
                    {
                        byte[] payload = new byte[length];
                        ReadExact(payload, 0, length);
                    }
                }
            }
            catch { }
        }

        private void WriteFrame(int opcode, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];
            byte[] opBytes = BitConverter.GetBytes(opcode);
            byte[] lenBytes = BitConverter.GetBytes(payload.Length);
            Buffer.BlockCopy(opBytes, 0, header, 0, 4);
            Buffer.BlockCopy(lenBytes, 0, header, 4, 4);
            pipe.Write(header, 0, 8);
            pipe.Write(payload, 0, payload.Length);
            pipe.Flush();
        }

        private void ResetPipe()
        {
            handshaken = false;
            if (pipe != null)
            {
                try { pipe.Dispose(); } catch { }
                pipe = null;
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        public void Dispose()
        {
            lock (syncLock)
            {
                disposed = true;
                ResetPipe();
            }
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
