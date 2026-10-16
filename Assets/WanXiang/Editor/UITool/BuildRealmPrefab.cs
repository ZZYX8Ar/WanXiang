// ============================================================================
//  Panel_Realm —— 秘境**入口抉择**面板构建器（2026-10-16）
//  ---------------------------------------------------------------------------
//  ★★★ 这个面板**不画地图**。
//     用户 2026-10-16："这个秘境就是和节点地图一模一样啊，直接复用就完事了…
//      你看一下第五幕，不也是只有5个线性的节点吗…
//      你直接把这些秘境想象成特殊的幕不就行了吗"
//
//     ⇒ 秘境内部图 = CampaignPanel 注入一张外部 ActGraph，零额外地图代码。
//     ⇒ 所以这个面板退化成一个**居中抉择框**：场域三行 + 三个按钮。
//     上一版把它做成 1920×1080 全屏、里面还塞了一套"自绘小地图"——全部作废。
// ============================================================================

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    internal static class BuildRealmPrefab
    {
        private const string Dir = "Assets/Resources/UI";

        // 居中抉择框
        private const float BoxW = 760f, BoxH = 560f;
        private const float PadX = 40f;

  [MenuItem("WanXiang/UI/生成秘境面板", priority = 102)]
        internal static void Build()
        {
if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
      if (!AssetDatabase.IsValidFolder(Dir))
     AssetDatabase.CreateFolder("Assets/Resources", "UI");

        // 中文字体（Deng SDF；换机把源 TTF 放回 Assets/ArtRes/Fonts 再跑字体菜单）
    var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
if (font == null)
      font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
   "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            UIBuild.SetFont(font);

      BuildRealm();

   AssetDatabase.SaveAssets();
   AssetDatabase.Refresh();
  Debug.Log("[BuildRealm] Panel_Realm 生成完毕 → " + Dir);
        }

        private static void BuildRealm()
        {
            SafeDelete("Panel_Realm");

  var root = Place(Null(), "Panel_Realm", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(BoxW, BoxH), Vector2.zero);
            var comp = root.gameObject.AddComponent<RealmPanel>();
            UIBuild.Img(root, new Color(0.06f, 0.05f, 0.08f, 0.96f));

// ---- 标题 ----
            var title = UIBuild.Tmp(Place(root, "Tmp_Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
              new Vector2(BoxW - PadX * 2f, 52f), new Vector2(0f, -28f)),
        "秘境 · 抉择", 32, UIBuild.Ink, TextAlignmentOptions.Center);
            var status = UIBuild.Tmp(Place(root, "Tmp_Status", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
        new Vector2(BoxW - PadX * 2f, 28f), new Vector2(0f, -84f)),
                "本局还可进入 2 次", 18, UIBuild.Ink2, TextAlignmentOptions.Center);

            // ---- 场域三行（效果 / 收益 / 代价）----
 var info = Place(root, "Root_RealmInfo", new Vector2(0f, 1f), new Vector2(0f, 1f),
    new Vector2(BoxW - PadX * 2f, 300f), new Vector2(PadX, -124f));
            UIBuild.Img(info, UIBuild.Card, true);

        // ⚠ 每行给足高度：都是会换行的长句（18px 中文在 680 宽里能折两三行），
   //   高度不够就会压到下一行上（第一版踩过，截图才看出来）。
            float innerW = BoxW - PadX * 2f - 48f;
         var infoName = UIBuild.Tmp(Place(info, "Tmp_RealmName", new Vector2(0f, 1f), new Vector2(0f, 1f),
       new Vector2(innerW, 52f), new Vector2(24f, -20f)),
         "苍泽", 30, UIBuild.Ink, TextAlignmentOptions.TopLeft);
 var infoHazard = UIBuild.Tmp(Place(info, "Tmp_Hazard", new Vector2(0f, 1f), new Vector2(0f, 1f),
        new Vector2(innerW, 84f), new Vector2(24f, -80f)),
                "场地：每回合敌方全体「湿」1 层", 18, UIBuild.Red, TextAlignmentOptions.TopLeft);
  var infoBoon = UIBuild.Tmp(Place(info, "Tmp_Boon", new Vector2(0f, 1f), new Vector2(0f, 1f),
      new Vector2(innerW, 64f), new Vector2(24f, -172f)),
     "收益：产出 ×1.5", 18, UIBuild.Ink, TextAlignmentOptions.TopLeft);
      var infoCost = UIBuild.Tmp(Place(info, "Tmp_Cost", new Vector2(0f, 1f), new Vector2(0f, 1f),
    new Vector2(innerW, 84f), new Vector2(24f, -244f)),
           "代价：全程速度 -10%", 18, UIBuild.Ink2, TextAlignmentOptions.TopLeft);

 // ---- 底部三个按钮 ----
            float btnY = 44f, btnH = 72f, btnW = 210f;
   var enter = MakeBtn(root, "Btn_Enter", new Vector2(0.5f, 0f),
        new Vector2(btnW, btnH), new Vector2(-234f, btnY), "进入秘境", UIBuild.Silk, 24f);
            var leave = MakeBtn(root, "Btn_Leave", new Vector2(0.5f, 0f),
      new Vector2(btnW, btnH), new Vector2(0f, btnY), "暂不进入", UIBuild.Card, 24f);
          var reroll = MakeBtn(root, "Btn_Reroll", new Vector2(0.5f, 0f),
    new Vector2(btnW, btnH), new Vector2(234f, btnY), "换一个", UIBuild.Card, 24f);

 UIBuild.Bind(comp, "_tmpTitle", title);
        UIBuild.Bind(comp, "_tmpStatus", status);
            UIBuild.Bind(comp, "_infoRoot", info);
            UIBuild.Bind(comp, "_tmpRealmName", infoName);
UIBuild.Bind(comp, "_tmpHazard", infoHazard);
     UIBuild.Bind(comp, "_tmpBoon", infoBoon);
     UIBuild.Bind(comp, "_tmpCost", infoCost);
 UIBuild.Bind(comp, "_btnEnter", enter.GetComponent<Button>());
     UIBuild.Bind(comp, "_btnLeave", leave.GetComponent<Button>());
      UIBuild.Bind(comp, "_btnReroll", reroll.GetComponent<Button>());

  UIBuild.SavePrefab(root.gameObject, "Panel_Realm");
        }

        /// <summary>
      /// 放一个子节点。⚠ pivot 设成 anchorMin —— 否则子节点从锚点往两边各伸一半，
      ///   文字会跑到卡片外面（第一版踩过，截图才发现）。
        /// </summary>
        private static RectTransform Place(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 pos)
 {
  var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    var rt = (RectTransform)go.transform;
     rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
  rt.sizeDelta = size;
 rt.anchoredPosition = pos;
            return rt;
        }

   /// <summary>固定尺寸按钮（pivot 跟着锚点，避免文字/按钮错位）。</summary>
        private static RectTransform MakeBtn(Transform parent, string name, Vector2 anchor,
            Vector2 size, Vector2 pos, string label, Color c, float fontSize)
    {
            var rt = Place(parent, name, anchor, anchor, size, pos);
            UIBuild.Img(rt, c, true);
 var label2 = Place(rt, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
      UIBuild.Tmp(label2, label, fontSize, UIBuild.Ink);
            UIBuild.Btn(rt);
      return rt;
        }

        /// <summary>UIBuild 的工具都以 Transform 为 parent，这里给个假父级建根。</summary>
   private static Transform Null()
        {
      var go = new GameObject("_RealmBuilderRoot", typeof(RectTransform));
    return go.transform;
    }

        private static void SafeDelete(string prefabName)
{
            string path = Dir + "/" + prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
   AssetDatabase.DeleteAsset(path);
        }
    }
}