// ============================================================================
//  给 Panel_Campaign 补一个「秘境测试」按钮（2026-10-16）
//  ---------------------------------------------------------------------------
//  需求（用户原话）："给我在节点地图加一个秘境测试按钮，点击就可以进入秘境，
//  撤离就返回到先前的幕和节点"。
//
//  为什么**克隆**而不是从零建：
//    手工编辑 prefab YAML 去复制一个 Button 的完整字段（RectTransform/CanvasRenderer/
//    Image/Button/子节点 TMP）极易漏字段 ⇒ 表现是"按钮有底色没反应"或"文字不显示"。
//    克隆 Btn_DebugFog 拿到的就是一份**保证可用**的按钮（含它的排版与音效钩子）。
//
//  ⛔ 这是**增量补丁**（项目铁律）：LoadPrefabContents → 只补缺 → 存回，不重建 prefab。
// ============================================================================

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    internal static class PatchCampaignRealmButton
    {
        private const string Path = "Assets/Resources/UI/Panel_Campaign.prefab";
        private const string Name = "Btn_DebugRealm";

        [MenuItem("WanXiang/UI/给节点地图补挂秘境测试按钮", priority = 114)]
        internal static void Patch()
        {
   var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
       if (root == null) { Debug.LogError("[RealmBtn] 找不到 " + Path); return; }
            if (root.transform.Find(Name) != null)
         { Debug.Log("[RealmBtn] 已存在，跳过"); return; }

      var srcT = root.transform.Find("Btn_DebugFog");
       if (srcT == null) { Debug.LogError("[RealmBtn] 找不到克隆源 Btn_DebugFog"); return; }

      var inst = PrefabUtility.LoadPrefabContents(Path);
        try
   {
    var src = inst.transform.Find("Btn_DebugFog");
      var go = Object.Instantiate(src.gameObject, inst.transform);
                go.name = Name;

            // ⚠ Instantiate 会**复制位置**（同一个父节点下会重叠在瘴雾按钮上）——
     // ⚠ 摆位：紧挨在迷雾按钮（Btn_DebugFog）右侧。
            //   它的 pos=(24,24) size=(170,56) ⇒ 右边缘 194；留 16 的缝 ⇒ 210。
   //   ⛔ 千万别放右下角：那里是「出征」按钮（pos=(-204,69) size 360×110），会被压住
      //   （第一次就放那儿，截图里按钮整个被盖住、点不到）。
      var rt = (RectTransform)go.transform;
    rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
   rt.pivot = new Vector2(0f, 0f);
  rt.anchoredPosition = new Vector2(210f, 24f);
            rt.sizeDelta = new Vector2(170f, 56f);

      // ⚠ 克隆源里的文字子节点叫 **Tmp_Fog**（不是 Tmp_Label —— 那是 UIBuild 生成器的命名，
      //   Panel_Campaign 是老 prefab，命名不一致）。找不到就退而求其次找第一个 TMP。
    const string LabelName = "Tmp_Fog";
     var labelT = go.transform.Find(LabelName);
     if (labelT == null)
       {
  foreach (Transform c in go.transform)   // 兜底：任意带 TMP 的子节点
         {
    var t0 = c.GetComponent<TMPro.TMP_Text>();
if (t0 != null) { labelT = c; break; }
          }
    }
       var label = labelT != null ? labelT.GetComponent<TMPro.TMP_Text>() : null;
            if (label != null) label.text = "秘境测试";
            else Debug.LogWarning("[RealmBtn] 克隆出来的按钮里找不到文字节点，文字将沿用原样（" +
      "原按钮写的是'瘴雾'，会误导）");

        EditorUtility.SetDirty(inst);
                  Debug.Log("[RealmBtn] 已补挂 " + Name + "（左下角，迷雾按钮右侧）");
            }
     finally
   {
        PrefabUtility.SaveAsPrefabAsset(inst, Path);
       PrefabUtility.UnloadPrefabContents(inst);
            }
 AssetDatabase.Refresh();
     Debug.Log("[RealmBtn] 完成 → " + Path);
        }

        [MenuItem("WanXiang/UI/校验秘境测试按钮", priority = 115)]
        internal static void Verify()
 {
       var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
  if (root == null) { Debug.LogError("找不到 " + Path); return; }
   var t = root.transform.Find(Name);
  if (t == null) { Debug.LogError("❌ 没有 " + Name); return; }
    var b = t.GetComponent<Button>();
         var rt = (RectTransform)t;
    Debug.Log("[校验] " + Name + "\n  Button=" + (b != null) +
        "\n  anchor=" + rt.anchorMin + "/" + rt.anchorMax +
  "\n  pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta +
  "\n  文字=" + (t.Find("Tmp_Fog") != null
  ? t.Find("Tmp_Fog").GetComponent<TMPro.TMP_Text>().text : "(无文字节点)"));
        }
    }
}