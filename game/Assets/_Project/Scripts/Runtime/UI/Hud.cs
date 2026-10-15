using NightWorld.Player;
using UnityEngine;
using UnityEngine.UI;

namespace NightWorld.UI
{
    /// <summary>
    /// The hero's HP bar, level, XP bar and gold in the top-left corner. Builds its own Screen Space
    /// Overlay canvas from code in Awake, so there's no prefab or scene YAML. Layout, in pixels at
    /// 1920×1080 (the CanvasScaler scales it to the real screen):
    ///   Hud            (Canvas + CanvasScaler)
    ///    ├── HpBar     (dark back) ── Fill (red), HpText "75 / 100"
    ///    ├── LevelText "Lv 2"
    ///    ├── XpBar     (dark back) ── Fill (yellow)
    ///    └── GoldText  "42 g"
    /// Created and bound by GameBootstrap. Overlay UI doesn't show up in zone screenshots.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static readonly Color HpColor = new Color(0.9f, 0.15f, 0.15f, 1f);
        public static readonly Color XpColor = new Color(1f, 0.85f, 0.1f, 1f);
        public static readonly Color GoldColor = new Color(1f, 0.8f, 0.2f, 1f);
        private static readonly Color BarBackColor = new Color(0f, 0f, 0f, 0.6f);

        /// <summary>The stats shown, set by <see cref="Bind"/>.</summary>
        public PlayerStats Stats { get; private set; }

        public Image HpFill { get; private set; }
        public Image XpFill { get; private set; }
        public Text HpText { get; private set; }
        public Text LevelText { get; private set; }
        public Text GoldText { get; private set; }

        // A filled Image only fills with a sprite; Unity has no built-in runtime UI sprite, so make one.
        private static Sprite whiteSprite;
        private Font font;

        private void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (whiteSprite == null)
            {
                var white = Texture2D.whiteTexture;
                whiteSprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height), new Vector2(0.5f, 0.5f));
            }

            HpFill = AddBar("HpBar", new Vector2(24f, -24f), new Vector2(380f, 40f), HpColor);
            HpText = AddText("HpText", HpFill.transform.parent, Vector2.zero, new Vector2(380f, 40f), 26, TextAnchor.MiddleCenter, Color.white);
            LevelText = AddText("LevelText", transform, new Vector2(24f, -72f), new Vector2(90f, 32f), 28, TextAnchor.MiddleLeft, Color.white);
            XpFill = AddBar("XpBar", new Vector2(114f, -76f), new Vector2(290f, 24f), XpColor);
            GoldText = AddText("GoldText", transform, new Vector2(24f, -112f), new Vector2(200f, 36f), 28, TextAnchor.MiddleLeft, GoldColor);
        }

        /// <summary>Show these stats from now on. The HUD polls them every LateUpdate.</summary>
        public void Bind(PlayerStats stats)
        {
            Stats = stats;
            Refresh();
        }

        // ponytail: polls every frame; switch to Health/PlayerProgression events if it ever shows up in profiling.
        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (Stats == null || Stats.Health == null || Stats.Progression == null) return;
            var health = Stats.Health;
            var progression = Stats.Progression;

            HpFill.fillAmount = (float)health.Current / health.Max;
            HpText.text = $"{health.Current} / {health.Max}";
            LevelText.text = $"Lv {progression.Level}";
            XpFill.fillAmount = progression.IsMaxLevel ? 1f : (float)progression.Xp / progression.XpToNext;
            GoldText.text = $"{progression.Gold} g";
        }

        /// <summary>A dark bar with a coloured fill inset by 3 px; returns the fill.</summary>
        private Image AddBar(string name, Vector2 position, Vector2 size, Color color)
        {
            var back = AddRect(name, transform, position, size).gameObject.AddComponent<Image>();
            back.color = BarBackColor;

            var fillRect = AddRect("Fill", back.transform, Vector2.zero, Vector2.zero);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);

            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.sprite = whiteSprite;
            fill.color = color;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            return fill;
        }

        private Text AddText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            var text = AddRect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // A dark outline keeps the text readable on bright grass and sky.
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return text;
        }

        /// <summary>A child RectTransform anchored and pivoted at its parent's top-left corner.</summary>
        private static RectTransform AddRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
