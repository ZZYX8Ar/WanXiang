// ============================================================================
//  万相 · 输入资产生成工具（仅编辑器）
//  ---------------------------------------------------------------------------
//  为什么用代码生成，而不是手写 .inputactions 的 JSON：
//    .inputactions 是 JSON 文本资产，但结构不简单 —— map / action / binding
//    三层嵌套，每个 Action 与 Binding 都带一个 GUID，且 binding 要靠
//    "action" 字段指回所属 action 的名字。手写极易出结构错误，而 Unity 导入
//    失败时的报错信息非常难定位（往往只说"JSON 格式错误"，不说是哪一层）。
//    走 InputActionSetupExtensions 的官方 API 构建、再 ToJson() 导出，
//    结构由 API 保证，写不歪。
//
//  ⚠ 本工具只在「第一次」是安全的 —— 请认真读这一段：
//    重复生成会重建所有 Action / Binding 的 GUID；而玩家的改键配置
//    （InputActionAsset.SaveBindingOverridesAsJson 存下来的那份）
//    是**按 GUID 匹配**的。GUID 一变，全部改键失效。
//    所以：初次生成用本工具；之后要改键位，请直接用 Unity 的
//    Input Actions 编辑器打开 Assets/ArtRes/Input/WanXiang.inputactions 改。
//    工具里对已存在的文件做了覆盖确认，正是为了拦这一下。
//
//  增删 Action / Binding 的正确姿势：
//    第一次生成后，就把 .inputactions 当作唯一事实来源（single source of
//    truth）在编辑器里维护。本工具保留下来是给「新开一个工程」或
//    「资产被误删需要重建」用的。
// ============================================================================

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WanXiang.EditorTools.InputTool
{
    /// <summary>
    /// 生成 <c>Assets/ArtRes/Input/WanXiang.inputactions</c>。
    ///
    /// 四个 Action Map 的职责见 ARCHITECTURE.md §6.2，简表：
    /// <list type="bullet">
    /// <item><c>UI</c> —— 交给 InputSystemUIInputModule 用，含鼠标/键盘/手柄导航</item>
    /// <item><c>Gameplay</c> —— 战斗中的玩家干预：释放绝技、天时覆盖、战斗加速</item>
    /// <item><c>Global</c> —— 与场景无关的全局键：返回、菜单、截图</item>
    /// <item><c>Debug</c> —— 仅开发版：GM 指令。打正式包时由 InputService 整体禁用</item>
    /// </list>
    /// </summary>
    public static class InputAssetGenerator
    {
        private const string OutputDir = "Assets/ArtRes/Input";
        private const string OutputPath = OutputDir + "/WanXiang.inputactions";

        // binding 上的 groups 名，用于按设备过滤。与 controlScheme 配合使用。
        private const string Kbm = "Keyboard&Mouse";
        private const string Pad = "Gamepad";

        [MenuItem("万相/输入/生成输入资产", priority = 100)]
        public static void Generate()
        {
            if (File.Exists(OutputPath) && !ConfirmOverwrite()) return;

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "WanXiang";

            BuildUi(asset);
            BuildGameplay(asset);
            BuildGlobal(asset);
            BuildDebug(asset);

            Directory.CreateDirectory(OutputDir);
            File.WriteAllText(OutputPath, asset.ToJson(), new UTF8Encoding(false));
            UnityEngine.Object.DestroyImmediate(asset);

            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);

            var loaded = AssetDatabase.LoadAssetAtPath<InputActionAsset>(OutputPath);
            if (loaded == null)
            {
                Debug.LogError("[万相·输入] 生成后无法加载 " + OutputPath +
                               "，请看 Unity 控制台的导入报错（通常是资产路径或 GUID 冲突）。");
                return;
            }

            Selection.activeObject = loaded;
            EditorGUIUtility.PingObject(loaded);

            var summary = string.Join("、", loaded.actionMaps
                .Select(m => m.name + "(" + m.actions.Count + " action)").ToArray());

            Debug.Log("[万相·输入] 已生成 " + OutputPath + "\n" +
                      "  Action Map：" + summary + "\n" +
                      "  ⚠ 之后改键位请直接用 Input Actions 编辑器改，不要再跑本工具（会重置 GUID，玩家改键失效）。");
        }

        private static bool ConfirmOverwrite()
        {
            return EditorUtility.DisplayDialog(
                "输入资产已存在",
                OutputPath + "\n\n" +
                "重新生成会重建所有 Action / Binding 的 GUID。\n" +
                "玩家已保存的改键配置（Binding Overrides）是按 GUID 匹配的，\n" +
                "因此会全部失效。\n\n" +
                "如果只是想调整键位，请直接用 Unity 的 Input Actions 编辑器打开它。\n\n" +
                "确定要覆盖重建吗？",
                "覆盖重建", "取消");
        }

        // --------------------------------------------------------------------
        //  UI：InputSystemUIInputModule 认的是一组固定名字的 Action。
        //  名字必须完全一致（Point / Click / Navigate / Submit / Cancel …），
        //  否则在 EventSystem 上挂模块后，对应字段会留空、那项交互就静默失效。
        //  这里只建非 VR 的部分；将来做 VR 再加 TrackedDevicePosition / Orientation。
        // --------------------------------------------------------------------
        private static void BuildUi(InputActionAsset asset)
        {
            var map = asset.AddActionMap("UI");

            // 指针位置：鼠标 / 触控笔
            var point = Add(map, "Point", InputActionType.PassThrough, "Vector2");
            Bind(point, "<Mouse>/position", Kbm);
            Bind(point, "<Pen>/position", Kbm);

            // 主点击
            var click = Add(map, "Click", InputActionType.PassThrough, "Button");
            Bind(click, "<Mouse>/leftButton", Kbm);
            Bind(click, "<Pen>/tip", Kbm);
            Bind(click, "<Gamepad>/buttonSouth", Pad);

            var middle = Add(map, "MiddleClick", InputActionType.PassThrough, "Button");
            Bind(middle, "<Mouse>/middleButton", Kbm);

            var right = Add(map, "RightClick", InputActionType.PassThrough, "Button");
            Bind(right, "<Mouse>/rightButton", Kbm);

            var scroll = Add(map, "ScrollWheel", InputActionType.PassThrough, "Vector2");
            Bind(scroll, "<Mouse>/scroll", Kbm);

            // UI 导航：键盘两套 + 手柄两套
            var nav = Add(map, "Navigate", InputActionType.PassThrough, "Vector2");
            nav.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", Kbm)
                .With("Down", "<Keyboard>/s", Kbm)
                .With("Left", "<Keyboard>/a", Kbm)
                .With("Right", "<Keyboard>/d", Kbm);
            nav.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", Kbm)
                .With("Down", "<Keyboard>/downArrow", Kbm)
                .With("Left", "<Keyboard>/leftArrow", Kbm)
                .With("Right", "<Keyboard>/rightArrow", Kbm);
            Bind(nav, "<Gamepad>/leftStick", Pad);
            Bind(nav, "<Gamepad>/dpad", Pad);

            var submit = Add(map, "Submit", InputActionType.Button, "Button");
            Bind(submit, "<Keyboard>/enter", Kbm);
            Bind(submit, "<Keyboard>/numpadEnter", Kbm);
            Bind(submit, "<Gamepad>/buttonSouth", Pad);

            // ⚠ UI/Cancel 与 Global/Back 都绑了 Escape，这不是笔误：
            //   UI/Cancel 只发给 EventSystem 的当前选中对象（实现 ICancelHandler 的组件），
            //   用来关下拉列表之类的 UI 内部行为；
            //   Global/Back 是框架层的「返回上一层」，由 InputService 统一处理。
            //   两者语义不同、接收方不同，不会互相抢占，详见 ARCHITECTURE.md §6。
            var cancel = Add(map, "Cancel", InputActionType.Button, "Button");
            Bind(cancel, "<Keyboard>/escape", Kbm);
            Bind(cancel, "<Gamepad>/buttonEast", Pad);
        }

        // --------------------------------------------------------------------
        //  Gameplay：战斗中的玩家干预。
        //  键位与 Global 不重叠 —— 战斗中这两张 Map 是同时启用的。
        // --------------------------------------------------------------------
        private static void BuildGameplay(InputActionAsset asset)
        {
            var map = asset.AddActionMap("Gameplay");

            var ultimate = Add(map, "Ultimate", InputActionType.Button, "Button");
            Bind(ultimate, "<Keyboard>/space", Kbm);
            Bind(ultimate, "<Gamepad>/buttonSouth", Pad);

            var celestial = Add(map, "OverrideCelestial", InputActionType.Button, "Button");
            Bind(celestial, "<Keyboard>/q", Kbm);
            Bind(celestial, "<Gamepad>/buttonWest", Pad);

            var speed = Add(map, "ToggleSpeed", InputActionType.Button, "Button");
            Bind(speed, "<Keyboard>/leftShift", Kbm);
            Bind(speed, "<Gamepad>/buttonNorth", Pad);
        }

        // --------------------------------------------------------------------
        //  Global：与场景无关的全局键。
        //  Menu 用 Tab 而非 Escape，是为了避开 Global/Back —— 两者同属一张 Map，
        //  同一张 Map 内两个 Action 绑同一个键会同时触发。
        // --------------------------------------------------------------------
        private static void BuildGlobal(InputActionAsset asset)
        {
            var map = asset.AddActionMap("Global");

            var back = Add(map, "Back", InputActionType.Button, "Button");
            Bind(back, "<Keyboard>/escape", Kbm);
            Bind(back, "<Gamepad>/buttonEast", Pad);

            var menu = Add(map, "Menu", InputActionType.Button, "Button");
            Bind(menu, "<Keyboard>/tab", Kbm);
            Bind(menu, "<Gamepad>/start", Pad);

            var shot = Add(map, "Screenshot", InputActionType.Button, "Button");
            Bind(shot, "<Keyboard>/f12", Kbm);
        }

        // --------------------------------------------------------------------
        //  Debug：仅开发版。刻意避开 F1 —— QFramework 的 ConsoleKit 用 F1 开
        //  控制台，且走的是旧输入 API；绑同一个键会一次按出两个东西。
        // --------------------------------------------------------------------
        private static void BuildDebug(InputActionAsset asset)
        {
            var map = asset.AddActionMap("Debug");

            var gm = Add(map, "ToggleGmPanel", InputActionType.Button, "Button");
            Bind(gm, "<Keyboard>/f3", Kbm);

            var reload = Add(map, "ReloadConfig", InputActionType.Button, "Button");
            Bind(reload, "<Keyboard>/f5", Kbm);

            var god = Add(map, "ToggleGodMode", InputActionType.Button, "Button");
            Bind(god, "<Keyboard>/f6", Kbm);
        }

        // --------------------------------------------------------------------
        //  两个 shorthand：AddAction 与 AddBinding 的参数表都很长，
        //  包一层之后上面那张"键位表"才读得出来。
        // --------------------------------------------------------------------
        private static InputAction Add(InputActionMap map, string name,
            InputActionType type, string expectedLayout)
        {
            return map.AddAction(name, type, expectedControlLayout: expectedLayout);
        }

        private static void Bind(InputAction action, string path, string groups)
        {
            action.AddBinding(path, groups: groups);
        }
    }
}
