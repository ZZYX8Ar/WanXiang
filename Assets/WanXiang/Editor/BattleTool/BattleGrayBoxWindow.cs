// ============================================================================
//  万相 · 编辑器工具 · 战斗灰盒预览（菜单：万相/战斗/灰盒预览）
//  ---------------------------------------------------------------------------
//  它存在的唯一目的是完成 GDD 7.1 验证顺序的第 0 步：
//
//      「0. 战斗好不好看（灰盒 + 1 木 vs 1 火，看 20 遍）」
//
//  以及第 7 章验收标准②「灰盒状态下（纯色方块，无任何美术）连续看 10 场不觉得无聊」。
//  这两条**都是人的判断**，工具能做的只是把料备齐、把试错成本压到最低：
//
//    - 逐帧步进 / 变速连播 / 一键换种子重开 / 勾选连播自动跑下一场
//    - 四个五行系数就地可调（验收③：改了立刻看得见）
//    - 棋盘上直接画出相生（鎏金实线）与相克（墨色虚线）的格对连线
//    - **全程只读 ViewFrame 快照**，不自己算任何战斗规则
//
//  ⚠ 为什么是 EditorWindow 而不是运行时 UI：
//    ① 这台机器的编辑器不维持 Play 模式的播放器循环（见项目记忆），起 Play 看不了；
//       而 EditorApplication.update 是可靠的（诊断通道一直靠它活着）。
//    ② 灰盒是**验证手段**，不是产品。做成运行时 UI 会先欠下一堆
//       Prefab/Canvas/Sprite 的债，等美术定了又得重做。
//    正式表现层（WanXiang.Modules.Battle）等有美术方向后再做，那时它读的
//    还是同一份 ViewFrame 流，所以这一步的投入不会白费。
//
//  ⚠ 回放是**累积式**的：ViewFrame 存的是"相对上一帧变了什么"（增量），
//    要画出第 k 帧，必须把 0..k 全部叠起来。所以这里维护一个累积状态，
//    按帧序前进时只叠差量；往回拖才从头重建。第 0 帧是全量帧，就是这个机制的起点。
//
//  色板取自 Docs/Design/data/palette.json，**不自己编颜色** ——
//  灰盒也要用"宣纸 + 墨 + 五行主体色"，否则看不出画面整体的色彩关系。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;

namespace WanXiang.Editor.BattleTool
{
    public class BattleGrayBoxWindow : EditorWindow
    {
        // ================================================================
        //  色板与绘制原语
        //  ----------------------------------------------------------------
        //  它们现在住在 WanXiang.Modules.Battle 里，**运行时棋盘视图读的是同一份**。
        //  这里原先自己抄了一份颜色常量，那是典型的分叉写法：
        //  改了运行时视图的绿、忘了改窗口的绿，就会出现"编辑器里看着对、游戏里不对"，
        //  而灰盒的全部意义就是"眼睛看到的算数"。
        //
        //  短别名仅为了让下面的绘制代码可读；不要再往这里加颜色。
        // ================================================================

        private static readonly Color Ink = BattlePalette.Ink;
        private static readonly Color InkSoft = BattlePalette.InkSoft;
        private static readonly Color Paper = BattlePalette.Paper;
        private static readonly Color Silk = BattlePalette.Silk;
        private static readonly Color Gold = BattlePalette.Gold;
        private static readonly Color Crimson = BattlePalette.Crimson;
        private static readonly Color Shield = BattlePalette.Vital;
        private static readonly Color Dead = BattlePalette.Dead;
        private static Color[] ElementColor => BattlePalette.Element;

        private static Color Mix(Color a, Color b, float t) => BattlePalette.Mix(a, b, t);
        private static void DrawSeg(Vector2 a, Vector2 b, Color c, float t, bool dashed)
            => BattleHud.Segment(a, b, c, t, dashed);
        private static Vector2 CellCenter(Rect area, int index, float cellW, float cellH, float gap)
            => BattleHud.CellCentre(area, index, cellW, cellH, gap);
        private static void DrawBorder(Rect r, Color c, float t) => BattleHud.Border(r, c, t);
        private static void RectFill(Rect r, Color c) => BattleHud.Fill(r, c);

