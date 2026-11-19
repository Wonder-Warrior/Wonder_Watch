package com.wonderwatch.app;

import android.content.Context;
import android.content.res.AssetManager;
import android.net.Uri;
import android.webkit.WebResourceResponse;

import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

/**
 * Fast, pre-loaded content and popup shield backed by compiled filter assets.
 * Performs O(1) domain suffix lookups and high-signal URL checks off the UI thread.
 */
public final class AdShieldEngine {

    private static final String[] ESSENTIAL_HOSTS = {
        "wonder-warrior.github.io",
        "anilist.co",
        "api.ani.zip",
        "api.jikan.moe",
        "firebaseapp.com",
        "firebase.google.com",
        "firebaseio.com",
        "googleapis.com",
        "gstatic.com",
        "accounts.google.com",
        "accounts.youtube.com",
        "apis.google.com",
        "consent.google.com",
        "myaccount.google.com",
        "gds.google.com",
        "www.google.com",
        "googleusercontent.com",
        "cdn.tailwindcss.com",
        "cdnjs.cloudflare.com",
        "thetvdb.com",
        "themoviedb.org",
        "tmdb.org",
        "kitsu.app",
        "kitsu.io"
    };

    private static final Set<String> TRUSTED_AUTH_OR_APP_HOSTS = new HashSet<String>() {{
        add("wonder-warrior.github.io");
        add("accounts.google.com");
        add("firebaseapp.com");
    }};

    private final Set<String> blockedDomains = new HashSet<String>(131072);
    private final List<String> blockedPatterns = new ArrayList<String>(128);
    private final CountDownLatch readyLatch = new CountDownLatch(1);
    private volatile boolean started = false;

