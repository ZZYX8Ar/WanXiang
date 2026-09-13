// ============================================================================
//  万相 · 输入启动引导
//  ---------------------------------------------------------------------------
//  挂在启动场景的空 GameObject 上（建议命名 [InputBootstrap]）。
//
//  它做两件事：
//    ① 创建 InputService，按初始上下文把 Map 摆好
//    ② 确保场景里有一个 EventSystem，且用的是 InputSystemUIInputModule
//
//  为什么 ② 放在输入层而不是 UI 层：
//    因为「用哪个事件模块」这件事，取决于输入后端的选择，而不是 UI 的需要。
//    uGUI 自带的是 StandaloneInputModule（走旧 Input），而本工程用的是新
//    Input System —— 两者混用会出现「UI 点不动」或「点击触发两次」。
//    新后端下正确的模块是 InputSystemUIInputModule，且它要拿到我们的
//    UI Action Map 才能工作。这只有输入层手里有。
//
//  ⚠ 关于 Both 后端：
//    本工程 activeInputHandler = Both（见 ARCHITECTURE.md §6.1）。
//    所以旧模块其实**还能用** —— 但我们仍然换成新模块，因为：
//      · 改键之后，UI 导航键也应该跟着变（旧模块读的是 Input Manager 的
//        轴配置，改键对它无效）
//      · 手柄支持上，新模块是唯一正路
//
//  执行顺序 -990 —— 排在 UIBootstrap(-1000) 之后、业务脚本之前。
//  排在 UI 之后是因为输入服务启动时会动 EventSystem，而 UIBootstrap
//  会建 Canvas 层级，两者互不依赖，只是让日志顺序好看一点。
//
//  本文件不依赖 QFramework，单独可用。接入 QFramework 见 Integration 目录。
// ============================================================================

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using WanXiang.Framework.Inputs;

namespace WanXiang.Framework.Boot
{
    [DefaultExecutionOrder(-990)]
    [DisallowMultipleComponent]
    public sealed class InputBootstrap : MonoBehaviour
    {
        [Header("输入资产")]
        [Tooltip("WanXiang.inputactions。用「万相/输入/生成输入资产」菜单生成后拖进来。")]
        [SerializeField] private InputActionAsset _asset;

        [Tooltip("资产留空时，尝试从 Resources 目录按这个路径加载（不带扩展名）。原型期用。")]
        [SerializeField] private string _resourcesFallbackPath = "WanXiang_InputActions";

        [Header("启动状态")]
        [Tooltip("进入游戏时的初始上下文。建议留 None，由启动流程显式切换到 MainMenu。")]
        [SerializeField] private InputContext _initialContext = InputContext.None;

        [Header("生命周期")]
        [Tooltip("是否跨场景保留。输入服务应当常驻，否则每次切场景都要重建 Map。")]
        [SerializeField] private bool _dontDestroyOnLoad = true;

        [Header("EventSystem")]
        [Tooltip("勾选则自动创建/修正 EventSystem，装上 InputSystemUIInputModule。\n" +
                 "若你的场景里已有精心配置过的 EventSystem，可取消勾选自行维护。")]
        [SerializeField] private bool _setupEventSystem = true;

        /// <summary>全局输入服务入口。业务代码通过它切上下文、改键。</summary>
        public static IInputService Input { get; private set; }

        private void Awake()
        {
            if (Input != null)
            {
                Debug.LogWarning(
                    "[输入] 检测到第二个 InputBootstrap，已销毁重复实例。请确认场景里只放了一个。");
                Destroy(gameObject);
                return;
            }

            var asset = ResolveAsset();
            if (asset == null)
            {
                Debug.LogError(
                    "[输入] 找不到输入资产。请先用菜单「万相/输入/生成输入资产」生成，\n" +
                    "然后拖到 InputBootstrap 的 Asset 字段上；\n" +
                    "或把生成好的 .inputactions 放到 Resources/" + _resourcesFallbackPath + "。\n" +
                    "输入系统未启动，所有输入相关的功能都不会响应。");
                return;
            }

            if (_dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            var service = new InputService(asset, _initialContext);
            service.Initialize();
            Input = service;

            if (_setupEventSystem)
            {
                SetupEventSystem(asset);
            }

            Debug.Log($"[输入] InputService 已启动。资产「{asset.name}」，共 {asset.actionMaps.Count} 张 Map，" +
                      $"初始上下文 {_initialContext}（Debug Map " +
                      (service.DebugMapEnabled ? "已启用" : "已禁用") + "）。");
        }

        private void OnDestroy()
        {
            if (Input == null) return;
            Input.Dispose();
            Input = null;
        }

        private InputActionAsset ResolveAsset()
        {
            if (_asset != null) return _asset;
            if (string.IsNullOrEmpty(_resourcesFallbackPath)) return null;

            // ⚠ 从 Resources 加载会让资产被打进包体、且无法热更。
            //   这是原型期的临时手段，P4 接入 YooAsset 后应当改成
            //   「由资源系统加载后注入」，别把这条路带到正式版。
            return Resources.Load<InputActionAsset>(_resourcesFallbackPath);
        }

        /// <summary>
        /// 找到或创建 EventSystem，并确保它用的是 InputSystemUIInputModule。
        /// </summary>
        private void SetupEventSystem(InputActionAsset asset)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystem = FindObjectOfType<EventSystem>();
            }

            if (eventSystem == null)
            {
                var go = new GameObject("[EventSystem]");
                if (_dontDestroyOnLoad) DontDestroyOnLoad(go);
                eventSystem = go.AddComponent<EventSystem>();
            }

            var go2 = eventSystem.gameObject;

            // uGUI 默认给的是 StandaloneInputModule（走旧输入）。新后端下它
            // 虽然因 Both 模式还能苟活，但改键对它无效、手柄支持也不完整，换掉。
            var legacy = go2.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                // 先停用再销毁：Destroy 是延迟到帧末执行的，中间这段时间
                // 两个模块会同时存在，Unity 会在控制台吐警告。
                legacy.enabled = false;
                Destroy(legacy);
            }

            var module = go2.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                module = go2.AddComponent<InputSystemUIInputModule>();
            }

            // 把整个资产交给模块，它会按 action 名字自动认领
            // Point / Click / Navigate / Submit / Cancel 等 ——
            // 这正是生成器里 UI Map 必须用标准名字的原因。
            if (module.actionsAsset != asset)
            {
                module.actionsAsset = asset;
            }
        }
    }
}
