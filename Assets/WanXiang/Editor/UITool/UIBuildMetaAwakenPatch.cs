// ============================================================================
//  万相 · UI 补丁：Panel_Meta 的「觉醒技」行与选择弹层
//  ---------------------------------------------------------------------------
//  设计（用户定案）：觉醒技槽【只能装终结技】；来源 = 已解锁异兽的终结技 + 探索掉落。
//
//  本补丁补出（增量，保留手工修改）：
//    · Tmp_Skill4      —— 详情里的第 4 行（④ 觉醒技：XXX）
//    · Btn_AwakenPick  —— 「更换」按钮（打开选择弹层）
//    · AwakenPicker    —— 选择弹层（标题 + 列表模板 Item_Awaken + 关闭）
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildMetaAwakenPatch
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Meta.prefab";

        [MenuItem("WanXiang/UI/补丁：局外养成的觉醒技控件", priority = 107)]
        internal static void Patch()
        {
            if (!System.IO.File.Exists(PrefabPath)) { Debug.LogError("[AwakenPatch] 找不到 " + PrefabPath); return; }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[AwakenPatch] 载入失败"); return; }
            try
            {
                bool changed = false;
                changed |= EnsureSkill4Row(root);
                changed |= EnsurePicker(root);
                BindFields(root);

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    AssetDatabase.Refresh();
                    Debug.Log("[AwakenPatch] OK 已补丁 " + PrefabPath);
                }
                else Debug.Log("[AwakenPatch] 节点已存在，仅重做绑定");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>详情里第 4 行 + 「更换」按钮（放在 Tmp_Skill3 下方）。</summary>
        private static bool EnsureSkill4Row(GameObject root)
        {
            bool created = false;
            var detail = FindDeep(root.transform, "Root_Detail");

            if (FindDeep(root.transform, "Tmp_Skill4") == null)
            {
                var s3 = FindDeep(root.transform, "Tmp_Skill3");
                Transform parent = s3 != null ? s3.parent : (detail != null ? detail : root.transform);

                var rt = NewNode(parent, "Tmp_Skill4");
                var r = (RectTransform)rt.transform;
                if (s3 != null)
                {
                    var sr = (RectTransform)s3;
                    r.anchorMin = sr.anchorMin; r.anchorMax = sr.anchorMax; r.pivot = sr.pivot;
                    r.sizeDelta = sr.sizeDelta;
                    r.anchoredPosition = sr.anchoredPosition + new Vector2(0f, -56f);
                }
                else
                {
                    r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                    r.pivot = new Vector2(0f, 1f);
                    r.sizeDelta = new Vector2(900f, 48f);
                    r.anchoredPosition = new Vector2(28f, -760f);
                }
                var t = rt.AddComponent<TextMeshProUGUI>();
                t.text = "④ 觉醒技：――（觉醒后可装备）";
                t.fontSize = 22;
                t.color = UIBuild.Ink;
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.raycastTarget = false;
                if (UIBuild.Font != null) t.font = UIBuild.Font;
                created = true;
                Debug.Log("[AwakenPatch] + Tmp_Skill4");
            }

            if (FindDeep(root.transform, "Btn_AwakenPick") == null)
            {
                var s3 = FindDeep(root.transform, "Tmp_Skill3");
                Transform parent = s3 != null ? s3.parent : (detail != null ? detail : root.transform);
                var rt = NewNode(parent, "Btn_AwakenPick");
                var r = (RectTransform)rt.transform;
                r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(1f, 1f);
                r.sizeDelta = new Vector2(140f, 44f);
                r.anchoredPosition = new Vector2(-28f, -762f);
                var img = rt.AddComponent<Image>();
                img.color = UIBuild.Gold;
                var btn = rt.AddComponent<Button>();
                btn.targetGraphic = img;
                var lb = NewNode(rt.transform, "Tmp_Label");
                Fill((RectTransform)lb.transform);
                var lt = lb.AddComponent<TextMeshProUGUI>();
                lt.text = "更换";
                lt.fontSize = 24;
                lt.color = UIBuild.Ink;
                lt.alignment = TextAlignmentOptions.Center;
                lt.raycastTarget = false;
                if (UIBuild.Font != null) lt.font = UIBuild.Font;
                created = true;
                Debug.Log("[AwakenPatch] + Btn_AwakenPick");
            }
            return created;
        }

        /// <summary>选择弹层（遮罩 + 标题 + 纵向列表模板 + 关闭）。</summary>
        private static bool EnsurePicker(GameObject root)
        {
            if (FindDeep(root.transform, "AwakenPicker") != null) return false;

            var mask = NewNode(root.transform, "AwakenPicker");
            var mr = (RectTransform)mask.transform;
            mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one;
            mr.offsetMin = Vector2.zero; mr.offsetMax = Vector2.zero;
            var mimg = mask.AddComponent<Image>();
            mimg.color = new Color(0f, 0f, 0f, 0.45f);
            mask.AddComponent<Button>().targetGraphic = mimg;

            var card = NewNode(mask.transform, "Card");
            var cr = (RectTransform)card.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(900f, 620f);
            cr.anchoredPosition = Vector2.zero;
            card.AddComponent<Image>().color = UIBuild.Card;

            var title = NewNode(card.transform, "Tmp_Title");
            var tr = (RectTransform)title.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(820f, 52f);
            tr.anchoredPosition = new Vector2(0f, -20f);
            var tt = title.AddComponent<TextMeshProUGUI>();
            tt.text = "选择觉醒技（只能装备终结技）";
            tt.fontSize = 30; tt.color = UIBuild.Ink;
            tt.alignment = TextAlignmentOptions.Center; tt.raycastTarget = false;
            if (UIBuild.Font != null) tt.font = UIBuild.Font;

            // 滚动区
            var scrollGo = NewNode(card.transform, "Scroll");
            var sr2 = (RectTransform)scrollGo.transform;
            sr2.anchorMin = new Vector2(0f, 0f); sr2.anchorMax = new Vector2(1f, 1f);
            sr2.offsetMin = new Vector2(30f, 100f); sr2.offsetMax = new Vector2(-30f, -84f);
            var sc = scrollGo.AddComponent<ScrollRect>();
            sc.horizontal = false; sc.vertical = true; sc.movementType = ScrollRect.MovementType.Clamped;
            var vp = NewNode(scrollGo.transform, "Viewport");
            var vr = (RectTransform)vp.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            vp.AddComponent<RectMask2D>();
            sc.viewport = vr;
            var content = NewNode(vp.transform, "Content");
            var conr = (RectTransform)content.transform;
            conr.anchorMin = new Vector2(0f, 1f); conr.anchorMax = new Vector2(1f, 1f);
            conr.pivot = new Vector2(0.5f, 1f);
            conr.sizeDelta = new Vector2(0f, 0f);
            sc.content = conr;

            // 条目模板
            var item = NewNode(content.transform, "Item_Awaken");
            var ir = (RectTransform)item.transform;
            ir.anchorMin = new Vector2(0f, 1f); ir.anchorMax = new Vector2(1f, 1f);
            ir.pivot = new Vector2(0.5f, 1f);
            ir.sizeDelta = new Vector2(-16f, 84f);
            ir.anchoredPosition = new Vector2(8f, -8f);
            item.AddComponent<Image>().color = UIBuild.Silk;
            var it = NewNode(item.transform, "Tmp_Item");
            FillOffset((RectTransform)it.transform, new Vector2(20f, 0f), new Vector2(-20f, 0f));
            var itt = it.AddComponent<TextMeshProUGUI>();
            itt.text = "技能名（来源）";
            itt.fontSize = 24; itt.color = UIBuild.Ink;
            itt.alignment = TextAlignmentOptions.MidlineLeft; itt.raycastTarget = false;
            if (UIBuild.Font != null) itt.font = UIBuild.Font;
            var ib = item.AddComponent<Button>();
            ib.targetGraphic = item.GetComponent<Image>();
            item.SetActive(false);

            var close = NewNode(card.transform, "Btn_AwakenClose");
            var clr = (RectTransform)close.transform;
            clr.anchorMin = clr.anchorMax = new Vector2(0.5f, 0f);
            clr.pivot = new Vector2(0.5f, 0f);
            clr.sizeDelta = new Vector2(200f, 64f);
            clr.anchoredPosition = new Vector2(0f, 20f);
            var cimg = close.AddComponent<Image>();
            cimg.color = UIBuild.Silk;
            var cb = close.AddComponent<Button>(); cb.targetGraphic = cimg;
            var cl = NewNode(close.transform, "Tmp_Label");
            Fill((RectTransform)cl.transform);
            var clt = cl.AddComponent<TextMeshProUGUI>();
            clt.text = "关闭";
            clt.fontSize = 26; clt.color = UIBuild.Ink;
            clt.alignment = TextAlignmentOptions.Center; clt.raycastTarget = false;
            if (UIBuild.Font != null) clt.font = UIBuild.Font;

            mask.SetActive(false);
            Debug.Log("[AwakenPatch] + AwakenPicker（列表 + 关闭）");
            return true;
        }

        private static void BindFields(GameObject root)
        {
            var panel = root.GetComponent<WanXiang.Modules.UI.MetaPanel>();
            if (panel == null) { Debug.LogError("[AwakenPatch] 根上没有 MetaPanel"); return; }
            var so = new SerializedObject(panel);
            SetRef(so, root, "_tmpSkill4", "Tmp_Skill4");
            SetRef(so, root, "_btnAwakenPick", "Btn_AwakenPick");
            SetRef(so, root, "_awakenPicker", "AwakenPicker");
            SetRef(so, root, "_awakenPickerTitle", "Tmp_Title");
            SetRef(so, root, "_awakenItemTemplate", "Item_Awaken");
            SetRef(so, root, "_btnAwakenClose", "Btn_AwakenClose");
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[AwakenPatch] 字段绑定完成");
        }

        private static void SetRef(SerializedObject so, GameObject root, string field, string nodeName)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[AwakenPatch] 字段不存在：" + field); return; }
            var t = FindDeep(root.transform, nodeName);
            p.objectReferenceValue = t != null ? t.gameObject : null;
            if (t == null) Debug.LogWarning("[AwakenPatch] 节点缺失：" + nodeName);
        }

        private static GameObject NewNode(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void FillOffset(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = min; rt.offsetMax = max;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
#endif
