// ============================================================================
//  Panel_Meta —— 局外成长（境 · 五条成长线）
//  v1.2 实现：五条轨道按 GDD 第 8 章渲染。
//    L1 血脉 / L2 灵魄 / L3 图鉴 / L5 起手 —— 显示进度，功能待接（灰置说明）
//    L4 祭坛 —— **本轮实现**：五行各 5 级、每级 +1.6%、总封顶 +8%（GDD 8.3），
//               用灵卵升级（3 + 级 × 2），永久生效（走 BattleRequest.PlayerMul）。
//  核心原则（GDD 8）：数值类局外增益封顶 +8% —— 真正变强靠局内构筑。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Meta", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class MetaPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpRealm;           // Tmp_RealmText 当前境·劫
        [BindArray("Tmp_TrackName_{0}", 5)]
        [SerializeField] private TMP_Text[] _tmpTrackNames;    // 五条轨道名
        [BindArray("Track_{0}", 5)]
        [SerializeField] private RectTransform[] _trackNodes;  // 五条轨道容器（节点圆点由代码生成）
        [SerializeField] private RectTransform _rewardItemTemplate;  // Item_Reward（模板，默认隐藏）
        [SerializeField] private TMP_Text _tmpCap;             // Tmp_MetaCap 数值类增益封顶 +8%

        private static readonly string[] TrackDesc =
        {
            "血脉 · 孵穴扩容宿主池（待接）",
            "灵魄 · 灵魂池扩容（待接）",
            "图鉴 · 每 5 条解锁 1 个被动（待接）",
            "祭坛 · 五行各 5 级，每级 +1.6%（可用灵卵升级）",
            "起手 · 开局条件解锁（待接）",
        };
        private static readonly int[] AltarCostBase = { 3, 5, 7, 9, 11 };   // 升到第 N 级的花费

        protected override void OnCreate()
        {
            // TODO(交互): 领取奖励由代码生成的 Reward 按钮触发
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Render();
            return UniTask.CompletedTask;
        }

        private void Render()
        {
            var run = WanXiang.Run.RunSave.Current;

            // ★★ 局外养成走 MetaStore（跨局持久），不再读 run 的局内字段：
            //    run.RealmText 是废弃字段；run.MetaAltar/run.Eggs 都是局内的，每局重置 = 养成无意义。
            var meta = WanXiang.Meta.MetaStore.Ensure();

            if (_tmpRealm != null)
            {
                _tmpRealm.text = meta == null
                    ? "局外修行"
                    : ("局外修行 · 累计 " + meta.RunsPlayed + " 局 · 最远第 " +
                       meta.BestActReached + " 幕 · 通关 " + meta.RunsCompleted + " 次");
            }
            if (_tmpCap != null)
            {
                _tmpCap.text = (meta != null ? ("墨铊 " + meta.Ink + "　｜　") : "")
                    + "数值类局外增益封顶 +8% —— 真正的变强在局内构筑";
            }

            if (_tmpTrackNames != null && _trackNodes != null)
            {
                for (int i = 0; i < _trackNodes.Length && i < 5; i++)
                {
                    if (_tmpTrackNames[i] == null) continue;

                    // L4 祭坛（第 4 条）：可升级 —— 花【墨铊】升【局外】祭坛
                    if (i == 3)
                    {
                        float bonus = meta != null ? meta.AltarBonusTotal : 0f;
                        _tmpTrackNames[i].text = TrackDesc[i] + "　当前 +" +
                                                 (bonus * 100f).ToString("0.0") + "%";
                        BuildAltar(_trackNodes[i], meta);
                        continue;
                    }

                    // 其余四条：进度骨架（待接）
                    _tmpTrackNames[i].text = TrackDesc[i];
                    BuildPlaceholder(_trackNodes[i]);
                }
            }
        }

        /// <summary>
        /// 祭坛轨道：五行各一行（等级 + 升级按钮）。
        /// ★ 数据全部来自局外存档（MetaStore）：等级 AltarLevels、货币 Ink（墨铊）。
        ///   升级 = 花墨铊永久 +1.6%/级，跨局生效（GDD 8.3：五条全点封顶 +8%）。
        /// </summary>
        private void BuildAltar(RectTransform track, WanXiang.Meta.MetaState meta)
        {
            ClearChildren(track);
            if (meta == null) return;

            string[] elems = { "木", "火", "土", "金", "水" };
            for (int i = 0; i < 5; i++)
            {
                int lv = i < meta.AltarLevels.Length ? meta.AltarLevels[i] : 0;
                int cost = lv < AltarCostBase.Length ? AltarCostBase[lv] : 0;

                var row = new GameObject("Altar_" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(track, false);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -i * 64f);
                row.sizeDelta = new Vector2(-16f, 56f);

                var bg = row.gameObject.AddComponent<Image>();
                bg.color = lv >= 5 ? new Color(0.85f, 0.76f, 0.56f, 0.35f) : new Color(0.96f, 0.94f, 0.89f, 0.8f);

                var name = NewTmp(row, "Tmp", new Vector2(14f, 0f), new Vector2(-190f, 0f), 24,
                    TextAlignmentOptions.MidlineLeft);
                name.text = elems[i] + " · 等级 " + lv + "/5　（+" + (lv * 1.6f).ToString("0.0") + "%）";

                var btn = new GameObject("Btn_Up", typeof(RectTransform)).GetComponent<RectTransform>();
                btn.SetParent(row, false);
                btn.anchorMin = btn.anchorMax = new Vector2(1f, 0.5f);
                btn.pivot = new Vector2(1f, 0.5f);
                btn.anchoredPosition = new Vector2(-12f, 0f);
                btn.sizeDelta = new Vector2(180f, 44f);
                var bImg = btn.gameObject.AddComponent<Image>();
                bImg.color = lv >= 5 ? new Color(0.8f, 0.8f, 0.78f, 0.6f) : new Color(0.79f, 0.63f, 0.39f, 1f);
                var bBtn = btn.gameObject.AddComponent<Button>();
                bBtn.targetGraphic = bImg;
                var bTxt = NewTmp(btn, "Tmp", Vector2.zero, Vector2.one, 20, TextAlignmentOptions.Center);
                bTxt.color = new Color(0.16f, 0.13f, 0.09f, 1f);
                bTxt.text = lv >= 5 ? "已满级" : ("升级（" + cost + " 墨铊）");
                bBtn.interactable = lv < 5 && meta.Ink >= cost;

                int slot = i;
                bBtn.onClick.AddListener(() =>
                {
                    var m = WanXiang.Meta.MetaStore.Ensure();
                    if (m == null) return;
                    int cur = slot < m.AltarLevels.Length ? m.AltarLevels[slot] : 0;
                    int c = cur < AltarCostBase.Length ? AltarCostBase[cur] : 0;
                    if (cur >= 5 || m.Ink < c) return;
                    if (m.UpgradeAltar(slot, m.Ink))     // 内部扣墨铊并 +1 级
                    {
                        WanXiang.Meta.MetaStore.Save();
                        UnityEngine.Debug.Log("[MetaPanel] 祭坛升级：" + elems[slot] +
                                              " → " + m.AltarLevels[slot] + " 级（墨铊剩 " + m.Ink +
                                              "，总加成 " + (m.AltarBonusTotal * 100f).ToString("0.0") + "%）");
                        Render();
                    }
                });
            }
        }

        /// <summary>其余四条轨道的占位行。</summary>
        private void BuildPlaceholder(RectTransform track)
        {
            ClearChildren(track);
            var row = new GameObject("Pending", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(track, false);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -30f);
            row.sizeDelta = new Vector2(-16f, 44f);
            var t = NewTmp(row, "Tmp", new Vector2(14f, 0f), new Vector2(-14f, 0f), 20,
                TextAlignmentOptions.MidlineLeft);
            t.text = "该成长线将在孵蛋 / 图鉴 / 开局解锁系统落地后开放";
            t.color = new Color(0.55f, 0.52f, 0.46f, 1f);
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var c = parent.GetChild(i).gameObject;
                c.SetActive(false);
                Object.Destroy(c);         // 先失活再销毁（同帧不重叠）
            }
        }

        private static TMP_Text NewTmp(RectTransform parent, string name,
                                       Vector2 offMin, Vector2 offMax, float size,
                                       TextAlignmentOptions align)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }
    }
}
