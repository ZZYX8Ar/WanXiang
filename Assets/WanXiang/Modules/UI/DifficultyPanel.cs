// ============================================================================
//  万相 · 难度选择弹窗（参考明日方舟肉鸽：点难度弹出列表，逐档写清数值）
//  选分队面板只留一行摘要 + "查看全部难度"入口；详细数值全在这。
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
    [UIPanel("Panel_Difficulty", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class DifficultyPanel : UIPanelBase
    {
        /// <summary>选中回传：关闭后由选分队面板读取并清理。</summary>
        public static int PendingLevel = -1;

        private readonly List<GameObject> _rows = new List<GameObject>(20);
        private Transform _content;
        private GameObject _rowTemplate;
        private TMP_Text _tmpUnlockHint;
        private int _current;

        protected override void OnCreate() { }

        private T Find<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private void Hook(string path, System.Action action)
        {
            var btn = Find<Button>(path);
            if (btn == null) { Debug.LogWarning("[Difficulty] 缺按钮：" + path); return; }
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }

        private static void SetText(GameObject row, string path, string v)
        {
            var t = row.transform.Find(path);
            if (t == null) return;
            var x = t.GetComponent<TMP_Text>();
            if (x != null) x.text = v;
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            WanXiang.Modules.UI.DataTables.EnsureInstalled();
            _content = Find<Transform>("Scroll_List/Viewport/Content");
            _rowTemplate = Find<Transform>("Scroll_List/Viewport/Content/Row_Diff_Template")?.gameObject;
            _tmpUnlockHint = Find<TMP_Text>("Tmp_UnlockHint");
            _current = payload is int lv ? lv : SquadSelectPanel.PendingDifficulty;
            Hook("Root_TopBar/Btn_Close", CloseSelf);
            BuildList();
            return UniTask.CompletedTask;
        }

        private void BuildList()
        {
            if (_content == null || _rowTemplate == null) return;
            for (int i = 0; i < _rows.Count; i++) if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
            _rowTemplate.SetActive(false);

            int unlocked = ProgressionRules.MaxUnlockedChallenge();
            if (_tmpUnlockHint != null)
                _tmpUnlockHint.text = "通关 1 次解锁下一档；当前已解锁 N1 – N" + unlocked;

            var all = DifficultyTable.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                bool locked = d.Level > unlocked;
                var go = Instantiate(_rowTemplate, _content);
                go.name = "Row_N" + d.Level;
                go.SetActive(true);
                _rows.Add(go);

                bool isCurrent = d.Level == _current;
                var img = go.GetComponent<Image>();
                if (img != null)
                    img.color = locked ? new Color(0.80f, 0.79f, 0.76f, 0.9f)
                              : isCurrent ? new Color(0.85f, 0.93f, 0.80f, 1f)
                              : Color.white;

                SetText(go, "Tmp_Level", "N" + d.Level);
                SetText(go, "Tmp_Title", string.IsNullOrEmpty(d.Desc) ? "数值提升" : d.Desc);
                SetText(go, "Tmp_Nums",
                    "生命×" + d.HpMul.ToString("0.00") + "　攻×" + d.AtkMul.ToString("0.00")
                    + "　防×" + d.DefMul.ToString("0.00") + "　词条 +" + d.ExtraTraits
                    + "　灵卵×" + d.EggMul.ToString("0.00"));
                SetText(go, "Tmp_RuleText", string.IsNullOrEmpty(d.NewRule)
                    ? "无新规则（本档仅数值提升）" : HumanRule(d.NewRule));
                SetText(go, "Tmp_State", locked ? "锁 · 通 " + Mathf.Max(0, d.Level - 1) + " 次"
                    : isCurrent ? "当前" : "已解锁");

                var btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    btn.interactable = !locked;
                    int lv = d.Level;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => Pick(lv));
                }
                var nameT = go.transform.Find("Tmp_Level")?.GetComponent<TMP_Text>();
                if (nameT != null) nameT.color = locked ? new Color(0.45f, 0.43f, 0.40f, 1f) : Color.black;
            }
        }

        private void Pick(int level)
        {
            PendingLevel = level;
            CloseSelf();
        }

        private static string HumanRule(string key)
        {
            switch (key)
            {
                case "elite_extra_add": return "精英位多 1 随从";
                case "boss_phase2": return "首领进入第二阶段";
                case "ai_aggressive": return "敌方按激进策略出招";
                case "turns_minus6": return "回合上限 −6";
                case "enemy_calamity": return "敌方带 1 个劫象";
                case "boss_shield_all": return "全部首领 +15% 且开场带护盾";
                default: return key;
            }
        }
    }
}
