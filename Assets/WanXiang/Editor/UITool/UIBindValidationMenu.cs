// ============================================================================
//  WanXiang · UI 绑定校验菜单
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / UI / 校验面板绑定
//  检查三件事：
//    1. Prefab 命名（文件名 = 根节点名 = Panel_ 前缀，且挂了 UIPanelBase 派生脚本）
//    2. 子节点命名前缀在白名单内、同层无重名
//    3. [BindArray] 数组字段能按命名契约找到全部节点
//  配套菜单：WanXiang / UI / 校验面板配置（UIPanelValidationMenu，查 key 冲突与缓存策略）
// ============================================================================

using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.EditorTools
{
    public static class UIBindValidationMenu
    {
        private static readonly string[] Prefixes =
            { "Root_", "Btn_", "Tmp_", "Img_", "Bar_", "Sld_", "Tgl_", "List_", "Item_", "Cell_", "Goods_", "Draft_",
              "Opt_", "Choice_", "Tab_", "Track_", "HpBar_", "Scroll_", "Viewport", "Content" };

        [MenuItem("WanXiang/UI/校验面板绑定", priority = 101)]
        public static void Validate()
        {
            var problems = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/UI" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                string file = System.IO.Path.GetFileNameWithoutExtension(path);

                if (!file.StartsWith("Panel_") && !file.StartsWith("Toast_") && !file.StartsWith("Loading_"))
                    problems.Add($"{file}: 面板文件名必须以 Panel_ 开头（{path}）");
                if (go.name != file)
                    problems.Add($"{file}: 根节点名（{go.name}）与文件名不一致");
                if (file.StartsWith("Panel_") && go.GetComponent<UIPanelBase>() == null)
                    problems.Add($"{file}: 根节点没有挂 UIPanelBase 派生脚本");

                var all = go.GetComponentsInChildren<Transform>(true);
                foreach (var t in all)
                {
                    if (t == go.transform) continue;
                    string n = t.name;
                    if (n.EndsWith("(Clone)"))
                        problems.Add($"{file}/{GetPath(t)}: 节点名带 (Clone) 后缀");
                    bool ok = false;
                    foreach (var p in Prefixes) { if (n.StartsWith(p)) { ok = true; break; } }
                    if (!ok) problems.Add($"{file}/{GetPath(t)}: 节点名前缀不在白名单（{n}）");
                }

                // 同父同名才违规（不同按钮下的 Tmp_Label 各自独立，属正常）
                foreach (var t in all)
                {
                    var seen = new HashSet<string>();
                    for (int i = 0; i < t.childCount; i++)
                    {
                        if (!seen.Add(t.GetChild(i).name))
                            problems.Add($"{file}: 「{t.name}」下存在同名节点「{t.GetChild(i).name}」");
                    }
                }

                var panel = go.GetComponent<UIPanelBase>();
                if (panel == null) continue;

                var fields = panel.GetType().GetFields(BindingFlags.Instance |
                            BindingFlags.NonPublic | BindingFlags.Public);
                foreach (var f in fields)
                {
                    var bind = f.GetCustomAttribute<BindArrayAttribute>();
                    if (bind == null) continue;
                    for (int i = 0; i < bind.Count; i++)
                    {
                        string node = string.Format(bind.Format, i);
                        if (go.transform.FindDeep(node) == null)
                            problems.Add($"{file}: 字段 {f.Name} 缺节点 {node}");
                    }
                }
            }

            if (problems.Count == 0) Debug.Log("[UIBind] 面板绑定校验通过。");
            else
            {
                foreach (var p in problems) Debug.LogError($"[UIBind] {p}");
                Debug.LogError($"[UIBind] 共 {problems.Count} 个问题");
            }
        }

        private static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null && t.parent != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        private static Transform FindDeep(this Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = root.GetChild(i).FindDeep(name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