        // ================================================================
        //  状态
        // ================================================================

        private BattleState _state;
        private BattleResult _result;

        /// <summary>界面上可调的参数。每场战斗拿它的 <c>Clone()</c>，
        /// 所以在播放中拖滑块不会污染正在跑的这一局。</summary>
        private BattleConfig _cfg = BattleConfig.Default;

        private int _scenario;
        private string _seedText = "20260914";
        private int _battleIndex;

        private int _frameCursor;
        private bool _playing;
        private float _framesPerSecond = 8f;
        private double _accum;
        private double _lastTick;
        private double _nextBattleAt = -1;
        private bool _autoNext = true;

        /// <summary>
        /// 待执行的重生成请求。**不在按钮回调里直接重建**，而是记一个标记、等
        /// OnGUI 画完再执行：重建会换掉 _state，而当前这一遍 OnGUI 手里还拿着旧的
        /// _state 在往下画（棋盘、事件流、汇总），中途换掉就会读到已经对不上的下标。
        /// 用标记把"改状态"和"画界面"错开，比到处写 GUIUtility.ExitGUI() 干净。
        /// </summary>
        private bool _pendingRegenerate;

        // 字体样式由 BattleHud.Begin() 统一构建（见 OnGUI 开头），不再各自 new ——
        // 编辑器与运行时用同一套字号基准，灰盒才"看到什么就是什么"。

        // 累积回放状态
        private readonly Dictionary<string, UnitSnapshot> _viewState = new Dictionary<string, UnitSnapshot>();
        private int _viewStateFrame = -1;

        // 相邻格对（位置在 STEP 1 不变化，所以只在生成时算一次）
        // ⚠ 类型来自核心层的 BoardPairScan —— 与运行时棋盘视图共用同一套判据。
        //   画线和结算必须由同一个函数推导，否则会出现"画了相冲线但什么都没结算"。
        private readonly List<AdjacentPair>[] _pairs =
            { new List<AdjacentPair>(), new List<AdjacentPair>() };

        [MenuItem("万相/战斗/灰盒预览", priority = 100)]
        public static void Open()
        {
            var w = GetWindow<BattleGrayBoxWindow>("战斗灰盒");
            w.minSize = new Vector2(1090, 730);
            w.Show();
        }

