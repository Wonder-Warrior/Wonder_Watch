package com.wonderwatch.app;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.Dialog;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Typeface;
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
    private WebView webView;
    private SplashOverlayView splashOverlay;
    private LinearLayout errorOverlay;
    private FrameLayout fullscreenContainer;
    private View customFullscreenView;
    private WebChromeClient.CustomViewCallback customViewCallback;
    private Dialog authPopupDialog;
    private final AdShieldEngine shieldEngine = new AdShieldEngine();
    private boolean initialPageRevealed = false;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Start loading shield rules immediately in parallel with WebView init
        shieldEngine.startPreload(this);

        configureSystemBars();

        rootContainer = new FrameLayout(this);
        rootContainer.setBackgroundColor(BG_DARK);

        webView = new WebView(this);
        webView.setBackgroundColor(BG_DARK);
        webView.setLayerType(View.LAYER_TYPE_HARDWARE, null);
        webView.setOverScrollMode(View.OVER_SCROLL_NEVER);
        configureWebSettings(webView.getSettings());

        CookieManager cookies = CookieManager.getInstance();
        cookies.setAcceptCookie(true);
        cookies.setAcceptThirdPartyCookies(webView, true);

        webView.setWebViewClient(new WonderWebViewClient());
        webView.setWebChromeClient(new WonderWebChromeClient());

        rootContainer.addView(webView, new FrameLayout.LayoutParams(
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

        errorOverlay = createErrorOverlay();
        errorOverlay.setVisibility(View.GONE);
        rootContainer.addView(errorOverlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        splashOverlay = new SplashOverlayView(this);
        rootContainer.addView(splashOverlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ));

        applySafeSystemInsets(webView);
        setContentView(rootContainer);

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
            // Wait briefly up to 250ms if needed so shield rules are warm before first navigation
            new Thread(new Runnable() {
                @Override
                public void run() {
                    shieldEngine.awaitReady(250);
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            splashOverlay.setStatusText("Loading WonderWatch...");
                            webView.loadUrl(HOME_URL);
                        }
                    });
                }
            }).start();
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

        // Strip '; wv' token so OAuth sign-in flows work cleanly inside WebView
        String ua = settings.getUserAgentString();
        if (ua != null) {
            settings.setUserAgentString(ua.replace("; wv)", ")").replace("; wv", ""));
        }
    }

    private void configureSystemBars() {
        Window window = getWindow();
        window.setStatusBarColor(BG_DARK);
        window.setNavigationBarColor(BG_DARK);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false);
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
            public void onClick(View v) {
                errorOverlay.setVisibility(View.GONE);
                webView.loadUrl(HOME_URL);
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
        if (webView.canGoBack()) {
            webView.goBack();
            return;
        }
        finish();
    }

    @Override
    protected void onResume() {
        super.onResume();
        webView.onResume();
    }

    @Override
    protected void onPause() {
        webView.onPause();
        super.onPause();
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
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);
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

    private int dp(int value) {
        return Math.round(TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            value,
            getResources().getDisplayMetrics()
        ));
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
            if (!initialPageRevealed) {
                initialPageRevealed = true;
                splashOverlay.dismissAnimated();
            }
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            super.onPageFinished(view, url);
            view.evaluateJavascript(AdShieldEngine.getAppIntegrationScript(), null);
            if (!initialPageRevealed) {
                initialPageRevealed = true;
                splashOverlay.dismissAnimated();
            }
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            super.onReceivedError(view, request, error);
            if (request != null && request.isForMainFrame()) {
                splashOverlay.dismissAnimated();
                errorOverlay.setVisibility(View.VISIBLE);
            }
        }
    }

    private final class WonderWebChromeClient extends WebChromeClient {

        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            super.onProgressChanged(view, newProgress);
            if (newProgress >= 85 && !initialPageRevealed) {
                initialPageRevealed = true;
                splashOverlay.dismissAnimated();
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
            if (!isUserGesture || (extra != null && shieldEngine.isBlockedDomainUrl(extra))) {
                return false;
            }

            // Route window.open (used by account sign-in) into a deferred popup;
            // only display the dialog if the destination URL is a trusted auth/app origin.
            final WebView popupView = new WebView(MainActivity.this);
            popupView.setBackgroundColor(BG_DARK);
            configureWebSettings(popupView.getSettings());

            if (extra != null && !AdShieldEngine.isTrustedAuthOrAppUri(extra)) {
                popupView.destroy();
                return false;
            }

            final Dialog pendingDialog = new Dialog(MainActivity.this, android.R.style.Theme_DeviceDefault_NoActionBar);
            pendingDialog.setContentView(popupView);
            authPopupDialog = pendingDialog;

            popupView.setWebChromeClient(new WebChromeClient() {
                @Override
                public void onCloseWindow(WebView window) {
                    if (pendingDialog.isShowing()) {
                        pendingDialog.dismiss();
                    }
                    popupView.destroy();
                }
            });

            popupView.setWebViewClient(new WebViewClient() {
                private void checkOrReject(String u) {
                    if (!AdShieldEngine.isTrustedAuthOrAppUri(u)) {
                        if (pendingDialog.isShowing()) {
                            pendingDialog.dismiss();
                        }
                        popupView.destroy();
                    } else if (!pendingDialog.isShowing() && !isFinishing()) {
                        pendingDialog.show();
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
                        popupView.destroy();
                        return true;
                    }
                    if (!pendingDialog.isShowing() && !isFinishing()) {
                        pendingDialog.show();
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
