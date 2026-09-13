// ============================================================================
//  WanXiang · UI 框架 · UI 系统（核心）
//  ---------------------------------------------------------------------------
//  这是整个 UI 框架的中枢，负责五件事：
//    1. 层级管理  —— 每个 UILayer 一个独立 Canvas（合批隔离，见 UIDefines 注释）
//    2. 生命周期  —— 打开 / 暂停 / 恢复 / 关闭，业务只重写回调
//    3. 加载去重  —— 同一面板并发打开请求合并成一个加载任务（防连点）
//    4. 遮罩联动  —— 谁在最上面，谁决定该层的遮罩透明度与点击行为
//    5. 缓存淘汰  —— Resident 常驻 / Cached 走 LRU / Transient 用完即弃
//
//  【本文件最关键的一处设计：RefreshStackStates】
//  面板的"暂停/恢复"不是打开时手动调一次、关闭时手动调一次，
//  而是每次栈变化后，把整个栈从上到下重算一遍"期望状态"。
//
//  为什么这么做：手工在打开/关闭两条路径上分别维护状态，一旦出现
//  "关闭的不是栈顶"（比如从背包直接跳到设置，中间夹着详情页），
//  两条路径的逻辑就会打架，最终表现为"关掉弹窗后下层面板点不动了"。
//  重算则天然正确 —— 无论栈怎么变，状态永远是当前栈形状的唯一函数。
//
//  依赖：UniTask（Cysharp）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Framework.UI
{
    /// <summary>
    /// 加载指示器（转圈 / 黑屏遮罩）。
    ///
    /// 做成接口而不是直接引用某个 Loading 面板，是因为加载指示常驻在最底层，
    /// 不该走面板系统自己 —— 否则"加载 Loading 面板需要先加载 Loading 面板"。
    /// </summary>
    public interface IUILoadingIndicator
    {
        void Show();
        void Hide();
    }

    /// <summary>UI 系统。业务持有 <see cref="IUISystem"/> 接口即可，不依赖本实现类。</summary>
    public sealed class UISystem : IUISystem, IDisposable
    {
        // ==================================================================
        //  可调参数
        // ==================================================================

        /// <summary>参考分辨率。1920×1080 是 PC / Steam 的安全默认。</summary>
        public Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>
        /// 宽高适配权重。0 = 以宽度为准（上下可能裁切 / 留黑），
        /// 1 = 以高度为准，0.5 = 折中。PC 端多分辨率差异大，0.5 最稳。
        /// </summary>
        public float MatchWidthOrHeight = 0.5f;

        /// <summary>
        /// Cached 策略面板的缓存上限（个数）。
        /// 8 是个经验值：够覆盖"主界面 → 背包 → 图鉴 → 详情"这类常用往返路径，
        /// 又不至于让几十个用不到的面板堆在内存里。
        /// </summary>
        public int CacheCapacity = 8;

        /// <summary>
        /// 加载多久才显示加载指示（毫秒）。
        /// 100ms 是感知阈值：短于这个时间的等待，显示转圈反而让玩家觉得"卡了一下"。
        /// </summary>
        public int LoadingIndicatorDelayMs = 100;

        /// <summary>全屏面板的遮罩透明度。0 = 完全透明，仅用于拦截射线。</summary>
        public float FullScreenMaskAlpha = 0f;

        /// <summary>非全屏（半屏弹窗）的遮罩透明度。</summary>
        public float PartialMaskAlpha = 0.62f;

        /// <summary>遮罩底色。</summary>
        public Color MaskColor = Color.black;

        // ==================================================================
        //  对外只读状态
        // ==================================================================

        /// <summary>UI 根节点。整棵 UI 树都挂在它下面，便于统一 DontDestroyOnLoad 与调试。</summary>
        public GameObject Root { get; private set; }

        /// <summary>根 Canvas。</summary>
        public Canvas RootCanvas { get; private set; }

        /// <summary>便于快速取用的当前实例。推荐业务仍通过架构注入，此处只作兜底。</summary>
        public static UISystem Current { get; private set; }

        // ==================================================================
        //  内部字段
        // ==================================================================

        private sealed class LayerRuntime
        {
            public UILayer Layer;
            public GameObject Root;
            public Canvas Canvas;
            public RectTransform Content;
            public UIPanelMask Mask;
        }

        /// <summary>加载指示的显示标志。用类包裹是为了让后台任务能回写状态给调用方。</summary>
        private sealed class IndicatorScope
        {
            public bool Shown;
        }

        private readonly IUIPanelLoader _loader;
        private readonly IUILoadingIndicator _loadingIndicator;
        private readonly CancellationTokenSource _appCts = new CancellationTokenSource();

        private readonly Dictionary<UILayer, LayerRuntime> _layers =
            new Dictionary<UILayer, LayerRuntime>(8);

        private readonly Dictionary<Type, UIPanelBase> _instancesByType =
            new Dictionary<Type, UIPanelBase>(32);

        private readonly List<UIPanelBase> _allInstances = new List<UIPanelBase>(32);

        /// <summary>已打开面板，按打开顺序升序。列表尾 = 最上层。这是"上下关系"的唯一真相来源。</summary>
        private readonly List<UIPanelBase> _openPanels = new List<UIPanelBase>(16);

        /// <summary>正在加载中的面板 key → 完成源。并发请求复用同一个任务，避免重复实例化。</summary>
        private readonly Dictionary<string, UniTaskCompletionSource<UIPanelBase>> _pendingOpens =
            new Dictionary<string, UniTaskCompletionSource<UIPanelBase>>(StringComparer.Ordinal);

        /// <summary>面板 → 最后关闭次序。用于 LRU 淘汰排序。</summary>
        private readonly Dictionary<UIPanelBase, int> _lastCloseTick =
            new Dictionary<UIPanelBase, int>();

        private int _closeTick;
        private int _sequenceCounter;
        private bool _disposed;

        // ==================================================================
        //  构造
        // ==================================================================

        public UISystem(
            IUIPanelLoader loader,
            Transform parent = null,
            IUILoadingIndicator loadingIndicator = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _loadingIndicator = loadingIndicator;

            CreateRoot(parent);
            Current = this;
        }

        // ==================================================================
        //  IUISystem 实现
        // ==================================================================

        public async UniTask<T> OpenAsync<T>(object payload = null) where T : UIPanelBase
        {
            var panel = await OpenInternalAsync(typeof(T), payload);
            return panel as T;
        }

        public void Close<T>() where T : UIPanelBase
        {
            Close(Get<T>());
        }

        public void Close(UIPanelBase panel)
        {
            if (_disposed || panel == null) return;

            // 不在已打开列表里 = 已经关了 / 从未打开，静默忽略。
            // 这里不报错是刻意的：双击按钮、遮罩点击与按钮点击同时到达，都会走到这条路。
            if (!_openPanels.Remove(panel)) return;

            panel.InternalClose();
            _lastCloseTick[panel] = ++_closeTick;

            RefreshStackStates();
            UpdateLayerMask(panel.Layer);
            TrimCache();
        }

        public void CloseTop()
        {
            if (_disposed) return;

            for (int i = _openPanels.Count - 1; i >= 0; i--)
            {
                var p = _openPanels[i];
                if (!IsBackTarget(p)) continue;
                Close(p);
                return;
            }
        }

        public bool HandleBack()
        {
            if (_disposed) return false;

            for (int i = _openPanels.Count - 1; i >= 0; i--)
            {
                var p = _openPanels[i];
                if (!IsBackTarget(p)) continue;

                // 不可关闭的面板（强制引导、结算）要"吃掉"这次返回输入，
                // 返回 false 会让调用方弹出"再按一次退出游戏"，玩家会觉得游戏在赶他走。
                if (!p.AllowBackClose) return true;

                Close(p);
                return true;
            }

            return false;
        }

        public void CloseAll(UILayer? onlyLayer = null)
        {
            if (_disposed) return;

            // 先快照再关，避免遍历过程中 _openPanels 被修改。
            var snapshot = new List<UIPanelBase>(_openPanels);
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                var p = snapshot[i];
                if (onlyLayer.HasValue && p.Layer != onlyLayer.Value) continue;
                Close(p);
            }
        }

        public T Get<T>() where T : UIPanelBase
        {
            return _instancesByType.TryGetValue(typeof(T), out var p) ? p as T : null;
        }

        public bool IsOpen<T>() where T : UIPanelBase
        {
            var p = Get<T>();
            if (p == null) return false;
            return p.State == UIPanelState.Opened
                || p.State == UIPanelState.Paused
                || p.State == UIPanelState.Opening;
        }

        /// <summary>
        /// 返回栈深度：当前有几个面板可以被「返回键 / 手柄 B 键」逐层关掉。
        ///
        /// ⚠ 这不是「已打开面板的数量」。常驻面板（Resident，如主界面、HUD）与附属
        /// 表现层（Background / Toast / Loading / Debug）都不计入，判据见
        /// <see cref="IsBackTarget"/> —— 它们本来就不该被返回键关掉。
        /// 例：Main 层 + Resident 的主界面打开着，本值仍为 0，这是正确行为。
        ///
        /// 典型用途：
        ///   - 判断"现在还有没有界面可以退"，决定返回键是否交还给上层逻辑
        ///   - 调试时确认弹窗是否被正确压栈
        /// </summary>
        public int StackDepth
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _openPanels.Count; i++)
                {
                    var p = _openPanels[i];
                    if (!IsBackTarget(p)) continue;
                    n++;
                }
                return n;
            }
        }

        public async UniTask PreloadAsync<T>() where T : UIPanelBase
        {
            if (_disposed) return;

            var meta = UIPanelRegistry.Get<T>();
            try
            {
                await _loader.LoadPanelPrefabAsync(meta.Key, _appCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 应用退出，正常路径
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 预加载面板失败 key = {meta.Key}\n{ex}");
            }
        }

        // ==================================================================
        //  打开流程
        // ==================================================================

        private async UniTask<UIPanelBase> OpenInternalAsync(Type type, object payload)
        {
            if (_disposed) return null;

            var meta = UIPanelRegistry.Get(type);

            // ---- 1. 实例已存在 ----
            if (_instancesByType.TryGetValue(type, out var existing) && existing != null)
            {
                switch (existing.State)
                {
                    case UIPanelState.Opening:
                    case UIPanelState.Opened:
                        // 已打开：只刷新内容，不重放 OnOpenAsync。
                        // 这样"图鉴里连续查看下一只异兽"不会闪一下面板。
                        existing.InternalRefresh(payload);
                        return existing;

                    case UIPanelState.Paused:
                        // 已打开但被上层盖住：关掉它上面的一切，把它重新顶到最上层。
                        CloseAbove(existing);
                        existing.InternalRefresh(payload);
                        return existing;

                    case UIPanelState.Created:
                    case UIPanelState.Closed:
                        // 落到下面走正常打开流程（会重新入栈、重算暂停关系）
                        break;

                    default:
                        // Loading / Closing 中途再次请求，忽略这一次，等前一个流程结束
                        return existing;
                }
            }

            // ---- 2. 加载中：复用同一个任务（防连点，也是防重复实例化）----
            if (_pendingOpens.TryGetValue(meta.Key, out var pending))
            {
                return await pending.Task;
            }

            // ---- 3. 发起加载 ----
            var tcs = new UniTaskCompletionSource<UIPanelBase>();
            _pendingOpens[meta.Key] = tcs;
            try
            {
                var panel = await CreateAndOpenAsync(type, meta, payload);
                tcs.TrySetResult(panel);
                return panel;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }
            finally
            {
                _pendingOpens.Remove(meta.Key);
            }
        }

        private async UniTask<UIPanelBase> CreateAndOpenAsync(
            Type type, UIPanelMeta meta, object payload)
        {
            var layer = GetLayer(meta.Layer);
            if (layer == null)
            {
                Debug.LogError(
                    $"[UI] 层级 {meta.Layer} 的 Canvas 未创建，无法打开 {type.Name}。" +
                    $"（该层可能因为非开发版被跳过，见 ShouldCreateLayer）");
                return null;
            }

            // 加载指示：延迟显示，短加载不闪
            var indicatorCts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
            var indicatorScope = new IndicatorScope();
            ShowIndicatorDelayedAsync(indicatorCts.Token, indicatorScope).Forget();

            UIPanelBase panel;
            try
            {
                panel = await GetOrCreateInstanceAsync(type, meta, layer);
            }
            finally
            {
                indicatorCts.Cancel();
                indicatorCts.Dispose();
                if (indicatorScope.Shown)
                {
                    _loadingIndicator?.Hide();
                }
            }

            if (panel == null) return null;

            // 关闭时解绑过订阅的面板，重开前需要恢复可订阅状态
            panel.PrepareReopen();

            if (!_openPanels.Contains(panel))
            {
                _openPanels.Add(panel);
                _lastCloseTick.Remove(panel);
            }

            // 同层内后打开的在上面（兄弟顺序决定渲染顺序）
            panel.transform.SetAsLastSibling();

            // 先重算一次：让下层在被遮挡的瞬间就进入 OnPause，
            // 而不是等新面板的打开动画播完才开始停 —— 否则会看到下层还在动。
            _sequenceCounter++;
            RefreshStackStates();
            UpdateLayerMask(meta.Layer);

            await panel.InternalOpenAsync(payload, _sequenceCounter);

            // 再重算一次：新面板 State 已变为 Opened，参与后续关系计算
            RefreshStackStates();
            UpdateLayerMask(meta.Layer);

            return panel;
        }

        /// <summary>取实例，没有就加载并创建。</summary>
        private async UniTask<UIPanelBase> GetOrCreateInstanceAsync(
            Type type, UIPanelMeta meta, LayerRuntime layer)
        {
            if (_instancesByType.TryGetValue(type, out var exist) && exist != null)
            {
                return exist;
            }

            GameObject prefab;
            try
            {
                prefab = await _loader.LoadPanelPrefabAsync(meta.Key, _appCts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UI] 加载面板资源失败 key = {meta.Key}\n{ex}");
                return null;
            }

            if (prefab == null)
            {
                Debug.LogError(
                    $"[UI] 面板资源不存在：\"{meta.Key}\"（面板类 {type.Name}）。\n" +
                    $"可能原因：① Prefab 未放进资源系统可加载的目录；" +
                    $"② 文件命名与推导规则不符（默认推导为 Panel_类名去 Panel 后缀）；" +
                    $"③ 需要在面板类上用 [UIPanel(\"你的Key\")] 显式指定。");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, layer.Content);
            go.name = meta.Key;

            var panel = go.GetComponent(type) as UIPanelBase;

            if (panel == null)
            {
                // 面板脚本必须挂在 Prefab 根节点上。
                // 挂在子节点时，框架的层级排序、全屏拉伸、射线管理都会作用在错误的对象上，
                // 表现出来是"面板能开但布局错乱/点不到"，极难排查 —— 所以这里直接拒绝。
                Debug.LogError(
                    $"[UI] Prefab \"{meta.Key}\" 的根节点上找不到组件 {type.Name}。" +
                    $"请把面板脚本挂在 Prefab 的根节点上（不要挂在子物体）。");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            if (meta.FullScreen)
            {
                // 只对全屏面板强制铺满。
                // 半屏弹窗保留 Prefab 自己设计的尺寸与居中位置，
                // 否则"居中确认框"会被拉成满屏 —— 框架好心办坏事的典型案例。
                StretchRect((RectTransform)go.transform);
            }

            panel.InternalInit(meta, this);

            _instancesByType[type] = panel;
            _allInstances.Add(panel);
            return panel;
        }

        /// <summary>关闭所有位于指定面板之上的面板（让指定面板重新可见）。</summary>
        private void CloseAbove(UIPanelBase panel)
        {
            for (int i = _openPanels.Count - 1; i >= 0; i--)
            {
                var p = _openPanels[i];
                if (p == panel) break;
                Close(p);
            }
        }

        private async UniTaskVoid ShowIndicatorDelayedAsync(
            CancellationToken ct, IndicatorScope scope)
        {
            if (_loadingIndicator == null) return;

            try
            {
                // UnscaledDeltaTime：即使 timeScale = 0（暂停/加载）也要能计时
                await UniTask.Delay(
                    LoadingIndicatorDelayMs, DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, ct);

                scope.Shown = true;
                _loadingIndicator.Show();
            }
            catch (OperationCanceledException)
            {
                // 加载很快，指示器不显示 —— 这正是延迟的目的
            }
        }

        // ==================================================================
        //  栈状态重算（本文件的核心）
        // ==================================================================

        /// <summary>
        /// 按当前栈形状重算每个面板的期望状态。
        ///
        /// 规则只有一句：**若面板上方存在任意一个"会遮挡下层"的面板，它就该是 Paused。**
        /// 从栈顶往下扫一遍即可，因为"栈顶一定可见"是天然的起始条件。
        /// </summary>
        private void RefreshStackStates()
        {
            bool occludedBelow = false;

            for (int i = _openPanels.Count - 1; i >= 0; i--)
            {
                var p = _openPanels[i];

                if (occludedBelow)
                {
                    if (p.State == UIPanelState.Opened) p.InternalPause();
                }
                else
                {
                    if (p.State == UIPanelState.Paused) p.InternalResume();
                }

                // 这一句放在最后：它影响的是"下一个（更下面的）面板"，
                // 而不是当前这个面板自己的状态。
                if (p.ShouldPauseBelow) occludedBelow = true;
            }
        }

        // ==================================================================
        //  遮罩
        // ==================================================================

        private void UpdateLayerMask(UILayer layer)
        {
            // 常驻层不参与遮罩。这一句同时保证了"主界面永远不会被压暗"。
            if (!UILayerUtil.NeedsMask(layer)) return;

            var runtime = GetLayer(layer);
            if (runtime?.Mask == null) return;

            UIPanelBase top = null;
            for (int i = _openPanels.Count - 1; i >= 0; i--)
            {
                if (_openPanels[i].Layer == layer)
                {
                    top = _openPanels[i];
                    break;
                }
            }

            if (top == null)
            {
                // 该层空了：遮罩必须停止拦截射线，否则会挡住下层的正常操作。
                runtime.Mask.SetTargetAlpha(0f, false, null);
                return;
            }

            float alpha = top.FullScreen ? FullScreenMaskAlpha : PartialMaskAlpha;

            // FullScreen 时 alpha 常为 0，但 raycast 依然为 true —— 这是"隐形拦截"，
            // 用来阻止玩家点到被遮住的下层按钮。
            Action onClick = top.CloseOnMaskClick
                ? (Action)(() => OnMaskClicked(top))
                : null;

            runtime.Mask.SetTargetAlpha(alpha, true, onClick);
        }

        private void OnMaskClicked(UIPanelBase expectedTop)
        {
            // 遮罩淡出动画期间玩家又点了一下，此时面板可能已经关了。
            // 不校验状态会变成"点一次关了，点第二次去把更下层也关了"。
            if (expectedTop == null || expectedTop.State != UIPanelState.Opened) return;
            Close(expectedTop);
        }

        // ==================================================================
        //  缓存淘汰
        // ==================================================================

        private void TrimCache()
        {
            // 1. Transient：关闭即销毁
            for (int i = _allInstances.Count - 1; i >= 0; i--)
            {
                var p = _allInstances[i];
                if (p == null)
                {
                    _allInstances.RemoveAt(i);
                    continue;
                }

                if (p.State == UIPanelState.Closed &&
                    p.CachePolicy == UICachePolicy.Transient)
                {
                    DestroyInstance(p);
                }
            }

            // 2. Cached：超出容量时淘汰最久未使用的
            List<UIPanelBase> cached = null;
            for (int i = 0; i < _allInstances.Count; i++)
            {
                var p = _allInstances[i];
                if (p == null) continue;
                if (p.State != UIPanelState.Closed) continue;
                if (p.CachePolicy != UICachePolicy.Cached) continue;

                (cached ??= new List<UIPanelBase>()).Add(p);
            }

            if (cached == null || cached.Count <= CacheCapacity) return;

            cached.Sort((a, b) => GetCloseTick(a).CompareTo(GetCloseTick(b)));

            int removeCount = cached.Count - CacheCapacity;
            for (int i = 0; i < removeCount; i++)
            {
                DestroyInstance(cached[i]);
            }
        }

        private int GetCloseTick(UIPanelBase panel)
        {
            return _lastCloseTick.TryGetValue(panel, out var t) ? t : 0;
        }

        private void DestroyInstance(UIPanelBase panel)
        {
            if (panel == null) return;

            // 反查 key。面板实例数很少（几十个），这里没必要为 O(1) 维护双向映射。
            Type key = null;
            foreach (var kv in _instancesByType)
            {
                if (kv.Value == panel) { key = kv.Key; break; }
            }
            if (key != null) _instancesByType.Remove(key);

            _allInstances.Remove(panel);
            _openPanels.Remove(panel);
            _lastCloseTick.Remove(panel);

            string resourceKey = panel.Key;

            // 先 InternalDestroy（解绑订阅、取消令牌、触发 OnDestroyed），再销毁 GameObject
            panel.InternalDestroy();
            _loader?.ReleasePanelPrefab(resourceKey);

            if (panel.gameObject != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(panel.gameObject);
                else UnityEngine.Object.DestroyImmediate(panel.gameObject);
            }
        }

        // ==================================================================
        //  层级与 Canvas 构建
        // ==================================================================

        private void CreateRoot(Transform parent)
        {
            Root = new GameObject(
                "[UIRoot]",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            if (parent != null)
            {
                Root.transform.SetParent(parent, false);
            }
            else
            {
                UnityEngine.Object.DontDestroyOnLoad(Root);
            }

            RootCanvas = Root.GetComponent<Canvas>();
            RootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            RootCanvas.sortingOrder = 0;

            var scaler = Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                if (!ShouldCreateLayer(layer)) continue;
                CreateLayer(layer);
            }
        }

        private static bool ShouldCreateLayer(UILayer layer)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            // 调试层的面板不该出现在正式包里 —— 玩家按出调试面板是个真实的翻车点
            return layer != UILayer.Debug;
#endif
        }

        private void CreateLayer(UILayer layer)
        {
            var layerGo = new GameObject(
                "Layer_" + layer,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));

            layerGo.transform.SetParent(Root.transform, false);

            var rt = (RectTransform)layerGo.transform;
            StretchRect(rt);
            rt.localScale = Vector3.one;

            var canvas = layerGo.GetComponent<Canvas>();

            // overrideSorting = true 是必须的。
            // 不设它，嵌套 Canvas 会沿用父 Canvas 的排序，sortingOrder 写了也不生效 ——
            // 于是"Toast 层压在弹窗下面"这种 bug 就出现了，而且极难猜原因。
            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)layer;

            // 子 Canvas 必须有自己的 GraphicRaycaster。
            // Unity 的 GraphicRaycaster 只收集"属于自己这个 Canvas"的 Graphic，
            // 根 Canvas 的 raycaster 管不到子 Canvas 里的按钮 ——
            // 漏了它，该层所有 UI 都点不动，且没有任何报错。
            // （上面 new GameObject 时已经带上了 GraphicRaycaster）

            // 遮罩先创建 → 兄弟索引更小 → 渲染在下层。顺序即层叠关系，别调换。
            //
            // 只为"覆盖层"创建遮罩（Normal / Popup / Overlay）。
            // 常驻层（Main / Toast 等）绝不能有遮罩：那会把主界面压暗，
            // 还会拦住主界面自己的按钮 —— 因为遮罩是全屏的，比面板更早吃到射线判定的边界情况。
            UIPanelMask mask = null;
            if (UILayerUtil.NeedsMask(layer))
            {
                var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(Image));
                maskGo.transform.SetParent(layerGo.transform, false);
                StretchRect((RectTransform)maskGo.transform);

                var maskImage = maskGo.GetComponent<Image>();
                maskImage.color = new Color(MaskColor.r, MaskColor.g, MaskColor.b, 0f);
                maskImage.raycastTarget = false;
                mask = maskGo.AddComponent<UIPanelMask>();
            }

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(layerGo.transform, false);
            StretchRect((RectTransform)contentGo.transform);

            _layers[layer] = new LayerRuntime
            {
                Layer = layer,
                Root = layerGo,
                Canvas = canvas,
                Content = (RectTransform)contentGo.transform,
                Mask = mask,
            };
        }

        private static void StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition3D = Vector3.zero;
        }

        private LayerRuntime GetLayer(UILayer layer)
        {
            return _layers.TryGetValue(layer, out var r) ? r : null;
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>
        /// 该面板是否参与"返回键"与"栈深度"计算。
        /// 背景 / Toast / Loading / Debug 属于附属表现层，不是真正的界面层级 ——
        /// 让返回键去关一个飘字提示显然不对。
        /// 这里复用 NeedsMask 的层判定，保证"能返回的"与"有遮罩的"永远是同一批层。
        /// </summary>
        private static bool IsBackTarget(UIPanelBase p)
        {
            if (p.CachePolicy == UICachePolicy.Resident) return false;
            return UILayerUtil.NeedsMask(p.Layer);
        }

        /// <summary>获取某层的容器节点。业务一般不需要，调试或做特殊挂载时可用。</summary>
        public RectTransform GetLayerContent(UILayer layer)
        {
            return GetLayer(layer)?.Content;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _appCts.Cancel(); } catch { /* 忽略 */ }

            // 未完成的加载任务要显式取消，否则 await 方会永远等下去
            foreach (var kv in _pendingOpens)
            {
                kv.Value.TrySetCanceled();
            }
            _pendingOpens.Clear();

            for (int i = _allInstances.Count - 1; i >= 0; i--)
            {
                var p = _allInstances[i];
                if (p == null) continue;
                p.InternalDestroy();
            }

            _allInstances.Clear();
            _instancesByType.Clear();
            _openPanels.Clear();
            _lastCloseTick.Clear();
            _layers.Clear();

            if (Root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
                else UnityEngine.Object.DestroyImmediate(Root);
                Root = null;
            }

            if (Current == this) Current = null;

            _appCts.Dispose();
        }
    }

    /// <summary>
    /// 最简加载指示器：一个全屏纯色 Image，靠 CanvasGroup 淡入淡出。
    /// 原型期够用；正式项目通常换成带进度条与美术资源的 Loading 界面。
    /// 注意 Show / Hide 必须幂等 —— 框架可能在延迟窗口内多次调用。
    /// </summary>
    public sealed class SimpleLoadingIndicator : MonoBehaviour, IUILoadingIndicator
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private float _fadeSpeed = 8f;

        private float _target;

        private void Awake()
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group != null) _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        public void Show()
        {
            if (_group == null) return;
            gameObject.SetActive(true);
            _target = 1f;
        }

        public void Hide()
        {
            _target = 0f;
            if (_group != null) _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_group == null) return;
            float a = Mathf.MoveTowards(_group.alpha, _target, _fadeSpeed * Time.unscaledDeltaTime);
            _group.alpha = a;
        }
    }
}