        /// <summary>
        /// 诊断通道入口（命令 <c>battle.graybox</c>）。存在的理由是：需要一个
        /// **不需要人坐在电脑前**的自检 —— 窗口第一次打开时报的错，
        /// 往往是空引用或布局越界这类只有渲染才会触发的问题，
        /// 而"打开窗口 → 读控制台"这件事本身是可以自动化的。
        ///
        /// ⚠ 用 <c>delayCall</c> 延迟开窗：这个入口是从 EditorApplication.update
        ///   里被调用的，那个时机不在 GUI 事件中，直接 GetWindow/Show 有风险。
        /// </summary>
        public static string[] Run(string command)
        {
            // 先预跑一局，确认数据链路是通的，再把窗口排进队列
            var probe = BattleSampleContent.BuildScenario(0, 7UL, BattleConfig.Default);
            var r = BattleSimulator.Run(probe);

            EditorApplication.delayCall += Open;

            return new[]
            {
                "已把「战斗灰盒」窗口排入打开队列（菜单：万相/战斗/灰盒预览）",
                $"预跑 1v1 数据链路：{OutcomeText(r.Outcome)}，{r.Turns} 回合，" +
                $"{r.EventCount} 条事件，{probe.Frames.Count} 帧",
                $"五行矩阵自检：{ElementMatrix.SelfCheck(ElementCoefficients.Default) ?? "通过"}",
                "回放是累积式的：第 0 帧是全量帧，画第 k 帧 = 把 0..k 的差量叠起来；" +
                "表现层因此完全不需要自己算战斗规则。",
            };
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            if (_state == null) Regenerate();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        // ================================================================
        //  生成与驱动
        // ================================================================

        private ulong ParsedSeed()
        {
            ulong v;
            if (!ulong.TryParse(_seedText, out v)) v = 20260914UL;
            return v;
        }

        private void Prepare(ulong seed)
        {
            _accum = 0;
            _nextBattleAt = -1;
            _lastTick = EditorApplication.timeSinceStartup;

            _state = BattleSampleContent.BuildScenario(_scenario, seed, _cfg.Clone());
            _result = BattleSimulator.Run(_state);
            _frameCursor = 0;
            RebuildViewState();
            BuildPairs();
            Repaint();
        }

        private void Regenerate()
        {
            _battleIndex = 0;
            _playing = false;          // 手动重生成 → 停下来，让人先看清楚
            Prepare(ParsedSeed());
        }

        private void NextBattle()
        {
            _battleIndex++;
            _playing = _autoNext;      // 连播：下一场接着自己跑
            Prepare(ParsedSeed() + (ulong)_battleIndex);
        }

        /// <summary>预先算出相邻格对的关系。位置在 STEP 1 不变（没有换位/融合），
        /// 所以只算一次 —— 每帧重算会让"连线"这种东西变得比战斗本身还贵。
        /// ⚠ 判据来自 BoardPairScan，和战斗结算用的是同一份逻辑。</summary>
        private void BuildPairs()
        {
            BoardPairScan.ScanBoth(_state, _pairs[0], _pairs[1]);
        }

        private void Tick()
        {
            if (_state == null) return;
            double now = EditorApplication.timeSinceStartup;

            // 连播：到末尾后停一拍再开下一场
            if (_nextBattleAt > 0)
            {
                if (now >= _nextBattleAt) NextBattle();
                return;
            }

            if (!_playing) { _lastTick = now; return; }

            double dt = now - _lastTick;
            _lastTick = now;
            // 编辑器卡顿 / 窗口失焦时不要一次跳一大段 —— 回放跳帧比慢放更难看
            if (dt < 0 || dt > 0.5) dt = 0;
            _accum += dt;

            double interval = 1.0 / Mathf.Max(1f, _framesPerSecond);
            int last = _state.Frames.Count - 1;
            bool moved = false;
            int guard = 0;
            while (_accum >= interval && guard++ < 240)
            {
                _accum -= interval;
                if (_frameCursor < last)
                {
                    _frameCursor++;
                    moved = true;
                }
                else
                {
                    if (_autoNext) _nextBattleAt = now + 1.2;
                    else _playing = false;
                    break;
                }
            }

            if (moved) { EnsureViewState(_frameCursor); Repaint(); }
            else if (_nextBattleAt > 0) Repaint();
        }

        // ================================================================
        //  累积回放状态
        // ================================================================

        /// <summary>把 <see cref="_viewState"/> 推进到第 targetFrame 帧（含）。
        /// 往前走只叠差量，往回走才从头重建。</summary>
        private void EnsureViewState(int targetFrame)
        {
            if (_state == null) return;
            var frames = _state.Frames;
            if (frames.Count == 0) { _viewState.Clear(); _viewStateFrame = -1; return; }

            void Apply(int index)
            {
                var changed = frames[index].Changed;
                for (int i = 0; i < changed.Length; i++)
                    _viewState[changed[i].UnitId] = changed[i];
            }

            if (targetFrame < 0)
            {
                _viewState.Clear();
                _viewStateFrame = -1;
                return;
            }

            if (_viewStateFrame < 0 || targetFrame < _viewStateFrame)
            {
                _viewState.Clear();
                for (int i = 0; i <= targetFrame; i++) Apply(i);
            }
            else
            {
                for (int i = _viewStateFrame + 1; i <= targetFrame; i++) Apply(i);
            }
            _viewStateFrame = targetFrame;
        }

        private void RebuildViewState()
        {
            _viewStateFrame = -1;
            EnsureViewState(_frameCursor);
        }

        private bool TrySnap(string unitId, out UnitSnapshot snap)
            => _viewState.TryGetValue(unitId, out snap);

        // ================================================================
        //  绘制
        // ================================================================

        private void EnsureStyles()
        {
            // 编辑器里字号基准取 1（窗口本来就按 1090×730 设计）。
            // 运行时棋盘视图会传自己的缩放 —— 从此两边只在"缩放"这一个参数上不同。
            BattleHud.Begin(1f);
        }

        private void OnGUI()
        {
            EnsureStyles();
            RectFill(new Rect(0, 0, position.width, position.height), Paper);

            DrawToolbar();
            if (_state == null) return;

            EnsureViewState(_frameCursor);

            const float cellW = 116f, cellH = 96f, gap = 6f;
            const float boardW = cellW * 3 + gap * 2;
            const float boardH = cellH * 3 + gap * 2;

            float leftX = 12f;
            float midX = leftX + boardW + 16f;
            const float midW = 300f;
            float rightX = midX + midW + 16f;
            float topY = 62f;

            GUI.Label(new Rect(leftX, topY, boardW, 18f), "我方", BattleHud.Title);
            GUI.Label(new Rect(rightX, topY, boardW, 18f), "敌方", BattleHud.Title);

            float boardY = topY + 20f;
            DrawBoard(new Rect(leftX, boardY, boardW, boardH), TeamSide.Player, cellW, cellH, gap);
            DrawBoard(new Rect(rightX, boardY, boardW, boardH), TeamSide.Enemy, cellW, cellH, gap);
            DrawCenterColumn(new Rect(midX, boardY, midW, boardH));

            float bottomY = boardY + boardH + 10f;
            DrawBottom(new Rect(leftX, bottomY, position.width - leftX * 2f, position.height - bottomY - 8f));

            // 改状态一律推迟到画完之后（见 _pendingRegenerate 的说明）
            if (_pendingRegenerate)
            {
                _pendingRegenerate = false;
                Regenerate();
            }
        }

        private void DrawToolbar()
        {
            float x = 12f;
            const float y = 6f;

            GUI.Label(new Rect(x, y + 4f, 30f, 18f), "场景", BattleHud.Small); x += 32f;
            var names = new string[BattleSampleContent.ScenarioCount];
            for (int i = 0; i < names.Length; i++) names[i] = BattleSampleContent.ScenarioName(i);
            int newScenario = EditorGUI.Popup(new Rect(x, y + 1f, 300f, 18f), _scenario, names);
            x += 308f;

            GUI.Label(new Rect(x, y + 4f, 30f, 18f), "种子", BattleHud.Small); x += 32f;
            _seedText = EditorGUI.TextField(new Rect(x, y + 1f, 92f, 18f), _seedText); x += 98f;

            if (GUI.Button(new Rect(x, y + 1f, 68f, 18f), "重新生成"))
            {
                _scenario = newScenario;
                _pendingRegenerate = true;
            }
            x += 76f;

            if (GUI.Button(new Rect(x, y + 1f, 28f, 18f), "⏮")) { _playing = false; _frameCursor = 0; RebuildViewState(); Repaint(); } x += 30f;
            if (GUI.Button(new Rect(x, y + 1f, 28f, 18f), "◀"))
            {
                _playing = false;
                if (_frameCursor > 0) _frameCursor--;
                EnsureViewState(_frameCursor); Repaint();
            }
            x += 30f;
            if (GUI.Button(new Rect(x, y + 1f, 52f, 18f), _playing ? "⏸ 暂停" : "▶ 播放"))
            {
                _playing = !_playing;
                _nextBattleAt = -1;
                _lastTick = EditorApplication.timeSinceStartup;
                _accum = 0;
            }
            x += 58f;
            if (GUI.Button(new Rect(x, y + 1f, 28f, 18f), "▶"))
            {
                _playing = false;
                if (_frameCursor < _state.Frames.Count - 1) _frameCursor++;
                EnsureViewState(_frameCursor); Repaint();
            }
            x += 30f;
            if (GUI.Button(new Rect(x, y + 1f, 28f, 18f), "⏭"))
            {
                _playing = false;
                _frameCursor = Mathf.Max(0, _state.Frames.Count - 1);
                EnsureViewState(_frameCursor); Repaint();
            }
            x += 34f;

            GUI.Label(new Rect(x, y + 4f, 30f, 18f), "速度", BattleHud.Small); x += 32f;
            _framesPerSecond = GUI.HorizontalSlider(new Rect(x, y + 6f, 84f, 16f), _framesPerSecond, 1f, 30f);
            x += 90f;
            GUI.Label(new Rect(x, y + 4f, 66f, 18f), $"{_framesPerSecond:F0} 帧/秒", BattleHud.Small); x += 70f;

            _autoNext = GUI.Toggle(new Rect(x, y + 2f, 56f, 18f), _autoNext, "连播");
            x += 62f;
            GUI.Label(new Rect(x, y + 4f, 120f, 18f), $"第 {_battleIndex + 1} 场", BattleHud.Small);

            // ---- 参数行（验收③：改了立刻看得见） ----
            float px = 12f;
            const float py = 30f;
            GUI.Label(new Rect(px, py + 3f, 58f, 16f), "五行系数", BattleHud.Small); px += 60f;

            px = CoefField(px, py, "克", ref _cfg.Elements.Counter);
            px = CoefField(px, py, "被克", ref _cfg.Elements.Countered);
            px = CoefField(px, py, "同属", ref _cfg.Elements.Same);
            px = CoefField(px, py, "无关", ref _cfg.Elements.Neutral);

            bool centerPacify = _cfg.CenterSuppressesAdjacentCounter;
            bool rageGate = _cfg.UltimateNeedsRage;
            bool newCenter = GUI.Toggle(new Rect(px, py + 2f, 126f, 16f), centerPacify, "中宫平息相冲"); px += 130f;
            bool newRage = GUI.Toggle(new Rect(px, py + 2f, 116f, 16f), rageGate, "绝技需怒气"); px += 120f;

            if (newCenter != centerPacify || newRage != rageGate)
            {
                _cfg.CenterSuppressesAdjacentCounter = newCenter;
                _cfg.UltimateNeedsRage = newRage;
                _pendingRegenerate = true;
            }

            if (GUI.Button(new Rect(px, py, 108f, 18f), "应用系数并重跑"))
                _pendingRegenerate = true;
            px += 116f;

            if (_result.Outcome != BattleOutcome.Ongoing || _frameCursor >= _state.Frames.Count - 1)
            {
                GUI.Label(new Rect(px, py + 3f, 300f, 16f),
                    $"本场：{OutcomeText(_result.Outcome)}／{_result.Turns} 回合／" +
                    $"帧 {_state.Frames.Count}／指纹 0x{_result.Fingerprint:X8}", BattleHud.Small);
            }
        }

        private float CoefField(float x, float y, string label, ref float value)
        {
            GUI.Label(new Rect(x, y + 3f, 30f, 16f), label, BattleHud.Small); x += 30f;
            value = EditorGUI.FloatField(new Rect(x, y + 1f, 50f, 18f), value);
            return x + 54f;
        }

        // ---- 棋盘 ----

        private void DrawBoard(Rect area, TeamSide side, float cellW, float cellH, float gap)
        {
            RectFill(area, Silk);

            var slots = _state.SlotsOf(side);
            CurrentEventEndpoints(out int evActor, out int evTarget);

            // 先画格对连线，再画格子 —— 否则线会压在格子上
            var infos = _pairs[(int)side];
            for (int k = 0; k < infos.Count; k++)
            {
                var a = slots[infos[k].A];
                var b = slots[infos[k].B];
                if (!IsAliveInView(a) || !IsAliveInView(b)) continue;

                Vector2 pa = CellCenter(area, infos[k].A, cellW, cellH, gap);
                Vector2 pb = CellCenter(area, infos[k].B, cellW, cellH, gap);

                switch (infos[k].Kind)
                {
                    case AdjacentPairKind.Generate: DrawSeg(pa, pb, Gold, 2f, false); break;
                    case AdjacentPairKind.Pacified: DrawSeg(pa, pb, InkSoft, 1f, true); break;
                    default: DrawSeg(pa, pb, Ink, 2f, true); break;
                }
            }

            for (int idx = 0; idx < BoardLayout.CellCount; idx++)
            {
                int r = idx / BoardLayout.Columns, c = idx % BoardLayout.Columns;
                var cell = new Rect(area.x + c * (cellW + gap), area.y + r * (cellH + gap), cellW, cellH);

                var u = slots[idx];
                if (u == null)
                {
                    RectFill(cell, new Color(0.90f, 0.88f, 0.84f));
                    DrawBorder(cell, new Color(Ink.r, Ink.g, Ink.b, 0.22f), 1f);
                    if (idx == BoardLayout.CenterIndex) GUI.Label(cell, " 中宫", BattleHud.Small);
                    continue;
                }

                UnitSnapshot snap;
                if (!TrySnap(u.RuntimeId, out snap)) snap = ViewSnapshot.Of(u);
                DrawUnit(cell, snap, idx, u.RuntimeId, evActor, evTarget);
            }
        }

        private void DrawUnit(Rect cell, UnitSnapshot s, int idx, string runtimeId, int evActor, int evTarget)
        {
            var elem = ElementColor[Mathf.Clamp((int)s.Element, 0, 5)];
            RectFill(cell, s.Alive ? Mix(Silk, elem, 0.34f) : Mix(Silk, Dead, 0.5f));

            DrawBorder(cell, Ink, 2f);
            if (idx == BoardLayout.CenterIndex) DrawBorder(cell, Gold, 2f);

            if (runtimeId == IdOf(evTarget)) DrawBorder(cell, Crimson, 3f);
            else if (runtimeId == IdOf(evActor)) DrawBorder(cell, Gold, 3f);

            // 名字 + 共鸣
            GUI.Label(new Rect(cell.x + 4f, cell.y + 2f, cell.width - 8f, 15f),
                $"{ElementText(s.Element)}·{s.Name}", BattleHud.Name);
            if (s.ResonanceBonus > 0.001f)
            {
                var badge = new Rect(cell.xMax - 46f, cell.y + 3f, 42f, 14f);
                RectFill(badge, Gold);
                GUI.Label(badge, $"共鸣+{s.ResonanceBonus * 100f:F0}%", BattleHud.Small);
            }

            // CD 三个小格
            float cdX = cell.x + 4f;
            for (int i = 0; i < 3; i++)
            {
                int cd = s.Cd(i);
                var cdRect = new Rect(cdX, cell.y + 19f, 17f, 13f);
                RectFill(cdRect, cd > 0 ? InkSoft : Gold);
                GUI.Label(cdRect, cd > 0 ? cd.ToString() : "●", BattleHud.Small);
                cdX += 19f;
            }

            // 状态
            if (!string.IsNullOrEmpty(s.Statuses))
                GUI.Label(new Rect(cell.x + 62f, cell.y + 18f, cell.width - 66f, 26f), s.Statuses, BattleHud.Small);

            // 血条 + 护盾
            var hpBg = new Rect(cell.x + 4f, cell.y + cell.height - 30f, cell.width - 8f, 11f);
            RectFill(hpBg, new Color(0.16f, 0.13f, 0.11f));
            if (s.Alive)
            {
                float hpW = hpBg.width * Mathf.Clamp01(s.HpRatio);
                RectFill(new Rect(hpBg.x, hpBg.y, hpW, hpBg.height), elem);
                if (s.Shield > 0)
                {
                    float shW = Mathf.Min(hpBg.width * (s.Shield / Mathf.Max(1f, s.MaxHp)), hpBg.width - hpW);
                    if (shW > 0f) RectFill(new Rect(hpBg.x + hpW, hpBg.y, shW, hpBg.height), Shield);
                }
            }
            GUI.Label(new Rect(hpBg.x, hpBg.y - 12f, hpBg.width, 12f),
                s.Alive ? $"{s.Hp}/{s.MaxHp}{(s.Shield > 0 ? $" +{s.Shield}盾" : "")}" : "已阵亡", BattleHud.Small);

            // 怒气条
            var rgBg = new Rect(cell.x + 4f, hpBg.y + 13f, cell.width - 8f, 5f);
            RectFill(rgBg, new Color(0.16f, 0.13f, 0.11f));
            RectFill(new Rect(rgBg.x, rgBg.y, rgBg.width * Mathf.Clamp01(s.Rage / 100f), rgBg.height), Gold);
        }

        private void DrawCenterColumn(Rect area)
        {
            RectFill(area, Silk);

            var frame = CurrentFrame();
            float y = area.y + 4f;

            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 16f),
                $"第 {frame.Turn} 回合　帧 {_frameCursor + 1}/{_state.Frames.Count}", BattleHud.Title);
            y += 18f;

