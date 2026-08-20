using System;
using System.Collections.Generic;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace MpHideDebugLine
{
    /// <summary>
    /// Top-right toast + Notification Center renderer (IMGUI/OnGUI based).
    /// </summary>
    public class ToastRenderer : MonoBehaviour
    {
        private sealed class Toast
        {
            public string msg;
            public bool isError;
            public float age;
            public float fade;
            public float dur;
            public float totalSeconds;
            public bool sizeValid;
            public float boxW;
            public float boxH;
        }

        private const float PADDING = 10f;
        private const float STACK_GAP = 8f;
        private const float SCREEN_MARGIN = 16f;

        private readonly List<Toast> _active = new List<Toast>();
        private readonly List<Toast> _pool = new List<Toast>();

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private float _lastFontSize;
        private bool _stylesReady;

        // Notification Center slide state
        private float _centerSlide; // 0=closed, 1=open (time-based progress)
        private GUIStyle _centerLabelStyle;
        private Vector2 _centerScroll;
        private int _lastEntryCount;

        // Scrollbar style cache (rebuilt on uiScale change)
        private GUIStyle _styledScrollbar;
        private GUIStyle _styledScrollbarThumb;
        private float _cachedScrollUiScale = -1f;

        private string _searchQuery = "";
        private GUIStyle _clearStyle;
        private GUIStyle _searchStyle;
        private GUIStyle _searchLabelStyle;

        private const string MCSearchInputName = "MCSearchInput";

        public static ToastRenderer Create()
        {
            var go = new GameObject("MpHideDebugLineGUI");
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<ToastRenderer>();
        }

        private void Awake()
        {
            // Nothing is created here; the renderer is created lazily on first message.
        }

        public void Show(string msg, bool isError)
        {
            if (string.IsNullOrEmpty(msg))
                return;

            Toast t = Acquire();
            if (t == null)
                return;

            t.msg = msg;
            t.isError = isError;
            t.fade = Mathf.Max(0.01f, Plugin.FadeSeconds.Value);
            t.dur = Mathf.Max(0.1f, Plugin.ToastDurationSeconds.Value);
            t.totalSeconds = t.fade * 2f + t.dur;
            t.age = 0f;
            t.sizeValid = false;

            MoveToFront(t);

            LogFirstToast(t);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Toast t = _active[i];
                t.age += dt;

                if (t.age >= t.totalSeconds)
                {
                    _active.RemoveAt(i);
                    _pool.Add(t);
                }
            }

            // Update the Notification Center slide progress (1 when open, 0 when closed).
            float target = Plugin.CenterOpen ? 1f : 0f;
            float speed = 1f / Mathf.Max(0.01f, Plugin.SlideDuration.Value);
            _centerSlide = Mathf.MoveTowards(_centerSlide, target, dt * speed);
        }

        // Fallback when the mod is not loaded; the mod's Postfix draws otherwise.
        private void OnGUI()
        {
            try
            {
                if (UIBullshit.main != null)
                    return; // the postfix handles drawing when the mod is present
            }
            catch
            {
            }

            DrawImmediateGUI();
        }

        // Draws toasts and the Notification Center together (runs after the mod's IMGUI).
        internal void DrawImmediateGUI()
        {
            HandleGlobalInputEsc();

            try
            {
                GUI.enabled = true; // guard against mod dropdown exceptions
            }
            catch
            {
            }

            bool consoleOpen = false;
            try
            {
                consoleOpen = Con.IsConsoleOpen();
            }
            catch
            {
                // still attempt to draw even if the console gate fails
            }

            DrawToasts(consoleOpen);

            // Notification Center (drawn after toasts so it always appears above them)
            if (_centerSlide > 0.001f || Plugin.CenterOpen)
            {
                DrawNotificationCenter();
            }
        }

        private void DrawToasts(bool consoleOpen)
        {
            if (_active.Count > 0 && !consoleOpen)
            {
                EnsureStyles();
                RefreshBoxTexture();

                // Stack from the top-right downward.
                float y = SCREEN_MARGIN;
                foreach (Toast t in _active)
                {
                    if (!t.sizeValid)
                        Measure(t);

                    float alpha = ComputeAlpha(t);
                    if (alpha <= 0.001f)
                        continue;

                    float x = Screen.width - SCREEN_MARGIN - t.boxW;

                    // Background (mod button tone, semi-transparent)
                    Color bgColor = t.isError
                        ? new Color(1f, 0.82f, 0.82f, 0.8f)
                        : new Color(1f, 1f, 1f, 0.8f);
                    bgColor.a *= alpha;

                    if (_boxStyle != null)
                    {
                        Color prev = GUI.color;
                        GUI.color = bgColor;
                        GUI.Box(new Rect(x, y, t.boxW, t.boxH), GUIContent.none, _boxStyle);
                        GUI.color = prev;
                    }

                    // Text (game font, center-left)
                    Color textColor = t.isError
                        ? new Color(1f, 0.55f, 0.55f, 1f)
                        : new Color(1f, 1f, 1f, 1f);
                    textColor.a *= alpha;

                    if (_labelStyle != null)
                    {
                        Color prev = GUI.color;
                        GUI.color = textColor;
                        GUI.Label(
                            new Rect(x + PADDING, y, t.boxW - PADDING * 2f, t.boxH),
                            t.msg, _labelStyle);
                        GUI.color = prev;
                    }

                    y += t.boxH + STACK_GAP;
                }
            }
        }

        private static float ComputeAlpha(Toast t)
        {
            float fadeInEnd = t.fade;
            float holdEnd = t.fade + t.dur;

            if (t.age < fadeInEnd)
                return Mathf.Clamp01(t.age / fadeInEnd);
            if (t.age < holdEnd)
                return 1f;
            return Mathf.Clamp01(1f - (t.age - holdEnd) / t.fade);
        }

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private static float EaseInCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        // Dims the screen and slides in a scrollable history panel from the right.
        private void DrawNotificationCenter()
        {
            try
            {
                if (_centerSlide <= 0.001f && !Plugin.CenterOpen)
                    return;

                // Screen dim: black translucent, like the game's PauseHandler/GlobalDark.
                float dim = Plugin.DimAmount.Value * _centerSlide;
                if (dim > 0.001f)
                {
                    DrawDim(new Color(0f, 0f, 0f, dim));
                }

                // Input blocking: block world clicks/attacks while the Center is open.
                if (Plugin.CenterOpen)
                {
                    BlockWorldInput();
                }

                // Right slide-in panel, eased with EaseOutCubic/EaseInCubic.
                float margin = Mathf.Max(4f, Plugin.CenterMargin.Value);
                float ratio = Mathf.Clamp01(Plugin.PanelWidthRatio.Value);
                float panelW = Screen.width * ratio;
                float s = _centerSlide;
                bool opening = Plugin.CenterOpen;
                float eased = opening ? EaseOutCubic(s) : (1f - EaseInCubic(1f - s));
                float panelX = Screen.width - margin - (panelW * eased);

                DrawPanel(panelX, margin, panelW, Screen.height - margin * 2f);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"DrawNotificationCenter failed: {ex}");
            }
        }

        private void DrawDim(Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        // Sets the mod's overlap flag to block world clicks/attacks.
        private void BlockWorldInput()
        {
            try
            {
                // Register the full screen as an overlap so the mod blocks attack/drop.
                UIBullshit.CheckCursorOverlap(new Rect(0f, 0f, Screen.width, Screen.height));
            }
            catch
            {
            }
        }

        // Black translucent panel (game-settings style) as two separate 9-slice boxes.
        private void DrawPanel(float x, float y, float width, float height)
        {
            try
            {
                float scale = GetUiScaleSafe();
                float titleH = 56f * scale; // mod title bar height (56 * uiScale)

                Rect titleRect = new Rect(x, y, width, titleH);
                Rect bodyRect = new Rect(x, y + titleH, width, height - titleH);

                UIBullshit._GUI_9SlicePanel(in bodyRect, 1f, 0.6980392f, check_overlap: true);
                UIBullshit._GUI_9SlicePanel(in titleRect, 1f, 0.6980392f, check_overlap: true);

                int b = GetBoxBorderSize();

                GUILayout.BeginArea(new Rect(x, y, width, titleH));
                DrawHeader(titleRect, titleH);
                GUILayout.EndArea();

                int nb = UIBullshit.uiBlockNanoBorderSize;

                float toolbarH = 30f * scale + nb * 2f;    // input row height + slack

                Rect toolbarRect = new Rect(
                    bodyRect.x + b, bodyRect.y + b,
                    bodyRect.width - b * 2f, toolbarH);
                UIBullshit._GUI_9SlicePanel(in toolbarRect, 1f, 0.5f, check_overlap: false, UIBullshit.unscaled_uiBlockNano);

                float bottomReserve = nb;

                float listY = toolbarRect.yMax + nb;
                Rect listRect = new Rect(
                    bodyRect.x + b, listY,
                    bodyRect.width - b * 2f, bodyRect.yMax - listY - b - bottomReserve);
                UIBullshit._GUI_9SlicePanel(in listRect, 1f, 0.5f, check_overlap: false, UIBullshit.unscaled_uiBlockNano);

                Rect toolbarInner = new Rect(
                    toolbarRect.x + nb, toolbarRect.y + nb,
                    toolbarRect.width - nb * 2f, toolbarRect.height - nb * 2f);
                GUILayout.BeginArea(toolbarInner);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear", GetClearStyle(), GUILayout.Height(30f * scale)))
                {
                    NotificationLog.Clear();
                    _centerScroll = Vector2.zero;
                    _lastEntryCount = 0;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("Search:  ", GetSearchLabelStyle(), GUILayout.Height(30f * scale));
                GUI.SetNextControlName(MCSearchInputName);
                string newSearch = GUILayout.TextField(_searchQuery, GetSearchStyle(),
                    GUILayout.Width(toolbarInner.width * 0.45f),
                    GUILayout.Height(30f * scale));
                if (newSearch != _searchQuery)
                    _searchQuery = newSearch;
                GUILayout.EndHorizontal();
                GUILayout.EndArea();

                Rect listInner = new Rect(
                    listRect.x + nb, listRect.y + nb,
                    listRect.width - nb * 2f, listRect.height - nb * 2f);

                // Temporarily swap the scrollbar for the mod's DoSliderThings style.
                GUIStyle origScrollbar = GUI.skin.verticalScrollbar;
                GUIStyle origThumb = GUI.skin.verticalScrollbarThumb;
                bool styled = TryApplyStyledScrollbar();
                try
                {
                    GUILayout.BeginArea(listInner);
                    GUILayout.BeginVertical();

                    _centerScroll = GUILayout.BeginScrollView(_centerScroll, false, false, GUILayout.Height(listInner.height));

                    DrawEntries(listInner.width - nb * 2f);

                    GUILayout.EndScrollView();

                    GUILayout.EndVertical();
                    GUILayout.EndArea();
                }
                finally
                {
                    if (styled)
                    {
                        GUI.skin.verticalScrollbar = origScrollbar;
                        GUI.skin.verticalScrollbarThumb = origThumb;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"DrawPanel failed: {ex}");
            }
        }

        // Swaps the scrollbar for the mod's style; false keeps the original.
        private bool TryApplyStyledScrollbar()
        {
            try
            {
                float scale = UIBullshit.uiScale;
                if (scale <= 0f) scale = 1f;

                // Recreate cached styles only on uiScale change (avoid new GUIStyle).
                if (_styledScrollbar == null || Mathf.Abs(scale - _cachedScrollUiScale) > 0.001f)
                {
                    _cachedScrollUiScale = scale;

                    _styledScrollbar = new GUIStyle(GUI.skin.verticalScrollbar);
                    _styledScrollbarThumb = new GUIStyle(GUI.skin.verticalScrollbarThumb);
                    ApplyScrollStyle(_styledScrollbar, _styledScrollbarThumb);
                    _styledScrollbar.fixedWidth = 20f * scale; // mod UIBullshit.cs:897
                }

                GUI.skin.verticalScrollbar = _styledScrollbar;
                GUI.skin.verticalScrollbarThumb = _styledScrollbarThumb;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Reproduces the mod's DoSliderThings scrollbar style.
        private static void ApplyScrollStyle(GUIStyle slider, GUIStyle thumb)
        {
            thumb.normal.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 1f, 1f);
            thumb.focused.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 0.9f, 1f);
            thumb.hover.background = thumb.focused.background;
            thumb.active.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 0.8f, 1f);
            slider.normal.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 1f, 1f);

            slider.overflow = new RectOffset(0, 0, 0, 0);
            thumb.overflow = new RectOffset(0, 0, 0, 0);
            slider.padding = new RectOffset(0, 0, 0, 0);

            int nb = UIBullshit.uiBlockNanoBorderSize;
            slider.border = new RectOffset(nb, nb, nb, nb);
            thumb.border = slider.border;

            float num = 20f * UIBullshit.uiScale;
            if (thumb.fixedHeight != 0f) thumb.fixedHeight = num;
            if (thumb.fixedWidth != 0f) thumb.fixedWidth = num;
        }

        // Clear button style: nano button with white border and 0.85x font.
        private GUIStyle GetClearStyle()
        {
            if (_clearStyle == null)
                _clearStyle = new GUIStyle(GUI.skin.button);

            try
            {
                UIBullshit._GUI_SetButtonSkinTexture(_clearStyle, small: false);
            }
            catch
            {
                // keep default if not loaded
            }

            _clearStyle.font = GetGameFont();
            _clearStyle.fontSize = (int)(UIBullshit.GetCurFontSize() * 0.85f);
            _clearStyle.alignment = TextAnchor.MiddleCenter;
            _clearStyle.normal.textColor = Color.white;
            _clearStyle.contentOffset = Vector2.zero;
            return _clearStyle;
        }

        // Search input style: nano background, re-skinned every frame.
        private GUIStyle GetSearchStyle()
        {
            if (_searchStyle == null)
                _searchStyle = new GUIStyle(GUI.skin.textField);

            try
            {
                _searchStyle.normal.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 0.8f, 0.8f);
                _searchStyle.hover.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 0.9f, 0.8f);
                _searchStyle.focused.background = UIBullshit.GetChangedTex(UIBullshit.unscaled_uiBlockNano, 1f, 0.8f);

                int nb = UIBullshit.uiBlockNanoBorderSize;
                _searchStyle.border = new RectOffset(nb, nb, nb, nb);
            }
            catch
            {
                // keep default if not loaded
            }

            _searchStyle.font = GetGameFont();
            _searchStyle.fontSize = (int)GetButtonFontSize();
            _searchStyle.alignment = TextAnchor.MiddleLeft;
            _searchStyle.normal.textColor = Color.white;
            _searchStyle.richText = false;
            return _searchStyle;
        }

        // "Search:" label style, light gray to distinguish it from the field.
        private GUIStyle GetSearchLabelStyle()
        {
            if (_searchLabelStyle == null)
                _searchLabelStyle = new GUIStyle(GUI.skin.label);

            _searchLabelStyle.font = GetGameFont();
            _searchLabelStyle.fontSize = (int)GetButtonFontSize();
            _searchLabelStyle.alignment = TextAnchor.MiddleRight;
            _searchLabelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            _searchLabelStyle.richText = false;
            return _searchLabelStyle;
        }

        private void DrawHeader(Rect titleRect, float titleH)
        {
            EnsureHeaderStyles();

            // Title: centered, 1.5x font.
            _headerStyle.fontSize = (int)(GetButtonFontSize() * 1.5f);
            GUI.Label(new Rect(0f, 0f, titleRect.width, titleH), "Status Messages", _headerStyle);

            // Close X: top-right; coords are local to the title area (absolute clips).
            EnsureCloseStylePerFrame();
            int bc = GetBoxBorderSize();
            float side = titleH - bc * 2f;
            Rect closeRect = new Rect(
                titleRect.width - bc - side,
                bc,
                side,
                side);

            if (GUI.Button(closeRect, "X", _closeStyle))
            {
                Plugin.CenterOpen = false; // same as the mod menu's SetOpen(false)
            }
        }

        private GUIStyle _headerStyle;
        private GUIStyle _closeStyle;

        private void EnsureHeaderStyles()
        {
            if (_headerStyle != null)
                return;

            _headerStyle = new GUIStyle();
            _headerStyle.font = GetGameFont();
            _headerStyle.normal.textColor = Color.white;
            _headerStyle.richText = false;
            _headerStyle.alignment = TextAnchor.MiddleCenter;
        }

        // Re-skins the close button style every frame to avoid stale textures.
        private void EnsureCloseStylePerFrame()
        {
            if (_closeStyle == null)
                _closeStyle = new GUIStyle(GUI.skin.button);

            try
            {
                UIBullshit._GUI_SetButtonSkinTexture(_closeStyle, small: false);
            }
            catch
            {
                // keep the default button style if the mod is not loaded
            }

            // Explicitly match the default text values the mod inherits.
            _closeStyle.font = GetGameFont();
            _closeStyle.fontSize = (int)GetButtonFontSize();
            _closeStyle.alignment = TextAnchor.MiddleCenter;
            _closeStyle.normal.textColor = Color.white;
            _closeStyle.contentOffset = Vector2.zero;
        }

        private static float GetUiScaleSafe()
        {
            try { return UIBullshit.uiScale > 0f ? UIBullshit.uiScale : 1f; }
            catch { return 1f; }
        }

        private void DrawEntries(float width)
        {
            EnsureCenterLabelStyle();
            DrawMessageEntries(width);
        }

        private void DrawMessageEntries(float width)
        {
            var entries = NotificationLog.Entries;

            // Auto-scroll to the bottom when a new message arrives.
            if (entries.Count > _lastEntryCount)
            {
                _centerScroll.y = float.MaxValue;
                _lastEntryCount = entries.Count;
            }

            bool filtering = !string.IsNullOrEmpty(_searchQuery);
            float timeW = GetTimeWidth();
            // Reserve scrollbar width so content does not overflow horizontally.
            float sbW = (GUI.skin.verticalScrollbar != null) ? GUI.skin.verticalScrollbar.fixedWidth : 20f;
            float msgW = Mathf.Max(20f, width - timeW - sbW - 4f);

            // Oldest (front) on top, newest (end) at the bottom.
            for (int i = 0; i < entries.Count; i++)
            {
                NotificationLog.Entry e = entries[i];

                if (filtering && e.msg.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Color textColor = e.isError ? TextErrorColor : TextNormalColor;
                _centerLabelStyle.normal.textColor = textColor;

                GUILayout.BeginHorizontal();
                GUILayout.Label(e.time.ToString("HH:mm:ss"), _centerTimeStyle, GUILayout.Width(timeW));
                GUILayout.Label(e.msg, _centerLabelStyle, GUILayout.Width(msgW));
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }
        }

        // Releases ESC focus from the search input (at the start of DrawImmediateGUI).
        private static void HandleGlobalInputEsc()        {
            try
            {
                if (Event.current.type != EventType.KeyDown
                    || (Event.current.keyCode != KeyCode.Escape))
                    return;

                string focused = GUI.GetNameOfFocusedControl();
                if (focused == MCSearchInputName)
                {
                    GUI.FocusControl("");
                    Event.current.Use();
                }
            }
            catch
            {
            }
        }

        private static readonly Color TextNormalColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color TextErrorColor = new Color(1f, 0.55f, 0.55f, 1f);

        private GUIStyle _centerTimeStyle;
        private float _cachedTimeWidth = -1f;

        private float GetTimeWidth()
        {
            if (_cachedTimeWidth < 0f && _centerTimeStyle != null)
                _cachedTimeWidth = _centerTimeStyle.CalcSize(new GUIContent("88:88:88")).x + 8f;
            if (_cachedTimeWidth < 0f)
                _cachedTimeWidth = 60f;
            return _cachedTimeWidth;
        }

        private void EnsureCenterLabelStyle()
        {
            float fontSize = GetButtonFontSize();
            if (_centerLabelStyle != null && Mathf.Abs(fontSize - _lastFontSize) < 0.5f)
                return;

            if (_centerLabelStyle == null)
                _centerLabelStyle = new GUIStyle();
            _centerLabelStyle.font = GetGameFont();
            _centerLabelStyle.fontSize = (int)fontSize;
            _centerLabelStyle.normal.textColor = TextNormalColor;
            _centerLabelStyle.richText = false;
            _centerLabelStyle.wordWrap = true;
            _centerLabelStyle.clipping = TextClipping.Clip;
            _centerLabelStyle.alignment = TextAnchor.UpperLeft;

            if (_centerTimeStyle == null)
                _centerTimeStyle = new GUIStyle(_centerLabelStyle);
            _centerTimeStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            _centerTimeStyle.alignment = TextAnchor.UpperLeft;
            _cachedTimeWidth = -1f;
        }

        // Prepares GUIStyles from the game font; recreated when the font size changes.
        private void EnsureStyles()
        {
            float fontSize = GetButtonFontSize();

            if (_stylesReady && Mathf.Abs(fontSize - _lastFontSize) < 0.5f)
                return;

            _lastFontSize = fontSize;

            Font font = GetGameFont();

            if (_boxStyle == null)
                _boxStyle = new GUIStyle();
            // Background texture is re-fetched each frame (scene change can destroy it).
            _boxStyle.normal.background = null;
            int b = GetBoxBorderSize();
            _boxStyle.border = new RectOffset(b, b, b, b);
            _boxStyle.padding = new RectOffset((int)PADDING, (int)PADDING, (int)PADDING, (int)PADDING);

            if (_labelStyle == null)
                _labelStyle = new GUIStyle();
            _labelStyle.font = font;
            _labelStyle.fontSize = (int)fontSize;
            _labelStyle.alignment = TextAnchor.MiddleLeft;
            _labelStyle.wordWrap = true;
            _labelStyle.richText = false;
            _labelStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        // Mod button background texture or default black.
        private static Texture2D GetBoxTexture()
        {
            try
            {
                Texture2D tex = UIBullshit.tex_button_normal;
                if (tex != null && (bool)tex)
                    return tex;
            }
            catch
            {
            }
            return null;
        }

        // Re-fetches the background texture each frame to recover it after scene changes.
        private void RefreshBoxTexture()
        {
            if (_boxStyle == null)
                return;
            try
            {
                Texture2D tex = GetBoxTexture();
                if (tex != null && (bool)tex && _boxStyle.normal.background != tex)
                    _boxStyle.normal.background = tex;
            }
            catch
            {
            }
        }

        // Background 9-slice border (same as the mod button).
        private static int GetBoxBorderSize()
        {
            try
            {
                int b = UIBullshit.uiBlockSmallBorderSize;
                if (b > 0)
                    return b;
            }
            catch
            {
            }
            return 3;
        }

        // Measures the box from text width (IMGUI is pixel-based, no scale dependency).
        private void Measure(Toast t)
        {
            try
            {
                if (_labelStyle == null)
                    return;

                float maxW = Mathf.Max(120f, Plugin.MaxWidth.Value);

                float naturalW = _labelStyle.CalcSize(new GUIContent(t.msg)).x;
                float lineHeight = _labelStyle.CalcSize(new GUIContent("M")).y;

                if (naturalW + PADDING * 2f <= maxW)
                {
                    t.boxW = naturalW + PADDING * 2f;
                    t.boxH = lineHeight + PADDING * 2f;
                }
                else
                {
                    float wrapW = maxW - PADDING * 2f;
                    float h = _labelStyle.CalcHeight(new GUIContent(t.msg), wrapW);
                    t.boxW = maxW;
                    t.boxH = h + PADDING * 2f;
                }

                t.boxW = Mathf.Ceil(t.boxW);
                t.boxH = Mathf.Ceil(t.boxH);
                t.sizeValid = true;
            }
            catch
            {
                // Measurement failed: keep sizeValid=false so it is retried next frame.
            }
        }

        private void MoveToFront(Toast t)
        {
            _active.Insert(0, t);
            if (_active.Count > Plugin.MaxVisible.Value)
            {
                Toast victim = _active[_active.Count - 1];
                _active.RemoveAt(_active.Count - 1);
                _pool.Add(victim);
            }
        }

        private Toast Acquire()
        {
            if (_pool.Count > 0)
            {
                Toast t = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                return t;
            }

            if (_active.Count < Plugin.MaxVisible.Value)
                return new Toast();

            var oldest = _active[_active.Count - 1];
            _active.RemoveAt(_active.Count - 1);
            return oldest;
        }

        // Returns the game menu text size (GetCurFontSize = 20 * uiScale, pixels).
        private static float GetButtonFontSize()
        {
            try
            {
                return Mathf.Max(6f, UIBullshit.GetCurFontSize());
            }
            catch
            {
                return 20f;
            }
        }

        private static Font _cachedFont;

        // Reads the game's IMGUI font via reflection (the class is internal).
        private static Font GetGameFont()
        {
            if (_cachedFont != null && (bool)_cachedFont)
                return _cachedFont;

            try
            {
                Type type = Type.GetType("KrokoshaCasualtiesMP.KrokoshaCoopModAssets, KrokoshaCasualtiesMP");
                if (type != null)
                {
                    var field = type.GetField(
                        "gamefont",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    Font f = field?.GetValue(null) as Font;
                    if (f != null && (bool)f)
                    {
                        _cachedFont = f;
                        return f;
                    }
                }
            }
            catch
            {
                // mod not loaded etc.: fall back
            }

            // Fallback: the built-in skin font
            try
            {
                Font g = GUI.skin.label.font;
                if (g != null && (bool)g)
                {
                    _cachedFont = g;
                    return g;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool _firstToastLogged;

        private static void LogFirstToast(Toast t)
        {
            if (_firstToastLogged)
                return;
            _firstToastLogged = true;

            try
            {
                Plugin.Log?.LogInfo(
                    $"Toast (IMGUI): font=\"{GetGameFont()?.name ?? "(none)"}\" " +
                    $"fontsize={GetButtonFontSize():F0} pad={PADDING:F0} " +
                    $"maxW={Plugin.MaxWidth.Value:F0} firstMsg=\"{t.msg}\"");
            }
            catch
            {
            }
        }
    }
}
