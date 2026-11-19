package com.wonderwatch.app;

import android.animation.Animator;
import android.animation.AnimatorListenerAdapter;
import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.SweepGradient;
import android.graphics.Typeface;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.animation.LinearInterpolator;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * Clean dark-mode splash & animated loading screen matching WonderWatch Desktop.
 */
public final class SplashOverlayView extends FrameLayout {

    private static final int BG_DARK = Color.parseColor("#050608");
    private static final int BRAND_LIME = Color.parseColor("#3BFF53");
    private static final int BRAND_GREEN = Color.parseColor("#22CC37");
    private static final int BRAND_LIGHT = Color.parseColor("#9EFFAB");
    private static final int ICON_INNER = Color.parseColor("#080A0C");
    private static final int TEXT_MUTED = Color.parseColor("#A1A1AA");

    private final EmblemSpinnerView emblemView;
    private final TextView statusLabel;
    private boolean dismissed = false;

    public SplashOverlayView(Context context) {
        super(context);
        setBackgroundColor(BG_DARK);
        setClickable(true);
        setFocusable(true);

        LinearLayout centerStack = new LinearLayout(context);
        centerStack.setOrientation(LinearLayout.VERTICAL);
        centerStack.setGravity(Gravity.CENTER_HORIZONTAL);

        emblemView = new EmblemSpinnerView(context);
        int emblemPx = dp(104);
        LinearLayout.LayoutParams emblemParams = new LinearLayout.LayoutParams(emblemPx, emblemPx);
        emblemParams.bottomMargin = dp(18);
        centerStack.addView(emblemView, emblemParams);

        LinearLayout brandRow = new LinearLayout(context);
        brandRow.setOrientation(LinearLayout.HORIZONTAL);
        brandRow.setGravity(Gravity.CENTER);

        TextView wonderText = new TextView(context);
        wonderText.setText("WONDER");
        wonderText.setTextColor(Color.WHITE);
        wonderText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22f);
        wonderText.setTypeface(Typeface.create("sans-serif-black", Typeface.BOLD));
        wonderText.setLetterSpacing(0.08f);