            var ev = EventAt(frame.EventIndex);
            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 15f), "当前事件", BattleHud.Small);
            y += 15f;
            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 32f),
                ev.HasValue ? ev.Value.ToString().Trim() : "（战斗开始前）", BattleHud.Small);
            y += 34f;

            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 15f), "本回合出手序列（速度降序）", BattleHud.Small);
            y += 15f;
            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 46f), LatestOrderNote(frame.EventIndex), BattleHud.Small);
            y += 50f;

            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 15f), "相邻格关系", BattleHud.Small);
            y += 15f;
            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 80f), AdjacencyText(), BattleHud.Small);
            y += 84f;

            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 15f), "图例", BattleHud.Small);
            y += 15f;
            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 62f),
                "鎏金实线 = 相生相邻（回复 + 同气）\n" +
                "墨色虚线 = 相克相冲（真伤 + 怒气 -10%）\n" +
                "淡墨虚线 = 被中宫平息\n" +
                "金框 = 最近一次出手者　朱砂框 = 最近一次受击者", BattleHud.Small);
            y += 66f;

            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 40f),
                "我方/敌方各自一块 3×3 棋盘。\n" +
                "上阵 5 人 ⇒ 必有空位 —— 空位是布局，不是浪费。", BattleHud.Small);
        }

        private void DrawBottom(Rect area)
        {
            RectFill(area, Silk);
            float y = area.y + 4f;

            GUI.Label(new Rect(area.x + 6f, y, area.width - 12f, 16f), "事件流（截至当前帧）", BattleHud.Title);
            y += 18f;

            int eventIndex = CurrentFrame().EventIndex;
            int from = Mathf.Max(0, eventIndex - 15);
            var sb = new System.Text.StringBuilder();
            var evs = _state.Log.Events;
            for (int i = from; i <= eventIndex && i < evs.Count; i++)
                sb.AppendLine("  " + evs[i]);
            GUI.Label(new Rect(area.x + 6f, y, area.width * 0.62f, area.height - 26f), sb.ToString(), BattleHud.Small);

            GUI.Label(new Rect(area.x + area.width * 0.64f, y, area.width * 0.35f, area.height - 26f),
                _result.Summary() + "\n\n" +
                "【验收②怎么用这个窗口】勾上「连播」，把速度调到 8~12 帧/秒，\n" +
                "让它自己跑 10 场。每场结束后问自己三个问题：\n" +
                "  ① 这一场我看到了什么新东西？（没有 ⇒ 内容同质）\n" +
                "  ② 伤害数字与出手序列能不能一眼读懂？（不能 ⇒ 表现层缺失）\n" +
                "  ③ 打完想不想知道为什么输？（不想 ⇒ 策略不可见）", BattleHud.Small);
        }

        // ---- 帧与事件 ----

        private ViewFrame CurrentFrame()
        {
            if (_state == null || _state.Frames.Count == 0) return default(ViewFrame);
            return _state.Frames[Mathf.Clamp(_frameCursor, 0, _state.Frames.Count - 1)];
        }

        private bool IsAliveInView(BattleUnit u)
        {
            if (u == null) return false;
            UnitSnapshot s;
            if (TrySnap(u.RuntimeId, out s)) return s.Alive;
            return u.IsAlive;
        }

        private BattleEvent? EventAt(int index)
        {
            var evs = _state.Log.Events;
            if (index < 0 || index >= evs.Count) return null;
            return evs[index];
        }

        /// <summary>找出当前帧附近最后一条"有受动者"的事件，用来高亮出手者与受击者。</summary>
        private void CurrentEventEndpoints(out int actor, out int target)
        {
            actor = -1;
            target = -1;
            var evs = _state.Log.Events;
            int start = CurrentFrame().EventIndex;
            if (start >= evs.Count) start = evs.Count - 1;
            for (int i = start; i >= 0; i--)
            {
                var e = evs[i];
                if (e.Kind != BattleEventKind.Damage && e.Kind != BattleEventKind.Heal
                    && e.Kind != BattleEventKind.Shield && e.Kind != BattleEventKind.StatusApplied)
                    continue;
                actor = IndexOf(e.ActorId);
                target = IndexOf(e.TargetId);
                return;
            }
        }

        private int _cachedOrderFrame = -2;
        private string _cachedOrder = "";

        private string LatestOrderNote(int eventIndex)
        {
            if (eventIndex == _cachedOrderFrame) return _cachedOrder;
            _cachedOrderFrame = eventIndex;
            _cachedOrder = "";
            var evs = _state.Log.Events;
            int start = Mathf.Min(eventIndex, evs.Count - 1);
            for (int i = start; i >= 0; i--)
            {
                if (evs[i].Kind != BattleEventKind.RoundResolve) continue;
                var note = evs[i].Note;
                if (string.IsNullOrEmpty(note)) continue;
                if (note.StartsWith("出手序列", System.StringComparison.Ordinal))
                {
                    _cachedOrder = note.Substring("出手序列".Length).Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(_cachedOrder)) _cachedOrder = "（本回合还没生成）";
            return _cachedOrder;
        }

        private string AdjacencyText()
        {
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < 2; s++)
            {
                sb.Append(s == 0 ? "我方　" : "\n敌方　");
                sb.Append(BoardPairScan.Describe(_pairs[s]));
            }
            return sb.ToString();
        }

        private int IndexOf(string runtimeId)
        {
            if (string.IsNullOrEmpty(runtimeId)) return -1;
            var units = _state.AllUnits;
            for (int i = 0; i < units.Count; i++)
                if (units[i].RuntimeId == runtimeId) return i;
            return -1;
        }

        private string IdOf(int unitIndex)
        {
            if (unitIndex < 0) return null;
            var units = _state.AllUnits;
            return unitIndex < units.Count ? units[unitIndex].RuntimeId : null;
        }

        // ---- 画图小工具 ----
        // 绘制实现已全部搬到 BattleHud，本文件顶部只留短别名。
        // 这里刻意**不留**任何"看起来等价"的私有实现 —— 保留一份私有实现
        // 就是下一次分叉的起点（编辑器里改一处、运行时没跟着改）。

        /// <summary>枚举 → 中文名。走核心层的 Cn，不在表现层再抄一份五行表。</summary>
        private static string ElementText(Element e) => Cn.Of(e);
        private static string OutcomeText(BattleOutcome o) => Cn.Of(o);
    }
}
