// ============================================================================
//  Panel_Meta —— 局外养成 · 异兽培养
//  ---------------------------------------------------------------------------
//  布局（仿图鉴 Panel_Codex，但详情【常驻右侧】）：
//    顶栏：墨铊 / 累计局数 / 最远幕
//    页签：全部 + 木火土金水
//    左侧：异兽列表（点选）
//    右侧：立绘 / 名称 / 五行·定位·品阶 / 属性(含升级预览) / 技能×3 / 特性
//          [升级 · N 墨铊]  [进化 · 条件]
//
//  ⚠ 祭坛已移除：它"全局五行 +1.6%/级"和"异兽升级"功能重复，
//    保留异兽培养（更有养成感、且绑定具体异兽）。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Meta", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言。
    public sealed class MetaPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;         // Tmp_Title
        [SerializeField] private TMP_Text _tmpStatus;        // Tmp_Status（墨铊/统计）
        [SerializeField] private Button[] _tabBtns;          // Tab_0..Tab_5（全部+五行）

        [SerializeField] private ScrollRect _scrollList;     // Scroll_Grid
        [SerializeField] private RectTransform _listContent; // Content
        [SerializeField] private RectTransform _itemTemplate;// Item_Beast（模板，默认隐藏）

        [SerializeField] private GameObject _rootDetail;     // Root_Detail
        [SerializeField] private Image _imgBig;              // Img_BeastBig
        [SerializeField] private TMP_Text _tmpName;          // Tmp_Name
        [SerializeField] private TMP_Text _tmpClass;         // Tmp_Class
        [SerializeField] private TMP_Text _tmpStats;         // Tmp_Stats
        [SerializeField] private TMP_Text _tmpSkill1;        // Tmp_Skill1
        [SerializeField] private TMP_Text _tmpSkill2;        // Tmp_Skill2
        [SerializeField] private TMP_Text _tmpSkill3;        // Tmp_Skill3
        [SerializeField] private TMP_Text _tmpTrait;         // Tmp_Trait
        [SerializeField] private TMP_Text _tmpEvolveCond;    // Tmp_EvolveCond
        [SerializeField] private Button _btnUpgrade;         // Btn_Upgrade
        [SerializeField] private Button _btnEvolve;          // Btn_Evolve
        [SerializeField] private Button _btnBack;            // Btn_Back

        private const int MaxLevel = 5;
        private const float PerLevelBonus = 0.016f;   // 每级 +1.6%（5 级 = +8%，与 GDD 数值封顶一致）

        /// <summary>升级单价（墨铊）：第 N 级 = 3 + N×2。</summary>
        private static int UpgradeCost(int curLevel) => 3 + curLevel * 2;

        private readonly List<BeastDef> _shown = new List<BeastDef>();
        private readonly List<RectTransform> _items = new List<RectTransform>();
        private int _tab;            // 0=全部，1..5=五行
        private int _selected = -1;

        protected override void OnCreate()
        {
            for (int i = 0; i < (_tabBtns != null ? _tabBtns.Length : 0); i++)
            {
                int idx = i;
                if (_tabBtns[i] != null) _tabBtns[i].onClick.AddListener(() => { _tab = idx; _selected = 0; Refresh(); });
            }
            if (_btnUpgrade != null) _btnUpgrade.onClick.AddListener(OnUpgradeClicked);
            if (_btnEvolve != null) _btnEvolve.onClick.AddListener(OnEvolveClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(CloseSelf);
            if (_itemTemplate != null) _itemTemplate.gameObject.SetActive(false);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Refresh();
            return UniTask.CompletedTask;
        }

        private void Refresh()
        {
            RenderTop();
            RenderList();
            RenderDetail();
        }

        // ---------------------------------------------------------------- 顶栏

        private void RenderTop()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (_tmpStatus != null)
                _tmpStatus.text = meta == null ? "墨铊 0" :
                    ("墨铊 " + meta.Ink);   // 累计局数/最远幕 → 以后移到独立的【历程面板】
        }

        // ---------------------------------------------------------------- 列表

        private void RenderList()
        {
            if (_listContent == null || _itemTemplate == null) return;

            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i] != null) { _items[i].gameObject.SetActive(false); Destroy(_items[i].gameObject); }
            _items.Clear();
            _shown.Clear();

            var all = AllBeasts();
            if (all == null) return;

            for (int i = 0; i < all.Length; i++)
                if (_tab <= 0 || ElementMatches(all[i].Element, _tab)) _shown.Add(all[i]);

            for (int i = 0; i < _shown.Count; i++)
            {
                var rt = Instantiate(_itemTemplate, _listContent);
                rt.gameObject.SetActive(true);
                rt.name = "Item_" + _shown[i].Id;
                rt.anchoredPosition = new Vector2(8f, -8f - i * 104f);
                rt.sizeDelta = new Vector2(-16f, 96f);

                var nameT = rt.Find("Tmp_ItemName") != null ? rt.Find("Tmp_ItemName").GetComponent<TMP_Text>() : null;
                if (nameT != null) nameT.text = _shown[i].DisplayName;
                var lvT = rt.Find("Tmp_ItemLevel") != null ? rt.Find("Tmp_ItemLevel").GetComponent<TMP_Text>() : null;
                if (lvT != null) lvT.text = "Lv." + LevelOf(_shown[i].Id);

                var btn = rt.GetComponent<Button>();
                if (btn == null) btn = rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = rt.GetComponent<Image>();
                int idx = i;
                btn.onClick.AddListener(() => { _selected = idx; RenderDetail(); });

                _items.Add(rt);
            }

            if (_selected >= _shown.Count) _selected = _shown.Count - 1;
            _listContent.sizeDelta = new Vector2(_listContent.sizeDelta.x, _shown.Count * 104f + 16f);
        }

        // ---------------------------------------------------------------- 详情

        private void RenderDetail()
        {
            if (_rootDetail == null) return;

            if (_selected < 0 || _selected >= _shown.Count)
            {
                _rootDetail.SetActive(false);
                return;
            }
            _rootDetail.SetActive(true);

            var b = _shown[_selected];
            var meta = WanXiang.Meta.MetaStore.Ensure();
            int lv = LevelOf(b.Id);
            float mul = 1f + lv * PerLevelBonus;
            bool evolved = IsEvolved(b.Id);

            if (_tmpName != null) _tmpName.text = b.DisplayName + (evolved ? " · 觉醒" : "");
            if (_tmpClass != null)
                _tmpClass.text = ElementCn(b.Element) + " · " + RoleCn(b.Role) + " · " + RarityCn(b.Rarity);

            if (_tmpStats != null)
            {
                // ★ 属性必须用与战斗同一套公式（BattleConfig.ApplyPlaceholderStats：
                //   职业基准 × 稀有度乘数），否则显示的是内容目录里的 0（用户反馈"看不到生命攻击"）。
                var statDef = b.Clone();
                WanXiang.Battle.Core.BattleConfig.Default.ApplyPlaceholderStats(statDef);

                int hp = Mathf.RoundToInt(statDef.BaseHp * mul);
                int atk = Mathf.RoundToInt(statDef.BaseAtk * mul);
                int nextLv = Mathf.Min(MaxLevel, lv + 1);
                int hpNext = Mathf.RoundToInt(statDef.BaseHp * (1f + nextLv * PerLevelBonus));
                int atkNext = Mathf.RoundToInt(statDef.BaseAtk * (1f + nextLv * PerLevelBonus));
                string head = "生命 " + hp + "　攻击 " + atk +
                              "　防御 " + statDef.BaseDef + "　速度 " + statDef.BaseSpeed + "\n";
                _tmpStats.text = lv >= MaxLevel
                    ? (head + "等级 " + lv + "/" + MaxLevel + "（已满）")
                    : (head + "升级后：生命 " + hpNext + "　攻击 " + atkNext +
                       "　（Lv." + lv + " → Lv." + nextLv + "，+1.6%）");
            }

            if (_tmpSkill1 != null) _tmpSkill1.text = SkillText(b, 0, "①");
            if (_tmpSkill2 != null) _tmpSkill2.text = SkillText(b, 1, "②");
            if (_tmpSkill3 != null)
                _tmpSkill3.text = evolved
                    ? SkillText(b, 2, "③")
                    : "③ ―― 觉醒后开放特殊技能槽（可装备探索中收集的技能）";

            if (_tmpTrait != null)
                _tmpTrait.text = "特性：" + (string.IsNullOrEmpty(b.Trait.Name) ? "—" : b.Trait.Name);

            if (_tmpEvolveCond != null)
                _tmpEvolveCond.text = evolved ? "已觉醒（可更换第 3 技能）" : EvolveConditionText(b);

            int cost = UpgradeCost(lv);
            if (_btnUpgrade != null)
            {
                _btnUpgrade.interactable = lv < MaxLevel && meta != null && meta.Ink >= cost;
                var l = _btnUpgrade.transform.Find("Tmp_Label") != null
                        ? _btnUpgrade.transform.Find("Tmp_Label").GetComponent<TMP_Text>() : null;
                if (l != null) l.text = lv >= MaxLevel ? "已满级" : ("升级 · " + cost + " 墨铊");
            }
            if (_btnEvolve != null)
            {
                _btnEvolve.interactable = !evolved;
                var l = _btnEvolve.transform.Find("Tmp_Label") != null
                        ? _btnEvolve.transform.Find("Tmp_Label").GetComponent<TMP_Text>() : null;
                if (l != null) l.text = evolved ? "已觉醒" : "进化";
            }
        }

        // ---------------------------------------------------------------- 交互

        private void OnUpgradeClicked()
        {
            if (_selected < 0 || _selected >= _shown.Count) return;
            var meta = WanXiang.Meta.MetaStore.Ensure();
            var b = _shown[_selected];
            if (meta == null) return;

            int lv = LevelOf(b.Id);
            int cost = UpgradeCost(lv);
            if (lv >= MaxLevel || meta.Ink < cost) return;

            meta.Ink -= cost;
            SetLevel(b.Id, lv + 1);
            WanXiang.Meta.MetaStore.Save();
            Debug.Log("[MetaPanel] 升级 " + b.Id + " → Lv." + (lv + 1) + "（墨铊剩 " + meta.Ink + "）");
            Refresh();
        }

        private void OnEvolveClicked()
        {
            if (_selected < 0 || _selected >= _shown.Count) return;
            var b = _shown[_selected];
            if (IsEvolved(b.Id)) return;
            // ⚠ 进化（材料 + 剧情条件）待阶段 ③-4 接上数据层
            Debug.LogWarning("[MetaPanel] 进化「" + b.DisplayName + "」尚未实现：" + EvolveConditionText(b));
        }

        // ------------------------------------------------- 数据层（MetaStore）

        private static int LevelOf(string id)
        {
            var m = WanXiang.Meta.MetaStore.Current;
            if (m == null) return 0;
            int i = m.BeastIds.IndexOf(id);
            return i >= 0 ? m.BeastLevels[i] : 0;
        }

        private static void SetLevel(string id, int level)
        {
            var m = WanXiang.Meta.MetaStore.Current;
            if (m == null) return;
            int i = m.BeastIds.IndexOf(id);
            if (i < 0)
            {
                m.BeastIds.Add(id);
                m.BeastLevels.Add(level);
                m.BeastEvolved.Add(false);
            }
            else m.BeastLevels[i] = level;
        }

        private static bool IsEvolved(string id)
        {
            var m = WanXiang.Meta.MetaStore.Current;
            if (m == null) return false;
            int i = m.BeastIds.IndexOf(id);
            return i >= 0 && i < m.BeastEvolved.Count && m.BeastEvolved[i];
        }

        /// <summary>该异兽的局外等级加成（供战斗读取）。</summary>
        public static float LevelBonusOf(string id) => LevelOf(id) * PerLevelBonus;

        // ---------------------------------------------------------------- 小工具

        private static BeastDef[] AllBeasts()
        {
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) return null;
            return WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
        }

        private static bool ElementMatches(WanXiang.Battle.Core.Element e, int tab)
        {
            switch (tab)
            {
                case 1: return e == WanXiang.Battle.Core.Element.Wood;
                case 2: return e == WanXiang.Battle.Core.Element.Fire;
                case 3: return e == WanXiang.Battle.Core.Element.Earth;
                case 4: return e == WanXiang.Battle.Core.Element.Metal;
                case 5: return e == WanXiang.Battle.Core.Element.Water;
                default: return true;
            }
        }

        /// <summary>技能一行：① 名称（类型·冷却）—— 描述。</summary>
        private static string SkillText(BeastDef b, int slot, string index)
        {
            var arr = b.AllSkills;
            if (arr == null || slot >= arr.Length) return index + " ――";
            var sk = arr[slot];
            if (sk == null) return index + " ――";

            string typeCn;
            switch (sk.Type)
            {
                case SkillType.Basic: typeCn = "普攻"; break;
                case SkillType.Active: typeCn = "战技"; break;
                case SkillType.Ultimate: typeCn = "终结技"; break;
                default: typeCn = sk.Type.ToString(); break;
            }
            string cd = sk.Cd > 0 ? ("·CD" + sk.Cd) : "";
            string desc = string.IsNullOrEmpty(sk.Description) ? "" : ("　" + sk.Description);
            return index + " " + sk.Name + "（" + typeCn + cd + "）" + desc;
        }

        private static string EvolveConditionText(BeastDef b)
            => "进化条件：精魄 ×2 + 8 墨铊（精魄在探索中随机掉落）";

        private static string ElementCn(WanXiang.Battle.Core.Element e)
        {
            switch (e)
            {
                case WanXiang.Battle.Core.Element.Wood: return "木";
                case WanXiang.Battle.Core.Element.Fire: return "火";
                case WanXiang.Battle.Core.Element.Earth: return "土";
                case WanXiang.Battle.Core.Element.Metal: return "金";
                case WanXiang.Battle.Core.Element.Water: return "水";
                default: return "无";
            }
        }

        private static string RoleCn(RoleType r) => r.ToString();

        private static string RarityCn(WanXiang.Battle.Core.Rarity r)
        {
            switch (r)
            {
                case WanXiang.Battle.Core.Rarity.Legend: return "传说";
                case WanXiang.Battle.Core.Rarity.Epic: return "史诗";
                default: return "稀有";
            }
        }
    }
}
