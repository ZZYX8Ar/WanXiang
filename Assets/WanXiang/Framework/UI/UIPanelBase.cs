// ============================================================================
//  WanXiang · UI 框架 · 面板基类
//  ---------------------------------------------------------------------------
//  所有业务面板继承本类。框架负责生命周期调用、事件订阅的自动回收、
//  层级与遮罩管理；业务只需要关心"我这个界面长什么样、点了之后干什么"。
//
//  为什么事件订阅要由框架统一回收（本文件最重要的一处设计）：
//    手写 RegisterEvent 而忘记 Unregister 是 UI 项目最高频的 bug 来源，后果有三：
//      1. 内存泄漏 —— 事件持有面板引用，面板对象永远无法被 GC
//      2. 空引用崩溃 —— 事件触发时面板已销毁，访问已释放的组件
//      3. 逻辑错乱 —— 已经关闭的面板仍在响应事件，比如背包关了还在刷新
//    框架用 _disposables 统一收集、关闭时统一释放，把"依赖开发者自觉"
//    变成"框架保证"。这是商业项目和练习项目的分水岭之一。
//
//  动效（tween）也归框架管，理由与上面完全同构：
//    Normal 层面的面板走 Cached 策略，关闭时只是 SetActive(false)，对象并不销毁。
//    若此刻还有 tween 在跑，它会继续修改一个"已经关掉"的面板 —— 下次打开时
//    面板就带着残留状态出现：半透明、歪了一点、位置偏了几像素。
//    更糟的是 Cached 面板被淘汰销毁时，tween 攥着的是已销毁对象。
//    所以动效必须经 TrackTween 登记，由框架在关闭时统一 Kill。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;      // 面板动效（见 TrackTween）
using UnityEngine;
using UnityEngine.UI;   // CanvasGroup
using WanXiang.Framework.Audio;   // 面板背景音乐（BgmId）

namespace WanXiang.Framework.UI
{
    /// <summary>可释放句柄。用于把"取消订阅"这个动作包装成可统一收集的对象。</summary>
    internal sealed class UIActionDisposable : IDisposable
    {
        private Action _onDispose;

