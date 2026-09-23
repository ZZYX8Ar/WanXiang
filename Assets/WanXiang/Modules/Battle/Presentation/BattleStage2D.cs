// ============================================================================
//  万相 · 2D 战场（美术方案 V2 形态：立绘 + Sprite 全套）
//  ---------------------------------------------------------------------------
//  2D 正交视角。与 3D 灰盒（BattleStageGraybox）共用同一套数据契约
//  （只读 ViewFrame 快照 + BattleEvent 事件流），差别只在"画的东西"：
//      · 背景一张季别远景（ArtRes/Battle/BG_xxx.png）
//      · 每方一块 3×3 棋盘（格子 Sprite，可整体换图）
//      · 单位 = 立绘 SpriteRenderer（SpriteCatalog 按 BeastDef.Id 查，
//        查不到回退五行色块 —— 新异兽没图也不会穿帮）
//      · 血条 = 双 Sprite、伤害数字 = TextMesh 上浮、死亡 = 灰化+下沉
//
//  ⚠ 用户替换素材的点位（全部命名规范，换 Sprite/Image 即可，不动代码）：
//      BG_Spring / BG_Summer        背景
//      BoardP/Cell0..8、BoardE/...  棋盘格（含中宫）
//      Unit_xxx/Body                单位立绘
//      Canvas/WeatherBar、Btn_*     UI 贴图
// ============================================================================
//
//  ⚠ 相机与布局（2D 正交）：
//    相机 orthographicSize 5.4、位于 (0,0,-10)，宣纸色清屏；
//    我方棋盘中心 (-2.95, -0.2)、敌方 (2.95, -0.2)，格距 1.15，单位高 ~1.7；
//    UI 用 Overlay Canvas 1920×1080（见 Battle2DSceneBuilder）。

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    internal sealed class UnitView2D
    {
        public string UnitId;
        public GameObject Root;        // 立绘容器
        public SpriteRenderer Body;    // 立绘
        public SpriteRenderer HpBg;
        public SpriteRenderer HpFill;
        public TextMesh NameText;
        public int Hp, MaxHp;
        public bool Alive = true;
        public Vector2 HomePos;        // 站位（未加浮动）
        public float Phase;            // 待机浮动相位（随机，方案 §最易做错三件事之一）
        public float DeadBlend;
        public float ShakePulse;         // 受击回弹（>0 时向基色回退）
        public Color BaseColor = Color.white;

        // ---- 出手冲锋（表现层演出，不改战斗数据）----
        // 战斗核心只有事件流，没有"动画"概念；冲上去再回来完全由表现层决定。
        public bool LungeActive;
        public float LungeElapsed;
        public Vector2 LungeTo;          // 冲锋落点（目标前方一点，避免盖住目标立绘）
        public int BaseSortingOrder = 0; // 冲锋时临时置顶，回位要还原
    }

    public sealed class BattleStage2D : MonoBehaviour
    {
        private const float Cell = 1.75f;        // 格子边长（放大后棋盘占屏宽 ~27%，原来只有 ~18%）
        /// <summary>立绘枢轴在底部中点，抬高让脚踩在格子中心（Build 与 Step 必须用同一个值）。</summary>
        private const float FootOffset = 0.7f;

        /// <summary>立绘显示高度（世界单位）。格子 1.75 —— 立绘略高于格，气势更足。</summary>
        private const float UnitHeight = Cell * 0.60f;   // ★ 与行距(0.66×Cell)匹配：既在美术网格内，又不会上下排重叠
                                                        //   （原来写死 1.9 > 行距 1.155 ⇒ 视觉挤在一起）

        /// <summary>血条相对立绘容器的高度（立绘高 1.9，浮在头顶上沿）。</summary>
        private const float HpBarY = 1.95f;

        private const float BoardY = -0.2f;      // 棋盘（3×3 网格）中心 y —— 网格与单位**必须共用**
        private const float PlayerX = -4.3f;
        private const float EnemyX = 4.3f;

        public Sprite BgSpring;        // ArtRes/Battle/BG_spring.png
        public Sprite CatalogSpriteFallback;

        private BattleState _state;
        private readonly Dictionary<string, UnitView2D> _views = new Dictionary<string, UnitView2D>(16);
        private readonly List<GameObject> _tempTexts = new List<GameObject>(16);
        private readonly List<float> _tempLife = new List<float>(16);
        private Camera _cam;
        private string _actingId;

        public Camera Camera => _cam;

        // ================================================================
        //  搭建
        // ================================================================

        public void Build(BattleState st, Sprite springBg, SpriteCatalog catalog = null)
        {
            _catalog = catalog;
            Clear();
            _state = st;

            var bg = new GameObject("BG_Spring");
            bg.transform.SetParent(transform, false);
            var bgSr = bg.AddComponent<SpriteRenderer>();
            if (springBg != null)
            {
                bgSr.sprite = springBg;
                // 铺满相机视野（ortho 5.4 → 高 10.8、宽 19.2 @16:9）
                var b = springBg.bounds;
                float sx = 19.2f / b.size.x, sy = 10.8f / b.size.y;
                bg.transform.localScale = new Vector3(sx, sy, 1f);
            }
            bgSr.sortingOrder = -10;

            BuildBoard("BoardP", PlayerX, BoardY, true);
            BuildBoard("BoardE", EnemyX, BoardY, false);
            BuildUnits();
            BuildCamera();

            // ⚠ 必须套第 0 帧（初始快照）。BattleState 传进来时**这场战斗已经跑完了**
            //   （BattlePlayback 构造里一次 Run 到底），所以 BuildUnits 拿到的是终局
            //   HP/存活状态 —— 不套初帧的话，开局画面就是"一半人已经死了"。
            if (st.Frames.Count > 0) ApplyFrame(st.Frames[0]);
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _tempTexts.Clear();
            _tempLife.Clear();
            _state = null;
            _cam = null;
        }

        // player=true 我方（左，不镜像）；player=false 敌方（右，列镜像 ⇒ 与我方对称）
        private void BuildBoard(string boardName, float cx, float cy, bool player)
        {
            var board = new GameObject(boardName);
            board.transform.SetParent(transform, false);
            for (int i = 0; i < 9; i++)
            {
                bool center = i == 4;
                var cell = new GameObject($"Cell_{boardName[5]}{i}" + (center ? "_center" : ""));
                cell.transform.SetParent(board.transform, false);
                var sr = cell.AddComponent<SpriteRenderer>();
                // 格子：程序化纯色 + 墨色描边（用户换图点位：Cell_x 的 sprite 直接换）
                sr.sprite = SolidSprite(
                    center ? new Color(0.94f, 0.89f, 0.78f) : new Color(0.91f, 0.89f, 0.82f),
                    (int)(Cell * 100), (int)(Cell * 100), 3,
                    center ? new Color(0.79f, 0.63f, 0.39f) : new Color(0.16f, 0.13f, 0.09f));
                sr.sortingOrder = -5;
                int col = i % 3;
                if (!player) col = 2 - col;   // 敌方列镜像，与我方对称
                int row = i / 3;
                cell.transform.localPosition = new Vector3(
                    cx + (col - 1) * Cell, cy + (1 - row) * Cell * 0.72f, 0f);
            }
        }

        private void BuildUnits()
        {
            var catalog = FindCatalog();
            var units = _state.AllUnits;
            int unitIndex = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.Pos.IsValid) continue;

                var view = new UnitView2D
                {
                    UnitId = u.RuntimeId,
                    Hp = u.Hp, MaxHp = u.MaxHp,
                    Alive = u.IsAlive,
                };

                var root = new GameObject($"Unit_{u.RuntimeId}");
                root.transform.SetParent(transform, false);
                bool player = u.Side == TeamSide.Player;
                view.HomePos = CellPos(player, u.Pos.Index);
                // Pivot=底部中 + Y 上偏 = 立绘脚踩格子中心（遮挡关系由 sortingOrder 管）
                root.transform.localPosition = new Vector3(view.HomePos.x, view.HomePos.y + FootOffset, 0f);
                view.Root = root;

                // 立绘：SpriteCatalog 按 BeastDef.Id 查；查不到回退五行色块
                var bodySr = root.AddComponent<SpriteRenderer>();
                var sprite = catalog != null ? catalog.Get(u.Def.Id) : null;
                // ⚠ 测试阵容 Id（pw/eg 等）对不上 catalog 里的真实异兽 id ——
                //   查不到就**按 catalog 序号分配**：Player 取前半、Enemy 取后半，
                //   这样立绘立刻出现在棋盘上（后续真实内容接入后 Id 会对上）。
                if (sprite == null && catalog != null && catalog.Entries.Count > 0)
                {
                    int idx = catalog.Entries.Count > 5 && !player
                        ? 5 + (unitIndex % (catalog.Entries.Count - 5))
                        : unitIndex % System.Math.Min(5, catalog.Entries.Count);
                    sprite = catalog.Entries[idx].Body;
                }
                if (sprite != null)
                {
                    bodySr.sprite = sprite;
                    float h = sprite.bounds.size.y;
                    if (h > 0.001f)
                    {
                        float k = UnitHeight / h;   // 统一按显示高度换算缩放
                        root.transform.localScale = new Vector3(k, k, 1f);
                    }
                }
                else
                {
                    // fallback 色块：96px PPU=100 = 0.96 世界单位，直接可用
                    bodySr.sprite = SolidSprite(BattlePalette.OfElement(u.Element), 96, 128, 3,
                                                new Color(0.16f, 0.13f, 0.09f));
                }
                bodySr.sortingOrder = u.Pos.Index / 3;   // row 0=后 1=中 2=前
                // 敌方整体镜像：立绘原画朝一侧，敌阵要面向我方才自然
                bodySr.flipX = !player;
                // 敌方压暗一档（§4.4 敌我同源 + 浊化的轻量版）
                if (!player) bodySr.color = new Color(0.72f, 0.72f, 0.80f);
                view.Body = bodySr;
                view.BaseColor = bodySr.color;

                view.HpBg = MakeChildSprite(root, "HpBg", new Color(0.10f, 0.08f, 0.06f),
                                            new Vector2(0.85f, 0.10f), new Vector2(0f, HpBarY), 2);
                view.HpFill = MakeChildSprite(root, "HpFill", BattlePalette.Vital,
                                              new Vector2(0.80f, 0.07f), new Vector2(0f, HpBarY), 3);
                SetHpBar2D(view, u.Hp, u.MaxHp);

                var nameGo = new GameObject("Name");
                nameGo.transform.SetParent(root.transform, false);
                nameGo.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                var f = LegacyFont();
                view.NameText = nameGo.AddComponent<TextMesh>();
                if (f != null)
                {
                    view.NameText.font = f;
                    nameGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                }
                view.NameText.text = u.DisplayName;
                view.NameText.characterSize = 0.055f;
                view.NameText.fontSize = 48;
                view.NameText.anchor = TextAnchor.LowerCenter;
                view.NameText.alignment = TextAlignment.Center;
                view.NameText.color = new Color(0.16f, 0.13f, 0.09f);

                int h2 = u.RuntimeId != null ? u.RuntimeId.GetHashCode() : i * 7919;
                view.Phase = (Mathf.Abs(h2) % 1000) / 1000f * Mathf.PI * 2f;

                _views[u.RuntimeId] = view;
                unitIndex++;
            }
        }

        private void BuildCamera()
        {
            var go = new GameObject("Cam2D");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5.4f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = BattlePalette.Paper;
            go.transform.localPosition = new Vector3(0f, 0f, -10f);
        }

        // ================================================================
        //  回放（与 3D 灰盒同契约：ApplyFrame + ApplyEvent + Step）
        // ================================================================

        public void ApplyFrame(ViewFrame frame)
        {
            if (frame.Changed == null) return;
            for (int i = 0; i < frame.Changed.Length; i++)
            {
                var s = frame.Changed[i];
                if (!_views.TryGetValue(s.UnitId, out var v)) continue;
                v.Hp = s.Hp; v.MaxHp = s.MaxHp;
                SetHpBar2D(v, s.Hp, s.MaxHp);
                if (v.Alive && !s.Alive) v.DeadBlend = 0f;
                v.Alive = s.Alive;
            }
        }

        // ================================================================
        //  出手冲锋
        // ================================================================

        private const float LungeForward = 0.14f;   // 前冲时长
        private const float LungeHold = 0.10f;      // 贴脸停留（受击反馈在这段里播）
        private const float LungeBack = 0.16f;      // 回位时长

        /// <summary>
        /// 让施动者冲到目标面前。停在目标前方 0.75 世界单位处（棋盘格 1.15，
        /// 这个距离刚好"贴上但不盖住"），冲锋时立绘临时置顶，回位还原。
        /// </summary>
        public void StartLunge(string actorId, string targetId)
        {
            if (string.IsNullOrEmpty(actorId)) return;
            if (!_views.TryGetValue(actorId, out var a) || a.Root == null || !a.Alive) return;
            if (a.LungeActive) return;                  // 已经在冲，别叠

            Vector2 stop;
            if (!string.IsNullOrEmpty(targetId) && _views.TryGetValue(targetId, out var t) && t.Root != null)
            {
                Vector2 delta = t.HomePos - a.HomePos;
                stop = delta.sqrMagnitude > 0.0001f
                    ? t.HomePos - delta.normalized * 0.75f
                    : t.HomePos;
            }
            else
            {
                // 群体技 / 无单体目标：朝对面方向冲一步
                float dir = a.HomePos.x < 0f ? 1f : -1f;
                stop = a.HomePos + new Vector2(dir * 1.6f, 0f);
            }

            a.LungeActive = true;
            a.LungeElapsed = 0f;
            a.LungeTo = stop;
            if (a.Body != null)
            {
                if (a.BaseSortingOrder == 0) a.BaseSortingOrder = a.Body.sortingOrder;
                a.Body.sortingOrder = 20;               // 冲锋中压过目标立绘
            }
        }

        public void ApplyEvent(int index, BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.ActionBegin:
                    _actingId = e.ActorId;
                    break;

                case BattleEventKind.SkillCast:
                    // 出手：冲上去（GDD 的"跑到目标面前"）。目标 id 缺失（群体技/
                    // 纯增益）时退化为"朝敌方方向冲一段"，不影响观感。
                    StartLunge(e.ActorId, e.TargetId);
                    break;

                case BattleEventKind.Damage:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hit))
                    {
                        SpawnDamageNumber(hit, e.Amount, e.Note != null && e.Note.Contains("融冰"));
                        hit.Body.color = new Color(1f, 0.55f, 0.5f);
                        hit.ShakePulse = 0.16f;
                    }
                    break;

                case BattleEventKind.Heal:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hv))
                    {
                        SpawnNumber(hv, "+" + e.Amount, new Color(0.42f, 0.85f, 0.45f));   // 绿：恢复
                        hv.Body.color = new Color(0.72f, 1f, 0.78f);
                    }
                    break;

                case BattleEventKind.Shield:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var sv))
                    {
                        SpawnNumber(sv, "盾 +" + e.Amount, new Color(0.45f, 0.72f, 1f));   // 蓝：护盾
                        sv.Body.color = new Color(0.75f, 0.88f, 1f);
                    }
                    break;

                case BattleEventKind.Crit:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var cv))
                        cv.Body.color = new Color(1f, 0.25f, 0.2f);
                    break;

                case BattleEventKind.Death:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var dv))
                        dv.Alive = false;
                    break;
            }
        }

        public void Step(float dt)
        {
            foreach (var v in _views.Values)
            {
                if (v.Root == null) continue;
                float bob = Mathf.Sin((Time.realtimeSinceStartup + v.Phase) * 2.6f) * 0.035f;
                float sink = 0f;
                if (!v.Alive)
                {
                    v.DeadBlend = Mathf.Min(1f, v.DeadBlend + dt / 0.35f);
                    sink = -0.5f * v.DeadBlend;
                    var c = v.Body.color;
                    float g = Mathf.Lerp(1f, 0.35f, v.DeadBlend);
                    v.Body.color = new Color(v.BaseColor.r * g, v.BaseColor.g * g, v.BaseColor.b * g, 1f - 0.5f * v.DeadBlend);
                }
                else if (v.Body.color != v.BaseColor)
                {
                    v.Body.color = Color.Lerp(v.Body.color, v.BaseColor, dt * 10f);
                }

                // ---- 出手冲锋：前冲 → 短暂停留 → 回位 ----
                Vector2 lunge = Vector2.zero;
                if (v.LungeActive)
                {
                    v.LungeElapsed += dt;
                    Vector2 delta = v.LungeTo - v.HomePos;
                    if (v.LungeElapsed < LungeForward) lunge = delta * (v.LungeElapsed / LungeForward);
                    else if (v.LungeElapsed < LungeForward + LungeHold) lunge = delta;
                    else if (v.LungeElapsed < LungeForward + LungeHold + LungeBack)
                    {
                        float k = (v.LungeElapsed - LungeForward - LungeHold) / LungeBack;
                        lunge = delta * (1f - k);
                    }
                    else
                    {
                        v.LungeActive = false;
                        if (v.Body != null) v.Body.sortingOrder = v.BaseSortingOrder;
                    }
                }

                v.Root.transform.localPosition = new Vector3(
                    v.HomePos.x + lunge.x, v.HomePos.y + FootOffset + lunge.y + bob + sink, 0f);

                // 血条跟随立绘（相机 2D 朝 -Z，直接摆即可）
                if (v.HpBg != null) v.HpBg.transform.localPosition = new Vector3(0f, HpBarY, -0.01f);
                if (v.HpFill != null) v.HpFill.transform.localPosition = new Vector3(0f, HpBarY, -0.02f);
            }

            // 伤害数字上浮 + 回收
            for (int i = _tempTexts.Count - 1; i >= 0; i--)
            {
                _tempLife[i] -= dt;
                var go = _tempTexts[i];
                if (go != null)
                {
                    go.transform.localPosition += new Vector3(0f, dt * 0.9f, 0f);
                    var tm = go.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        var c = tm.color;
                        tm.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(_tempLife[i] / 0.7f));
                    }
                }
                if (_tempLife[i] <= 0f)
                {
                    // ⚠⚠ 这里在 Step(dt) 里，是**每帧路径** —— 原来用 DestroyImmediate
                    //   （强制同步销毁，Unity 运行时明令禁用）⇒ 战斗越久触发次数越多、越来越卡，
                    //   最后卡死（用户实测"战斗久一点就卡死"）。必须用 Destroy（延迟销毁）。
                    if (go != null) Destroy(go);
                    _tempTexts.RemoveAt(i); _tempLife.RemoveAt(i);
                }
            }
        }

        // ================================================================

        /// <summary>通用飘字（伤害/恢复/护盾共用）。美术替换：改这里的字体/字号/描边即可。</summary>
        private void SpawnNumber(UnitView2D v, string text, Color color)
        {
            SpawnNumberInternal(v, text, color);
        }

        private void SpawnDamageNumber(UnitView2D v, int amount, bool isMelt)
        {
            SpawnNumberInternal(v, amount.ToString(), isMelt ? BattlePalette.GoldRich : BattlePalette.Crimson);
        }

        private void SpawnNumberInternal(UnitView2D v, string text, Color color)
        {
            var go = new GameObject("Dmg");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = v.Root.transform.position + new Vector3(0f, 1.5f, -1f);
            var f = LegacyFont();
            var tm = go.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = text;
            tm.characterSize = 0.13f;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            _tempTexts.Add(go);
            _tempLife.Add(0.7f);
            // ⚠ 运行时不能用 DestroyImmediate（强制同步销毁，长战斗里反复触发会卡）；
            //   改用 Destroy 并把上限放宽到 24，给延迟销毁留出缓冲。
            while (_tempTexts.Count > 24)
            {
                if (_tempTexts[0] != null) Destroy(_tempTexts[0]);
                _tempTexts.RemoveAt(0); _tempLife.RemoveAt(0);
            }
        }

        private static void SetHpBar2D(UnitView2D v, int hp, int max)
        {
            if (v.HpFill == null || max <= 0) return;
            float ratio = Mathf.Clamp01((float)hp / max);
            var t = v.HpFill.transform;
            var s = t.localScale;
            t.localScale = new Vector3(0.90f * ratio, s.y, s.z);
            t.localPosition = new Vector3(-(0.90f - 0.90f * ratio) * 0.5f, t.localPosition.y, t.localPosition.z);
            v.HpFill.color = ratio > 0.5f ? BattlePalette.Vital
                          : ratio > 0.25f ? BattlePalette.Gold : BattlePalette.Crimson;
        }

        // ================================================================
        //  工具
        // ================================================================

        /// <summary>
        /// 单位落位：★ 必须与 <see cref="BuildBoard"/> 的格子公式**完全一致**，
        /// 否则单位站不进格子（用户反馈"九宫格站不对"的真因：网格用 1.00/0.72，
        /// 单位却用 0.82/0.66 且双方 y 基准相反 ⇒ 永远对不上）。
        /// 网格格子中心 = (cx + (col-1)*Cell, BoardY + (1-row)*Cell*0.72)。
        /// 单位枢轴在底部中点、Step 会再加 FootOffset 抬脚 ⇒ 这里先减去它。
        /// 敌方做列镜像（col→2-col）与我方对称；BuildBoard 用同一镜像公式，单位与格子始终对齐。
        /// </summary>
        private static Vector2 CellPos(bool player, int cell)
        {
            int col = cell % 3;
            if (!player) col = 2 - col;   // 敌方列镜像 ⇒ 同码两侧呈镜像对称
            int row = cell / 3;
            float cx = player ? PlayerX : EnemyX;
            return new Vector2(cx + (col - 1) * Cell,
                               BoardY + (1 - row) * Cell * 0.72f - FootOffset);
        }

        private SpriteRenderer MakeChildSprite(GameObject parent, string name, Color c,
                                               Vector2 size, Vector2 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, -0.01f * order);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SolidSprite(c, (int)(size.x * 100), (int)(size.y * 100), 0, c);
            sr.sortingOrder = order;
            return sr;
        }

        private static Sprite _solid;
        public static Sprite SolidSprite(Color c, int w, int h, int border, Color borderColor)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool edge = border > 0 && (x < border || y < border || x >= w - border || y >= h - border);
                    px[y * w + x] = edge ? borderColor : c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Font _legacyFont;
        private static Font LegacyFont()
        {
            if (_legacyFont != null) return _legacyFont;
            _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacyFont == null) _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacyFont;
        }

        private SpriteCatalog _catalog;
        private SpriteCatalog FindCatalog()
        {
            if (_catalog != null) return _catalog;
            // Resources.FindObjectsOfTypeAll 在编辑器里能找到**未加载进场景的资产**（含 SO）
            var all = Resources.FindObjectsOfTypeAll<SpriteCatalog>();
            foreach (var c in all)
                if (c != null && c.Entries.Count > 0) { _catalog = c; break; }
            return _catalog;
        }
    }
}
