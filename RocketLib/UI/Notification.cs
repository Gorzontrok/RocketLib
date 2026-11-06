using System;
using System.Collections.Generic;
using HarmonyLib;
using RocketLib.Menus.Utilities;
using RocketLib.Utils;
using UnityEngine;

namespace RocketLib.UI
{
    /// <summary>Severity tier of a notification.</summary>
    public enum NotificationSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>Screen anchor for the notification stack.</summary>
    public enum NotificationPosition
    {
        TopLeft,
        TopRight,
        TopCenter,
        BottomLeft,
        BottomRight,
        BottomCenter
    }

    /// <summary>Static facade for showing transient on-screen notifications.</summary>
    public static class Notification
    {
        /// <summary>Screen anchor for the notification stack.</summary>
        public static NotificationPosition Position { get; set; } = NotificationPosition.TopRight;

        /// <summary>Maximum number of notifications visible on screen at once. Calls beyond the cap are queued internally and slide in as visible notifications dismiss; the queue length is capped by <see cref="MaxQueued" />.</summary>
        public static int MaxVisible { get; set; } = 5;

        /// <summary>Caps how many notifications wait behind the visible ones; arrivals beyond the cap collapse into a single "N more notifications" entry whose click action is the first click action among the folded notifications.</summary>
        public static int MaxQueued { get; set; } = 30;

        /// <summary>When true (default), notifications fired before the main menu is reached AND the Pixel04B font has loaded are buffered and flushed once both conditions are met. Avoids font-spam and intro-splash notifications. Set to false in mod init if you need notifications to render during boot.</summary>
        public static bool DeferUntilReady { get; set; } = true;

        /// <summary>
        /// Display a notification. Must be called from the main thread. `onClick` makes the body clickable; clicking the close button never invokes it.
        /// </summary>
        public static void Show(string message,
            NotificationSeverity severity = NotificationSeverity.Info,
            float duration = 5f,
            string source = null,
            Action onClick = null)
        {
            NotificationHost.Instance.Enqueue(message, severity, duration, source, onClick);
        }
    }

    internal class NotificationHost : MonoBehaviour
    {
        private const float Width = 360f;
        private const float Padding = 12f;
        private const float Gap = 8f;
        private const float SeverityBarWidth = 4f;
        private const float HeaderHeight = 18f;
        private const float FooterHeight = 16f;
        private const int MaxLines = 4;

        private const float SlideInDuration = 0.20f;
        private const float FadeOutDuration = 0.15f;
        private const float ReflowSpeed = 600f;
        private const float HoverExpandDuration = 0.10f;

        private const float SummaryDuration = 5f;

        private const float GatePollInterval = 1f;
        private const float GateTimeout = 10f;
        private const float HostGateDeadline = 60f;
        private const float FontRetryInterval = 1f;

        private static NotificationHost _instance;

        public static NotificationHost Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("RocketLib.Notification");
                    DontDestroyOnLoad(go);
                    RocketLibUtils.MakeObjectUnpausable(go);
                    _instance = go.AddComponent<NotificationHost>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            _hostCreatedTime = Time.unscaledTime;
        }

        private void OnDestroy()
        {
            if (_whiteTex != null)
            {
                Destroy(_whiteTex);
                _whiteTex = null;
            }
        }

        private readonly List<ActiveNotification> _active = new List<ActiveNotification>();
        private readonly Queue<ActiveNotification> _pending = new Queue<ActiveNotification>();
        private ActiveNotification _overflowSummary;
        private bool _isReady;
        internal static bool _mainMenuReached;
        private bool _anyHovered;
        private float _lastGatePoll = -GatePollInterval;
        private float _mainMenuReachedTime = -1f;
        private float _hostCreatedTime;
        private bool _stylesBuilt;
        private bool _fontsResolved;
        private float _lastFontRetry = -FontRetryInterval;

        private GUIStyle _messageStyle;
        private GUIStyle _sourcePillStyle;
        private GUIStyle _countBadgeStyle;
        private GUIStyle _severityIconStyle;
        private GUIStyle _closeButtonStyle;
        private GUIStyle _footerHintStyle;
        private GUIStyle _footerArrowStyle;

        private static Texture2D _whiteTex;

