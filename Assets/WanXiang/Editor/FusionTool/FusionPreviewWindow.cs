// ============================================================================
//  万相 · 融合 · 预览窗口（灰盒，程序化占位图）
//  ---------------------------------------------------------------------------
//  菜单：万相/融合/融合预览。GDD STEP 2 验收③的人工判据：
//  「切换灵魂时，立绘的辉光与睛色实时变化，无需重载场景」。
//
//  画的是**程序化占位图**（美术方案 2 章的降级方案）：
//      宿主原色主体 + 纹样色 + 灵魂辉光罩 + 睛色光点 + 恒墨描边
//  没有任何美术资产 —— 颜色全部来自融合数据（FusionRules.Fuse 的输出）。
//  切换灵魂选择器时**当场重融合、当场重绘**，不重载任何场景 / 资产 ——
//  这正是验收③要的「实时」。
//
//  立绘就位后，这个窗口的绘制区替换为「宿主遮罩图 × 灵魂色板」的
//  Sprite 换色即可，数据管线（宿主/灵魂选择 → 融合 → 色区）原样保留。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Editor.FusionTool
{
    public sealed class FusionPreviewWindow : EditorWindow
    {
        private ContentCatalogSO _catalog;
        private BeastDef[] _beasts;
        private SoulDef[] _souls;
        private SkillResolver _resolver;

        private int _hostIndex;
        private int _soulIndex;
        private BeastDef _fused;

        private static readonly Color PanelBg = new Color(0.96f, 0.94f, 0.90f);   // 宣纸色
        private static readonly Color InkText = new Color(0.16f, 0.13f, 0.09f);

        [MenuItem("万相/融合/融合预览")]
        public static void Open()
        {
            var w = GetWindow<FusionPreviewWindow>("融合预览");
            w.minSize = new Vector2(520f, 620f);
            w.ReloadContent();
        }

        private void OnEnable() => ReloadContent();

        private void ReloadContent()
        {
            _catalog = FindCatalog();
            if (_catalog == null)
            {
                _beasts = System.Array.Empty<BeastDef>();
                _souls = System.Array.Empty<SoulDef>();
                _fused = null;
                return;
            }
            _beasts = ContentLibrary.BuildBeasts(_catalog);
            _souls = ContentLibrary.BuildSouls(_catalog);
            _resolver = ContentLibrary.ResolverFrom(_catalog);
            _hostIndex = System.Math.Min(_hostIndex, System.Math.Max(0, _beasts.Length - 1));
            _soulIndex = System.Math.Min(_soulIndex, System.Math.Max(0, _souls.Length - 1));
            Refuse();
        }

        private static ContentCatalogSO FindCatalog()
        {
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void Refuse()
        {
            if (_beasts == null || _beasts.Length == 0 || _souls == null || _souls.Length == 0)
            {
                _fused = null;
                return;
            }
            _fused = FusionRules.Fuse(_beasts[_hostIndex], _souls[_soulIndex], _resolver);
        }

        private void OnGUI()
        {
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "未找到内容目录。请先执行菜单「万相/融合/① 导入异兽内容」。",
                    MessageType.Warning);
                if (GUILayout.Button("执行导入"))
                {
                    foreach (var l in BeastContentImporter.Import()) Debug.Log("[内容导入] " + l);
                    ReloadContent();
                }
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("宿主（外形与骨架）", EditorStyles.boldLabel);
            int newHost = EditorGUILayout.Popup(_hostIndex, NameList(_beasts));
            EditorGUILayout.LabelField("灵魂（元气与改写）", EditorStyles.boldLabel);
            int newSoul = EditorGUILayout.Popup(_soulIndex, NameList(_souls));

            if (newHost != _hostIndex || newSoul != _soulIndex)
            {
                _hostIndex = newHost;
                _soulIndex = newSoul;
                Refuse();   // 切换即重融合 —— 不重载场景，不重载资产
            }

            EditorGUILayout.Space(6);
            if (_fused == null)
            {
                EditorGUILayout.HelpBox("没有可融合的内容。", MessageType.Info);
                return;
            }

            // ---- 程序化占位图（占位美术：颜色全部来自融合数据） ----
            var placeholderRect = GUILayoutUtility.GetRect(220f, 220f, GUILayout.ExpandWidth(false));
            DrawPlaceholder(placeholderRect, _fused.Palette);

            GUILayout.Space(6);

            EditorGUILayout.LabelField(_fused.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("五行", Cn.Of(_fused.Element));
            EditorGUILayout.LabelField("职业 / 稀有度",
                $"{Cn.Of(_fused.Role)} / {Cn.Of(_fused.Rarity)}（继承宿主）");
            EditorGUILayout.LabelField("特性", _fused.Trait.Name);
            EditorGUILayout.LabelField("　描述", _fused.Trait.Description);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("技能组（宿主骨架 + 灵魂改写）", EditorStyles.boldLabel);
            SkillRow("普攻", _fused.Basic);
            SkillRow("战技", _fused.Active);
            SkillRow("绝技", _fused.Ultimate);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("色区（五通道）", EditorStyles.boldLabel);
            DrawPaletteRow("主体", _fused.Palette.BodyMain);
            DrawPaletteRow("纹样", _fused.Palette.BodyAccent);
            DrawPaletteRow("辉光", _fused.Palette.EnergyGlow);
            DrawPaletteRow("睛色", _fused.Palette.EyeCore);
            DrawPaletteRow("描边", _fused.Palette.Outline);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "验收③：切换上面的「灵魂」选择器 —— 辉光与睛色应立刻变化（重绘即变，" +
                "无需重载场景）。立绘就位后由宿主遮罩图 × 灵魂色板替换占位图。",
                MessageType.Info);
        }

        // ====================================================================

        // 列表很小（30 项），直接每帧重建，不做缓存 —— 缓存键不好选，
        // 之前按数量缓存会在「宿主/灵魂都是 30 项」时串标签。

        private string[] NameList(BeastDef[] list) => LabelList(list, b =>
            $"{b.DisplayName}（{Cn.Of(b.Element)}{Cn.Of(b.Role)}·{Cn.Of(b.Rarity)}）");

        private string[] NameList(SoulDef[] list) => LabelList(list, s =>
            $"{s.DisplayName}（{Cn.Of(s.ElementOverride)}·{s.Epithet}）");

        private static string[] LabelList<T>(IList<T> list, System.Func<T, string> label)
        {
            var arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                arr[i] = item == null ? "（空）" : label(item);
            }
            return arr;
        }

        private void SkillRow(string slot, SkillDef skill)
        {
            if (skill == null)
            {
                EditorGUILayout.LabelField(slot, "（缺）");
                return;
            }
            EditorGUILayout.LabelField(
                $"{slot}　{skill.Name}（CD {skill.Cd}）",
                new GUIStyle(EditorStyles.label) { normal = { textColor = InkText } });
        }

        private void DrawPaletteRow(string label, string hex)
        {
            var rect = EditorGUILayout.GetControlRect(GUILayout.Height(18f));
            rect.width = 24f;
            EditorGUI.DrawRect(rect, ColorFromHex(hex, Color.gray));
            var textRect = new Rect(rect.xMax + 6f, rect.y, 300f, rect.height);
            EditorGUI.LabelField(textRect, $"{label}　{hex}");
        }

        private static Color ColorFromHex(string hex, Color fallback)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        // ====================================================================
        //  程序化占位图：主体 / 纹样 / 辉光 / 睛色 / 描边 —— 五个色区各就各位
        // ====================================================================

        private static void DrawPlaceholder(Rect r, PaletteHex p)
        {
            EditorGUI.DrawRect(r, PanelBg);

            float cx = r.x + r.width / 2f;
            float cy = r.y + r.height / 2f;
            float bodyW = r.width * 0.42f;
            float bodyH = r.height * 0.60f;

            var outline = ColorFromHex(p.Outline, Color.black);
            var body = ColorFromHex(p.BodyMain, Color.gray);
            var accent = ColorFromHex(p.BodyAccent, Color.gray);
            var glow = ColorFromHex(p.EnergyGlow, Color.cyan);
            var eye = ColorFromHex(p.EyeCore, Color.yellow);

            // 辉光罩（灵魂元气层）—— 半透明外圈，画在身体后面。
            float glowPad = r.width * 0.06f;
            DrawGlowRing(cx, cy, bodyW / 2f + glowPad, bodyH / 2f + glowPad, glow);

            // 身体（宿主主体色）+ 恒墨描边。
            var bodyRect = new Rect(cx - bodyW / 2f, cy - bodyH / 2f, bodyW, bodyH);
            EditorGUI.DrawRect(Inflate(bodyRect, 2f), outline);
            EditorGUI.DrawRect(bodyRect, body);

            // 纹样（灵魂偏色后的 accent）—— 三道斜纹示意。
            float stripeW = bodyW * 0.10f;
            for (int i = 0; i < 3; i++)
            {
                float sx = bodyRect.x + bodyW * (0.18f + 0.28f * i);
                EditorGUI.DrawRect(new Rect(sx, bodyRect.y + bodyH * 0.15f, stripeW, bodyH * 0.70f), accent);
            }

            // 眼睛（灵魂睛色）—— 两点。
            float eyeY = bodyRect.y + bodyH * 0.22f;
            float eyeR = System.Math.Max(2f, bodyW * 0.05f);
            EditorGUI.DrawRect(new Rect(cx - bodyW * 0.16f - eyeR, eyeY - eyeR, eyeR * 2f, eyeR * 2f), eye);
            EditorGUI.DrawRect(new Rect(cx + bodyW * 0.16f - eyeR, eyeY - eyeR, eyeR * 2f, eyeR * 2f), eye);
        }

        private static Rect Inflate(Rect r, float d)
            => new Rect(r.x - d, r.y - d, r.width + d * 2f, r.height + d * 2f);

        /// <summary>辉光：多层半透明矩形外扩（灰盒级别的"发光"示意）。</summary>
        private static void DrawGlowRing(float cx, float cy, float rx, float ry, Color glow)
        {
            for (int i = 4; i >= 1; i--)
            {
                float k = 1f + 0.16f * i;
                var c = glow;
                c.a = 0.10f * (5 - i) / 4f;
                EditorGUI.DrawRect(new Rect(cx - rx * k, cy - ry * k, rx * 2f * k, ry * 2f * k), c);
            }
        }
    }
}
