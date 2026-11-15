// 万相 · 骰子面板 prefab 构建器（2026-11-15）
// 只补缺、不重建；产出 Assets/Resources/UI/Panel_DiceRoll.prefab
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class DiceRollPrefabTool
    {
        private const string Path = "Assets/Resources/UI/Panel_DiceRoll.prefab";

        [MenuItem("万相/UI/生成骰子面板")]
        public static void Build()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            GameObject root;
            if (existing != null)
            {
                root = PrefabUtility.LoadPrefabContents(Path);
            }
            else
            {
                root = new GameObject("Panel_DiceRoll", typeof(RectTransform));
            }

            var rt = root.transform as RectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // 半透遮罩（不挡点击：演出期间由面板自己控制 raycastTarget）
            EnsureImage(root.transform, "Img_Mask", new Color(0.12f, 0.10f, 0.08f, 0.55f));

            // 中央米纸卡
            var card = EnsureNode(root.transform, "Root_Card",
                new Vector2(620f, 340f), Vector2.zero);
            EnsureImage(card, "Img_Card", new Color(0.97f, 0.95f, 0.90f, 1f),
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Kit/card_solid.png"));
            EnsureText(root.transform, "Root_Card/Tmp_Title", "庄家开赌", 30, new Vector2(560f, 50f), new Vector2(0f, 120f));
            EnsureText(root.transform, "Root_Card/Tmp_Result", "", 24, new Vector2(560f, 60f), new Vector2(0f, -110f));

            // 三颗骰子（1~6 点数用 TMP 文本显示，动画在 Presenter 里转/缩放）
            string[] dots = { "·", "·", "·" };
            for (int i = 0; i < 3; i++)
            {
                var d = EnsureNode(root.transform, "Root_Card/Dice_" + i,
                    new Vector2(110f, 110f), new Vector2((i - 1) * 150f, 0f));
                EnsureImage(d, "Img_Die", new Color(1f, 0.99f, 0.94f, 1f),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Kit/card_hover.png"));
                EnsureTextOn(d, "Tmp_Pips", dots[i], 54, new Vector2(100f, 100f));
            }

            if (existing != null) { PrefabUtility.SaveAsPrefabAsset(root, Path); PrefabUtility.UnloadPrefabContents(root); }
            else { PrefabUtility.SaveAsPrefabAsset(root, Path); Object.DestroyImmediate(root); }
            AssetDatabase.Refresh();
            Debug.Log("[DiceRollPrefabTool] 骰子面板就绪：" + Path);
        }

        private static RectTransform EnsureNode(Transform parent, string path, Vector2 size, Vector2 pos)
        {
            var t = parent.Find(path);
            if (t == null)
            {
                string name = path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
                Transform p = parent;
                if (path.Contains("/"))
                {
                    var seg = path.Substring(0, path.LastIndexOf('/'));
                    p = parent.Find(seg);
                    if (p == null) p = EnsureNode(parent, seg, new Vector2(620f, 340f), Vector2.zero);
                }
                var go = new GameObject(name, typeof(RectTransform));
                t = go.transform; t.SetParent(p, false);
            }
            var r = t as RectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size; r.anchoredPosition = pos;
            return r;
        }

        private static void EnsureImage(Transform parent, string name, Color c, Sprite sp = null)
        {
            var t = EnsureNode(parent, name, new Vector2(200f, 200f), Vector2.zero);
            if (name == "Img_Mask")
            {
                t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one;
                t.offsetMin = Vector2.zero; t.offsetMax = Vector2.zero;
            }
            else if (name == "Img_Card" || name == "Img_Die")
            {
                t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one;
                t.offsetMin = Vector2.zero; t.offsetMax = Vector2.zero;
            }
            var im = t.GetComponent<Image>();
            if (im == null) im = t.gameObject.AddComponent<Image>();
            im.color = c; im.sprite = sp;
            if (sp != null) im.type = Image.Type.Sliced;
            im.raycastTarget = false;
        }

        private static void EnsureText(Transform parent, string path, string text, int size, Vector2 sz, Vector2 pos)
            => EnsureTextOn(EnsureNode(parent, path, sz, pos), "Tmp_Text", text, size, sz);

        private static void EnsureTextOn(Transform t, string childName, string text, int size, Vector2 sz)
        {
            var c = t.Find(childName);
            if (c == null && t.name.StartsWith("Tmp_")) c = t;
            if (c == null)
            {
                var go = new GameObject(childName, typeof(RectTransform), typeof(TextMeshProUGUI));
                c = go.transform; c.SetParent(t, false);
            }
            var r = c as RectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var tx = c.GetComponent<TMP_Text>();
            if (tx == null) tx = c.gameObject.AddComponent<TextMeshProUGUI>();
            tx.text = text; tx.fontSize = size;
            tx.alignment = TextAlignmentOptions.Center;
            tx.color = new Color(0.22f, 0.18f, 0.14f, 1f);
            tx.raycastTarget = false;
        }
    }
}
