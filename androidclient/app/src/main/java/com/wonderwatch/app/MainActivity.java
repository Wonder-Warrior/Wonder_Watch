package com.wonderwatch.app;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.Dialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Message;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.webkit.CookieManager;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.window.OnBackInvokedCallback;
import android.window.OnBackInvokedDispatcher;

public final class MainActivity extends Activity {

    private static final String HOME_URL = "https://wonder-warrior.github.io/Wonder_Watch/";
    private static final int BG_DARK = Color.parseColor("#050608");
    private static final int BRAND_LIME = Color.parseColor("#3BFF53");

    private FrameLayout rootContainer;
    private FrameLayout contentContainer;
    private WebView webView;
    private SplashOverlayView splashOverlay;
    private LinearLayout errorOverlay;
    private FrameLayout fullscreenContainer;
    private View customFullscreenView;
    private WebChromeClient.CustomViewCallback customViewCallback;
    private Dialog authPopupDialog;
    private final AdShieldEngine shieldEngine = new AdShieldEngine();
    private boolean initialPageRevealed = false;
    private boolean mainPageFinished = false;
    private int lastLoadProgress = 0;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Start loading shield rules immediately in parallel with WebView init
        shieldEngine.startPreload(this);

        rootContainer = new FrameLayout(this);
        rootContainer.setBackgroundColor(BG_DARK);

        // Wrapper receives system bar / cutout insets so WebView never renders under status bar
        contentContainer = new FrameLayout(this);
        contentContainer.setBackgroundColor(BG_DARK);

        webView = new WebView(this);
        webView.setBackgroundColor(BG_DARK);
        webView.setLayerType(View.LAYER_TYPE_HARDWARE, null);
        webView.setOverScrollMode(View.OVER_SCROLL_NEVER);
        webView.setVerticalScrollBarEnabled(false);
        webView.setHorizontalScrollBarEnabled(false);
        webView.setScrollBarStyle(View.SCROLLBARS_INSIDE_OVERLAY);
        configureWebSettings(webView.getSettings());

        CookieManager cookies = CookieManager.getInstance();
        cookies.setAcceptCookie(true);
        cookies.setAcceptThirdPartyCookies(webView, true);

        webView.setWebViewClient(new WonderWebViewClient());
        webView.addJavascriptInterface(new AndroidBridge(), "WonderWatchAndroid");
        webView.setWebChromeClient(new WonderWebChromeClient());