        public void Enqueue(string message, NotificationSeverity severity, float duration, string source, Action onClick)
        {
            string text = message ?? string.Empty;
            float lifetime = Mathf.Max(0.5f, duration);
            source = string.IsNullOrEmpty(source) ? null : source;

            var duplicate = FindDuplicate(text, severity, source);
            if (duplicate != null)
            {
                duplicate.Count++;
                duplicate.Remaining = Mathf.Max(duplicate.Remaining, lifetime);
                return;
            }

            var entry = new ActiveNotification
            {
                Message = text,
                Severity = severity,
                Remaining = lifetime,
                Source = source,
                OnClick = onClick,
                EnterT = 0f,
                FadeT = 1f,
                State = NotificationState.Entering
            };

            if (!_isReady && !Notification.DeferUntilReady)
            {
                OpenGate();
            }
            QueueEntry(entry);
        }

        private ActiveNotification FindDuplicate(string message, NotificationSeverity severity, string source)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].State != NotificationState.Exiting && IsSameContent(_active[i], message, severity, source))
                {
                    return _active[i];
                }
            }
            foreach (var queued in _pending)
            {
                if (IsSameContent(queued, message, severity, source))
                {
                    return queued;
                }
            }
            return null;
        }

        private static bool IsSameContent(ActiveNotification n, string message, NotificationSeverity severity, string source)
        {
            return !n.IsSummary
                   && n.Severity == severity
                   && string.Equals(n.Message, message, StringComparison.Ordinal)
                   && string.Equals(n.Source, source, StringComparison.Ordinal);
        }

        // Everything goes through the queue and only Update moves entries on screen, so a Show call from an onClick handler can't mutate the list mid-draw.
        private void QueueEntry(ActiveNotification entry)
        {
            int freeSlots = Mathf.Max(0, Mathf.Max(1, Notification.MaxVisible) - CountLive());
            if (_overflowSummary != null || _pending.Count - freeSlots >= Mathf.Max(1, Notification.MaxQueued))
            {
                FoldIntoSummary(entry);
                return;
            }
            _pending.Enqueue(entry);
        }

        private void FoldIntoSummary(ActiveNotification entry)
        {
            if (_overflowSummary == null)
            {
                _overflowSummary = new ActiveNotification
                {
                    Severity = entry.Severity,
                    Source = entry.Source,
                    OnClick = entry.OnClick,
                    Remaining = SummaryDuration,
                    IsSummary = true,
                    OverflowCount = 1,
                    EnterT = 0f,
                    FadeT = 1f,
                    State = NotificationState.Entering
                };
                _pending.Enqueue(_overflowSummary);
            }
            else
            {
                _overflowSummary.OverflowCount++;
                if (entry.Severity > _overflowSummary.Severity)
                {
                    _overflowSummary.Severity = entry.Severity;
                }
                if (_overflowSummary.Source != null && !string.Equals(_overflowSummary.Source, entry.Source, StringComparison.Ordinal))
                {
                    _overflowSummary.Source = null;
                }
                if (_overflowSummary.OnClick == null)
                {
                    _overflowSummary.OnClick = entry.OnClick;
                }
            }

            _overflowSummary.Message = _overflowSummary.OverflowCount == 1
                ? "1 more notification"
                : _overflowSummary.OverflowCount + " more notifications";
            _overflowSummary.CachedForWidth = -1f;
        }

        private int CountLive()
        {
            int liveCount = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].State != NotificationState.Exiting)
                {
                    liveCount++;
                }
            }
            return liveCount;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var n = _active[i];
                if (n.State == NotificationState.Entering)
                {
                    n.EnterT += dt / SlideInDuration;
                    if (n.EnterT >= 1f)
                    {
                        n.EnterT = 1f;
                        n.State = NotificationState.Visible;
                    }
                }

                if (n.State == NotificationState.Visible && !_anyHovered)
                {
                    n.Remaining -= dt;
                    if (n.Remaining <= 0f)
                    {
                        n.State = NotificationState.Exiting;
                    }
                }

                if (n.State == NotificationState.Exiting)
                {
                    n.FadeT -= dt / FadeOutDuration;
                    if (n.FadeT <= 0f)
                    {
                        _active.RemoveAt(i);
                        continue;
                    }
                }

                n.HoverExpandT = Mathf.MoveTowards(n.HoverExpandT, n.IsHovered ? 1f : 0f, dt / HoverExpandDuration);
                n.CurrentY = Mathf.MoveTowards(n.CurrentY, n.TargetY, ReflowSpeed * dt);
            }

            UpdateReadyGate();

            if (!_isReady)
            {
                return;
            }

            // Index 0 is nearest the anchor, so the newest entry always lands there.
            while (_pending.Count > 0 && CountLive() < Mathf.Max(1, Notification.MaxVisible))
            {
                var next = _pending.Dequeue();
                if (next == _overflowSummary)
                {
                    _overflowSummary = null;
                }
                _active.Insert(0, next);
            }
        }

        private void UpdateReadyGate()
        {
            if (_isReady)
            {
                return;
            }

            // An opt-out mod still needs anything queued before the opt-out to be released.
            if (!Notification.DeferUntilReady)
            {
                OpenGate();
                return;
            }

            // Backstop: if the MainMenu patch never applied, the gate must still open rather
            // than silently swallow every notification for the session.
            if (Time.unscaledTime - _hostCreatedTime >= HostGateDeadline)
            {
                OpenGate();
                return;
            }

            if (!_mainMenuReached)
            {
                return;
            }
            if (_mainMenuReachedTime < 0f)
            {
                _mainMenuReachedTime = Time.unscaledTime;
            }

            if (Time.unscaledTime - _lastGatePoll < GatePollInterval)
            {
                return;
            }
            _lastGatePoll = Time.unscaledTime;

            if (FontManager.TryGetFont(BroforceFont.Pixel04B) != null || Time.unscaledTime - _mainMenuReachedTime >= GateTimeout)
            {
                OpenGate();
            }
        }

        private void OpenGate()
        {
            _isReady = true;
        }

        private void OnGUI()
        {
            if (_active.Count == 0)
            {
                return;
            }

            EnsureStyles();

            float scale = LogicalScale();
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float logicalWidth = Screen.width / scale;
            float logicalHeight = Screen.height / scale;

            bool isLeft = Notification.Position == NotificationPosition.TopLeft || Notification.Position == NotificationPosition.BottomLeft;
            bool isCenter = Notification.Position == NotificationPosition.TopCenter || Notification.Position == NotificationPosition.BottomCenter;
            bool isBottom = Notification.Position == NotificationPosition.BottomLeft || Notification.Position == NotificationPosition.BottomRight || Notification.Position == NotificationPosition.BottomCenter;

            float anchorX;
            if (isLeft)
            {
                anchorX = 16f;
            }
            else if (isCenter)
            {
                anchorX = (logicalWidth - Width) * 0.5f;
            }
            else
            {
                anchorX = logicalWidth - Width - 16f;
            }

            float runningOffset = 0f;
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].Height = ComputeHeight(_active[i]);
                _active[i].TargetY = isBottom ? -(runningOffset + _active[i].Height) : runningOffset;
                runningOffset += _active[i].Height + Gap;

                if (!_active[i].Initialized)
                {
                    _active[i].CurrentY = _active[i].TargetY;
                    _active[i].Initialized = true;
                }
            }

            float anchorYBase = isBottom ? logicalHeight - 16f : 16f;

            bool nextHovered = false;
            // mousePosition is already in post-GUI.matrix space, so don't divide by scale.
            Vector2 mouse = Event.current.mousePosition;

            for (int i = 0; i < _active.Count; i++)
            {
                var n = _active[i];

                float slideEase = 1f - Mathf.Pow(1f - Mathf.Clamp01(n.EnterT), 3f);
                float slideOffsetX = (1f - slideEase) * (isLeft ? -Width - 32f : isCenter ? 0f : Width + 32f);
                float slideOffsetY = isCenter ? (1f - slideEase) * (isBottom ? 24f : -24f) : 0f;
                float fadeT = Mathf.Clamp01(n.FadeT);
                float alpha = slideEase * (1f - ((1f - fadeT) * (1f - fadeT)));

                float yPos = anchorYBase + n.CurrentY + slideOffsetY;
                Rect rect = new Rect(anchorX + slideOffsetX, yPos, Width, n.Height);

                if (rect.Contains(mouse))
                {
                    nextHovered = true;
                    n.IsHovered = true;
                }
                else
                {
                    n.IsHovered = false;
                }

                DrawNotification(rect, n, alpha);
            }

            _anyHovered = nextHovered;

            GUI.matrix = prevMatrix;
        }

        private static float LogicalScale()
        {
            return Mathf.Max(1f, Screen.height / 900f);
        }

        /// <summary>Measures the message once per content width; callers that change the message reset CachedForWidth.</summary>
        private void EnsureMeasured(ActiveNotification n, float contentWidth)
        {
            if (_messageStyle == null || n.CachedForWidth == contentWidth)
            {
                return;
            }
            n.CachedFullHeight = _messageStyle.CalcHeight(new GUIContent(n.Message), contentWidth);
            n.CachedDisplayText = TruncateToLines(n.Message, _messageStyle, contentWidth, MaxLines);
            n.CachedForWidth = contentWidth;
        }

        private float ComputeHeight(ActiveNotification n)
        {
            float contentWidth = Width - SeverityBarWidth - (Padding * 2f);
            EnsureMeasured(n, contentWidth);
            float messageHeight = _messageStyle != null ? n.CachedFullHeight : 20f;

            float lineHeight = _messageStyle != null ? _messageStyle.lineHeight : 18f;
            float maxMessageHeight = lineHeight * MaxLines;
            bool truncated = messageHeight > maxMessageHeight;
            n.Truncated = truncated;

            // A huge message (stack traces) would otherwise expand past the screen on hover.
            float expandCeiling = Mathf.Max(maxMessageHeight, Screen.height / LogicalScale() * 0.5f);
            float expandedHeight = Mathf.Min(messageHeight, expandCeiling);

            float visibleMessageHeight = truncated && n.HoverExpandT < 0.999f
                ? Mathf.Lerp(maxMessageHeight, expandedHeight, n.HoverExpandT)
                : expandedHeight;

            float footer = n.OnClick != null ? FooterHeight + 4f : 0f;
            return Padding + HeaderHeight + 6f + visibleMessageHeight + footer + Padding;
        }

        private void DrawNotification(Rect rect, ActiveNotification n, float alpha)
        {
            Color severityColor = ColorFor(n.Severity);
            bool clickable = n.OnClick != null;
            bool hover = n.IsHovered && n.State != NotificationState.Exiting;

            DrawSolid(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), new Color(0f, 0f, 0f, 0.35f * alpha));

            // Background
            Color bg = hover && clickable ? new Color(0.137f, 0.137f, 0.137f, 0.95f) : new Color(0.078f, 0.078f, 0.078f, 0.92f);
            bg.a *= alpha;
            DrawSolid(rect, bg);

            // Severity bar
            Color barColor = severityColor;
            if (hover && clickable)
            {
                barColor = Color.Lerp(severityColor, Color.white, 0.18f);
            }
            barColor.a *= alpha;
            DrawSolid(new Rect(rect.x, rect.y, SeverityBarWidth, rect.height), barColor);

            float contentX = rect.x + SeverityBarWidth + Padding;
            float contentY = rect.y + Padding;
            float contentWidth = rect.width - SeverityBarWidth - (Padding * 2f);

            // Severity icon
            Color prevColor = GUI.color;
            GUI.color = new Color(severityColor.r, severityColor.g, severityColor.b, alpha);
            Rect iconRect = new Rect(contentX, contentY, 16f, HeaderHeight);
            GUI.Label(iconRect, IconFor(n.Severity), _severityIconStyle);

            float closeLeft = rect.x + rect.width - Padding - 16f;

            string badgeText = n.Count > 1 ? "×" + n.Count : null;
            float badgeWidth = badgeText != null ? _countBadgeStyle.CalcSize(new GUIContent(badgeText)).x : 0f;
            float badgeReserve = badgeText != null ? badgeWidth + 6f : 0f;

            // Source pill
            float pillRight = contentX + 16f + 6f;
            float badgeLeft = pillRight;
            if (!string.IsNullOrEmpty(n.Source))
            {
                Vector2 pillSize = _sourcePillStyle.CalcSize(new GUIContent(n.Source));
                float pillW = Mathf.Min(pillSize.x + 12f, Mathf.Max(0f, closeLeft - 6f - badgeReserve - pillRight));
                Rect pillBg = new Rect(pillRight, contentY + ((HeaderHeight - 18f) * 0.5f), pillW, 18f);
                DrawSolid(pillBg, new Color(1f, 1f, 1f, 0.12f * alpha));
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(pillBg, n.Source, _sourcePillStyle);
                badgeLeft = pillRight + pillW + 6f;
            }

            // Duplicate count badge
            if (badgeText != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f * alpha);
                Rect badgeRect = new Rect(badgeLeft, contentY + ((HeaderHeight - 18f) * 0.5f), badgeWidth, 18f);
                GUI.Label(badgeRect, badgeText, _countBadgeStyle);
            }

            // Close button (×)
            GUI.color = new Color(1f, 1f, 1f, 0.7f * alpha);
            Rect closeRect = new Rect(closeLeft, contentY, 16f, HeaderHeight);
            if (GUI.Button(closeRect, "×", _closeButtonStyle))
            {
                Dismiss(n, invokeClick: false);
            }

            // Message
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float messageTop = contentY + HeaderHeight + 6f;
            float messageBottom = rect.y + rect.height - Padding - (clickable ? FooterHeight + 4f : 0f);
            Rect messageRect = new Rect(contentX, messageTop, contentWidth, Mathf.Max(0f, messageBottom - messageTop));
            string displayMessage = n.Message;
            if (n.Truncated && n.HoverExpandT < 0.5f)
            {
                EnsureMeasured(n, contentWidth);
                displayMessage = n.CachedDisplayText;
            }
            GUI.Label(messageRect, displayMessage, _messageStyle);

            // Footer
            if (clickable)
            {
                float footerY = rect.y + rect.height - Padding - FooterHeight;
                Color hint = new Color(1f, 1f, 1f, (hover ? 0.85f : 0.55f) * alpha);
                GUI.color = hint;
                Rect arrowRect = new Rect(rect.x + rect.width - Padding - 20f, footerY - 2f, 20f, FooterHeight + 4f);
                GUI.Label(arrowRect, "→", _footerArrowStyle);

                if (hover)
                {
                    Rect captionRect = new Rect(contentX, footerY, contentWidth - 24f, FooterHeight);
                    GUI.Label(captionRect, "Click to view", _footerHintStyle);
                }
            }
            GUI.color = prevColor;

            // Click body excluding close button; dismisses even without an onClick
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (rect.Contains(Event.current.mousePosition) && !closeRect.Contains(Event.current.mousePosition))
                {
                    Dismiss(n, invokeClick: true);
                    Event.current.Use();
                }
            }
        }

        private void Dismiss(ActiveNotification n, bool invokeClick)
        {
            if (n.State == NotificationState.Exiting)
            {
                return;
            }
            n.State = NotificationState.Exiting;
            if (invokeClick && n.OnClick != null)
            {
                try
                {
                    n.OnClick();
                }
                catch (ExitGUIException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Exception("Notification onClick handler threw", ex);
                }
            }
        }

        private static Color ColorFor(NotificationSeverity severity)
        {
            switch (severity)
            {
                case NotificationSeverity.Error: return new Color(0.902f, 0.271f, 0.271f, 1f);
                case NotificationSeverity.Warning: return new Color(0.902f, 0.710f, 0.235f, 1f);
                default: return new Color(0.353f, 0.714f, 0.902f, 1f);
            }
        }

        private static string IconFor(NotificationSeverity severity)
        {
            switch (severity)
            {
                case NotificationSeverity.Error: return "✕";
                case NotificationSeverity.Warning: return "!";
                default: return "i";
            }
        }

        private static string TruncateToLines(string message, GUIStyle style, float width, int maxLines)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }
            float maxHeight = style.lineHeight * maxLines;
            if (style.CalcHeight(new GUIContent(message + "…"), width) <= maxHeight)
            {
                return message;
            }

            // Longest prefix that still fits, assuming height is monotonic in cut length.
            int low = 1;
            int high = message.Length - 1;
            int best = 1;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                if (style.CalcHeight(new GUIContent(message.Substring(0, mid) + "…"), width) <= maxHeight)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            string cut = message.Substring(0, best).TrimEnd();
            // Never end inside a rich-text tag fragment.
            int lastOpen = cut.LastIndexOf('<');
            if (lastOpen >= 0 && cut.IndexOf('>', lastOpen) < 0)
            {
                cut = cut.Substring(0, lastOpen).TrimEnd();
            }
            return cut + "…";
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (_stylesBuilt)
            {
                RetryFontResolution();
                return;
            }

            // A null font is fine on a GUIStyle; it falls back to the default skin font.
            Font bodyFont = FontManager.TryGetFont(BroforceFont.Arial);
            Font titleFont = FontManager.TryGetFont(BroforceFont.AndrewFootit);
            Font iconFont = FontManager.TryGetFont(BroforceFont.Pixel04B);
            _fontsResolved = bodyFont != null && titleFont != null && iconFont != null;

            if (_messageStyle == null)
            {
                _messageStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = 12,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    font = bodyFont
                };
                _messageStyle.normal.textColor = Color.white;
            }
            if (_sourcePillStyle == null)
            {
                _sourcePillStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(6, 6, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Clip,
                    font = titleFont
                };
                _sourcePillStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }
            if (_countBadgeStyle == null)
            {
                _countBadgeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    clipping = TextClipping.Clip,
                    font = iconFont
                };
                _countBadgeStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }
            if (_severityIconStyle == null)
            {
                _severityIconStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    fontStyle = FontStyle.Bold,
                    font = iconFont
                };
            }
            if (_closeButtonStyle == null)
            {
                _closeButtonStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    fontStyle = FontStyle.Bold,
                    font = iconFont
                };
                _closeButtonStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
                _closeButtonStyle.hover.textColor = Color.white;
            }
            if (_footerHintStyle == null)
            {
                _footerHintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    font = bodyFont
                };
            }
            if (_footerArrowStyle == null)
            {
                _footerArrowStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    fontStyle = FontStyle.Bold,
                    font = bodyFont
                };
            }

            _stylesBuilt = true;
        }

        /// <summary>Styles built before the fonts loaded keep the default skin font; patch them in as they appear.</summary>
        private void RetryFontResolution()
        {
            if (_fontsResolved || Time.unscaledTime - _lastFontRetry < FontRetryInterval)
            {
                return;
            }
            _lastFontRetry = Time.unscaledTime;

            Font bodyFont = FontManager.TryGetFont(BroforceFont.Arial);
            Font titleFont = FontManager.TryGetFont(BroforceFont.AndrewFootit);
            Font iconFont = FontManager.TryGetFont(BroforceFont.Pixel04B);

            if (bodyFont != null && _messageStyle.font != bodyFont)
            {
                _messageStyle.font = bodyFont;
                _footerHintStyle.font = bodyFont;
                _footerArrowStyle.font = bodyFont;
                // Cached heights were measured against the previous font.
                for (int i = 0; i < _active.Count; i++)
                {
                    _active[i].CachedForWidth = -1f;
                }
            }
            if (titleFont != null)
            {
                _sourcePillStyle.font = titleFont;
            }
            if (iconFont != null)
            {
                _severityIconStyle.font = iconFont;
                _closeButtonStyle.font = iconFont;
                _countBadgeStyle.font = iconFont;
            }

            _fontsResolved = bodyFont != null && titleFont != null && iconFont != null;
        }
    }

    internal enum NotificationState
    {
        Entering,
        Visible,
        Exiting
    }

    internal class ActiveNotification
    {
        public string Message;
        public NotificationSeverity Severity;
        public float Remaining;
        public string Source;
        public Action OnClick;
        public int Count = 1;
        public bool IsSummary;
        public int OverflowCount;

        public NotificationState State;
        public float EnterT;
        public float FadeT;
        public float HoverExpandT;

        public float Height;
        public float CurrentY;
        public float TargetY;
        public bool Initialized;

        public bool IsHovered;
        public bool Truncated;

        public string CachedDisplayText;
        public float CachedFullHeight;
        public float CachedForWidth = -1f;
    }

    [HarmonyPatch(typeof(MainMenu), "Awake")]
    internal static class NotificationGate_MainMenu_Awake_Patch
    {
        private static void Postfix()
        {
            NotificationHost._mainMenuReached = true;
        }
    }
}