    public void startPreload(final Context context) {
        if (started) return;
        started = true;
        final Context appContext = context.getApplicationContext();
        Thread loader = new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    AssetManager assets = appContext.getAssets();
                    loadLines(assets, "shield/domains.txt", blockedDomains);
                    loadLines(assets, "shield/patterns.txt", blockedPatterns);
                } catch (Exception ignored) {
                } finally {
                    readyLatch.countDown();
                }
            }
        }, "WW-ShieldLoader");
        loader.setPriority(Thread.MAX_PRIORITY);
        loader.start();
    }

    public boolean awaitReady(long timeoutMs) {
        try {
            return readyLatch.await(timeoutMs, TimeUnit.MILLISECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return false;
        }
    }

    private static void loadLines(AssetManager assets, String assetPath, java.util.Collection<String> target) {
        BufferedReader reader = null;
        try {
            reader = new BufferedReader(new InputStreamReader(assets.open(assetPath), StandardCharsets.UTF_8), 32768);
            String line;
            while ((line = reader.readLine()) != null) {
                String trimmed = line.trim();
                if (!trimmed.isEmpty()) {
                    target.add(trimmed);
                }
            }
        } catch (Exception ignored) {
        } finally {
            if (reader != null) {
                try {
                    reader.close();
                } catch (Exception ignored) {
                }
            }
        }
    }

    public boolean shouldBlock(Uri uri, boolean isMainFrame) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (scheme == null) return false;
        if (!"http".equalsIgnoreCase(scheme) && !"https".equalsIgnoreCase(scheme)) {
            return false;
        }

        String host = uri.getHost();
        if (host == null || host.isEmpty()) return false;
        String lowerHost = host.toLowerCase(Locale.US);

        if (isEssentialHost(lowerHost)) {
            return false;
        }

        awaitReady(1200);

        if (isDomainBlocked(lowerHost)) {
            return true;
        }

        if (isMainFrame) {
            return false;
        }

        String path = uri.getEncodedPath();
        String query = uri.getEncodedQuery();
        String lowerPath = path != null ? path.toLowerCase(Locale.US) : "";

        // Skip pattern matching for high-throughput media/font segments
        if (lowerPath.endsWith(".ts") || lowerPath.endsWith(".m4s") ||
            lowerPath.endsWith(".mp4") || lowerPath.endsWith(".m3u8") ||
            lowerPath.endsWith(".vtt") || lowerPath.endsWith(".woff2")) {
            return false;
        }

        String target = query != null ? (lowerHost + lowerPath + "?" + query.toLowerCase(Locale.US)) : (lowerHost + lowerPath);
        for (int i = 0, len = blockedPatterns.size(); i < len; i++) {
            if (target.contains(blockedPatterns.get(i))) {
                return true;
            }
        }
        return false;
    }

    public boolean isBlockedDomainUrl(String url) {
        if (url == null || url.isEmpty()) return false;
        try {
            return shouldBlock(Uri.parse(url), false);
        } catch (Exception e) {
            return false;
        }
    }

    public static boolean isTrustedAuthOrAppUri(String url) {
        if (url == null || url.isEmpty()) return false;
        if ("about:blank".equalsIgnoreCase(url)) return true;
        try {
            Uri uri = Uri.parse(url);
            String host = uri.getHost();
            if (host == null) return false;
            String lower = host.toLowerCase(Locale.US);
            for (String trusted : TRUSTED_AUTH_OR_APP_HOSTS) {
                if (lower.equals(trusted) || lower.endsWith("." + trusted)) {
                    return true;
                }
            }
            if (lower.startsWith("accounts.google.") ||
                lower.equals("accounts.youtube.com") ||
                lower.startsWith("myaccount.google.") ||
                lower.startsWith("consent.google.") ||
                lower.equals("gds.google.com") ||
                lower.equals("apis.google.com") ||
                lower.endsWith(".googleapis.com") ||
                lower.endsWith(".gstatic.com")) {
                return true;
            }
            if (lower.endsWith(".google.com") && uri.getPath() != null) {
                String path = uri.getPath().toLowerCase(Locale.US);
                if (path.contains("oauth") || path.contains("signin") || path.contains("servicelogin") || path.contains("account") || path.contains("consent")) {
                    return true;
                }
            }
        } catch (Exception ignored) {
        }
        return false;
    }

    private boolean isDomainBlocked(String host) {
        String candidate = host;
        while (candidate != null && !candidate.isEmpty()) {
            if (blockedDomains.contains(candidate)) {
                return true;
            }
            int dot = candidate.indexOf('.');
            if (dot < 0 || dot == candidate.length() - 1) break;
            candidate = candidate.substring(dot + 1);
        }
        return false;
    }

    private static boolean isEssentialHost(String lowerHost) {
        if (lowerHost.startsWith("accounts.google.") || lowerHost.startsWith("consent.google.") || lowerHost.startsWith("myaccount.google.")) {
            return true;
        }
        for (String safe : ESSENTIAL_HOSTS) {
            if (lowerHost.equals(safe) || lowerHost.endsWith("." + safe)) {
                return true;
            }
        }
        return false;
    }

    public static WebResourceResponse emptyBlockedResponse() {
        return new WebResourceResponse(
            "text/plain",
            "utf-8",
            200,
            "OK",
            Collections.singletonMap("Access-Control-Allow-Origin", "*"),
            new ByteArrayInputStream(new byte[0])
        );
    }

    public static String getAppIntegrationScript() {
        return "(function(){" +
            "window.__WONDERWATCH_APP__=true;" +
            "try{if(location.hostname.indexOf('github.io')!==-1){" +
            "localStorage.setItem('uBlockPromptDismissed','true');" +
            "if(!document.getElementById('__ww_app_css')){" +
            "var ac=document.createElement('style');ac.id='__ww_app_css';" +
            "ac.textContent='*,*::before,*::after{-webkit-user-select:none!important;user-select:none!important;-webkit-touch-callout:none!important;}input,textarea,[contenteditable=\"true\"]{-webkit-user-select:text!important;user-select:text!important;-webkit-touch-callout:default!important;}#navAppsLink,#mobileNavAppsLink,a[href=\"downloads.html\"]{display:none!important;}';" +
            "(document.head||document.documentElement).appendChild(ac);" +
            "}" +
            "}}catch(e){}" +
            "try{" +
            "if(location.hostname.indexOf('github.io')===-1){" +
            "var origOpen=window.open;" +
            "window.open=function(u){if(!u||String(u).indexOf('accounts.google.com')===-1){return null;}return origOpen.apply(this,arguments);};" +
            "if(!document.getElementById('__ww_shield_css')){" +
            "var st=document.createElement('style');st.id='__ww_shield_css';" +
            "st.textContent='[id*=\"ad-banner\"],[class*=\"ad-overlay\"],[class*=\"popunder\"],iframe[src*=\"doubleclick\"],iframe[src*=\"popads\"]{display:none!important;pointer-events:none!important;}';" +
            "(document.head||document.documentElement).appendChild(st);" +
            "}" +
            "}" +
            "}catch(e){}" +
            "})();";
    }
}