        contentContainer.addView(webView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        errorOverlay = createErrorOverlay();
        errorOverlay.setVisibility(View.GONE);
        contentContainer.addView(errorOverlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        rootContainer.addView(contentContainer, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        fullscreenContainer = new FrameLayout(this);
        fullscreenContainer.setBackgroundColor(Color.BLACK);
        fullscreenContainer.setVisibility(View.GONE);
        rootContainer.addView(fullscreenContainer, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        splashOverlay = new SplashOverlayView(this);
        rootContainer.addView(splashOverlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        applySafeSystemInsets(contentContainer);
        setContentView(rootContainer);
        configureSystemBars();
        contentContainer.requestApplyInsets();

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                OnBackInvokedDispatcher.PRIORITY_DEFAULT,
                new OnBackInvokedCallback() {
                    @Override
                    public void onBackInvoked() {
                        handleBackAction();
                    }
                }
            );
        }

        if (savedInstanceState != null) {
            webView.restoreState(savedInstanceState);
        } else {
            // Load immediately at t=0; AdShieldEngine preloads concurrently and gates third-party resources
            splashOverlay.setStatusText("Loading WonderWatch...");
            webView.loadUrl(HOME_URL);
            rootContainer.postDelayed(new Runnable() {
                @Override
                public void run() {
                    if (!initialPageRevealed && !isFinishing()) {
                        initialPageRevealed = true;
                        splashOverlay.dismissAnimated();
                    }
                }
            }, 9000);
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void configureWebSettings(WebSettings settings) {
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setSupportMultipleWindows(true);
        settings.setJavaScriptCanOpenWindowsAutomatically(true);
        settings.setLoadWithOverviewMode(true);
        settings.setUseWideViewPort(true);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);

        // Use modern clean Chrome User-Agent without Mobile Safari WebView indicators
        // Google OAuth blocks WebViews that match "Version/4.0" or "; wv"
        String ua = settings.getUserAgentString();
        if (ua != null) {
            String cleanUa = ua.replace("; wv)", ")")
                               .replace("; wv", "")
                               .replaceAll("Version/\\d+\\.\\d+\\s*", "");
            settings.setUserAgentString(cleanUa);
        }
    }

    private void configureSystemBars() {
        Window window = getWindow();
        window.addFlags(android.view.WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
        window.setStatusBarColor(BG_DARK);
        window.setNavigationBarColor(BG_DARK);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false);
            window.getDecorView();
            WindowInsetsController controller = window.getInsetsController();
            if (controller != null) {
                controller.setSystemBarsAppearance(0, WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS);
                controller.setSystemBarsAppearance(0, WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS);
            }
        }
    }

    private void applySafeSystemInsets(final View targetView) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            targetView.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
                @Override
                public WindowInsets onApplyWindowInsets(View v, WindowInsets insets) {
                    Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                    v.setPadding(bars.left, bars.top, bars.right, bars.bottom);
                    return insets;
                }
            });
        }
    }

    private LinearLayout createErrorOverlay() {
        LinearLayout box = new LinearLayout(this);
        box.setBackgroundColor(BG_DARK);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER);
        int pad = dp(28);
        box.setPadding(pad, pad, pad, pad);

        TextView heading = new TextView(this);
        heading.setText("Connection Unavailable");
        heading.setTextColor(Color.WHITE);
        heading.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        heading.setGravity(Gravity.CENTER);
        box.addView(heading);

        TextView subtitle = new TextView(this);
        subtitle.setText("Please check your internet connection and try again.");
        subtitle.setTextColor(Color.parseColor("#A1A1AA"));
        subtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        subtitle.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams subParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        subParams.topMargin = dp(8);
        subParams.bottomMargin = dp(20);
        box.addView(subtitle, subParams);

        Button retryBtn = new Button(this);
        retryBtn.setText("Retry");
        retryBtn.setTextColor(Color.BLACK);
        retryBtn.setTypeface(Typeface.DEFAULT_BOLD);
        retryBtn.setAllCaps(false);
        GradientDrawable btnBg = new GradientDrawable();
        btnBg.setColor(BRAND_LIME);
        btnBg.setCornerRadius(dp(14));
        retryBtn.setBackground(btnBg);
        retryBtn.setPadding(dp(28), dp(12), dp(28), dp(12));
        retryBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(final View v) {
                // Interactive tap bounce & pulse animation
                v.setEnabled(false);
                v.animate()
                    .scaleX(0.90f)
                    .scaleY(0.90f)
                    .setDuration(110)
                    .withEndAction(new Runnable() {
                        @Override
                        public void run() {
                            v.animate()
                                .scaleX(1.0f)
                                .scaleY(1.0f)
                                .setDuration(140)
                                .withEndAction(new Runnable() {
                                    @Override
                                    public void run() {
                                        // Fade out error overlay smoothly to dark background before reloading
                                        errorOverlay.animate()
                                            .alpha(0f)
                                            .setDuration(180)
                                            .withEndAction(new Runnable() {
                                                @Override
                                                public void run() {
                                                    errorOverlay.setVisibility(View.GONE);
                                                    errorOverlay.setAlpha(1f);
                                                    v.setEnabled(true);
                                                    // Reset WebView to clean blank page to eliminate any Chromium error page artifacts
                                                    webView.stopLoading();
                                                    webView.loadUrl("about:blank");
                                                    webView.loadUrl(HOME_URL);
                                                }
                                            })
                                            .start();
                                    }
                                })
                                .start();
                        }
                    })
                    .start();
            }
        });
        box.addView(retryBtn);

        return box;
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        webView.saveState(outState);
    }

    @Override
    public void onBackPressed() {
        handleBackAction();
    }

    private void handleBackAction() {
        if (customFullscreenView != null) {
            if (customViewCallback != null) {
                customViewCallback.onCustomViewHidden();
            }
            exitVideoFullscreen();
            return;
        }
        if (authPopupDialog != null && authPopupDialog.isShowing()) {
            authPopupDialog.dismiss();
            return;
        }
        if (webView != null) {
            webView.evaluateJavascript(
                "(function(){try{if(typeof window.__wwHandleBack==='function'&&window.__wwHandleBack())return true;}catch(e){}return false;})()",
                new android.webkit.ValueCallback<String>() {
                    @Override
                    public void onReceiveValue(String value) {
                        if ("true".equals(value)) {
                            return;
                        }
                        if (webView.canGoBack()) {
                            webView.goBack();
                        } else {
                            finish();
                        }
                    }
                }
            );
            return;
        }
        finish();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (webView != null) {
            webView.resumeTimers();
            webView.onResume();
        }
    }

    @Override
    protected void onPause() {
        pauseMediaInWebView();
        if (webView != null) {
            webView.onPause();
            webView.pauseTimers();
        }
        super.onPause();
    }

    @Override
    protected void onStop() {
        pauseMediaInWebView();
        super.onStop();
    }

    private void pauseMediaInWebView() {
        if (webView == null) return;
        try {
            webView.evaluateJavascript(
                "(function(){" +
                "try{document.querySelectorAll('video,audio').forEach(function(m){m.pause();});}catch(e){}" +
                "try{var f=document.getElementById('playerFrame');if(f&&f.contentWindow){" +
                "f.contentWindow.postMessage(JSON.stringify({event:'command',func:'pauseVideo'}),'*');" +
                "f.contentWindow.postMessage(JSON.stringify({command:'pause'}),'*');" +
                "f.contentWindow.postMessage('{\"type\":\"pause\"}','*');" +
                "}}catch(e){}" +
                "})();",
                null
            );
        } catch (Exception ignored) {
        }
    }

    @Override
    protected void onDestroy() {
        if (authPopupDialog != null && authPopupDialog.isShowing()) {
            authPopupDialog.dismiss();
        }
        if (webView != null) {
            webView.destroy();
        }
        super.onDestroy();
    }

    private void enterVideoFullscreen(View view, WebChromeClient.CustomViewCallback callback) {
        if (customFullscreenView != null) {
            callback.onCustomViewHidden();
            return;
        }
        customFullscreenView = view;
        customViewCallback = callback;
        fullscreenContainer.addView(view, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));
        fullscreenContainer.setVisibility(View.VISIBLE);
        webView.setVisibility(View.GONE);
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);
        setImmersiveSystemBars(true);
    }

    private void exitVideoFullscreen() {
        if (customFullscreenView == null) return;
        fullscreenContainer.removeAllViews();
        fullscreenContainer.setVisibility(View.GONE);
        customFullscreenView = null;
        customViewCallback = null;
        webView.setVisibility(View.VISIBLE);
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT);
        setImmersiveSystemBars(false);
    }

    private void setImmersiveSystemBars(boolean immersive) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller == null) return;
            if (immersive) {
                controller.hide(WindowInsets.Type.systemBars());
                controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            } else {
                controller.show(WindowInsets.Type.systemBars());
            }
        } else {
            @SuppressWarnings("deprecation")
            int flags = immersive
                ? (View.SYSTEM_UI_FLAG_FULLSCREEN
                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY)
                : View.SYSTEM_UI_FLAG_VISIBLE;
            getWindow().getDecorView().setSystemUiVisibility(flags);
        }
    }

    private void tryRevealWhenReady(final WebView view) {
        if (initialPageRevealed || !mainPageFinished || lastLoadProgress < 100) return;
        view.evaluateJavascript(
            "(function(){return document.readyState==='complete' && !!document.body && document.body.children.length>0;})()",
            new android.webkit.ValueCallback<String>() {
                @Override
                public void onReceiveValue(String value) {
                    if (initialPageRevealed) return;
                    if ("true".equals(value)) {
                        initialPageRevealed = true;
                        view.postDelayed(new Runnable() {
                            @Override
                            public void run() {
                                splashOverlay.dismissAnimated();
                            }
                        }, 150);
                    }
                }
            }
        );
    }

    private int dp(int value) {
        return Math.round(TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            value,
            getResources().getDisplayMetrics()
        ));
    }

    private final class AndroidBridge {
        @android.webkit.JavascriptInterface
        public void checkForUpdates() {
            new Thread(() -> {
                String currentVer = "2.4.0";
                try {
                    currentVer = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
                } catch (Exception ignored) {}
                final String localVer = currentVer;

                try {
                    java.net.URL url = new java.net.URL(HOME_URL + "version.json?t=" + System.currentTimeMillis());
                    java.net.HttpURLConnection conn = (java.net.HttpURLConnection) url.openConnection();
                    conn.setConnectTimeout(8000);
                    conn.setReadTimeout(8000);
                    java.io.BufferedReader br = new java.io.BufferedReader(new java.io.InputStreamReader(conn.getInputStream()));
                    StringBuilder sb = new StringBuilder();
                    String line;
                    while ((line = br.readLine()) != null) sb.append(line);
                    br.close();

                    org.json.JSONObject root = new org.json.JSONObject(sb.toString());
                    org.json.JSONObject androidObj = root.optJSONObject("android");
                    final String remoteVer = androidObj != null ? androidObj.optString("version", localVer) : localVer;
                    final String apkUrl = androidObj != null ? androidObj.optString("apkUrl", "") : "";
                    final boolean hasUpdate = isNewerVersion(remoteVer, localVer) && !apkUrl.isEmpty();

                    runOnUiThread(() -> {
                        android.app.AlertDialog.Builder b = new android.app.AlertDialog.Builder(MainActivity.this);
                        b.setTitle(hasUpdate ? "Update Available" : "WonderWatch Updates");
                        b.setMessage("Current version: v" + localVer + "\nLatest version: v" + remoteVer + (hasUpdate ? "\n\nWould you like to download the latest APK?" : "\n\nYou are up to date!"));
                        if (hasUpdate) {
                            b.setPositiveButton("Update", (d, w) -> downloadAndInstallApk(apkUrl));
                            b.setNegativeButton("Later", null);
                        } else {
                            b.setPositiveButton("OK", null);
                        }
                        b.show();
                    });
                } catch (Exception e) {
                    runOnUiThread(() -> new android.app.AlertDialog.Builder(MainActivity.this)
                        .setTitle("WonderWatch Updates")
                        .setMessage("Current version: v" + localVer + "\n\nUnable to check for updates right now.")
                        .setPositiveButton("OK", null)
                        .show());
                }
            }).start();
        }
        private boolean isNewerVersion(String remote, String local) {
            try {
                String[] r = remote.split("\\.");
                String[] l = local.split("\\.");
                int len = Math.max(r.length, l.length);
                for (int i = 0; i < len; i++) {
                    int rv = i < r.length ? Integer.parseInt(r[i].replaceAll("[^0-9]", "")) : 0;
                    int lv = i < l.length ? Integer.parseInt(l[i].replaceAll("[^0-9]", "")) : 0;
                    if (rv > lv) return true;
                    if (rv < lv) return false;
                }
            } catch (Exception ignored) {}
            return false;
        }

        private void downloadAndInstallApk(String apkUrl) {
            try {
                android.app.DownloadManager dm = (android.app.DownloadManager) getSystemService(DOWNLOAD_SERVICE);
                if (dm == null) return;
                android.app.DownloadManager.Request req = new android.app.DownloadManager.Request(Uri.parse(apkUrl));
                req.setTitle("WonderWatch Update");
                req.setDescription("Downloading latest APK...");
                req.setNotificationVisibility(android.app.DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                req.setDestinationInExternalFilesDir(MainActivity.this, android.os.Environment.DIRECTORY_DOWNLOADS, "WonderWatch-Update.apk");
                final long downloadId = dm.enqueue(req);

                android.content.BroadcastReceiver onComplete = new android.content.BroadcastReceiver() {
                    @Override
                    public void onReceive(android.content.Context ctx, Intent intent) {
                        long id = intent.getLongExtra(android.app.DownloadManager.EXTRA_DOWNLOAD_ID, -1);
                        if (id != downloadId) return;
                        try { unregisterReceiver(this); } catch (Exception ignored) {}
                        Uri apkUri = dm.getUriForDownloadedFile(downloadId);
                        if (apkUri != null) {
                            Intent install = new Intent(Intent.ACTION_VIEW);
                            install.setDataAndType(apkUri, "application/vnd.android.package-archive");
                            install.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
                            try { startActivity(install); } catch (Exception ignored) {}
                        }
                    }
                };
                if (Build.VERSION.SDK_INT >= 33) {
                    registerReceiver(onComplete, new android.content.IntentFilter(android.app.DownloadManager.ACTION_DOWNLOAD_COMPLETE), android.content.Context.RECEIVER_EXPORTED);
                } else {
                    registerReceiver(onComplete, new android.content.IntentFilter(android.app.DownloadManager.ACTION_DOWNLOAD_COMPLETE));
                }
            } catch (Exception ignored) {}
        }
    }

    private final class WonderWebViewClient extends WebViewClient {

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            if (request == null || request.getUrl() == null) {
                return super.shouldInterceptRequest(view, request);
            }
            if (shieldEngine.shouldBlock(request.getUrl(), request.isForMainFrame())) {
                return AdShieldEngine.emptyBlockedResponse();
            }
            return super.shouldInterceptRequest(view, request);
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            if (request == null || request.getUrl() == null) return false;
            Uri uri = request.getUrl();
            String url = uri.toString();
            String scheme = uri.getScheme();

            if (!"http".equalsIgnoreCase(scheme) && !"https".equalsIgnoreCase(scheme)) {
                return true;
            }

            if (shieldEngine.shouldBlock(uri, true)) {
                return true;
            }

            // Keep WonderWatch & Google/Firebase auth inside the app; block ad redirects
            if (AdShieldEngine.isTrustedAuthOrAppUri(url)) {
                return false;
            }

            // Allow legitimate user-initiated external extension/repo links in system browser
            if (request.hasGesture() && (url.contains("github.com/") || url.contains("wonder-warrior.github.io"))) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, uri));
                } catch (Exception ignored) {
                }
                return true;
            }

            return !request.isForMainFrame();
        }

        @Override
        public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
            super.onPageStarted(view, url, favicon);
            view.evaluateJavascript(AdShieldEngine.getAppIntegrationScript(), null);
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            super.onPageCommitVisible(view, url);
            view.evaluateJavascript(AdShieldEngine.getAppIntegrationScript(), null);
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            super.onPageFinished(view, url);
            view.evaluateJavascript(AdShieldEngine.getAppIntegrationScript(), null);
            mainPageFinished = true;
            tryRevealWhenReady(view);
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            super.onReceivedError(view, request, error);
            if (request != null && request.isForMainFrame()) {
                // Clear the default Chromium "Webpage not available" error view (which displays the Android logo)
                view.loadUrl("about:blank");
                splashOverlay.dismissAnimated();
                errorOverlay.setVisibility(View.VISIBLE);
            }
        }
    }

    private final class WonderWebChromeClient extends WebChromeClient {

        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            super.onProgressChanged(view, newProgress);
            lastLoadProgress = newProgress;
            if (newProgress >= 100 && mainPageFinished) {
                tryRevealWhenReady(view);
            }
        }

        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            enterVideoFullscreen(view, callback);
        }

        @Override
        public void onHideCustomView() {
            exitVideoFullscreen();
        }

        @Override
        @SuppressLint("SetJavaScriptEnabled")
        public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
            WebView.HitTestResult hit = view.getHitTestResult();
            String extra = hit != null ? hit.getExtra() : null;

            // Block automated or ad-driven popup windows immediately
            if (extra != null && shieldEngine.isBlockedDomainUrl(extra)) {
                return false;
            }

            // Route window.open (used by Google/Firebase account sign-in) into a full-screen dialog window
            final WebView popupView = new WebView(MainActivity.this);
            popupView.setBackgroundColor(BG_DARK);
            popupView.setLayerType(View.LAYER_TYPE_HARDWARE, null);
            configureWebSettings(popupView.getSettings());
            CookieManager.getInstance().setAcceptCookie(true);
            CookieManager.getInstance().setAcceptThirdPartyCookies(popupView, true);

            LinearLayout dialogRoot = new LinearLayout(MainActivity.this);
            dialogRoot.setOrientation(LinearLayout.VERTICAL);
            dialogRoot.setBackgroundColor(BG_DARK);
            applySafeSystemInsets(dialogRoot);

            LinearLayout header = new LinearLayout(MainActivity.this);
            header.setOrientation(LinearLayout.HORIZONTAL);
            header.setGravity(Gravity.CENTER_VERTICAL);
            header.setBackgroundColor(Color.parseColor("#0B0C10"));
            header.setPadding(dp(18), dp(14), dp(14), dp(14));

            TextView titleView = new TextView(MainActivity.this);
            titleView.setText("WonderWatch Sign In");
            titleView.setTextColor(Color.WHITE);
            titleView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f);
            titleView.setTypeface(Typeface.DEFAULT_BOLD);
            LinearLayout.LayoutParams titleParams = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                1f
            );
            header.addView(titleView, titleParams);

            Button closeBtn = new Button(MainActivity.this);
            closeBtn.setText("Done");
            closeBtn.setTextColor(Color.BLACK);
            closeBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
            closeBtn.setTypeface(Typeface.DEFAULT_BOLD);
            closeBtn.setAllCaps(false);
            GradientDrawable closeBg = new GradientDrawable();
            closeBg.setColor(BRAND_LIME);
            closeBg.setCornerRadius(dp(999));
            closeBtn.setBackground(closeBg);
            closeBtn.setMinHeight(0);
            closeBtn.setMinimumHeight(0);
            closeBtn.setMinWidth(0);
            closeBtn.setMinimumWidth(0);
            closeBtn.setPadding(dp(14), dp(6), dp(14), dp(6));
            header.addView(closeBtn);

            dialogRoot.addView(header, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            ));
            dialogRoot.addView(popupView, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                0,
                1f
            ));

            final Dialog pendingDialog = new Dialog(MainActivity.this, android.R.style.Theme_DeviceDefault_NoActionBar);
            pendingDialog.setContentView(dialogRoot, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            ));
            authPopupDialog = pendingDialog;

            closeBtn.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (pendingDialog.isShowing()) {
                        pendingDialog.dismiss();
                    }
                }
            });

            pendingDialog.setOnDismissListener(new DialogInterface.OnDismissListener() {
                @Override
                public void onDismiss(DialogInterface dialog) {
                    if (authPopupDialog == pendingDialog) {
                        authPopupDialog = null;
                    }
                    try {
                        popupView.destroy();
                    } catch (Exception ignored) {
                    }
                }
            });

            if (pendingDialog.getWindow() != null) {
                pendingDialog.getWindow().setBackgroundDrawable(new ColorDrawable(BG_DARK));
                pendingDialog.getWindow().setLayout(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT
                );
            }
            if (!isFinishing()) {
                pendingDialog.show();
            }

            popupView.setWebChromeClient(new WebChromeClient() {
                @Override
                public void onCloseWindow(WebView window) {
                    if (pendingDialog.isShowing()) {
                        pendingDialog.dismiss();
                    }
                }
            });

            popupView.setWebViewClient(new WebViewClient() {
                private void checkOrReject(String u) {
                    if (u != null && !u.isEmpty() && !"about:blank".equalsIgnoreCase(u) && !AdShieldEngine.isTrustedAuthOrAppUri(u)) {
                        if (pendingDialog.isShowing()) {
                            pendingDialog.dismiss();
                        }
                    }
                }

                @Override
                public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest req) {
                    if (req == null || req.getUrl() == null) return false;
                    String u = req.getUrl().toString();
                    if (!AdShieldEngine.isTrustedAuthOrAppUri(u)) {
                        if (pendingDialog.isShowing()) {
                            pendingDialog.dismiss();
                        }
                        return true;
                    }
                    return false;
                }

                @Override
                public void onPageStarted(WebView v, String url, android.graphics.Bitmap favicon) {
                    checkOrReject(url);
                }
            });

            WebView.WebViewTransport transport = (WebView.WebViewTransport) resultMsg.obj;
            transport.setWebView(popupView);
            resultMsg.sendToTarget();
            return true;
        }
    }
}