        TextView watchText = new TextView(context);
        watchText.setText("WATCH");
        watchText.setTextColor(BRAND_LIME);
        watchText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22f);
        watchText.setTypeface(Typeface.create("sans-serif-black", Typeface.BOLD));
        watchText.setLetterSpacing(0.08f);

        brandRow.addView(wonderText);
        brandRow.addView(watchText);

        LinearLayout.LayoutParams brandParams = new LinearLayout.LayoutParams(
            LayoutParams.WRAP_CONTENT,
            LayoutParams.WRAP_CONTENT
        );
        brandParams.bottomMargin = dp(8);
        centerStack.addView(brandRow, brandParams);

        statusLabel = new TextView(context);
        statusLabel.setText("Preparing stream shield...");
        statusLabel.setTextColor(TEXT_MUTED);
        statusLabel.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        statusLabel.setGravity(Gravity.CENTER);
        centerStack.addView(statusLabel);

        LayoutParams stackParams = new LayoutParams(
            LayoutParams.WRAP_CONTENT,
            LayoutParams.WRAP_CONTENT,
            Gravity.CENTER
        );
        addView(centerStack, stackParams);
    }

    public void setStatusText(final String text) {
        if (dismissed) return;
        post(new Runnable() {
            @Override
            public void run() {
                statusLabel.setText(text);
            }
        });
    }

    public void dismissAnimated() {
        if (dismissed) return;
        dismissed = true;
        animate()
            .alpha(0f)
            .scaleX(1.04f)
            .scaleY(1.04f)
            .setDuration(280)
            .setInterpolator(new android.view.animation.DecelerateInterpolator())
            .setListener(new AnimatorListenerAdapter() {
                @Override
                public void onAnimationEnd(Animator animation) {
                    emblemView.stopAnimations();
                    setVisibility(View.GONE);
                }
            })
            .start();
    }

    private int dp(int value) {
        return Math.round(TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            value,
            getResources().getDisplayMetrics()
        ));
    }

    private static final class EmblemSpinnerView extends View {
        private final Paint ringTrackPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint ringArcPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint badgePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint playPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint glowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF ringRect = new RectF();
        private final RectF badgeRect = new RectF();
        private final Path playPath = new Path();

        private ValueAnimator spinAnimator;
        private ValueAnimator pulseAnimator;
        private float angle = 0f;
        private float pulseScale = 1f;

        EmblemSpinnerView(Context context) {
            super(context);
            float stroke = dpF(3f);

            ringTrackPaint.setStyle(Paint.Style.STROKE);
            ringTrackPaint.setStrokeWidth(stroke);
            ringTrackPaint.setColor(Color.parseColor("#1D212B"));

            ringArcPaint.setStyle(Paint.Style.STROKE);
            ringArcPaint.setStrokeWidth(stroke);
            ringArcPaint.setStrokeCap(Paint.Cap.ROUND);

            playPaint.setStyle(Paint.Style.FILL_AND_STROKE);
            playPaint.setStrokeWidth(dpF(2.5f));
            playPaint.setStrokeJoin(Paint.Join.ROUND);
            playPaint.setColor(ICON_INNER);

            glowPaint.setStyle(Paint.Style.FILL);
            glowPaint.setColor(Color.argb(28, 59, 255, 83));

            startAnimations();
        }

        private void startAnimations() {
            spinAnimator = ValueAnimator.ofFloat(0f, 360f);
            spinAnimator.setDuration(1100);
            spinAnimator.setRepeatCount(ValueAnimator.INFINITE);
            spinAnimator.setInterpolator(new LinearInterpolator());
            spinAnimator.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() {
                @Override
                public void onAnimationUpdate(ValueAnimator animation) {
                    angle = (Float) animation.getAnimatedValue();
                    invalidate();
                }
            });
            spinAnimator.start();

            pulseAnimator = ValueAnimator.ofFloat(0.96f, 1.04f);
            pulseAnimator.setDuration(900);
            pulseAnimator.setRepeatCount(ValueAnimator.INFINITE);
            pulseAnimator.setRepeatMode(ValueAnimator.REVERSE);
            pulseAnimator.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() {
                @Override
                public void onAnimationUpdate(ValueAnimator animation) {
                    pulseScale = (Float) animation.getAnimatedValue();
                }
            });
            pulseAnimator.start();
        }

        void stopAnimations() {
            if (spinAnimator != null) spinAnimator.cancel();
            if (pulseAnimator != null) pulseAnimator.cancel();
        }

        @Override
        protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            super.onSizeChanged(w, h, oldw, oldh);
            float pad = dpF(6f);
            ringRect.set(pad, pad, w - pad, h - pad);

            SweepGradient sweep = new SweepGradient(
                w / 2f,
                h / 2f,
                new int[]{Color.TRANSPARENT, BRAND_GREEN, BRAND_LIME, BRAND_LIGHT},
                new float[]{0.0f, 0.45f, 0.8f, 1.0f}
            );
            ringArcPaint.setShader(sweep);

            float badgeSize = w * 0.60f;
            float left = (w - badgeSize) / 2f;
            float top = (h - badgeSize) / 2f;
            badgeRect.set(left, top, left + badgeSize, top + badgeSize);

            badgePaint.setShader(new LinearGradient(
                left, top + badgeSize, left + badgeSize, top,
                new int[]{BRAND_GREEN, BRAND_LIME, BRAND_LIGHT},
                new float[]{0f, 0.55f, 1f},
                Shader.TileMode.CLAMP
            ));

            playPath.reset();
            playPath.moveTo(left + badgeSize * 0.39f, top + badgeSize * 0.30f);
            playPath.lineTo(left + badgeSize * 0.72f, top + badgeSize * 0.50f);
            playPath.lineTo(left + badgeSize * 0.39f, top + badgeSize * 0.70f);
            playPath.close();
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float cx = getWidth() / 2f;
            float cy = getHeight() / 2f;

            // Soft lime ambient glow
            canvas.drawCircle(cx, cy, (getWidth() * 0.38f) * pulseScale, glowPaint);

            // Track ring & spinning neon arc
            canvas.drawOval(ringRect, ringTrackPaint);
            canvas.save();
            canvas.rotate(angle, cx, cy);
            canvas.drawArc(ringRect, -90f, 250f, false, ringArcPaint);
            canvas.restore();

            // Pulsing WonderWatch lime badge
            canvas.save();
            canvas.scale(pulseScale, pulseScale, cx, cy);
            float radius = badgeRect.width() * 0.27f;
            canvas.drawRoundRect(badgeRect, radius, radius, badgePaint);
            canvas.drawPath(playPath, playPaint);
            canvas.restore();
        }

        private float dpF(float v) {
            return TypedValue.applyDimension(
                TypedValue.COMPLEX_UNIT_DIP,
                v,
                getResources().getDisplayMetrics()
            );
        }
    }
}
