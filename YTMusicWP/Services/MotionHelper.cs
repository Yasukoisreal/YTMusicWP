using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace YTMusicWP.Services
{
    /// <summary>
    /// One set of motion rules for every overlay in the app, so screens and dialogs open and close the same way:
    /// <list type="bullet">
    /// <item>Opening: <see cref="EnterMs"/>, CubicEase EaseOut (fast start, soft landing).</item>
    /// <item>Closing: <see cref="ExitMs"/>, CubicEase EaseIn (leaves quickly, never blocks the next action).</item>
    /// <item>Bottom sheet: the dimmed backdrop fades while the panel slides up from below its own height.</item>
    /// <item>Centered dialog: the backdrop fades while the card scales up from 0.92.</item>
    /// <item>Full screen page: slides up from the bottom edge (same direction as Playlist / Artist / Now Playing).</item>
    /// <item>Tab content: fades in over <see cref="ShortMs"/> while drifting up <see cref="TabRise"/> px.</item>
    /// <item>Icon state change (play/pause, like): <see cref="Pop"/>, a short grow with a slight overshoot.</item>
    /// <item>Press feedback (Themes/Styles.xaml): shrink in 80 ms, spring back in 250 ms, CubicEase.</item>
    /// </list>
    /// Only Opacity and RenderTransform are animated, so every animation runs on the compositor thread and stays
    /// smooth while the UI thread is busy loading the screen's data.
    /// XAML storyboards use the same numbers (0:0:0.25 in, 0:0:0.2 out, CubicEase); keep them in sync.
    /// </summary>
    public static class MotionHelper
    {
        public const int ShortMs = 200;
        /// <summary>How far tab content drifts up while it fades in.</summary>
        public const double TabRise = 16;
        public const int EnterMs = 250;
        public const int ExitMs = 200;
        public const int PopMs = 300;

        private const double DialogStartScale = 0.92;
        private const double DialogEndScale = 0.95;

        private enum Kind { Sheet, Dialog, Page }

        private sealed class Running
        {
            public Storyboard Storyboard;
            public bool Hiding;
        }

        // Keyed by the overlay root; overlays live as long as the page, so a plain dictionary is fine.
        private static readonly Dictionary<FrameworkElement, Running> _running = new Dictionary<FrameworkElement, Running>();

        // ── Public API ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Shows a bottom sheet: <paramref name="root"/> is the full-screen backdrop, <paramref name="panel"/> the sheet.</summary>
        public static void ShowSheet(FrameworkElement root, FrameworkElement panel) { Show(root, panel, Kind.Sheet); }

        public static void HideSheet(FrameworkElement root, FrameworkElement panel, Action hidden = null) { Hide(root, panel, Kind.Sheet, hidden); }

        /// <summary>Shows a centered dialog: <paramref name="root"/> is the backdrop, <paramref name="card"/> the dialog box.</summary>
        public static void ShowDialog(FrameworkElement root, FrameworkElement card) { Show(root, card, Kind.Dialog); }

        public static void HideDialog(FrameworkElement root, FrameworkElement card, Action hidden = null) { Hide(root, card, Kind.Dialog, hidden); }

        /// <summary>Shows a full screen page by sliding it up from the bottom edge.</summary>
        public static void ShowPage(FrameworkElement page) { Show(page, page, Kind.Page); }

        public static void HidePage(FrameworkElement page, Action hidden = null) { Hide(page, page, Kind.Page, hidden); }

        /// <summary>
        /// Fades an element in (tab content, fresh results). The element is made visible first. With
        /// <paramref name="rise"/> it also drifts up that many pixels, like the system entrance transition.
        /// </summary>
        public static void FadeIn(FrameworkElement element, int durationMs = ShortMs, double rise = 0)
        {
            if (element == null) return;
            Cancel(element);
            var t = rise != 0 ? GetTransform(element) : null;
            element.Opacity = 0;
            if (t != null) t.TranslateY = rise;
            element.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            sb.Children.Add(Animate(element, "Opacity", 0, 1, durationMs, EasingMode.EaseOut));
            if (t != null) sb.Children.Add(Animate(t, "TranslateY", rise, 0, durationMs, EasingMode.EaseOut));
            Run(element, sb, false, () =>
            {
                element.Opacity = 1;
                if (t != null) t.TranslateY = 0;
            });
        }

        /// <summary>
        /// A small "pop" for an icon whose state just changed (play/pause, like): it grows from 0.6 with a slight
        /// overshoot while fading in. Only for small elements; the overshoot looks wrong on large surfaces.
        /// </summary>
        public static void Pop(FrameworkElement icon)
        {
            if (icon == null || icon.Visibility != Visibility.Visible) return;
            var t = GetTransform(icon);
            if (t == null) return;
            Cancel(icon);
            t.ScaleX = 0.6; t.ScaleY = 0.6;
            icon.Opacity = 0.4;
            var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            var sb = new Storyboard();
            sb.Children.Add(Animate(t, "ScaleX", 0.6, 1, PopMs, pop));
            sb.Children.Add(Animate(t, "ScaleY", 0.6, 1, PopMs, pop));
            sb.Children.Add(Animate(icon, "Opacity", 0.4, 1, PopMs / 2, new CubicEase { EasingMode = EasingMode.EaseOut }));
            Run(icon, sb, false, () =>
            {
                t.ScaleX = 1; t.ScaleY = 1;
                icon.Opacity = 1;
            });
        }

        /// <summary>The element's CompositeTransform (created centered if it has none); null if it carries another kind.</summary>
        public static CompositeTransform EnsureTransform(FrameworkElement element)
        {
            return element == null ? null : GetTransform(element);
        }

        /// <summary>Target value for one property in a <see cref="MotionGroup"/> run.</summary>
        public static MotionProp To(DependencyObject target, string property, double value)
        {
            return new MotionProp(target, property, value);
        }

        /// <summary>Collapses an overlay at once (e.g. when a tab switch closes it), stopping any running animation.</summary>
        public static void HideNow(FrameworkElement root, FrameworkElement panel = null)
        {
            if (root == null) return;
            Cancel(root);
            root.Visibility = Visibility.Collapsed;
            ResetToRest(root, panel ?? root);
        }

        /// <summary>
        /// Readies a view whose XAML slide-in storyboard animates <paramref name="transform"/>.Y to 0 (Now Playing,
        /// Playlist, Artist), then makes it visible; the caller begins <paramref name="slideIn"/>.
        /// Those storyboards have no From, so they start wherever Y is. A view that was collapsed directly (tab
        /// switch, Go to artist) still has the last slide-in holding Y at 0, and would "slide" from 0 to 0; here it
        /// is moved back below the screen first. A view caught mid-close reverses from where it is (and the stopped
        /// slide-out no longer fires its Completed handler, so it cannot collapse the reopened view).
        /// </summary>
        public static void PrepareSlideIn(FrameworkElement view, TranslateTransform transform, Storyboard slideIn, Storyboard slideOut)
        {
            if (view == null || transform == null) return;
            bool wasShown = view.Visibility == Visibility.Visible;
            double y = transform.Y; // the animated value while a storyboard holds it
            try { if (slideOut != null) slideOut.Stop(); } catch { }
            try { if (slideIn != null) slideIn.Stop(); } catch { }
            transform.Y = wasShown ? y : OffscreenDistance(view);
            view.Visibility = Visibility.Visible;
        }

        /// <summary><see cref="PrepareSlideIn"/> followed by <paramref name="slideIn"/>.Begin().</summary>
        public static void BeginSlideIn(FrameworkElement view, TranslateTransform transform, Storyboard slideIn, Storyboard slideOut)
        {
            PrepareSlideIn(view, transform, slideIn, slideOut);
            if (slideIn != null) slideIn.Begin();
        }

        /// <summary>True while <paramref name="root"/> is playing its closing animation.</summary>
        public static bool IsHiding(FrameworkElement root)
        {
            Running r;
            return root != null && _running.TryGetValue(root, out r) && r.Hiding;
        }

        // ── Implementation ────────────────────────────────────────────────────────────────────────────────────

        private static void Show(FrameworkElement root, FrameworkElement panel, Kind kind)
        {
            if (root == null) return;
            if (panel == null) panel = root;
            var t = GetTransform(panel);

            // Start from wherever the element is now: mid-close it reverses smoothly, closed it starts off-screen.
            // Values are read before Cancel(), while any running animation still holds them.
            bool wasShown = root.Visibility == Visibility.Visible;
            if (wasShown && !_running.ContainsKey(root)) return; // already open and at rest
            double opacity = wasShown ? root.Opacity : (kind == Kind.Page ? 1 : 0);
            double y = wasShown && t != null ? t.TranslateY : (kind == Kind.Dialog ? 0 : OffscreenDistance(panel));
            double scale = wasShown && t != null ? t.ScaleX : (kind == Kind.Dialog ? DialogStartScale : 1);

            Cancel(root);
            root.Opacity = opacity;
            if (t != null) { t.TranslateY = y; t.ScaleX = scale; t.ScaleY = scale; }
            root.IsHitTestVisible = true;
            root.Visibility = Visibility.Visible;

            var sb = new Storyboard();
            if (kind != Kind.Page) sb.Children.Add(Animate(root, "Opacity", opacity, 1, EnterMs, EasingMode.EaseOut));
            if (t != null)
            {
                if (kind == Kind.Dialog)
                {
                    sb.Children.Add(Animate(t, "ScaleX", scale, 1, EnterMs, EasingMode.EaseOut));
                    sb.Children.Add(Animate(t, "ScaleY", scale, 1, EnterMs, EasingMode.EaseOut));
                }
                else
                {
                    sb.Children.Add(Animate(t, "TranslateY", y, 0, EnterMs, EasingMode.EaseOut));
                }
            }
            Run(root, sb, false, () => ResetToRest(root, panel));
        }

        private static void Hide(FrameworkElement root, FrameworkElement panel, Kind kind, Action hidden)
        {
            if (root == null) return;
            if (panel == null) panel = root;
            if (root.Visibility != Visibility.Visible)
            {
                if (hidden != null) hidden();
                return;
            }
            if (IsHiding(root)) return; // a second back press / tap during the close does nothing

            var t = GetTransform(panel);
            double opacity = root.Opacity;
            double y = t != null ? t.TranslateY : 0;
            double scale = t != null ? t.ScaleX : 1;

            Cancel(root);
            root.Opacity = opacity;
            if (t != null) { t.TranslateY = y; t.ScaleX = scale; t.ScaleY = scale; }
            root.IsHitTestVisible = false; // taps during the close fall through instead of re-triggering it

            var sb = new Storyboard();
            if (kind != Kind.Page) sb.Children.Add(Animate(root, "Opacity", opacity, 0, ExitMs, EasingMode.EaseIn));
            if (t != null)
            {
                if (kind == Kind.Dialog)
                {
                    sb.Children.Add(Animate(t, "ScaleX", scale, DialogEndScale, ExitMs, EasingMode.EaseIn));
                    sb.Children.Add(Animate(t, "ScaleY", scale, DialogEndScale, ExitMs, EasingMode.EaseIn));
                }
                else
                {
                    sb.Children.Add(Animate(t, "TranslateY", y, OffscreenDistance(panel), ExitMs, EasingMode.EaseIn));
                }
            }
            Run(root, sb, true, () =>
            {
                root.Visibility = Visibility.Collapsed;
                ResetToRest(root, panel);
                if (hidden != null) hidden();
            });
        }

        /// <summary>
        /// Begins <paramref name="sb"/> for <paramref name="key"/>. When it finishes, <paramref name="done"/> sets the final
        /// local values and the storyboard is stopped (HoldEnd would otherwise pin the animated values forever).
        /// </summary>
        private static void Run(FrameworkElement key, Storyboard sb, bool hiding, Action done)
        {
            var entry = new Running { Storyboard = sb, Hiding = hiding };
            _running[key] = entry;
            sb.Completed += (s, e) =>
            {
                Running cur;
                if (!_running.TryGetValue(key, out cur) || cur != entry) return; // superseded by a newer Show/Hide
                _running.Remove(key);
                done();
                sb.Stop();
            };
            sb.Begin();
        }

        private static void Cancel(FrameworkElement key)
        {
            Running r;
            if (!_running.TryGetValue(key, out r)) return;
            _running.Remove(key);
            try { r.Storyboard.Stop(); } catch { }
        }

        private static void ResetToRest(FrameworkElement root, FrameworkElement panel)
        {
            root.Opacity = 1;
            root.IsHitTestVisible = true;
            var t = GetTransform(panel);
            if (t != null) { t.TranslateY = 0; t.ScaleX = 1; t.ScaleY = 1; }
        }

        /// <summary>
        /// The panel's CompositeTransform, created on first use (centered, so dialogs scale around their middle).
        /// Returns null when the panel already carries a different transform, in which case only the fade runs.
        /// </summary>
        private static CompositeTransform GetTransform(FrameworkElement panel)
        {
            var ct = panel.RenderTransform as CompositeTransform;
            if (ct != null) return ct;
            var existing = panel.RenderTransform;
            var matrix = existing as MatrixTransform;
            if (existing != null && !(matrix != null && matrix.Matrix.IsIdentity)) return null;
            ct = new CompositeTransform();
            panel.RenderTransformOrigin = new Point(0.5, 0.5);
            panel.RenderTransform = ct;
            return ct;
        }

        /// <summary>How far below its resting place an element starts / ends: its own height, or the window height before first layout.</summary>
        private static double OffscreenDistance(FrameworkElement panel)
        {
            double h = panel.ActualHeight;
            if (h <= 0)
            {
                try { h = Window.Current.Bounds.Height; } catch { h = 800; }
            }
            return h + 20;
        }

        private static DoubleAnimation Animate(DependencyObject target, string property, double from, double to, int ms, EasingMode mode)
        {
            return Animate(target, property, from, to, ms, new CubicEase { EasingMode = mode });
        }

        internal static DoubleAnimation Animate(DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase ease)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
                EasingFunction = ease
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, property);
            return anim;
        }
    }
    public sealed class MotionProp
    {
        internal readonly DependencyObject Target;
        internal readonly string Property;
        internal readonly double Value;

        internal MotionProp(DependencyObject target, string property, double value)
        {
            Target = target;
            Property = property;
            Value = value;
        }

        /// <summary>The value on screen (the animated value while a storyboard holds it); setting it sets the local value.</summary>
        internal double Current
        {
            get
            {
                var ct = Target as CompositeTransform;
                if (ct != null)
                {
                    switch (Property)
                    {
                        case "TranslateX": return ct.TranslateX;
                        case "TranslateY": return ct.TranslateY;
                        case "ScaleX": return ct.ScaleX;
                        case "ScaleY": return ct.ScaleY;
                        case "Rotation": return ct.Rotation;
                    }
                }
                var tt = Target as TranslateTransform;
                if (tt != null) return Property == "X" ? tt.X : tt.Y;
                var ui = Target as UIElement;
                if (ui != null && Property == "Opacity") return ui.Opacity;
                throw new NotSupportedException(Property);
            }
            set
            {
                var ct = Target as CompositeTransform;
                if (ct != null)
                {
                    switch (Property)
                    {
                        case "TranslateX": ct.TranslateX = value; return;
                        case "TranslateY": ct.TranslateY = value; return;
                        case "ScaleX": ct.ScaleX = value; return;
                        case "ScaleY": ct.ScaleY = value; return;
                        case "Rotation": ct.Rotation = value; return;
                    }
                }
                var tt = Target as TranslateTransform;
                if (tt != null) { if (Property == "X") tt.X = value; else tt.Y = value; return; }
                var ui = Target as UIElement;
                if (ui != null && Property == "Opacity") { ui.Opacity = value; return; }
                throw new NotSupportedException(Property);
            }
        }
    }

    /// <summary>
    /// A set of properties that animate together, one run at a time (the toast, the Create button + sheet, the
    /// fullscreen lyrics). Starting a new run while one is playing continues from the current on-screen values
    /// instead of jumping, and the interrupted run's completion callback never fires, so a close that was
    /// reversed by a reopen can no longer hide the reopened element.
    /// </summary>
    public sealed class MotionGroup
    {
        private Storyboard _running;
        private MotionProp[] _runningProps;

        public bool IsRunning { get { return _running != null; } }

        public void Animate(int durationMs, EasingMode mode, Action completed, params MotionProp[] props)
        {
            Animate(durationMs, new CubicEase { EasingMode = mode }, completed, props);
        }

        public void Animate(int durationMs, EasingFunctionBase ease, Action completed, params MotionProp[] props)
        {
            if (_running != null)
            {
                // Freeze the previous run where it is (read while its animations still hold the values)
                var frozen = new double[_runningProps.Length];
                for (int i = 0; i < frozen.Length; i++) frozen[i] = _runningProps[i].Current;
                var old = _running;
                _running = null;
                try { old.Stop(); } catch { }
                for (int i = 0; i < frozen.Length; i++) _runningProps[i].Current = frozen[i];
            }

            var sb = new Storyboard();
            foreach (var p in props)
                sb.Children.Add(MotionHelper.Animate(p.Target, p.Property, p.Current, p.Value, durationMs, ease));
            _running = sb;
            _runningProps = props;
            sb.Completed += (s, e) =>
            {
                if (_running != sb) return; // superseded
                _running = null;
                foreach (var p in props) p.Current = p.Value; // final values become local, then release the storyboard
                sb.Stop();
                if (completed != null) completed();
            };
            sb.Begin();
        }
    }
}
