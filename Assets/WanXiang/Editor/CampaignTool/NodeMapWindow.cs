// ============================================================================
//  万相 · 节气节点图（灰盒窗口）
//  ---------------------------------------------------------------------------
//  菜单：万相/节气/节点图。GDD STEP 3「二十四节气节点图与分叉路径」的人工判据：
//
//      「节点名本身就在向玩家预告战场」—— 那么先把这张图摆出来，让测试者
//      走一遍：看得到本幕 6 个节点、我选了哪 4 个、放弃了哪 2 个、
//      这一场的场地天时是什么、上一季的余气还压着几个节点。
//
//  它跑的是**真链路**：RunState（路径/余气）+ RunDriver（组敌队+跑战斗）+
//  WeatherCatalog（节气天时）+ ContentCatalog 的 30 只异兽（若有）。
//  窗口只负责画，要显示什么在 NodeMapView 里算（那一层能被自检断言）。
//
//  ⚠ 灰盒窗口不进 Play 模式：本机编辑器的播放器循环不可靠（见项目记忆），
//    而这些全是纯计算 —— Edit 模式下点一下就把一场战斗跑完了。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;
using WanXiang.Editor.WeatherTool;
using WanXiang.Fusion;

namespace WanXiang.Editor.CampaignTool
{
    public sealed class NodeMapWindow : EditorWindow
    {
        private static readonly Color PanelBg = new Color(0.96f, 0.94f, 0.90f);   // 宣纸色
        private static readonly Color InkText = new Color(0.16f, 0.13f, 0.09f);
        private static readonly Color FadeText = new Color(0.45f, 0.41f, 0.35f);

        private ActGraph[] _acts;
        private BeastDef[] _beasts;
        private DeployEntry[] _playerSquad;
        private ICampaignContent _content;
        private RunDriver _driver;

        private ulong _seed = 20260914UL;
        private string _seedText = "20260914";
        private int _forced = -1;          // 玩家点分支时塞给选路器的下标
        private Vector2 _scroll;
        private string _loadNote;

        [MenuItem("万相/节气/节点图（灰盒）")]
        public static void Open()
        {
            var w = GetWindow<NodeMapWindow>("节气节点图");
            w.minSize = new Vector2(560f, 640f);
            w.Rebuild();
        }

        private void OnEnable() => Rebuild();

        // ================================================================
        //  装配
        // ================================================================

        private void Rebuild()
        {
            _acts = SolarTermGraph.BuildDefault();
            _beasts = LoadBeasts();
            _playerSquad = BuildPlayerSquad(_beasts);
            _content = BuildContent(_acts, _beasts);
            ResetRun();
            _loadNote = _beasts != null && _beasts.Length > 0
                ? $"内容：{_beasts.Length} 只异兽（ContentCatalog）"
                : "内容：未找到 ContentCatalog ⇒ 用灰盒样本单位";
        }

        private static BeastDef[] LoadBeasts()
        {
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids == null || guids.Length == 0) return System.Array.Empty<BeastDef>();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (catalog == null) return System.Array.Empty<BeastDef>();
            var beasts = ContentLibrary.BuildBeasts(catalog);
            return beasts ?? System.Array.Empty<BeastDef>();
        }

        /// <summary>我方阵容：按五职业各取一只（内容里没有该职业就顺延），站 0/1/4/7/8。</summary>
        private static DeployEntry[] BuildPlayerSquad(BeastDef[] beasts)
        {
            var roles = new[] { RoleType.Guard, RoleType.Striker, RoleType.Caster, RoleType.Support, RoleType.Swift };
            var slots = new[] { 0, 1, 4, 7, 8 };
            var picked = new List<BeastDef>(5);
            if (beasts != null)
            {
                foreach (var role in roles)
                    foreach (var b in beasts)
                        if (b.Role == role && !picked.Contains(b)) { picked.Add(b); break; }
                foreach (var b in beasts)
                    if (picked.Count < 5 && !picked.Contains(b)) picked.Add(b);
            }
            var result = new DeployEntry[picked.Count];
            for (int i = 0; i < picked.Count; i++)
                result[i] = DeployEntry.Player(picked[i], slots[System.Math.Min(i, slots.Length - 1)]);
            return result;
        }

        /// <summary>敌方：每幕取"该幕五行"的异兽池，守关取同名异兽（内容里没有就退回池中第一只）。</summary>
        private static ICampaignContent BuildContent(ActGraph[] acts, BeastDef[] beasts)
        {
            if (beasts == null || beasts.Length == 0) return new SeededEnemyProvider(_ => null);
            return new SeededEnemyProvider(
                act => PoolForAct(acts[act - 1], beasts),
                act => FindBoss(acts[act - 1], beasts));
        }

        private static BeastDef[] PoolForAct(ActGraph g, BeastDef[] beasts)
        {
            var list = new List<BeastDef>(10);
            foreach (var b in beasts) if (b.Element == g.SeasonElement) list.Add(b);
            if (list.Count == 0) list.AddRange(beasts);      // 该幕五行没有内容 ⇒ 退回全池
            return list.ToArray();
        }

