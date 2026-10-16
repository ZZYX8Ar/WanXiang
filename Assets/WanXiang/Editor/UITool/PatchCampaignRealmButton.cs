// ============================================================================
//  给 Panel_Campaign 补挂「秘境测试」与「离开秘境」两个按钮（2026-10-16）
//  ---------------------------------------------------------------------------
//  需求（用户原话）：
//    · "给我在节点地图加一个秘境测试按钮，点击就可以进入秘境"
//    · "这个离开按钮不要和节点地图返回主界面共用，先新加一个放到左下角往上一点就行，
//       后面把这些测试按钮删了再调位置"
//
//  为什么**克隆**而不是从零建：
//    手工编辑 prefab YAML 去复制一个 Button 的完整字段（RectTransform/CanvasRenderer/
//    Image/Button/子节点 TMP）极易漏字段 ⇒ 表现是"按钮有底色没反应"或"文字不显示"。
//    克隆 Btn_DebugFog 拿到的就是一份**保证可用**的按钮（含排版与音效钩子）。
//
//  ⛔ 这是**增量补丁**（项目铁律）：LoadPrefabContents → 只补缺 → 存回，不重建 prefab。
// ============================================================================

using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    internal static class PatchCampaignRealmButton
    {
        private const string Path = "Assets/Resources/UI/Panel_Campaign.prefab";
        private const string Name = "Btn_DebugRealm";
        private const string QuitName = "Btn_RealmQuit";
        private const string CloneSrc = "Btn_DebugFog";

        /// <summary>按钮尺寸（与克隆源一致；改这里要连着摆位一起看）。</summary>
        private const float BtnW = 170f, BtnH = 56f;

        [MenuItem("WanXiang/UI/给节点地图补挂秘境按钮", priority = 114)]
        internal static void PatchAll()
        {
            // ⚠ 摆位前**先读邻居的实际 rect**：迷雾按钮是 (24,24)、尺寸 170×56。
            //   秘境测试放它右侧（210,24）；离开按钮"左下角往上一点"（24, 24+56+12=92）。
            //   ⛔ 右下角不能放：那里是「出征」按钮（pos=(-204,69) size 360×110），会被压住
            //      —— 第一次就这么放过，截图里按钮整个被盖住、点不到。
            PatchOne(Name, "秘境测试", 210f, 24f);
            PatchOne(QuitName, "离开秘境", 24f, 24f + BtnH + 12f);
            AssetDatabase.Refresh();
            Debug.Log("[RealmBtn] 完成 → " + Path);
        }

        /// <summary>
        /// 只把三个调试按钮的**文字尺寸**调好（按钮已经在 prefab 里了）。
        /// <para>为什么需要单独一个：克隆出来的按钮沿用了原按钮的字号，
        /// 而原按钮写的是两行的"挂瘴雾天时(下一层)"，换成单行四字"秘境测试"后
        /// 字号偏大 ⇒ 文字被按钮裁掉（截图里能看到半截字）。</para>
        /// </summary>
        [MenuItem("WanXiang/UI/修调试按钮文字尺寸", priority = 116)]
        internal static void FixLabelSizes()
        {
            var inst = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var targets = new (string name, string text, float size)[]
                {
                    ("Btn_DebugFog",  "挂瘴雾", 22f),
                    (Name,            "秘境测试", 22f),
                    (QuitName,        "离开秘境", 22f),
                };
                foreach (var (nm, text, size) in targets)
                {
                    var t = inst.transform.Find(nm);
                    if (t == null) { Debug.LogWarning("[RealmBtn] 缺 " + nm); continue; }
                    var lt = t.Find("Tmp_Fog");
                    var tmp = lt != null ? lt.GetComponent<TMPro.TMP_Text>() : null;
                    if (tmp == null) { Debug.LogWarning("[RealmBtn] " + nm + " 无文字节点"); continue; }

                    tmp.text = text;
                    tmp.fontSize = size;
                    tmp.enableAutoSizing = true;      // 反正字号会随文字变，交给 TMP 自适应最稳
                    tmp.fontSizeMin = 12f;
                    tmp.fontSizeMax = size;
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.enableWordWrapping = false;   // 单行标签，禁止折行（折了更读不清）
                    // ⚠ 文字节点是 stretch 锚点 + sizeDelta=0（完全贴合按钮）⇒ 需要内缩留边，
                    //   否则字会顶到按钮圆角上。
                    var lrt = lt as RectTransform;
                    if (lrt != null) { lrt.offsetMin = new Vector2(8f, 4f); lrt.offsetMax = new Vector2(-8f, -4f); }
                }
                EditorUtility.SetDirty(inst);
                Debug.Log("[RealmBtn] 调试按钮文字已修（单行 + 自适应字号 + 内缩留边）");
            }
            finally
            {
                PrefabUtility.SaveAsPrefabAsset(inst, Path);
                PrefabUtility.UnloadPrefabContents(inst);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>克隆一个可用的按钮并摆到指定位置（幂等：已存在就跳过）。</summary>
        private static void PatchOne(string name, string label, float x, float y)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (root == null) { Debug.LogError("[RealmBtn] 找不到 " + Path); return; }
            if (root.transform.Find(name) != null) { Debug.Log("[RealmBtn] " + name + " 已存在，跳过"); return; }

            var inst = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                if (inst.transform.Find(name) != null) { Debug.Log("[RealmBtn] " + name + " 已存在（content 内）"); return; }
                var src = inst.transform.Find(CloneSrc);
                if (src == null) { Debug.LogError("[RealmBtn] 找不到克隆源 " + CloneSrc); return; }

                var go = Object.Instantiate(src.gameObject, inst.transform);
                go.name = name;

                // ⚠ Instantiate 会**复制位置**（同一父节点下会完全重叠）⇒ 必须显式挪开。
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(x, y);
                rt.sizeDelta = new Vector2(BtnW, BtnH);

                // ⚠ 克隆源的文字子节点叫 **Tmp_Fog**（不是 UIBuild 生成器用的 Tmp_Label）
                //   —— Panel_Campaign 是老 prefab，命名不统一；找不到就退而求其次找任一 TMP。
                var labelT = go.transform.Find("Tmp_Fog");
                if (labelT == null)
                {
                    foreach (Transform c in go.transform)
                        if (c.GetComponent<TMPro.TMP_Text>() != null) { labelT = c; break; }
                }
                var t = labelT != null ? labelT.GetComponent<TMPro.TMP_Text>() : null;
                if (t != null) t.text = label;
                else Debug.LogWarning("[RealmBtn] " + name +
                    " 找不到文字节点 —— 会沿用原按钮文字（'瘴雾'），必须手工改");

                EditorUtility.SetDirty(inst);
                Debug.Log("[RealmBtn] 已补挂 " + name + "（" + label + " @ " + x + "," + y + "）");
            }
            finally
            {
                PrefabUtility.SaveAsPrefabAsset(inst, Path);
                PrefabUtility.UnloadPrefabContents(inst);
            }
        }

        [MenuItem("WanXiang/UI/校验秘境按钮", priority = 115)]
        internal static void Verify()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (root == null) { Debug.LogError("找不到 " + Path); return; }
            var sb = new System.Text.StringBuilder("[校验] Panel_Campaign 按钮\n");
            foreach (var nm in new[] { Name, QuitName, CloneSrc, "Btn_Next" })
            {
                var t = root.transform.Find(nm);
                if (t == null) { sb.AppendLine("  " + nm + " ❌ 缺失"); continue; }
                var rt = (RectTransform)t;
                var lrt = t.Find("Tmp_Fog");
                string txt = lrt != null ? lrt.GetComponent<TMPro.TMP_Text>().text : "?";
                sb.AppendLine("  " + nm + "  pos=" + rt.anchoredPosition + "  size=" + rt.sizeDelta +
                              "  文字=" + txt);
            }
            Debug.Log(sb.ToString());
        }
    }
}
