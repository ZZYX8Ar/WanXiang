// ============================================================================
//  万相 · 遗物 HUD（节点地图 Panel_Campaign / 战斗 Panel_Battle 共用）
//  ---------------------------------------------------------------------------
//  把本局已获遗物在左上角铺成一排小牌（参考杀戮尖塔的遗物条），
//  鼠标悬浮任意一张 → 显示遗物名 + 效果说明。
//
//  节点结构（两个面板的 prefab 里都要有，由 Editor/UITool/BuildRelicHudPatch.cs 补丁）：
//    Root_Relics      容器（HorizontalLayoutGroup，左上角）
//      Item_Relic     小牌模板（默认隐藏，运行时克隆）→ Image + 子节点 Tmp_RelicName
//    Root_RelicTip    悬浮说明浮层（默认隐藏）→ 子节点 Tmp_RelicTip
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanXiang.Campaign;

namespace WanXiang.Modules.UI
{
    internal static class RelicHud
    {
        /// <summary>按已持有遗物铺满一条（先清旧的克隆，保留模板）。</summary>
        public static void Populate(RectTransform root, GameObject template, IList<string> relicIds,
                                    Action<RelicDef> onEnter, Action onExit)
        {
            if (root == null || template == null) return;

            // 清掉上一次的克隆（保留模板）
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var c = root.GetChild(i).gameObject;
                if (c == template) continue;
                UnityEngine.Object.Destroy(c);
            }
            template.SetActive(false);

            int shown = 0;
            if (relicIds != null)
            {
                for (int i = 0; i < relicIds.Count; i++)
                {
                    var def = RelicCatalog.Get(relicIds[i]);
                    if (def == null) continue;
                    var chip = UnityEngine.Object.Instantiate(template, root);
                    chip.SetActive(true);
                    chip.name = "Item_Relic_" + i;

                    var label = chip.transform.Find("Tmp_RelicName")?.GetComponent<TMP_Text>();
                    if (label != null) label.text = def.Name;
                    var img = chip.GetComponent<Image>();
                    if (img != null) img.color = RarityColor(def.Rarity);
                    Hook(chip, def, onEnter, onExit);
                    shown++;
                }
            }
            root.gameObject.SetActive(shown > 0);
        }

        /// <summary>把说明写进浮层并显示（浮层置顶，避免被后续兄弟节点盖住）。</summary>
        public static void ShowTip(GameObject tipRoot, TMP_Text tipText, RelicDef def)
        {
            if (tipText != null) tipText.text = Describe(def);
            if (tipRoot != null)
            {
                tipRoot.SetActive(true);
                tipRoot.transform.SetAsLastSibling();
            }
        }

        public static void HideTip(GameObject tipRoot)
        {
            if (tipRoot != null) tipRoot.SetActive(false);
        }

        public static string Describe(RelicDef def)
        {
            if (def == null) return "";
            string tier = def.Rarity == RelicRarity.Boss ? "首领"
                        : def.Rarity == RelicRarity.Rare ? "稀有" : "普通";
            return "<size=26><b>" + def.Name + "</b></size>　（" + tier + "遗物）\n" + def.Desc;
        }

        private static void Hook(GameObject chip, RelicDef def, Action<RelicDef> onEnter, Action onExit)
        {
            var trg = chip.GetComponent<EventTrigger>();
            if (trg == null) trg = chip.AddComponent<EventTrigger>();
            trg.triggers.Clear();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (onEnter != null) onEnter(def); });
            trg.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { if (onExit != null) onExit(); });
            trg.triggers.Add(exit);
        }

        private static Color RarityColor(RelicRarity r)
        {
            switch (r)
            {
                case RelicRarity.Boss: return new Color(0.83f, 0.68f, 0.36f, 1f);   // 金
                case RelicRarity.Rare: return new Color(0.64f, 0.74f, 0.86f, 1f);   // 青
                default: return new Color(0.86f, 0.82f, 0.74f, 1f);                 // 米
            }
        }
    }
}