        private static BeastDef FindBoss(ActGraph g, BeastDef[] beasts)
        {
            foreach (var b in beasts) if (b.DisplayName == g.BossName) return b;
            return null;
        }

        private void ResetRun()
        {
            _forced = -1;
            var content = _content;
            // 选路器：窗口点分支时用 _forced；否则走确定性默认（第一个合法节点）。
            NodeChooser chooser = (g, layer) =>
            {
                int v = _forced;
                _forced = -1;
                return v >= 0 ? v : g.Layers[layer][0];
            };
            _driver = new RunDriver(BattleConfig.Default, _acts, WeatherCatalog.GetSolarTerm,
                                    content, _seed, chooser);
        }

        // ================================================================
        //  绘制
        // ================================================================

        private void OnGUI()
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = PanelBg;
            EditorGUILayout.BeginVertical(Box());
            GUI.backgroundColor = prevBg;

            DrawHeader();
            EditorGUILayout.Space(4f);

            if (_driver == null)
            {
                EditorGUILayout.HelpBox("没有可选的内容（先跑 万相/融合/① 导入内容）。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawStatus();
            DrawActMap();
            DrawChoices();
            DrawLingers();
            DrawRecords();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("局种子", GUILayout.Width(48f));
                _seedText = GUILayout.TextField(_seedText, GUILayout.Width(120f));
                if (GUILayout.Button("重建这一局", GUILayout.Width(96f)))
                {
                    if (ulong.TryParse(_seedText, out var s)) _seed = s;
                    ResetRun();
                }
                if (GUILayout.Button("自动跑完整局", GUILayout.Width(110f)))
                    _driver.Play(_playerSquad);
                if (GUILayout.Button("本幕跑完（到守关前）", GUILayout.Width(150f)))
                {
                    while (_driver.Outcome == RunOutcome.InProgress && !_driver.State.AtBoss)
                        if (_driver.PlayOneBattle(_playerSquad) == null) break;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(_loadNote ?? "", Label(FadeText));
            }
        }

        private void DrawStatus()
        {
            GUILayout.Label("── 当前状态 ──────────────────────────", Label(FadeText));
            GUILayout.Label(NodeMapView.StatusLine(_driver.State, _driver.Outcome), Label(InkText));
        }

        private void DrawActMap()
        {
            var st = _driver.State;
            var g = st.CurrentGraph;
            GUILayout.Label($"── 第 {st.CurrentAct} 幕 · {g.SeasonCn}（守关 {g.BossName}）"
                          + " ──   ✔ 已过　◀ 当前　○ 可选　· 未开放", Label(FadeText));
            foreach (var line in NodeMapView.ActLines(g, st, WeatherCatalog.GetSolarTerm))
                GUILayout.Label(line, Label(InkText));
        }

        private void DrawChoices()
        {
            var st = _driver.State;
            if (_driver.Outcome != RunOutcome.InProgress) return;

            if (st.AtBoss)
            {
                string bossName = st.Finished ? "" : st.CurrentGraph.BossName;
                if (!st.Finished && GUILayout.Button($"⚔ 迎战守关：{bossName}（本场无节点天时）"))
                    _driver.PlayOneBattle(_playerSquad);
                return;
            }

            GUILayout.Space(4f);
            GUILayout.Label("── 选择下一步（肉鸽的核心资源是「放弃权」：本幕有 2 个节点永远走不到）──", Label(FadeText));
            var nextIndex = _driver.Records.Count + 1;
            foreach (var offset in NodeMapView.LegalNextOffsets(st.CurrentGraph, st))
            {
                int term = st.CurrentGraph.Terms[offset];
                var squad = _content?.EnemiesFor(st.CurrentAct, term, false,
                                                 _driver.SeedFor(st.CurrentAct, term, nextIndex));
                string label = $"○ 进入 {NodeMapView.NodeLabel(st.CurrentGraph, offset, WeatherCatalog.GetSolarTerm)}"
                             + $"　敌方：{NodeMapView.EnemyPreview(squad)}";
                if (GUILayout.Button(label))
                {
                    _forced = offset;                       // 让选路器选它
                    _driver.PlayOneBattle(_playerSquad);
                }
            }
        }

        private void DrawLingers()
        {
            GUILayout.Space(4f);
            GUILayout.Label("── 余气（上一季残留：强度减半、残留 2 个节点） ──", Label(FadeText));
            foreach (var line in NodeMapView.LingerLines(_driver.State))
                GUILayout.Label(line, Label(InkText));
        }

        private void DrawRecords()
        {
            GUILayout.Space(4f);
            GUILayout.Label($"── 战斗记录（{_driver.Records.Count} 场） ──", Label(FadeText));
            foreach (var line in NodeMapView.RecordLines(_driver.Records))
                GUILayout.Label(line, Label(InkText));
        }

        // ---- 小工具 ----

        private static GUIStyle _ink, _fade;
        private static GUIStyle Label(Color c)
        {
            var style = c == InkText ? _ink : _fade;
            if (style == null)
            {
                style = new GUIStyle(EditorStyles.label) { wordWrap = true };
                if (c == InkText) _ink = style; else _fade = style;
            }
            style.normal.textColor = c;
            return style;
        }

        private static GUIStyle Box()
        {
            return new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 8) };
        }
    }
}