        public UIActionDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            var action = _onDispose;
            _onDispose = null;
            action?.Invoke();
        }
    }

    [RequireComponent(typeof(RectTransform))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 元数据（由框架在实例化后注入，业务只读）
        // ------------------------------------------------------------------

        /// <summary>资源 key。</summary>
        public string Key { get; private set; }

        /// <summary>
        /// 彩排（Ghost）静默开关（2026-10-14 首播优化）：
        /// 预热/彩排会在**转场遮盖期**把大面板真开一遍再关掉，把"首次打开"的
        /// 一次性成本（实例化 / OnEnable 级联 / TMP 图集 / 首帧布局与贴图上传）
        /// 提前烤热，让玩家第一次进图不再卡（用户："第一次会有一点卡，后面就流畅"）。
        /// 期间置 true ⇒ 不播开/关界面音、不切 BGM；需要静默的面板在 OnOpenAsync 里
        /// 用本开关跳过有副作用的块（如开场面板触发、写档）。
        /// </summary>
        public static bool GhostSilent;

        /// <summary>所属层级。</summary>
        public UILayer Layer { get; private set; }

        /// <summary>缓存策略。</summary>
        public UICachePolicy CachePolicy { get; private set; }

        /// <summary>点击遮罩是否关闭。</summary>
        public bool CloseOnMaskClick { get; private set; }

        /// <summary>是否全屏独占。</summary>
        public bool FullScreen { get; private set; }

        /// <summary>当前生命周期状态。</summary>
        public UIPanelState State { get; private set; } = UIPanelState.None;

        /// <summary>
        /// 关闭时是否保留事件订阅。
        /// 默认 false（关闭即解绑，安全优先）。
        /// 常驻 HUD 这类需要长期监听数据变化的面板，可覆写为 true —— 但要自己保证
        /// 处理器内部判断 <see cref="State"/>，避免在关闭状态下执行 UI 更新。
        /// </summary>
        protected virtual bool KeepSubscriptionsOnClose => false;

        /// <summary>
        /// 打开时是否自动暂停下层面板。
        ///
        /// 判据是"本面板所在的层是否需要遮罩"（见 <see cref="UILayerUtil.NeedsMask"/>）：
        /// 有遮罩 → 下层输入已被拦住 → 下层没有理由继续跑动画与计时器。
        ///
        /// 注意这里**不**看 <see cref="FullScreen"/>：全屏只是视觉表现（是否铺满屏幕），
        /// 一个居中的半屏确认框同样会拦住下层输入，同样应该让下层停下来。
        /// 把这两件事混在一起，就会出现"确认框弹出着，背包的冷却倒计时还在跑"。
        ///
        /// 个别面板有特殊需求时可覆写本属性（例如"打开背包时 HUD 血条仍要跳动"）。
        /// </summary>
        public virtual bool ShouldPauseBelow => UILayerUtil.NeedsMask(Layer);

        /// <summary>
        /// 是否允许被"返回键 / 手柄 B 键"关闭。
        /// 覆写为 false 的场景：强制引导界面、必须做出选择的弹窗、结算界面。
        /// 千万不要让玩家能用返回键跳过付费确认或新手引导 —— 这类漏洞上线后很难补。
        /// </summary>
        public virtual bool AllowBackClose => true;

        /// <summary>
        /// 打开顺序号。由框架在打开时分配，用于判定面板之间的上下遮挡关系。
        /// </summary>
        public int OpenSequence { get; private set; }

        // ------------------------------------------------------------------
        // 运行时上下文
        // ------------------------------------------------------------------

        /// <summary>
        /// UI 系统引用，由框架注入。业务可用它开关其它面板。
        /// 命名刻意不叫 UISystem —— 那样会与类型名 UISystem 同名，
        /// 派生类里写 UISystem.Close() 时很难一眼看出调的是实例还是类型。
        /// </summary>
        protected IUISystem UI { get; private set; }

        /// <summary>本次打开的入参。在 OnOpenAsync / OnOpened 中可读。</summary>
        protected object Payload { get; private set; }

        private readonly List<IDisposable> _disposables = new List<IDisposable>(8);
        private readonly List<Tween> _trackedTweens = new List<Tween>(4);
        private RectTransform _rect;
        private CanvasGroup _canvasGroup;
        private CancellationTokenSource _panelCts;

        /// <summary>
        /// 面板级取消令牌。面板关闭时自动取消。
        /// 面板内启动的异步任务都应该传入它，避免"面板关了但协程还在跑"。
        /// </summary>
        protected CancellationToken PanelToken =>
            _panelCts?.Token ?? CancellationToken.None;

        protected RectTransform Rect
        {
            get
            {
                if (_rect == null) _rect = transform as RectTransform;
                return _rect;
            }
        }

        /// <summary>CanvasGroup，用于整面板的透明度与射线开关。</summary>
        protected CanvasGroup CanvasGroup
        {
            get
            {
                // 注意这里刻意不用 ?? ：UnityEngine.Object 重载了 == 运算符，
                // 但 ?? 走的是真正的引用判空，两者语义不同。
                // 混用会造成"看着有判空其实没判"的隐患，统一写成显式 if。
                if (_canvasGroup == null)
                {
                    _canvasGroup = GetComponent<CanvasGroup>();
                    if (_canvasGroup == null)
                    {
                        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }
                return _canvasGroup;
            }
        }

        /// <summary>
        /// **彩排期间把整个面板藏起来**（UISystem.GhostOpenCloseAsync 内部用）。
        ///
        /// ⚠ 为什么要它：GhostSilent 只保证"不播音/不写档"，面板是**真的打开并渲染**的。
        ///   彩排若发生在屏幕已露出来之后（转场播完 → 玩家正看着主界面），
        ///   玩家就会看到面板闪一下（用户实测：进主界面时"闪了一下节点地图"）。
        ///
        /// ⚠ 用 alpha=0 而不是 SetActive(false)：SetActive 关掉对象就不渲染，
        ///   "内容首帧渲染 + 贴图上传"这个最贵的帧不会发生 ⇒ 预热白做。
        ///   alpha=0 时布局、Canvas 重建、贴图上传照跑（成本烘热），只是看不见。
        ///
        /// ⚠⚠ **必须在 SetActive(true) 的同一帧就设好**，不能等打开流程 await 完再设
        ///   （用户实测：这样仍会闪一下）。
        ///   InternalOpenAsync 里是 SetActive(true) → await OnOpenAsync(...)，
        ///   那个 await 已经让出一帧、画面已经画出来了 ⇒ 补设 alpha 晚了整整一帧。
        ///   所以彩排要**先置 <see cref="_ghostHidden"/>**，由 InternalOpenAsync 在
        ///   SetActive(true) 之后立刻应用。
        /// </summary>
        internal void SetGhostInvisible(bool invisible)
        {
            _ghostHidden = invisible;
            ApplyGhostVisibility();
        }

        /// <summary>彩排藏身标记。由 InternalOpenAsync 在激活后立刻检查。</summary>
        private bool _ghostHidden;

        private void ApplyGhostVisibility()
        {
            var cg = CanvasGroup;
            if (cg == null) return;
            cg.alpha = _ghostHidden ? 0f : 1f;
            cg.interactable = !_ghostHidden;
            cg.blocksRaycasts = !_ghostHidden;
        }

        // ==================================================================
        //  框架内部调用（业务不要调用这些）
        // ==================================================================

        internal void InternalInit(UIPanelMeta meta, IUISystem uiSystem)
        {
            Key = meta.Key;
            Layer = meta.Layer;
            CachePolicy = meta.CachePolicy;
            CloseOnMaskClick = meta.CloseOnMaskClick;
            FullScreen = meta.FullScreen;
            UI = uiSystem;

            _rect = transform as RectTransform;
            if (_rect == null)
            {
                // 面板必须是 UI 元素。这里不尝试补救（给普通 GameObject 硬加 RectTransform
                // 会替换掉它的 Transform，造成更难查的问题），直接报错暴露配置错误。
                Debug.LogError(
                    $"[UI] 面板 {GetType().Name} 所在对象上没有 RectTransform，" +
                    $"它不是 UI 元素。请确认面板 Prefab 是在 Canvas 下创建的标准 UI 对象。");
            }

            _panelCts = new CancellationTokenSource();

            State = UIPanelState.Created;
            try
            {
                OnCreate();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnCreate 抛出异常：{ex}");
            }
        }

        internal async UniTask InternalOpenAsync(object payload, int openSequence)
        {
OpenSequence = openSequence;
     Payload = payload;
      gameObject.SetActive(true);
      SetInteractive(true);
         // ⚠ 彩排预隐必须在**这一帧**就应用，不能等下面的 await（用户实测：补设仍闪一下）。
        //InternalOpenAsync 是 SetActive(true) → await OnOpenAsync(...)，
        //   那个 await 已经让出一帧、画面已经画出来了。
      //   ⛔ 顺序：必须在 SetInteractive 之后 —— 它会把 interactable/blocksRaycasts 改回 true，
        //   那样就把彩排的"关射线"覆盖掉了。
        if (_ghostHidden) ApplyGhostVisibility();

            // 音乐先起，不等 OnOpenAsync 里的加载/网络 —— 界面一露脸就该有氛围。
            // （AudioSystem.PlayMusic 对"同一首正在播"会直接返回，不会重启，重复调用是安全的）
            // ⚠ GhostSilent（彩排期）：不播音、不切 BGM（见 GhostSilent 字段注释）
            if (!GhostSilent) ApplyPanelBgm();

            // 界面音效：全屏面板才播开合音；按钮点击音统一挂上（见文件末尾「界面音效」注释）
            if (FullScreen && !GhostSilent) AudioSystem.PlaySfx("sfx_ui_panel_open");
            HookButtonSfx();

            State = UIPanelState.Opening;
            try
            {
                await OnOpenAsync(payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnOpenAsync 抛出异常：{ex}");
            }

            State = UIPanelState.Opened;
            try
            {
                OnOpened();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnOpened 抛出异常：{ex}");
            }
        }

        /// <summary>
        /// 面板已处于打开状态，又被 OpenAsync 调用了一次时执行（不重放 OnOpenAsync）。
        /// 用途：连续查看图鉴时"从异兽甲切到异兽乙"，避免闪一下面板。
        /// </summary>
        internal void InternalRefresh(object payload)
        {
            Payload = payload;
            try
            {
                OnRefresh(payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnRefresh 抛出异常：{ex}");
            }
        }

        internal void InternalPause()
        {
            if (State != UIPanelState.Opened) return;

            State = UIPanelState.Paused;
            SetInteractive(false);
            try
            {
                OnPause();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnPause 抛出异常：{ex}");
            }
        }

        internal void InternalResume()
        {
            if (State != UIPanelState.Paused) return;

            State = UIPanelState.Opened;
            SetInteractive(true);

            // 上层弹窗关掉了，把自己那首音乐放回来。
            // 少了这一句，就会出现"打开过灵市之后，主界面一直没音乐"。
            ApplyPanelBgm();

            try
            {
                OnResume();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnResume 抛出异常：{ex}");
            }
        }

        internal void InternalClose()
        {
            if (State == UIPanelState.Closed ||
                State == UIPanelState.Closing ||
                State == UIPanelState.None) return;

            State = UIPanelState.Closing;
            SetInteractive(false);

            // 与打开音配对（同样只给全屏面板 —— 弹层关闭太频繁，加音效会吵）
            // ⚠ GhostSilent（彩排期）：不播关闭音
            if (FullScreen && !GhostSilent) AudioSystem.PlaySfx("sfx_ui_panel_close");

            try
            {
                OnClose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnClose 抛出异常：{ex}");
            }

            // 取消面板内所有异步任务
            try
            {
                if (_panelCts != null && !_panelCts.IsCancellationRequested)
                {
                    _panelCts.Cancel();
                }
            }
            catch (ObjectDisposedException) { /* 忽略 */ }

            // 统一释放事件订阅
            if (!KeepSubscriptionsOnClose)
            {
                DisposeSubscriptions();
            }

            // 统一停掉本面板登记的动效。
            // 必须显式 Kill：DOTween 的 tween 由它自己的 update loop 驱动，
            // 与 GameObject 的 active 状态无关 —— 面板 SetActive(false) 之后
            // 动效照样会继续跑、继续改属性。
            KillTrackedTweens();

            Payload = null;
            gameObject.SetActive(false);
            State = UIPanelState.Closed;
        }

        /// <summary>重新打开一个 Closed 状态的面板前调用：重建取消令牌。</summary>
        internal void PrepareReopen()
        {
            if (_panelCts == null || _panelCts.IsCancellationRequested)
            {
                try { _panelCts?.Dispose(); } catch { /* 忽略 */ }
                _panelCts = new CancellationTokenSource();
            }

            // 若关闭时解绑了订阅，重新打开时需重新订阅
            if (!KeepSubscriptionsOnClose && State == UIPanelState.Closed)
            {
                State = UIPanelState.Created;
            }
        }

        internal void InternalDestroy()
        {
            DisposeSubscriptions();
            KillTrackedTweens();
            UnhookButtonSfx();   // 把挂在子按钮上的点击音监听摘掉，避免悬挂引用

            try
            {
                if (_panelCts != null)
                {
                    if (!_panelCts.IsCancellationRequested) _panelCts.Cancel();
                    _panelCts.Dispose();
                    _panelCts = null;
                }
            }
            catch (Exception) { /* 忽略 */ }

            try
            {
                OnDestroyed();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name}.OnDestroyed 抛出异常：{ex}");
            }

            State = UIPanelState.None;
        }

        private void SetInteractive(bool on)
        {
            var cg = CanvasGroup;
            if (cg != null)
            {
                cg.interactable = on;
                cg.blocksRaycasts = on;
            }
        }

        private void DisposeSubscriptions()
        {
            for (int i = 0; i < _disposables.Count; i++)
            {
                try { _disposables[i]?.Dispose(); }
                catch (Exception ex) { Debug.LogError($"[UI] 释放订阅失败：{ex}"); }
            }
            _disposables.Clear();
        }

        /// <summary>
        /// 停掉本面板登记过的所有动效。
        ///
        /// 不靠 SetLink(KillOnDestroy) 兜底 —— 那只覆盖"对象被销毁"，
        /// 覆盖不到"面板关了但对象留着复用"，而这恰是 Cached 策略的常态。
        /// </summary>
        private void KillTrackedTweens()
        {
            for (int i = _trackedTweens.Count - 1; i >= 0; i--)
            {
                var tween = _trackedTweens[i];
                if (tween != null && tween.IsActive())
                {
                    tween.Kill(false);
                }
            }
            _trackedTweens.Clear();
        }

        // ==================================================================
        //  背景音乐（BGM）
        //  -------------------------------------------------------------------
        //  面板 → 音乐 的映射集中放在这一处，而不是散落在二十多个面板里。三个理由：
        //    ① 想换某个界面的音乐只改一行，不用翻遍业务代码；
        //    ② 配错、漏配一目了然（表就在眼前，而不是藏在各面板的覆写里）；
        //    ③ "配乐"属于表现层调度，不该让每个业务面板都关心。
        //
        //  生效时机有两条：打开时、以及"被上层弹窗遮挡后重新可见"时。
        //  后者是必须的 —— 打开灵市会把主界面压成 Paused，关掉灵市时若不重放一次，
        //  主界面的音乐就再也回不来了（战斗 → 结算 → 回主界面 同理）。
        // ==================================================================

        /// <summary>
        /// 本面板要播放的背景音乐 id（对应 <c>Resources/Audio/BGM/&lt;id&gt;</c> 的文件名）。
        /// 返回 null 或空字符串 = 不改变当前音乐 —— 未在表中列出的面板都属于这一类
        /// （例如结算界面应当延续战斗音乐，而不是切歌）。
        ///
        /// 个别面板可覆写本属性自行决定（战斗面板按对手是否为守关首领分流，就是这么做的）。
        /// </summary>
        public virtual string BgmId
        {
            get
            {
                if (string.IsNullOrEmpty(Key)) return null;
                string id;
                return PanelBgmTable.TryGetValue(Key, out id) ? id : null;
            }
        }

        /// <summary>
        /// 面板 → 背景音乐 映射表。
        ///
        /// ⚠ id 必须与 <c>Assets/Resources/Audio/BGM/</c> 下的文件名完全一致。
        /// 写错**不会报错**（AudioSystem 取不到资源会静默跳过），但那个界面就是没声音 ——
        /// 所以改完表建议调一次 <see cref="ValidatePanelBgm"/> 核对。
        /// </summary>
        private static readonly Dictionary<string, string> PanelBgmTable =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ---- 标题 / 主界面 / 旅途 ----
                { "Panel_Start",     "bgm_km_ripples" },         // 古筝冥想 · 标题
                { "Panel_Home",      "bgm_km_guzheng_city" },    // 古筝明亮 · 主界面
                { "Panel_Campaign",  "bgm_km_shenyang" },        // 二胡民俗 · 幕选择
                { "Panel_Tale",      "bgm_km_shenyang" },        // 节点地图 / 残卷
                { "Panel_ActIntro",  "bgm_km_snow_queen" },      // 神秘 · 幕间开场
                // ---- 战斗 ----
                { "Panel_Battle",    "bgm_km_clash_defiant" },   // 普通战（首领由 BattlePanel 覆写分流）
                { "Panel_Pvp",       "bgm_km_mountain_emperor" },// 太鼓 · 好友对战
                { "Panel_Trial",     "bgm_km_frost_waltz" },     // 冰霜 · 试炼
                // ---- 功能面板 ----
                { "Panel_Market",    "bgm_cc0_chinese_chip" },   // 轻快中国风 · 灵市
                { "Panel_Formation", "bgm_ccby_calm" },          // 安静 · 编队
                { "Panel_Codex",     "bgm_km_zen_moment" },      // 禅意 · 图鉴
                { "Panel_Archive",   "bgm_km_zen_moment" },      // 禅意 · 档案
                { "Panel_History",   "bgm_km_zen_moment" },      // 禅意 · 战记
                { "Panel_Forge",     "bgm_km_wizardtorium" },    // 诡异 · 锻造 / 觉醒
                { "Panel_Recruit",   "bgm_cc0_oriental1" },      // 东方 · 招募
                { "Panel_Relic",     "bgm_ccby_arukas" },        // 神秘 · 遗物
                { "Panel_Realm",     "bgm_km_shenyang" },        // 神秘 · 秘境
                { "Panel_Meta",      "bgm_km_temple_manes" },    // 暗黑 · 局外养成
                { "Panel_Omen",      "bgm_km_wizardtorium" },    // 诡异 · 天时 / 征兆
                { "Panel_Settings",  "bgm_km_zen_moment" },      // 禅意 · 设置
                { "Panel_Dialog",    "bgm_km_senbazuru" },       // 温暖 · 对话
                // 未列出的面板（面板_Result / Panel_Save 等）保持当前音乐不切换。
            };

        /// <summary>播放本面板的背景音乐。取不到资源会静默跳过（这是 AudioSystem 的既定行为）。</summary>
        private void ApplyPanelBgm()
        {
            var id = BgmId;
            if (!string.IsNullOrEmpty(id))
            {
                AudioSystem.PlayMusic(id);
            }
        }

        /// <summary>
        /// 自检：核对映射表里的每个 id 是否真的存在于 <c>Resources/Audio/BGM/</c>。
        /// 返回问题列表（空 = 全部正常）。排查"某个界面没声音"时先调它 ——
        /// 能立刻区分"id 写错"和"音乐没导入"这两种完全不同的原因。
        /// </summary>
        public static List<string> ValidatePanelBgm()
        {
            var problems = new List<string>();
            foreach (var kv in PanelBgmTable)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (Resources.Load<AudioClip>("Audio/BGM/" + kv.Value) == null)
                {
                    problems.Add($"{kv.Key} → 找不到音乐 \"{kv.Value}\"" +
                                 $"（期望文件：Assets/Resources/Audio/BGM/{kv.Value}.mp3 或 .ogg）");
                }
            }
            return problems;
        }

        // ==================================================================
        //  界面音效（SFX）
        //  -------------------------------------------------------------------
        //  两件事都放在框架层解决，业务面板一行都不用写：
        //    ① 全屏面板的开 / 合音 —— 弹层（Popup）**不播**，否则"点开一个确认框"
        //       也来一声卷轴音，听感很吵；弹层的反馈交给按钮点击音。
        //    ② 面板内所有按钮的点击音 —— 打开面板时自动挂。
        //       让 20 多个面板各自记得去挂，结果必然是"总有某个界面点着没声"。
        //       战斗面板例外（AutoButtonSfx = false）：它内部有专门的技能 / 指令音效，
        //       再叠一层通用点击音只会糊在一起。
        // ==================================================================

        /// <summary>是否自动给本面板内的按钮挂点击音效。战斗面板覆写为 false。</summary>
        protected virtual bool AutoButtonSfx => true;

        private readonly HashSet<Button> _sfxHookedButtons = new HashSet<Button>();

        /// <summary>给本面板下所有按钮挂点击音。幂等 —— 同一个按钮只会挂一次。</summary>
        private void HookButtonSfx()
        {
            if (!AutoButtonSfx) return;

            var btns = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null || _sfxHookedButtons.Contains(b)) continue;
                _sfxHookedButtons.Add(b);
                b.onClick.AddListener(OnAnyButtonClicked);
            }
        }

        private void OnAnyButtonClicked()
        {
            AudioSystem.PlaySfx("sfx_ui_btn_click");
        }

        // ---- 业务面板通用的节流播放 ----
        private string _lastSfxId;
        private float _lastSfxTime = -99f;

        /// <summary>
        /// 播一条音效，带「同 id 最小间隔」节流。
        ///
        /// 为什么需要：一次群体技能会连发多条伤害事件、地图上连续点几个节点 ——
        /// 不做节流就会叠成一团噪音（音效池只有 6 条，还会互相打断）。
        ///
        /// 默认间隔 0.05s；"每回合只该响一次"这类提示音可以传更大的 <paramref name="gap"/>。
        /// </summary>
        protected void SfxThrottled(string id, float vol = 1f, float gap = 0.05f)
        {
            if (string.IsNullOrEmpty(id)) return;

            float now = Time.unscaledTime;
            if (id == _lastSfxId && now - _lastSfxTime < gap) return;

            _lastSfxId = id;
            _lastSfxTime = now;
            AudioSystem.PlaySfx(id, vol);
        }

        /// <summary>面板真正销毁时把挂上去的监听摘掉（Cached 面板关闭时不销毁，所以不在这里清）。</summary>
        private void UnhookButtonSfx()
        {
            foreach (var b in _sfxHookedButtons)
            {
                if (b != null) b.onClick.RemoveListener(OnAnyButtonClicked);
            }
            _sfxHookedButtons.Clear();
        }

        // ==================================================================
        //  业务重写点
        // ==================================================================

        /// <summary>
        /// 只执行一次：缓存组件引用、准备对象池。
        /// 【不要在这里订阅事件】—— 事件订阅请写在 <see cref="OnOpenAsync"/> 中，
        /// 框架会在关闭时统一解绑，并在下次打开时重新调用 OnOpenAsync。
        /// </summary>
        protected abstract void OnCreate();

        /// <summary>每次打开时调用。事件订阅、数据绑定写在这里。</summary>
        protected virtual UniTask OnOpenAsync(object payload) => UniTask.CompletedTask;

        /// <summary>
        /// 面板已打开时再次被 OpenAsync 调用（如连续查看图鉴下一个条目）。
        /// 若未覆写，框架会退化为"什么都不做"，面板保持原样。
        /// </summary>
        protected virtual void OnRefresh(object payload) { }

        /// <summary>打开动画/过渡结束后调用。</summary>
        protected virtual void OnOpened() { }

        /// <summary>
        /// 被上层全屏面板遮挡时调用。
        /// 在这里停止动画、停掉计时器、暂停滚动 —— 别忘了，
        /// 少了这个回调，会出现"背包被详情页盖住但冷却倒计时还在跑"。
        /// </summary>
        protected virtual void OnPause() { }

        /// <summary>上层面板关闭、重新可见时调用。</summary>
        protected virtual void OnResume() { }

        /// <summary>每次关闭时调用。框架随后会自动解绑订阅、取消 PanelToken、禁用 GameObject。</summary>
        protected virtual void OnClose() { }

        /// <summary>被真正销毁前调用（Transient 策略关闭后、场景卸载前）。</summary>
        protected virtual void OnDestroyed() { }

        // ==================================================================
        //  业务辅助 API
        // ==================================================================

        /// <summary>关闭自身（等价于 <c>UI.Close(this)</c>）。</summary>
        public void CloseSelf()
        {
            UI?.Close(this);
        }

        /// <summary>按类型关闭自身（推荐用这个）。</summary>
        protected void CloseSelf<T>() where T : UIPanelBase
        {
            UI?.Close<T>();
        }

        /// <summary>打开另一个面板。</summary>
        protected UniTask<T> OpenPanelAsync<T>(object payload = null) where T : UIPanelBase
        {
            if (UI == null)
            {
                Debug.LogError($"[UI] {GetType().Name} 的 UISystem 未注入，无法打开 {typeof(T).Name}");
                return UniTask.FromResult<T>(null);
            }
            return UI.OpenAsync<T>(payload);
        }

        /// <summary>
        /// 登记一个动效，面板关闭时由框架统一 Kill。
        ///
        /// 用法：
        /// <code>
        /// TrackTween(CanvasGroup.DOFade(1f, 0.2f));
        /// TrackTween(Rect.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack));
        /// </code>
        ///
        /// 除登记之外，这里顺手做了 SetLink(KillOnDestroy)：万一业务漏了处理，
        /// Cached 面板被淘汰销毁时 DOTween 也会自己把动效收掉，不会去戳已销毁对象。
        /// </summary>
        protected T TrackTween<T>(T tween) where T : Tween
        {
            if (tween == null) return null;

            // 同一实例只登记一次：面板反复打开时业务可能重复开头同一个动效
            // （例如复用缓存的 Sequence），重复登记会让列表无谓增长
            if (!_trackedTweens.Contains(tween))
            {
                tween.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
                _trackedTweens.Add(tween);
            }
            return tween;
        }

        /// <summary>
        /// 注册一个可自动释放的订阅。
        /// 用法：<c>Register(disposable);</c> 或配合 <see cref="RegisterEvent{T}"/>。
        /// </summary>
        protected void Register(IDisposable disposable)
        {
            if (disposable == null) return;
            _disposables.Add(disposable);
        }

        /// <summary>
        /// 注册一个基于事件的订阅（把注册与注销函数配对传入）。
        ///
        /// 示例（接入 QFramework 的 TypeEventSystem）：
        /// <code>
        /// RegisterEvent&lt;PlayerGoldChangedEvent&gt;(
        ///     h => this.RegisterEvent(h),
        ///     h => this.UnRegisterEvent(h),
        ///     OnGoldChanged);
        /// </code>
        /// 这样写虽然比直接调用 RegisterEvent 多两个参数，但换来的是
        /// "绝不可能忘记解绑" —— 值得。
        /// </summary>
        protected void RegisterEvent<TDelegate>(
            Func<TDelegate, IDisposable> subscribe,
            TDelegate handler) where TDelegate : Delegate
        {
            if (subscribe == null || handler == null) return;
            Register(subscribe(handler));
        }

        /// <summary>启动一个与面板生命周期绑定的异步任务。面板关闭时自动取消。</summary>
        protected void RunTask(Func<CancellationToken, UniTask> task)
        {
            if (task == null) return;
            RunTaskInternal(task).Forget();
        }

        private async UniTaskVoid RunTaskInternal(Func<CancellationToken, UniTask> task)
        {
            try
            {
                await task(PanelToken);
            }
            catch (OperationCanceledException)
            {
                // 面板关闭导致的正常取消，不视为错误
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 面板 {GetType().Name} 异步任务异常：{ex}");
            }
        }

        // ==================================================================
        //  编辑器辅助
        // ==================================================================

        /// <summary>从类型名推导默认资源 key：BackpackPanel → Panel_Backpack</summary>
        internal static string DeriveDefaultKey(Type type)
        {
            string name = type.Name;
            if (name.EndsWith("Panel", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "Panel".Length);
            }
            return "Panel_" + name;
        }
    }

    /// <summary>面板元数据（运行时值对象）。由 UISystem 从特性解析得到。</summary>
    public struct UIPanelMeta
    {
        public string Key;
        public UILayer Layer;
        public UICachePolicy CachePolicy;
        public bool CloseOnMaskClick;
        public bool FullScreen;
    }
}
