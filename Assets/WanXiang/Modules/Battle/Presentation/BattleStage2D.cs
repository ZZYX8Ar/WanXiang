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

    public sealed partial class BattleStage2D : MonoBehaviour
    {
        public void Build(BattleState st, Sprite springBg, SpriteCatalog catalog = null, Sprite fgDecor = null, int act = 1,
                          string realmId = null)
        {
            _realmId = realmId;   // ★ 供 Boss 登场文案判断"秘境镇守"

            _catalog = catalog;
            Clear();
            _state = st;
            // 自证身份：看到这行 = 跑的是"身体特效已关"的 2026-10-09 版本
            Debug.Log("[Stage2D] Build v2026-10-09: StatusArtEnabled=" + StatusArtEnabled + "（false=身体特效不创建，只用头顶小图标）");

            var bg = new GameObject("BG_Spring");
            bg.transform.SetParent(transform, false);
            var bgSr = bg.AddComponent<SpriteRenderer>();
            if (springBg != null)
            {
                bgSr.sprite = springBg;
                // 铺满相机视野（ortho 5.4 → 高 10.8、宽 19.2 @16:9）
                var b = springBg.bounds;
                // ★ 2026-10-23 档 1：背景要做极缓视差漂移 ⇒ 超铺 6%，四边不会被漂出空白。
                const float bgOver = 1.06f;
                float sx = 19.2f * bgOver / b.size.x, sy = 10.8f * bgOver / b.size.y;
                bg.transform.localScale = new Vector3(sx, sy, 1f);
            }
            bgSr.sortingOrder = -10;
            _bgDecor = bg.transform;
            _bgHomePos = bg.transform.localPosition;

            // ⛔ 2026-10-23：多层缓动（雾/暖光）一并停用（用户改走动态视频背景）。

            // ⛔ 2026-10-23 用户："把战斗背景的 shader 和轻微呼吸晃动去掉，我准备用动态视频"
            //   ⇒ 背景恢复默认材质（原画直出），不再做 shader 效果。
            //   shader 与动效组件保留在工程里（BattleBgMotion / BattleBgMotion.shader），
            //   以后要做"静态图 + 光效"的方案可以原地启用。
            if (_bgMat != null) { Destroy(_bgMat); _bgMat = null; }

            // ---- 四周前景装饰（透明 PNG、随幕变化；sortingOrder 30 = 压过冲锋立绘 20）----
            if (fgDecor != null)
            {
                var fg = new GameObject("FG_Decor");
                fg.transform.SetParent(transform, false);
                var fgSr = fg.AddComponent<SpriteRenderer>();
                fgSr.sprite = fgDecor;
                var fb = fgDecor.bounds;
                // ⚠ 必须**超铺**：原本正好铺满视口(19.2x10.8)，而 Step() 里会绕自身中心
                //   旋转 ±0.5° 做"风吹摇曳" ， 矩形四角会翘出视口、露出底色边缘
                //   （用户 2026-10-12："晃动的时候有点暴露边缘，太贴近边缘，可以往边缘外放一下"）。
                //   超铺 12% ， 每边多出 ~1.0 世界单位，旋转 0.5° 的角部翘出仅 ~0.09，余量充足。
                const float over = 1.12f;
                fg.transform.localScale = new Vector3(19.2f * over / fb.size.x, 10.8f * over / fb.size.y, 1f);
                fgSr.sortingOrder = 30;
                _fgDecor = fg.transform;
            }

            // ---- 环境粒子（按幕：春花瓣/夏萤火/长夏花粉/秋落叶/冬雪花）----
            _ambient = GetComponent<BattleAmbientParticles>();
            if (_ambient == null) _ambient = gameObject.AddComponent<BattleAmbientParticles>();
            // ★ 2026-10-23：秘境战斗用**场域专属**飘落粒子（用户："每关秘境粒子没有变化"）
            if (!string.IsNullOrEmpty(realmId)) _ambient.BuildRealm(realmId);
            else _ambient.Build(act);

            BuildBoard("BoardP", PlayerX, BoardY, true);

            BuildBoard("BoardE", EnemyX, BoardY, false);
            BuildUnits();
            BuildCamera();
            BuildTurnArrow();

            // ⚠ 必须套第 0 帧（初始快照）。BattleState 传进来时**这场战斗已经跑完了**
            //   （BattlePlayback 构造里一次 Run 到底），所以 BuildUnits 拿到的是终局
            //   HP/存活状态 —— 不套初帧的话，开局画面就是"一半人已经死了"。
            if (st.Frames.Count > 0) ApplyFrame(st.Frames[0]);
        }

        // ================================================================
        //  当前行动者指示器：头顶向下箭头
        // ================================================================
        /// <summary>
        /// 外部（BattlePanel）告知"当前轮到谁"—— 等待下令期间 ActionBegin 还没发，
        /// 不调用的话箭头会停在上一个行动者头上（用户要的是"当前是谁在行动"）。
        /// </summary>
        public void SetTurnActor(string unitId) { _actingId = unitId; }

        private void BuildTurnArrow()
        {
            var go = new GameObject("TurnArrow");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            if (_turnArrowSprite == null) _turnArrowSprite = BuildArrowSprite();
            sr.sprite = _turnArrowSprite;
            sr.sortingOrder = 25;            // 压过立绘(20)，低于前景(30)
            sr.enabled = false;             // 默认隐藏，等 ActionBegin 出现再显示
            sr.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
            _turnArrow = sr;
        }

        // 程序化生成"向下箭头"贴图（64×64，白色，运行时按阵营染色）
        private static Sprite BuildArrowSprite()
        {
            const int W = 64, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool inside = false;
                if (y >= 30 && y <= 54)                          // 杆（上部）
                    inside = Mathf.Abs(x - 32) <= 5;
                else if (y >= 10 && y < 30)                      // 箭头头部（下部，向下）
                {
                    float t = (y - 10) / (30f - 10f);            // 0=底 1=顶
                    float halfW = 5f + t * 24f;                  // 底 5 -> 顶 29
                    inside = Mathf.Abs(x - 32) <= halfW;
                }
                px[y * W + x] = inside ? Color.white : Color.clear;
            }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        // ================================================================
        //  格挡 QTE 提示圈（2026-10-14，用户点名参考 33号远征队）
        //  ----------------------------------------------------------------
        //  收缩环 + 固定目标圈：环套上目标圈的那一拍 = 完美区间（判定在 BattlePanel，
        //  这里只负责"跟随攻击者 + 按进度收缩染色"）。贴图程序化生成，与行动箭头同套路。
        // ================================================================
        private SpriteRenderer _parryRing;       // 收缩环
        private SpriteRenderer _parryTarget;     // 目标圈（固定尺寸）
        private string _parryActorId;            // 窗口归属的攻击者（null = 无窗口）
        private float _parryK;                   // 环收缩进度（0→1，由面板每帧喂）
        private static Sprite _parryRingSprite;  // 环贴图（跨战斗共享）
        private readonly BurstPool _burstPool = new BurstPool();
        private static GameObject _corruptPsPrefab;
        private static Sprite[] _auraRings;
        private readonly QtePrompt _qtePrompt = new QtePrompt();
        private const float JudgeLife = 1.25f;
        public bool CutInPlaying => _skillFx != null && _skillFx.CutInActive;
        private static Sprite _parryTargetSprite;
        private static Sprite _qteBgSprite;   // QTE 提示条底衬贴图（实心方块，跨战斗共享）

        /// <summary>清空舞台（重开一场时用）。public 供 BattleSceneDriver.Restart 调用。</summary>
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _floaters.Reset();   // ★ 子物体已 DestroyImmediate，丢弃全部失效池引用（不能用 Clear）

            _verdictBanner.ClearRefs();
    _pendingDmg.Clear();
            _pendingCritFloat = null;
            _parryRing = null;      // 提示圈子物体已被上面销毁；标记清空等下次重建
            _parryTarget = null;
            _parryActorId = null;
            _qtePrompt.ClearRefs();
            _state = null;
            _cam = null;
            _fgDecor = null;
            _bgDecor = null;
            _ambient = null;   // 粒子子物体已被上面销毁；Build 里重新 GetComponent/AddComponent
            _skillFx = null;   // SkillFx 子物体一并销毁；下次 EnsureSkillFx 重建（否则持已毁对象）
            _turnArrow = null; // 箭头子物体已被上面销毁；Build 里重建
            _shakeDur = 0f; _shakeAmp = 0f;
            _deferredDmg.Clear();
            _burstPool.Reset();       // ★ 同上：丢弃失效池引用（ClearRefs 会把失效对象压回 free 栈）
            _bursts.Clear();            // ★ 抛物线火花：子物体已统一销毁，活跃表与回收栈一起清
            _burstFree.Clear();
        }

        // player=true 我方（左，不镜像）；player=false 敌方（右，列镜像 ， 与我方对称）
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
                // 格子：**尺寸必须与行间距一致**，否则上下行互相压边、看着"九宫格大小不齐"
                //（原先是 1.75×1.75 正方形 + 1.26 行距 ， 每行压掉 0.49，用户实测就是这个现象）。
                // ⚠ 单位落位用同一套公式（（1-row)*Cell*BoardRowGap），改这里必须同步改 CellPos。
                // 🔧 换美术：把 `Assets/Resources/UI/Board/cell.png`（+ `cell_center.png`）丢进去即可替换，缺图回退程序化纯色。
                var cellArt = Resources.Load<Sprite>("UI/Board/" + (center ? "cell_center" : "cell"));
                sr.sprite = cellArt != null
                    ? cellArt
                    : SolidSprite(
                        center ? new Color(0.94f, 0.89f, 0.78f) : new Color(0.91f, 0.89f, 0.82f),
                        (int)(Cell * 100), (int)(Cell * BoardRowGap * 100), 3,
                        center ? new Color(0.79f, 0.63f, 0.39f) : new Color(0.16f, 0.13f, 0.09f));
                sr.sortingOrder = -5;

                // 缓存格子渲染器与底色：技能预览高亮用（还原时用缓存值，不硬编码颜色）
                if (player) { _cellsPlayer[i] = sr; _cellBasePlayer[i] = sr.color; }
                else { _cellsEnemy[i] = sr; _cellBaseEnemy[i] = sr.color; }
                int col = i % 3;
                if (!player) col = 2 - col;   // 敌方列镜像，与我方对称
                int row = i / 3;
                cell.transform.localPosition = new Vector3(
                    cx + (col - 1) * Cell, cy + (1 - row) * Cell * BoardRowGap, 0f);
            }
        }

        /// <summary>
        /// 技能预览：把"会被作用到的九宫格格子"点亮。cell = 0..8（与 Pos.Index 同一套编号）。
        /// 敌方格 → 暖红（要挨打）；我方格 → 柔绿（吃治疗/护盾/增益）。传 null = 该侧不亮。
        /// 两侧都传 null/空 = 全部还原。
        /// ⚠ 只给**已有格子**换配色（数据填充），不新建任何对象；格子的美术仍可在场景里换。
        /// </summary>
        public void HighlightCells(List<int> playerCells, List<int> enemyCells)
        {
            for (int i = 0; i < 9; i++)
            {
                bool onP = playerCells != null && playerCells.Contains(i);
                bool onE = enemyCells != null && enemyCells.Contains(i);
                if (_cellsPlayer[i] != null)
                    _cellsPlayer[i].color = onP ? TintAllyCell : _cellBasePlayer[i];
                if (_cellsEnemy[i] != null)
                    _cellsEnemy[i].color = onE ? TintEnemyCell : _cellBaseEnemy[i];
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
            _camBase = go.transform.localPosition;   // 震动基准
            _zoomBase = _cam.orthographicSize;        // Zoom Punch 基准
        }

        // ================================================================
        //  终结技 / 觉醒技 大演出（SkillFxPlayer）
        // ================================================================

        /// <summary>惰性创建特效播放器（挂在舞台根下，与舞台同一时钟由 Step 驱动）。</summary>
        private void EnsureSkillFx()
        {
            if (_skillFx != null) return;
            var go = new GameObject("SkillFx");
            go.transform.SetParent(transform, false);
            _skillFx = go.AddComponent<SkillFxPlayer>();
            _skillFx.Bind(_cam, TriggerShake, TriggerZoom);
        }

        /// <summary>最近一次大演出（终结/觉醒/连携）的总时长；无演出 = 0。</summary>
        public float LastFxDuration => _skillFx != null ? _skillFx.LastBusyDuration : 0f;
        /// <summary>本次演出的立绘特写时长（秒）。事件回放只等这一段：特写结束后立刻出伤害。</summary>
        public float LastCutInDuration => _skillFx != null ? _skillFx.LastCutInDuration : 0f;
        /// <summary>本次大演出剩余时间（秒）。战斗收尾要等它播完。</summary>
        public float RemainingFxTime => _skillFx != null ? _skillFx.RemainingBusy : 0f;

        /// <summary>
        /// Boss 登场演出（2026-10-25）：战斗一开始给首领放一次 cut-in（立绘推入 + 震屏 + 元素环），
        /// 文案「首领 · 名字」，让玩家先"看清是谁"再开打。

        // ================================================================
        //  回放（与 3D 灰盒同契约：ApplyFrame + ApplyEvent + Step）
        // ================================================================

        public void ApplyFrame(ViewFrame frame)
        {
            if (frame.Changed == null) return;
            for (int i = 0; i < frame.Changed.Length; i++)
            {
                var s = frame.Changed[i];
                if (!_views.TryGetValue(s.UnitId, out var v))
                {
                    // ★ 中途增员（召唤 / 分身）**惰性补建**：它「此刻」才进入名册。
                    //   ⚠ 不能只靠开演时的预建：手动模式下 BattlePlayback 是**分片推进**的，
                    //     开演那一刻还没跑到"冰晶出现"的回合（用户 2026-10-04 报"还是什么都没有"的真因）。
                    v = EnsureView(s.UnitId);
                    if (v == null) continue;
                }
                // 该单位第一次出现在帧流里 = 真的入场了 ， 把（预建但隐藏 / 刚补建的）view 显出来。
                if (!v.Hidden && v.Root != null && !v.Root.activeSelf) v.Root.SetActive(true);
                // Boss 机制提示（冰晶期倒数等）：随帧流更新（空串 = 隐藏）
                // ⛔ 2026-10-26 用户："这个红色机制字不要一直悬浮着，直接删了"
                //   ⇒ 常驻文本一律清空，只在**变化时**飘一条（见下面的 StateHint 比对）
                if (v.Hint != null && !string.IsNullOrEmpty(v.Hint.text)) v.Hint.text = "";
                // ---- 起手预警（2026-10-16 FoeKit）：⚠ 内容一律来自**帧流**（FoeTelegraph），
                //   理由与 StateHint 相同（自动模式 state 是终局态，手动模式是分片的）。
                if (v.Telegraph != null)
                {
                    string tel = s.Alive ? (s.FoeTelegraph ?? "") : "";
                    if (v.Telegraph.text != tel) v.Telegraph.text = tel;
                    // 色块按谱系固定映射（设计文档 2.2）：玩家看颜色就能形成条件反射。
                    if (tel.Length > 0)
                    {
                        var c = TelegraphColorFor(tel);
                        if (v.Telegraph.color != c) v.Telegraph.color = c;
                    }
                }
                // 状态图标排：变了才重画（每秒都在刷的东西，别每帧重建 sprite）
                // ⛔ 阵亡单位必须**清空**图标排 —— 否则尸体头上还挂着一串状态（用户 2026-10-07 截图反馈）
                string sids = s.Alive ? (s.StatusIds ?? "") : "";
                if (v.StatusRow != null && v.StatusShown != sids) UpdateStatusRow(v, sids);
                // ★ 2026-10-26 用户："机制别用长条文字挂着，像伤害数字那样飘一下"
                //   常驻横幅仍保留（已缩成 2~5 字标签），变化时额外飘一条，让玩家"看到它发动了"
                if (s.StateHint != v.LastStateHint)
                {
                    v.LastStateHint = s.StateHint;
                    // 限流：0.7s 内只飘一条，避免多段机制同帧刷出两行叠在一起
                    if (!string.IsNullOrEmpty(s.StateHint) && v.Root != null &&
                        Time.time - _lastHintPop > 0.7f)
                    {
                        _lastHintPop = Time.time;
                        SpawnNumberInternal(v, s.StateHint, new Color(1f, 0.86f, 0.55f), 1.15f, 0.9f, raise: 1.05f);
                    }
                }
                v.LastSnap = s;
                v.Hp = s.Hp; v.MaxHp = s.MaxHp;
                SetHpBar2D(v, s.Hp, s.MaxHp);
                SetShield2D(v, s.Shield);
                SetAura2D(v, s);
                // ---- 破绽/失衡条：随帧流更新（死亡清空）----
                SetFlawBar2D(v,
                             s.Alive ? (s.FlawText ?? "") : "",
                             s.Alive ? s.FlawProgress : -1f,
                             s.Alive ? s.UnbalancedTurns : 0);
                SetOverlay2D(v, s.Alive ? s.StatusIds : null);   // 阵亡不挂状态外观
                // ⛔ 2026-11-01 用户："不要状态光环了，已经有状态图标" ⇒ 脚下只留元素附着圈。
                //   仍调用一次 null 把已经建出来的光环关掉（池化视图复用时会残留）。
                SetStatusAura2D(v, null);
                if (v.Alive && !s.Alive)
                {
                    v.DeadBlend = 0f;
                    HideUnitChrome(v);                       // ★ 阵亡：头顶 HUD / 状态排 / 光环 / 护盾膜 全部收掉
                }
                else if (!v.Alive && s.Alive)
                {
                    ShowUnitChrome(v);                       // ★ 复活（Boss 一两回合后站起来）
                    var rp = v.Root != null ? v.Root.transform.position : Vector3.zero;
                    ReviveFx.Spawn(rp + new Vector3(0f, 0.25f, -0.4f),
                                   new Color(1f, 0.90f, 0.62f, 1f));   // 金色复苏光柱
                }
                v.Alive = s.Alive;
            }
        }

        /// <summary>
        /// **世界空间点选**：鼠标世界坐标落在哪个单位上（用户定案：不用 UGUI 行悬停）。
        /// 命中口径 = 单位身上 `BoxCollider2D` 的**世界包围盒**（不是 Physics2D 查询，
        /// 少一层物理设置依赖）。多个重叠时取**最靠上**的那个（贴近"指到谁就是谁"）。
        /// 输出 <paramref name="anchorWorld"/> = 头顶上方一点，给 UGUI 面板定位用。
        /// </summary>
        public bool TryPickUnit(Vector3 worldPos, out string unitId, out string displayName, out Vector3 anchorWorld)
        {
            unitId = null; displayName = null; anchorWorld = worldPos;
            if (_views == null) return false;
            UnitView2D best = null; float bestTop = float.MinValue;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v.Root == null || !v.Alive) continue;   // 阵亡的不参与（指尸体不该弹面板）
                var col = v.Root.GetComponent<BoxCollider2D>();
                if (col == null) continue;
                // ⚠ 不用 `col.bounds` —— 它要物理引擎把碰撞体注册进去才有值（2D 物理没开/没步进时会是零）。
                //   自己按"碰撞体尺寸 × 世界缩放"算包围盒：确定、且**命中框仍在 Inspector 里可见可调**。
                var tr = v.Root.transform;
                Vector3 c = tr.TransformPoint(col.offset);
                Vector3 sz = Vector3.Scale(col.size, tr.lossyScale);
                float hx = Mathf.Abs(sz.x) * 0.5f, hy = Mathf.Abs(sz.y) * 0.5f;
                if (worldPos.x < c.x - hx || worldPos.x > c.x + hx) continue;
                if (worldPos.y < c.y - hy || worldPos.y > c.y + hy) continue;
                if (c.y + hy > bestTop) { bestTop = c.y + hy; best = v; }
            }
            if (best == null)
            {
                // ★ 2026-10-18：立绘包围盒之外，再认**状态图标排**（以及名字/血条那一带）。
                //   原本只有立绘本体参与命中 ⇒ 鼠标指着头顶那一排状态图标完全没反应，
                //   而"看这个状态是什么、能引爆什么反应"恰恰是玩家最想点的地方。
                best = PickByStatusRow(worldPos, out float rowTop);
                if (best == null) return false;
                unitId = best.UnitId;
                displayName = best.NameText != null ? best.NameText.text : null;
                // 锚点放**图标排之上**：提示面板不能盖住图标本身
                anchorWorld = new Vector3(best.Root.transform.position.x, rowTop + 0.25f,
                                          best.Root.transform.position.z);
                return true;
            }
            unitId = best.UnitId;
            displayName = best.NameText != null ? best.NameText.text : null;
            anchorWorld = new Vector3(best.Root.transform.position.x, bestTop, best.Root.transform.position.z);
            return true;
        }

        /// <summary>
        /// 命中「头顶信息带」（状态图标排 + 血条/名字）：这一带没有碰撞体，只能按渲染器世界包围盒算。
        /// <para>⚠ 只用 **SpriteRenderer.bounds**（渲染器自己算，不依赖 2D 物理注册），
        ///   与 <see cref="TryPickUnit"/> 里"不用 col.bounds"的理由相同。</para>
        /// </summary>
        private UnitView2D PickByStatusRow(Vector3 worldPos, out float topY)
        {
            topY = float.MinValue;
            UnitView2D best = null;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v.Root == null || !v.Alive) continue;
                if (v.StatusRow == null) continue;

                bool any = false;
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                // 图标排 + 血条 + 名字：都算"头顶信息带"
                AccumulateBounds(v.StatusRow.transform, ref any, ref minX, ref maxX, ref minY, ref maxY);
                if (v.HpBg != null) AccumulateBounds(v.HpBg.transform, ref any, ref minX, ref maxX, ref minY, ref maxY);
                if (v.NameText != null) AccumulateBounds(v.NameText.transform, ref any, ref minX, ref maxX, ref minY, ref maxY);
                if (!any) continue;

                const float pad = 0.10f;   // 手不用那么准
                if (worldPos.x < minX - pad || worldPos.x > maxX + pad) continue;
                if (worldPos.y < minY - pad || worldPos.y > maxY + pad) continue;
                if (maxY > topY) { topY = maxY; best = v; }
            }
            return best;
        }

        /// <summary>把一个节点（连同其激活的子渲染器）的世界包围盒并入统计。</summary>
        private static void AccumulateBounds(Transform t, ref bool any,
                                             ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            if (t == null) return;
            var srs = t.GetComponentsInChildren<SpriteRenderer>();
            for (int i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                if (!sr.gameObject.activeInHierarchy) continue;
                var b = sr.bounds;
                any = true;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
            }
        }

        /// <summary>
        /// 取某单位**当前画面**上的状态串（`id:层数|id:层数`）—— 给"悬停看状态列表"用。
        /// ⛔ 数据源是**帧流累积出来的视图**（ApplyFrame 逐帧叠出来的），**不是 `_state`**：
        ///   自动模式下 `_state` 是整场跑完的终局态，直接读会让提示从第一帧就显示"打完之后的状态"。
        /// </summary>
        /// <summary>取某单位**当前画面**上的破绽/失衡文案（空 = 没有）。给悬停提示解释机制用。</summary>
        public bool TryGetViewFlawText(string unitId, out string text)
        {
            text = null;
            if (unitId == null || _views == null) return false;
            if (!_views.TryGetValue(unitId, out var v) || v == null) return false;
            text = v.FlawShownText;
            return !string.IsNullOrEmpty(text);
        }

        /// <summary>阵亡：收掉这个单位**所有**附属显示（头顶 HUD / 状态排 / 破绽条 / 光环 / 护盾膜 / 状态外观）。
        /// ⛔ 只隐藏、不销毁 —— 复活的单位要原样恢复。</summary>
        private void HideUnitChrome(UnitView2D v)
        {
            if (v == null || v.ChromeHidden) return;
            v.ChromeHidden = true;
            if (v.Head != null) v.Head.gameObject.SetActive(false);
            if (v.StatusRow != null) v.StatusRow.SetActive(false);
            if (v.StatusAura != null) v.StatusAura.gameObject.SetActive(false);
            if (v.ShieldField != null) v.ShieldField.gameObject.SetActive(false);
            if (v.Overlay != null) v.Overlay.enabled = false;
            // 散落的 TextMesh（不挂在 Head 下的也要关）
            if (v.Hint != null) v.Hint.gameObject.SetActive(false);
            if (v.Telegraph != null) v.Telegraph.gameObject.SetActive(false);
            if (v.ShieldText != null) v.ShieldText.gameObject.SetActive(false);
            if (v.FlawLabel != null) v.FlawLabel.gameObject.SetActive(false);
            if (v.NameText != null) v.NameText.gameObject.SetActive(false);

            // 冲锋中阵亡：把冲锋状态与临时置顶一并复位（否则尸体永远压在别人上面）
            v.LungeActive = false;
            v.LungeElapsed = 0f;
            if (v.Body != null && v.BaseSortingOrder > 0) v.Body.sortingOrder = v.BaseSortingOrder;
            v.KickT = 0f;
            v.HitFlash = 0f;
        }

        /// <summary>复活：把阵列全部恢复（状态/光环/护盾交给下一帧的帧流重新判定）。</summary>
        private void ShowUnitChrome(UnitView2D v)
        {
            if (v == null || !v.ChromeHidden) return;
            v.ChromeHidden = false;
            if (v.Head != null) v.Head.gameObject.SetActive(true);
            if (v.StatusRow != null) v.StatusRow.SetActive(true);
            if (v.Overlay != null) v.Overlay.enabled = true;
            if (v.Hint != null) v.Hint.gameObject.SetActive(true);
            if (v.Telegraph != null) v.Telegraph.gameObject.SetActive(true);
            if (v.ShieldText != null) v.ShieldText.gameObject.SetActive(true);
            if (v.FlawLabel != null) v.FlawLabel.gameObject.SetActive(true);
            if (v.NameText != null) v.NameText.gameObject.SetActive(true);
            v.OverlayShown = null;      // 强制重设外观（否则复活后状态贴图不刷新）
            v.StatusShown = null;
            v.FlawShownText = null;
        }

        private UnitView2D EnsureView(string unitId)
        {
            if (_state == null || string.IsNullOrEmpty(unitId)) return null;
            var all = _state.AllUnitsEver;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.RuntimeId != unitId) continue;
                if (!u.Pos.IsValid) return null;
                BuildUnitsFor(new BattleUnit[] { u });
                UnitView2D v;
                _views.TryGetValue(unitId, out v);
                return v;
            }
            return null;
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


        /// <param name="dt">舞台动画步长（含倍速缩放——单位抖动/冲锋等随倍速变快是对的）。</param>
        /// <param name="fxDt">大招演出的步长（**不含倍速**——用户 2026-10-14：释放速度跟几倍速没关系，cut-in 别被 ×4 一闪而过）。&lt;0 表示同 dt。</param>

        /// <summary>双击单位 → 抛给 UI 层（BattlePanel 打开 UGUI 面板并暂停）。
        /// 参数：名字 / 数值行 / 状态行 / 技能行 / 机制行 / 元素色。</summary>
        public System.Action<string, string, string, string, string, Color> OnInspectRequested;

        /// <summary>双击任意单位 → 弹出信息卡（小怪也支持）。</summary>
        private void PollInspectClick()
        {
            if (_cam == null) return;
            // ⛔ QTE 窗口期不响应双击（用户 2026-10-26）：格挡要连点，别被"查看"抢走
            if (_qtePrompt.Visible) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (Time.unscaledTime - _lastClickAt < 0.30f) { _lastClickAt = -1f; TryInspectUnderMouse(); }
            else _lastClickAt = Time.unscaledTime;
        }

        private void TryInspectUnderMouse()
        {
            Vector3 wp = _cam.ScreenToWorldPoint(Input.mousePosition);
            wp.z = 0f;
            UnitView2D best = null; float bestD = 1.15f;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v == null || v.Root == null || !v.Root.activeSelf) continue;
                float d = Vector2.Distance(wp, v.Root.transform.position);
                if (d < bestD) { bestD = d; best = v; }
            }
            if (best == null) return;

            var s = best.LastSnap;
            string side = best.PlayerSide ? "我方" : "敌方";
            string nm = best.NameText != null ? best.NameText.text : "单位";
            string st = side + "　HP " + s.Hp + " / " + s.MaxHp +
                        (s.Shield > 0 ? "　护盾 " + s.Shield : "") +
                        "　攻击 " + s.Atk + "　元素 " + ElementCn(s.Element) +
                        (s.AuraEl != Element.None && s.AuraStacks > 0
                            ? "\n附着：" + ElementCn(s.AuraEl) + " ×" + s.AuraStacks + "（再打其它元素可触发反应）"
                            : "\n附着：无（先用一种元素打它，再换另一种）");
            string sk = BuildSkillDetail(best, s);

            // 机制说明：主线首领用**完整文案**（几回合召唤/半血做什么都写清楚），
            // 秘境暂时只有短标签（完整文案在 Campaign 层，Battle 不反向依赖）。
            string mech = null;
            if (!string.IsNullOrEmpty(best.DefId) && WanXiang.Battle.Core.BossCatalog.IsBoss(best.DefId))
                mech = WanXiang.Battle.Core.BossCatalog.MechanicText(best.DefId);
            else if (!string.IsNullOrEmpty(best.DefId) && best.DefId.StartsWith("rc_"))
                // 秘境：完整文案在 Campaign 层，Battle 不认识 —— 带上 id 让 UI 层去查，
                // 查不到就退回短标签（`@realm:<rcId>|<tag>` 是两层的约定格式）。
                mech = "@realm:" + best.DefId + "|" + (s.StateHint ?? "");
            if (string.IsNullOrEmpty(mech)) mech = s.StateHint ?? "";

            if (OnInspectRequested != null)
            {
                OnInspectRequested(nm, st, s.Statuses ?? "", sk, mech,
                                   BattlePalette.OfElement(s.Element));
            }
            else
            {
                var lines = new System.Collections.Generic.List<string>(6)
                { nm, st, string.IsNullOrEmpty(s.Statuses) ? "状态：无" : "状态：" + s.Statuses };
                if (!string.IsNullOrEmpty(s.StateHint)) lines.Add("机制：" + s.StateHint);
                UnitInspectCard.Show(best.Root.transform.position, lines.ToArray(), BattlePalette.OfElement(s.Element));
            }
        }

        /// <summary>技能明细（名字 + 效果）。用当前帧的 Def 取，不需要 state。</summary>
        private static string BuildSkillDetail(UnitView2D v, WanXiang.Battle.Core.UnitSnapshot snap)
        {
            var d = v != null ? v.Def : null;
            if (d == null) return "（无技能数据）";
            System.Func<SkillDef, string, int, string> fmt = (sk, tag, remain) =>
            {
                if (sk == null) return "";
                // ⛔ 2026-11-01 用户："技能没有 CD，别再写冷却" ⇒ 只留名字与效果
                string desc = string.IsNullOrEmpty(sk.Description) ? "" : "\n　　" + sk.Description;
                return "【" + tag + "】" + sk.Name + desc;
            };
            var sb = new System.Text.StringBuilder();
            sb.Append(fmt(d.Basic, "普攻", snap.Cd0)).Append('\n');
            sb.Append(fmt(d.Active, "战技", snap.Cd1)).Append('\n');
            sb.Append(fmt(d.Ultimate, "绝技", snap.Cd2));
            return sb.ToString();
        }

        private static string ElementCn(Element el)
        {
            switch (el)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                case Element.Earth: return "土";
                default: return "无";
            }
        }


        // ================================================================

        /// <summary>通用飘字（伤害/恢复/护盾共用）。美术替换：改这里的字体/字号/描边即可。</summary>
        // ---- 元素反应演出（2026-10-17）----
        private struct Burst { public Transform T; public SpriteRenderer Sr; public Vector2 Vel; public float Life, Max; }
        private readonly System.Collections.Generic.List<Burst> _bursts =
            new System.Collections.Generic.List<Burst>(32);
        // ★ 2026-11-17 性能优化：抛物线火花的回收栈（元素反应/魇化/落地尘共用）。
        //   原来每次 new GameObject + Destroy，现在 SetActive(false) 回收复用。
        private readonly System.Collections.Generic.Stack<Burst> _burstFree =
            new System.Collections.Generic.Stack<Burst>(32);
        private static readonly Color TbColPlayer  = new Color(0.55f, 0.95f, 0.75f);
        private static readonly Color TbColEnemy   = new Color(0.98f, 0.52f, 0.86f);
        private static Sprite _shieldCircle;
        private static Sprite _shieldFill;

        /// <summary>取一个火花对象（复用优先），只设置身份属性；速度/寿命由调用方填。</summary>
        private Burst AcquireBurst(Sprite sp, int order, Vector3 pos, float scale)
        {
            Burst b;
            if (_burstFree.Count > 0)
            {
                b = _burstFree.Pop();
                if (b.T != null)
                {
                    b.T.gameObject.SetActive(true);
                    b.T.position = pos;
                    b.T.localScale = Vector3.one * scale;
                }
                if (b.Sr != null) { b.Sr.sprite = sp; b.Sr.sortingOrder = order; b.Sr.color = Color.white; }
            }
            else
            {
                var go = new GameObject("Burst");
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * scale;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp; sr.sortingOrder = order;
                b = new Burst { T = go.transform, Sr = sr };
            }
            return b;
        }

        private static void RecycleBurst(Burst b)
        {
            if (b.T != null) b.T.gameObject.SetActive(false);
        }



        private static void RestoreBodyMaterial(UnitView2D v)
        {
            if (v == null) return;
            if (v.DissolveMat != null)
            {
                if (v.Body != null && v.BodyMaterial != null) v.Body.sharedMaterial = v.BodyMaterial;
                Destroy(v.DissolveMat);
                v.DissolveMat = null;
            }
        }

        /// <summary>护盾膜（懒创建）：挂一个和立绘同尺寸的 SpriteRenderer + ShieldField 材质。</summary>
        private void EnsureShieldField(UnitView2D v)
        {
            if (v == null || v.Root == null || v.Body == null) return;
            if (v.ShieldField != null && v.ShieldMat != null) return;   // 有膜且材质在 → 复用

            var sh = Resources.Load<Shader>("Battle/ShieldField");
            // ★ 2026-11-02 用户："护盾还是不圆" ⇒ 换成程序化**圆形**贴图（六边形贴图本身是方的）
            var sp = ShieldCircleSprite();
            if (sh == null || sp == null || !sh.isSupported) return;   // 不可用就静默不套膜

            var go = new GameObject("ShieldField");
            go.transform.SetParent(v.Body.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // 两层：里层"淡填充膜"（能看到立绘）+ 外层"环"（轮廓清晰）
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ShieldFillSprite();
            sr.sortingOrder = v.Body.sortingOrder + 2;
            sr.color = new Color(0.62f, 0.90f, 1f, 0.16f);   // 膜：极淡的青蓝填充

            var ringGo = new GameObject("ShieldRing");
            ringGo.transform.SetParent(go.transform, false);
            var ringSr = ringGo.AddComponent<SpriteRenderer>();
            ringSr.sprite = sp;
            ringSr.sortingOrder = v.Body.sortingOrder + 3;
            ringSr.color = new Color(0.62f, 0.92f, 1f, 0.85f);   // 环：亮青蓝
            // ⛔ 不要再碰 MeshRenderer —— 这是 SpriteRenderer，取 MeshRenderer 会抛
            //   MissingComponentException，把整个战斗播放循环打断 ⇒ 表现为"卡死"。
            //   sortingOrder 在上面 Add<SpriteRenderer> 后已经设过。

            // 尺寸：按立绘**世界**尺寸 ×1.15（⛔ 必须用 lossyScale：单位根节点有 0.1 缩放，
            //   直接拿 sprite.bounds 会算成世界尺寸，膜会大得离谱或小到看不见）
            var bs = v.Body.sprite != null ? v.Body.sprite.bounds.size : new Vector3(1f, 1f, 0f);
            var ls = v.Body.transform.lossyScale;
            float worldW = Mathf.Abs(bs.x * ls.x);
            float worldH = Mathf.Abs(bs.y * ls.y);

            // 父级是 Body（可能带缩放），localScale = 目标世界尺寸 / 父级世界缩放 / 贴图原始尺寸
            float pw = Mathf.Abs(v.Body.transform.lossyScale.x) > 0.0001f ? Mathf.Abs(v.Body.transform.lossyScale.x) : 1f;
            float ph = Mathf.Abs(v.Body.transform.lossyScale.y) > 0.0001f ? Mathf.Abs(v.Body.transform.lossyScale.y) : 1f;
            // ★ 2026-11-02：**统一直径**（格子边长 ×1.02）—— 大小一致才不像"随机泡泡"。
            //   长边方案会让大立绘的圈特别大、小立绘特别小（用户截图实锤）。
            float dia = Cell * 0.68f;   // ★ 再缩：0.78 → 0.68
            float spSize = Mathf.Max(0.001f, sp.bounds.size.x);
            go.transform.localScale = new Vector3(dia / pw / spSize, dia / ph / spSize, 1f);

            var mat = new Material(sh);
            mat.SetFloat("_Strength", 0.8f);
            mat.SetFloat("_ScrollSpeed", 0.06f);
            mat.SetFloat("_BaseAlpha", 0.6f);
            mat.SetFloat("_RimPower", 1.6f);
            mat.SetFloat("_HitPhase", 0f);
            mat.SetFloat("_BaseAlpha", 1f);
            mat.SetColor("_Color", new Color(0.66f, 0.92f, 1f, 1f));
            sr.material = mat;

            v.ShieldField = sr;
            v.ShieldMat = mat;
        }

        // ---- 回合横幅（带装饰的组：黑色压条 + 两侧细线 + 大字 + 小字） ----
        private Transform _tbGroup;
        private TextMesh _tbBig, _tbSub;
        private SpriteRenderer _tbBack, _tbRuleL, _tbRuleR;
        private float _tbT;                 // 剩余显示时间
        private int _tbTurn;
        private int _turnNo;                // 当前回合数（横幅与逻辑共用）
        private const float TbLife = 2.4f, TbFade = 0.55f, TbSlide = 0.28f;


        // ================================================================
        //  工具
        // ================================================================

        /// <summary>
        /// 单位落位：★ 必须与 <see cref="BuildBoard"/> 的格子公式**完全一致**，
        /// 否则单位站不进格子（用户反馈"九宫格站不对"的真因：网格用 1.00/0.72，
        /// 单位却用 0.82/0.66 且双方 y 基准相反 ， 永远对不上）。
        /// 网格格子中心 = (cx + (col-1)*Cell, BoardY + (1-row)*Cell*0.72)。
        /// 单位枢轴在底部中点、Step 会再加 FootOffset 抬脚 ， 这里先减去它。
        /// 敌方做列镜像（col→2-col）与我方对称；BuildBoard 用同一镜像公式，单位与格子始终对齐。
        /// </summary>
        /// <summary>格子中心的世界坐标（捕获球落点用；单位头顶锚点不等于格子中心）。</summary>
        public Vector3 CellWorldCenter(WanXiang.Battle.Core.TeamSide side, int index)
        {
            // ⚠ CellPos 是**单位落位**（比格子中心低 FootOffset），球要格子几何中心（用户 11-13：偏下）
            int col = index % 3;
            if (side != WanXiang.Battle.Core.TeamSide.Player) col = 2 - col;
            int row = index / 3;
            float cx = side == WanXiang.Battle.Core.TeamSide.Player ? PlayerX : EnemyX;
            var local = new Vector3(cx + (col - 1) * Cell, BoardY + (1 - row) * Cell * BoardRowGap, 0f);
            return transform.TransformPoint(local);
        }

        /// <summary>捕获成功后隐藏该单位（立绘 + 头顶 HUD），避免"抓到了还站在场上"。</summary>
        public void HideUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId) || _views == null) return;
            if (!_views.TryGetValue(unitId, out var v)) return;
            v.Hidden = true;                       // ★ 帧更新会无条件重激活 Root，必须标记
            if (v.Root != null) v.Root.SetActive(false);
            if (v.Head != null) v.Head.gameObject.SetActive(false);
        }

        /// <summary>被收服：金色爆发 + 立绘淡出 + 延迟隐藏（用户："直接消失太秃"）。</summary>
        public void CaptureVanish(string unitId)
        {
            if (string.IsNullOrEmpty(unitId) || _views == null) return;
            if (!_views.TryGetValue(unitId, out var v)) return;
            v.Hidden = true;
            if (v.Root != null)
            {
                var gold = new Color(1f, 0.88f, 0.55f, 1f);
                BossSkillBurst.Play(v.Root.transform.position, gold, 2.6f, 18, 0.62f, 0);
                BossSkillBurst.PlayInward(v.Root.transform.position, new Color(1f, 0.95f, 0.8f, 1f), 1.6f, 0.45f);
                var body = v.Body;
                if (body != null)
                {
                    var startC = body.color;
                    DG.Tweening.DOTween.To(() => 0f, k =>
                    {
                        if (body == null) return;
                        body.color = new Color(startC.r, startC.g, startC.b, Mathf.Lerp(1f, 0f, k));
                    }, 1f, 0.5f);
                    DG.Tweening.DOVirtual.DelayedCall(0.52f, () => { if (v.Root != null) v.Root.SetActive(false); });
                }
                else
                {
                    DG.Tweening.DOVirtual.DelayedCall(0.5f, () => { if (v.Root != null) v.Root.SetActive(false); });
                }
            }
            if (v.Head != null) v.Head.gameObject.SetActive(false);
        }

        /// <summary>捕获瞄准：可捕敌方格染金色（与技能预览的橙红区分，用户 11-13 混淆）</summary>
        public void HighlightCaptureCells(List<int> enemyCells)
        {
            var gold = new Color(0.96f, 0.83f, 0.45f, 1f);
            for (int i = 0; i < 9; i++)
            {
                if (_cellsEnemy[i] == null) continue;
                bool on = enemyCells != null && enemyCells.Contains(i);
                _cellsEnemy[i].color = on ? gold : _cellBaseEnemy[i];
            }
            for (int i = 0; i < 9; i++)
                if (_cellsPlayer[i] != null) _cellsPlayer[i].color = _cellBasePlayer[i];
        }

        private static Vector2 CellPos(bool player, int cell)
        {
            int col = cell % 3;
            if (!player) col = 2 - col;   // 敌方列镜像 ， 同码两侧呈镜像对称
            int row = cell / 3;
            float cx = player ? PlayerX : EnemyX;
            return new Vector2(cx + (col - 1) * Cell,
                               BoardY + (1 - row) * Cell * BoardRowGap - FootOffset);
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
        // ★ 2026-11-17 性能优化：纯色块 Sprite 缓存。
        //   原实现每次调用都 new Texture2D + Sprite.Create —— 元素反应火花每次炸 8 张纹理，
        //   一场战斗几百张，是比 GameObject 创建更严重的 GC/显存压力。
        //   纯色块的像素只由 (颜色,尺寸,边框,边框色) 决定 ⇒ 同参数完全可复用，行为零变化。
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _solidSpriteCache
            = new System.Collections.Generic.Dictionary<string, Sprite>(32);

        public static Sprite SolidSprite(Color c, int w, int h, int border, Color borderColor)
        {
            string key = c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3") + "," + c.a.ToString("F3")
                       + "|" + w + "x" + h + "|" + border + "|"
                       + borderColor.r.ToString("F3") + "," + borderColor.g.ToString("F3")
                       + "," + borderColor.b.ToString("F3") + "," + borderColor.a.ToString("F3");
            Sprite cached;
            if (_solidSpriteCache.TryGetValue(key, out cached) && cached != null) return cached;

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
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            _solidSpriteCache[key] = sprite;
            return sprite;
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
// [force-recompile 2026-10-18]
