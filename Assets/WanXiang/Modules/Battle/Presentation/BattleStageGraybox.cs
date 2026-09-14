// ============================================================================
//  万相 · 3D 灰盒战场（美术资产生产方案 v1.1 的 **V1 形态**）
//  ---------------------------------------------------------------------------
//  方案原话：「V1 纯色方块 + Tween + 伤害数字，美术成本 0，
//            通过标准 = 灰盒连看 10 场不腻（GDD STEP1 验收②）。
//            不通过就回去调数值节奏，别碰美术。」
//
//  所以这里**刻意用基础几何体**（Cube/Primitive）搭出整块战场，把方案里
//  "玩家视线路径"的五个信号先做出来 —— 它们比怪物动起来重要得多：
//    ① 伤害数字（普通米白 / 克制朱砂红放大 / 暴击描金）
//    ② 五行相生相克连线（相邻格：相生鎏金、相冲墨色、中宫平息淡墨）
//    ③ 出手单位高亮（+ 侧边速度序列条）
//    ④ CD 环与状态（灰盒里退化为头顶色块 + 状态名）
//    ⑤ 技能名横幅（技能名 + 五行色条，0.25 秒切入切出）
//
//  ⚠ 契约：**只读 ViewFrame 快照与 BattleEvent 事件流**，不自己算任何战斗规则。
//    位置/血量/存活来自 UnitSnapshot；伤害数字与技能横幅来自事件。
//
//  ⚠ 为什么放在运行时程序集（Modules.Battle）而不是编辑器工具：
//    灰盒是**验证手段**没错，但它的下一步就是 V2（把方块换成 30 张立绘）——
//    那时换掉的只是"单位外观"这一层，舞台、连线、序列条、横幅全都留着。
//    放在运行时，V2 只是换 Mesh/Sprite，不用把整块战场从编辑器搬到运行时。
//
//  ⚠ 本机编辑器不维持 Play 循环（项目记忆），所以驱动方式有两种：
//    · 编辑器窗口用 `EditorApplication.update` 调 Tick(dt)（可靠，灰盒验收走这条）
//    · 将来进 Play 模式时，把 `_selfTick = true`，Update() 自己跑
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    /// <summary>一个单位的可视体（方块 + 头 + 血条 + 名字 + 状态点）。</summary>
    internal sealed class UnitView
    {
        public string UnitId;
        public TeamSide Side;
        public Element Element;
        public GameObject Root;       // 容器：所有子件挂在它下面
        public GameObject Body;       // 身体方块（颜色随五行）
        public GameObject Head;       // 头（小方块，V2 换成立绘时删掉）
        public GameObject HpBarBg;    // 血条底
        public GameObject HpBarFill;  // 血条填充（缩放表现血量）
        public GameObject StatusDot;  // 状态指示（有状态时亮）
        public TextMesh NameText;     // 名字（灰盒用）
        public int Cell;              // 九宫格下标
        public int Hp, MaxHp, Shield;
        public bool Alive = true;

        // ---- 动效状态 ----
        public float Phase;           // 待机浮动相位（每只不同，方案：必须随机相位）
        public float PhasePeriod;     // 浮动周期 2.0 ± 0.4 秒
        public float Lunge;           // 冲刺进度 0..1
        public float LungeDir;        // +1 朝敌方 / -1 复位
        public float Flash;           // 闪白强度 0..1
        public Color FlashColor;
        public float Shake;           // 受击抖动剩余
        public float DeadBlend;       // 阵亡灰化 0..1
        public Vector3 IdleBase;      // 静止位（格坐标）
    }

    public sealed class BattleStageGraybox : MonoBehaviour
    {
        // ---- 灰盒画布尺寸（世界单位）----
        private const float CellSize = 1.30f;
        private const float CellGap = 0.14f;
        private const float BoardY = 0f;
        // ⚠ 对阵方向：**左右**而不是前后。
        //   一开始按"我方在下、敌方在上"摆，结果 16:9 画面里两块棋盘被压成中间一条窄柱
        //   （正交相机的宽度 = 高 × 宽高比 ⇒ 前后摆等于把纵深塞进高度，横向全浪费）。
        private const float PlayerX = -3.05f;
        private const float EnemyX = 3.05f;

        /// <summary>Play 模式里让组件自己跑；编辑器窗口驱动时保持 false。</summary>
        public bool SelfTick;

        private BattleState _state;
        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>(16);
        private readonly List<GameObject> _board = new List<GameObject>(9);
        private readonly List<LineRenderer> _links = new List<LineRenderer>(12);
        private readonly List<GameObject> _orderStrip = new List<GameObject>(10);
        private readonly List<System.Action<float>> _tweens = new List<System.Action<float>>(16);
        private readonly List<float> _tweenT = new List<float>(16);
        private readonly List<float> _tweenDur = new List<float>(16);
        private readonly List<GameObject> _tempTexts = new List<GameObject>(16);
        private readonly List<float> _tempLife = new List<float>(16);

        private readonly List<AdjacentPair> _playerPairs = new List<AdjacentPair>(12);
        private GameObject _banner;         // 技能名横幅（色条 + 文字）
        private GameObject _bannerBar;
        private TextMesh _bannerText;
        private float _bannerT;
        private Camera _cam;

        /// <summary>当前高亮出手的单位（ActionBegin 事件驱动）。</summary>
        private string _actingId;

        // ================================================================
        //  搭建
        // ================================================================

        /// <summary>按一场战斗搭出舞台（清掉上一次的）。</summary>
        public void Build(BattleState st)
        {
            Clear();
            _state = st;
            BuildBoard();
            BuildUnits();
            BuildLinks();
            BuildOrderStrip();
            BuildBanner();
            BuildCamera();
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _board.Clear();
            _links.Clear();
            _orderStrip.Clear();
            _tempTexts.Clear();
            _tempLife.Clear();
            _tweens.Clear();
            _tweenT.Clear();
            _tweenDur.Clear();
            _banner = null; _bannerBar = null; _bannerText = null;
            _cam = null;
            _state = null;
        }

        /// <summary>
        /// 两块九宫格台面（**每方一块**，各自标中宫）。
        /// ⚠ 这里踩过一次：游戏里是"每方各一块 3×3 棋盘"（各上阵 5 只、共 10 只 &gt; 9 格），
        ///   一开始按"共享一块棋盘"摆，10 个单位挤成一列、谁也看不出站位。
        /// </summary>
        private void BuildBoard()
        {
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? PlayerX : EnemyX;
                for (int i = 0; i < 9; i++)
                {
                    bool center = i == 4;
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Cell_{(side == 0 ? "P" : "E")}{i}" + (center ? "_center" : "");
                    cube.transform.SetParent(transform, false);
                    cube.transform.localScale = new Vector3(CellSize, center ? 0.22f : 0.14f, CellSize);
                    cube.transform.localPosition = CellLocal(i, x, center ? 0.11f : 0.07f);
                    Tint(cube, center ? BattlePalette.Mix(BattlePalette.Gold, BattlePalette.Silk, 0.55f)
                                      : BattlePalette.CellEmpty);
                    _board.Add(cube);
                }
            }
        }

        private void BuildUnits()
        {
            var units = _state.AllUnits;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.Pos.IsValid) continue;

                var view = new UnitView
                {
                    UnitId = u.RuntimeId,
                    Side = u.Side,
                    Element = u.Element,
                    Cell = u.Pos.Index,
                    Hp = u.Hp, MaxHp = u.MaxHp, Shield = u.Shield,
                    Alive = u.IsAlive,
                    IdleBase = UnitLocal(u.Side, u.Pos.Index),
                };
                // 方案原话：**必须随机相位 + 轻微周期差**，否则"像一组会呼吸的僵尸"。
                int h = u.RuntimeId != null ? u.RuntimeId.GetHashCode() : i * 7919;
                view.Phase = (Mathf.Abs(h) % 1000) / 1000f * Mathf.PI * 2f;
                view.PhasePeriod = 2.0f + ((Mathf.Abs(h / 7) % 100) / 100f - 0.5f) * 0.8f;

                var root = new GameObject($"Unit_{u.RuntimeId}");
                root.transform.SetParent(transform, false);
                root.transform.localPosition = view.IdleBase;
                view.Root = root;

                Color elem = BattlePalette.OfElement(u.Element);
                Color body = u.Side == TeamSide.Player
                    ? elem : BattlePalette.Mix(elem, BattlePalette.Soot, 0.28f);   // 敌方压暗一档

                var s = RoleSilhouette(u.Def.Role);
                view.Body = Primitive(root, "Body", PrimitiveType.Cube, body,
                                      new Vector3(s.x, s.y, s.x * 0.75f), new Vector3(0f, s.y * 0.5f, 0f));
                view.Head = Primitive(root, "Head", PrimitiveType.Cube,
                                      BattlePalette.Mix(body, BattlePalette.Paper, 0.35f),
                                      Vector3.one * s.x * 0.55f, new Vector3(0f, s.y + s.x * 0.32f, 0f));

                // 血条（底 + 填充），放在头顶上方，Tick 里朝相机
                view.HpBarBg = Primitive(root, "HpBg", PrimitiveType.Cube, BattlePalette.Soot,
                                         new Vector3(0.92f, 0.10f, 0.05f), new Vector3(0f, s.y + 0.55f, 0f));
                view.HpBarFill = Primitive(root, "HpFill", PrimitiveType.Cube, BattlePalette.Vital,
                                           new Vector3(0.92f, 0.10f, 0.06f), new Vector3(0f, s.y + 0.55f, -0.02f));

                var nameGo = new GameObject("Name");
                nameGo.transform.SetParent(root.transform, false);
                nameGo.transform.localPosition = new Vector3(0f, s.y + 0.62f, 0f);
                view.NameText = Text(nameGo, u.DisplayName, 0.045f, BattlePalette.Ink);

                view.StatusDot = Primitive(root, "Status", PrimitiveType.Cube, BattlePalette.Crimson,
                                           Vector3.one * 0.14f, new Vector3(-0.42f, s.y + 0.75f, 0f));
                view.StatusDot.SetActive(false);

                _views[u.RuntimeId] = view;
                SetHpBar(view, u.Hp, u.MaxHp);
                if (!u.IsAlive) ApplyDeathVisual(view, 1f);
            }
        }

        /// <summary>相生/相冲连线（方案里"本作最重要的差异化表现"）。</summary>
        private void BuildLinks()
        {
            // ⚠ ScanBoth 的两个列表都要给实参（内部会对两方各扫一次）——
            //   传 null 会在扫敌方那一步炸掉。灰盒把两方的连线都画出来。
            var pairs = new List<AdjacentPair>(12);
            var enemyPairs = new List<AdjacentPair>(12);
            BoardPairScan.ScanBoth(_state, pairs, enemyPairs);
            foreach (var p in pairs) _playerPairs.Add(p);
            pairs.AddRange(enemyPairs);
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                var go = new GameObject($"Link_{p.A}_{p.B}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.startWidth = lr.endWidth = p.Kind == AdjacentPairKind.Generate ? 0.075f : 0.05f;
                // 相生鎏金实线 / 相冲墨色 / 中宫平息淡墨（方案原文的三种画法）
                Color c = p.Kind == AdjacentPairKind.Generate ? BattlePalette.GoldRich
                        : p.Kind == AdjacentPairKind.Pacified ? BattlePalette.InkSoft
                        : BattlePalette.Ink;
                SetLineColor(lr, c);
                var side = _playerPairs.Contains(p) ? TeamSide.Player : TeamSide.Enemy;
                lr.SetPosition(0, CellTop(p.A, side));
                lr.SetPosition(1, CellTop(p.B, side));
                _links.Add(lr);
            }
        }

        /// <summary>
        /// 侧边出手序列条：按速度排的 10 枚小方块，当前出手高亮。
        /// ⚠ 顺序是**搭场景时算的快照**（速度在本局里不变，除非天时改速度）——
        ///   灰盒够用；V2 接真 UI 时应当每回合重排。
        /// </summary>
        private void BuildOrderStrip()
        {
            var all = new List<BattleUnit>(_state.AllUnits);
            all.Sort((a, b) =>
            {
                int c = _state.EffectiveSpeed(b).CompareTo(_state.EffectiveSpeed(a));
                return c != 0 ? c : string.CompareOrdinal(a.RuntimeId, b.RuntimeId);
            });
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                var go = Primitive(transform.gameObject, $"Order_{i}", PrimitiveType.Cube,
                                   u.Side == TeamSide.Player
                                       ? BattlePalette.OfElement(u.Element)
                                       : BattlePalette.Mix(BattlePalette.OfElement(u.Element), BattlePalette.Soot, 0.28f),
                                   Vector3.one * 0.22f,
                                   new Vector3(-6.1f, 1.9f - i * 0.30f, 0f));
                _orderStrip.Add(go);
            }
        }

        private void BuildBanner()
        {
            _banner = new GameObject("SkillBanner");
            _banner.transform.SetParent(transform, false);
            _banner.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            _bannerBar = Primitive(_banner, "Bar", PrimitiveType.Cube, BattlePalette.Gold,
                                   new Vector3(3.2f, 0.34f, 0.05f), Vector3.zero);
            var t = new GameObject("Text");
            t.transform.SetParent(_banner.transform, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            _bannerText = Text(t, "", 0.11f, BattlePalette.Paper);
            _banner.SetActive(false);
        }

        private void BuildCamera()
        {
            var go = new GameObject("GrayboxCamera");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 3.5f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // 宣纸底：灰盒也要看整体色彩关系（不是随便一个黑背景）
            _cam.backgroundColor = BattlePalette.Paper;
            go.transform.localPosition = new Vector3(0f, 5.4f, -6.4f);
            go.transform.LookAt(transform.position + new Vector3(0f, 0.25f, 0f));
        }

        // ================================================================
        //  回放：帧 + 事件
        // ================================================================

        /// <summary>套用一帧增量快照（位置/血量/存活/状态）。</summary>
        public void ApplyFrame(ViewFrame frame)
        {
            if (frame.Changed == null) return;
            for (int i = 0; i < frame.Changed.Length; i++)
            {
                var s = frame.Changed[i];
                if (!_views.TryGetValue(s.UnitId, out var v)) continue;

                int before = v.Hp + v.Shield;
                v.Hp = s.Hp; v.MaxHp = s.MaxHp; v.Shield = s.Shield;
                SetHpBar(v, s.Hp, s.MaxHp);

                if (v.Alive && !s.Alive) v.DeadBlend = 0f;      // 开始灰化（Tick 里推进）
                v.Alive = s.Alive;
                if (v.StatusDot != null)
                    v.StatusDot.SetActive(s.Alive && !string.IsNullOrEmpty(s.Statuses));

                // 掉血但没有对应事件（天时/持续伤害）时也抖一下，别让画面"静悄悄掉血"
                if (s.Hp + s.Shield < before && s.Alive)
                {
                    v.Shake = 0.18f;
                    v.Flash = 0.6f;
                    v.FlashColor = BattlePalette.Paper;
                }
            }
        }

        /// <summary>
        /// 套用一个战斗事件（伤害数字 / 技能横幅 / 出手高亮 / 闪白 / 阵亡灰化）。
        /// 两个入口分工：帧负责"状态"，事件负责"刚刚发生了什么"。
        /// </summary>
        public void ApplyEvent(int index, BattleEvent e)
        {
            _lastEventIndex = index;
            switch (e.Kind)
            {
                case BattleEventKind.ActionBegin:
                    _actingId = e.ActorId;
                    HighlightActing();
                    break;

                case BattleEventKind.SkillCast:
                    ShowBanner(e.SkillName ?? "行动", ElementOf(e.ActorId));
                    if (e.ActorId != null && _views.TryGetValue(e.ActorId, out var caster))
                        caster.Lunge = 0.001f;                   // 触发冲刺
                    break;

                case BattleEventKind.Damage:
                    SpawnDamageNumber(e);
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hit))
                    {
                        hit.Flash = 1f;
                        hit.FlashColor = BattlePalette.Paper;
                        hit.Shake = 0.20f;
                    }
                    break;

                case BattleEventKind.Crit:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var cv))
                    {
                        cv.Flash = 1f;
                        cv.FlashColor = BattlePalette.Crimson;   // 方案：暴击受击"白闪换绛红闪"
                        cv.Shake = 0.28f;
                    }
                    break;

                case BattleEventKind.Death:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var dv))
                        dv.Alive = false;                        // 灰化在 Tick 里推进
                    break;
            }
        }

        /// <summary>推进一行事件（帧游标由调用方管理）。</summary>
        public void Step(float dt)
        {
            // 动效推进
            for (int i = 0; i < _tweens.Count; i++)
            {
                _tweenT[i] += dt;
                float k = _tweenDur[i] <= 0f ? 1f : Mathf.Clamp01(_tweenT[i] / _tweenDur[i]);
                _tweens[i](k);
            }
            // 回收结束的（倒序删，避免下标错位）
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                if (_tweenDur[i] <= 0f || _tweenT[i] >= _tweenDur[i])
                {
                    _tweens.RemoveAt(i); _tweenT.RemoveAt(i); _tweenDur.RemoveAt(i);
                }
            }

            // 单位：待机浮动 / 冲刺 / 受击抖动 / 灰化 / 血条朝向
            foreach (var v in _views.Values)
            {
                if (v.Root == null) continue;
                float bob = Mathf.Sin((Time.realtimeSinceStartup + v.Phase) * (2f * Mathf.PI / v.PhasePeriod)) * 0.045f;

                // 冲刺：0.12s 去、0.20s 回（方案的 Ease 改成了线性 + 缓出，够用）
                float lunge = 0f;
                if (v.Lunge > 0f)
                {
                    v.Lunge += dt;
                    const float outDur = 0.12f, backDur = 0.20f;
                    lunge = v.Lunge < outDur
                        ? Mathf.Lerp(0f, 0.42f, v.Lunge / outDur)
                        : Mathf.Lerp(0.42f, 0f, Mathf.Clamp01((v.Lunge - outDur) / backDur));
                    if (v.Lunge >= outDur + backDur) v.Lunge = 0f;
                    if (v.Side == TeamSide.Enemy) lunge = -lunge;
                }

                float shake = 0f;
                if (v.Shake > 0f)
                {
                    v.Shake -= dt;
                    // 方案：抖动必须"衰减正弦"，不要纯随机
                    float decay = Mathf.Clamp01(v.Shake / 0.20f);
                    shake = Mathf.Sin((0.20f - v.Shake) * 90f) * 0.07f * decay;
                }

                float sink = 0f, tilt = 0f;
                if (!v.Alive)
                {
                    v.DeadBlend = Mathf.Min(1f, v.DeadBlend + dt / 0.35f);   // 0.35 秒灰化
                    sink = -0.30f * v.DeadBlend;                            // 下沉 30px 等比
                    tilt = 15f * v.DeadBlend;                               // 旋转 15°
                }

                v.Root.transform.localPosition = v.IdleBase + new Vector3(shake, bob + sink, lunge * (v.Side == TeamSide.Player ? 1f : -1f));
                v.Root.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                if (!v.Alive && v.DeadBlend > 0f)
                    ApplyDeathVisual(v, v.DeadBlend);

                // 受击闪白（方案：0→1→0 各 0.05s，这里做成衰减）
                if (v.Flash > 0f)
                {
                    v.Flash = Mathf.Max(0f, v.Flash - dt / 0.10f);
                    Tint(v.Body, Color.Lerp(BattlePalette.OfElement(v.Element), v.FlashColor, v.Flash));
                }
            }

            // 血条 / 名字 / 伤害数字始终朝相机（斜视角下文字被压扁最致命）
            if (_cam != null)
            {
                var camPos = _cam.transform.position;
                // ⚠ 文字用**相机朝向**做公告牌（不是 LookRotation(自己 - 相机)）：
                //   后者会把相机的俯角/侧倾一起带进来，文字在画面里是歪的（试过，
                //   截图里名字整排斜着）。相机旋转再翻 180°，文字既正对镜头又是正的。
                var billboard = _cam.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
                foreach (var v in _views.Values)
                {
                    if (v.Root == null) continue;
                    var q = Quaternion.LookRotation(v.Root.transform.position - camPos);
                    if (v.HpBarBg != null) v.HpBarBg.transform.rotation = q;
                    if (v.HpBarFill != null) v.HpBarFill.transform.rotation = q;
                    if (v.NameText != null) v.NameText.transform.rotation = billboard;
                }
                for (int i = 0; i < _tempTexts.Count; i++)
                    if (_tempTexts[i] != null) _tempTexts[i].transform.rotation = billboard;
                if (_bannerText != null) _bannerText.transform.rotation = billboard;
            }

            // 临时文字（伤害数字）到期回收
            for (int i = _tempTexts.Count - 1; i >= 0; i--)
            {
                _tempLife[i] -= dt;
                if (_tempLife[i] <= 0f)
                {
                    if (_tempTexts[i] != null) DestroyImmediate(_tempTexts[i]);
                    _tempTexts.RemoveAt(i);
                    _tempLife.RemoveAt(i);
                }
            }

            // 技能横幅停留时长
            if (_bannerT > 0f)
            {
                _bannerT -= dt;
                if (_bannerT <= 0f && _banner != null) _banner.SetActive(false);
            }
        }

        public Camera Camera => _cam;

        // ================================================================
        //  视觉细节
        // ================================================================

        /// <summary>
        /// 伤害数字（方案里"玩家 80% 的注意力在这里"）：
        /// 普通伤害米白、克制伤害朱砂红放大并带「克」角标、暴击描金加粗。
        /// </summary>
        private void SpawnDamageNumber(BattleEvent e)
        {
            if (e.TargetId == null || !_views.TryGetValue(e.TargetId, out var v)) return;

            // 战斗日志里暴击是"先 Crit 再 Damage"成对出现的（DealDamage 的顺序），
            // 所以"上一条是 Crit"就是这次的暴击判据 —— 不需要自己算暴击率。
            bool crit = false;
            var events = _state.Log.Events;
            if (_lastEventIndex > 0 && _lastEventIndex - 1 < events.Count)
                crit = events[_lastEventIndex - 1].Kind == BattleEventKind.Crit;
            bool countered = IsCounterHit(e);
            Color c = crit ? BattlePalette.GoldRich
                    : countered ? BattlePalette.Crimson
                    : BattlePalette.Paper;
            float size = crit ? 0.085f : countered ? 0.072f : 0.055f;
            string text = e.Amount.ToString() + (crit ? " 暴" : countered ? " 克" : "");

            var go = new GameObject("Dmg");
            go.transform.SetParent(transform, false);
            // 横向抖一下：连续多段伤害都飘在同一位置会糊成一坨
            float jitter = ((_tempTexts.Count % 3) - 1) * 0.22f;
            go.transform.localPosition = v.Root.transform.localPosition + new Vector3(jitter, 0.62f, -0.1f);
            var tm = Text(go, text, size, c);
            _tempTexts.Add(go);
            _tempLife.Add(0.7f);
            // 上限：同时存在的伤害数字太多会把战场糊掉（灰盒也要能看清谁在打谁）
            while (_tempTexts.Count > 8)
            {
                if (_tempTexts[0] != null) DestroyImmediate(_tempTexts[0]);
                _tempTexts.RemoveAt(0);
                _tempLife.RemoveAt(0);
            }

            var start = go.transform.localPosition;
            // 飘起 + 淡出（0.7 秒）
            AddTween(0.7f, k =>
            {
                if (go == null) return;
                go.transform.localPosition = start + new Vector3(0f, 0.6f * k, 0f);
                if (tm != null) tm.color = new Color(c.r, c.g, c.b, 1f - k * 0.9f);
            });
        }

        private int _lastEventIndex = -1;

        private bool IsCounterHit(BattleEvent e)
        {
            if (e.ActorId == null || e.TargetId == null) return false;
            if (!_views.TryGetValue(e.ActorId, out var a) || !_views.TryGetValue(e.TargetId, out var d)) return false;
            return ElementMatrix.Counters(a.Element, d.Element);
        }

        private void ShowBanner(string skillName, Element el)
        {
            if (_banner == null) return;
            _banner.SetActive(true);
            _bannerT = 0.45f;
            if (_bannerBar != null) Tint(_bannerBar, BattlePalette.OfElement(el));
            if (_bannerText != null) _bannerText.text = skillName;
        }

        private void HighlightActing()
        {
            for (int i = 0; i < _orderStrip.Count; i++) _orderStrip[i].transform.localScale = Vector3.one * 0.22f;
            foreach (var v in _views.Values)
            {
                if (v.Body == null) continue;
                bool acting = v.UnitId == _actingId;
                Tint(v.Body, acting
                    ? BattlePalette.Mix(BattlePalette.OfElement(v.Element), BattlePalette.Paper, 0.35f)
                    : BattlePalette.OfElement(v.Element));
            }
        }

        private void SetHpBar(UnitView v, int hp, int max)
        {
            if (v.HpBarFill == null || max <= 0) return;
            float ratio = Mathf.Clamp01((float)hp / max);
            var t = v.HpBarFill.transform;
            var s = t.localScale;
            t.localScale = new Vector3(0.92f * ratio, s.y, s.z);
            // 左对齐收缩（右端对齐血条底）
            var p = t.localPosition;
            t.localPosition = new Vector3(-(0.92f - 0.92f * ratio) * 0.5f, p.y, p.z);
            Tint(v.HpBarFill, ratio > 0.5f ? BattlePalette.Vital
                           : ratio > 0.25f ? BattlePalette.Gold : BattlePalette.Crimson);
        }

        private void ApplyDeathVisual(UnitView v, float blend)
        {
            if (v.Body == null) return;
            Color gray = BattlePalette.Mix(BattlePalette.OfElement(v.Element), BattlePalette.Dead, 0.75f);
            Tint(v.Body, Color.Lerp(BattlePalette.OfElement(v.Element), gray, blend));
            if (v.Head != null) Tint(v.Head, Color.Lerp(BattlePalette.Paper, gray, blend));
            if (v.HpBarFill != null) v.HpBarFill.SetActive(false);
        }

        // ================================================================
        //  几何/材质小工具
        // ================================================================

        private static Vector3 RoleSilhouette(RoleType role)
        {
            switch (role)
            {
                case RoleType.Guard: return new Vector3(1.05f, 0.62f, 0f);     // 矮胖
                case RoleType.Striker: return new Vector3(0.78f, 0.82f, 0f);
                case RoleType.Caster: return new Vector3(0.72f, 0.95f, 0f);    // 高瘦
                case RoleType.Support: return new Vector3(0.82f, 0.74f, 0f);
                default: return new Vector3(0.66f, 1.00f, 0f);                // 疾：最瘦最高
            }
        }

        /// <summary>本方棋盘上某一格的位置（sideX = 我方盘或敵方盘的横向中心）。</summary>
        private static Vector3 CellLocal(int index, float sideX, float y)
        {
            int col = index % 3, row = index / 3;
            // 列 → 盘内横向，行 → 纵深（row 0 靠后，row 2 靠前，两方朝向一致，读战报不会反）
            return new Vector3(sideX + (col - 1) * (CellSize + CellGap), y,
                               (1 - row) * (CellSize + CellGap));
        }

        private static float SideX(TeamSide side) => side == TeamSide.Player ? PlayerX : EnemyX;

        /// <summary>单位站位：踩在自己那一方的对应格子上（略抬高，别和台面穿模）。</summary>
        private static Vector3 UnitLocal(TeamSide side, int cell) => CellLocal(cell, SideX(side), 0.30f);

        private static Vector3 CellTop(int index, TeamSide side)
            => CellLocal(index, SideX(side), 0.34f);

        private GameObject Primitive(GameObject parent, string name, PrimitiveType type,
                                     Color color, Vector3 scale, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = scale;
            go.transform.localPosition = localPos;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);      // 灰盒不需要物理
            Tint(go, color);
            return go;
        }

        private TextMesh Text(GameObject parent, string content, float size, Color color)
        {
            var tm = parent.GetComponent<TextMesh>();
            if (tm == null) tm = parent.AddComponent<TextMesh>();
            var f = LegacyFont();
            if (f != null) { tm.font = f; parent.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
            tm.text = content;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            return tm;
        }

        private static Font _legacyFont;
        private static Font LegacyFont()
        {
            if (_legacyFont != null) return _legacyFont;
            // 2022 起内置字体改名：Arial.ttf 已移除，用 LegacyRuntime.ttf
            _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacyFont == null) _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacyFont;
        }

        private Element ElementOf(string unitId)
        {
            return unitId != null && _views.TryGetValue(unitId, out var v) ? v.Element : Element.None;
        }

        private static void Tint(GameObject go, Color c)
        {
            if (go == null) return;
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var mat = new Material(UnlitShader());
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            r.sharedMaterial = mat;
        }

        private static void SetLineColor(LineRenderer lr, Color c)
        {
            lr.startColor = c;
            lr.endColor = c;
            var mat = new Material(UnlitShader());
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            lr.material = mat;
        }

        private static Shader _unlit;
        private static Shader UnlitShader()
        {
            if (_unlit != null) return _unlit;
            // URP 工程（本项目）优先；找不到再退到内置/精灵着色器（灰盒不该因着色器名崩掉）
            string[] names =
            {
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Unlit/Color",
                "Sprites/Default",
            };
            for (int i = 0; i < names.Length && _unlit == null; i++) _unlit = Shader.Find(names[i]);
            if (_unlit == null) _unlit = Shader.Find("Standard");
            return _unlit;
        }

        private void AddTween(float dur, System.Action<float> step)
        {
            _tweens.Add(step);
            _tweenT.Add(0f);
            _tweenDur.Add(dur);
        }

        private void Update()
        {
            if (SelfTick) Step(Time.deltaTime);
        }
    }
}
