// ============================================================================
//  万相 · 技能槽面板（S4）
//  ---------------------------------------------------------------------------
//  左：兽列表（局外 MetaState.BeastIds）  右：4 个技能槽 + 候选弹层。
//  槽位规则：普攻/奥义/觉醒锁死；战记槽可换（SwapSlotCount 由品级决定；
//  当前池数据只支持 1 个战记槽，第二战记槽 UI 留位但禁用）。
//
//  prefab 由 SkillLoadoutPanelBuilder 生成；控件用 transform.Find 取（与 prefab 解耦）。
//  ⛔ 不碰战斗组装（BattleRequestFactory.ApplyLoadout 已在 B3-e 接好）。
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
    [UIPanel("Panel_SkillLoadout", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class SkillLoadoutPanel : UIPanelBase
    {
        private RectTransform _beastContent;
        private GameObject _rowBeastTemplate;
        private RectTransform _pickContent;
        private GameObject _rowSkillTemplate;
        private GameObject _picker;
        private TMP_Text _tmpBeastName;
        private readonly TMP_Text[] _tmpSkillNames = new TMP_Text[4];
        private readonly TMP_Text[] _tmpLocks = new TMP_Text[4];
        private Button _btnChange;

        private readonly List<string> _beasts = new List<string>();
        private readonly List<GameObject> _beastRows = new List<GameObject>();
        private readonly List<GameObject> _pickRows = new List<GameObject>();
        private string _currentBeast = "";

        protected override void OnCreate() { }

        protected override UniTask OnOpenAsync(object payload)
        {
            WanXiang.Modules.UI.DataTables.EnsureInstalled();
            Bind();
            BuildBeastList();
            return UniTask.CompletedTask;
        }

        private void Bind()
        {
            _beastContent = Find<RectTransform>("Scroll_Beasts/Viewport/Content");
            _rowBeastTemplate = Find<Transform>("Scroll_Beasts/Viewport/Content/Row_Beast_Template")?.gameObject;
            _pickContent = Find<RectTransform>("Root_Picker/Scroll_Pick/Viewport/Content");
            _rowSkillTemplate = Find<Transform>("Root_Picker/Scroll_Pick/Viewport/Content/Row_Skill_Template")?.gameObject;
            _picker = Find<Transform>("Root_Picker")?.gameObject;
            _tmpBeastName = Find<TMP_Text>("Root_Right/Tmp_BeastName");
            _btnChange = Find<Button>("Root_Right/Slot_1/Btn_Change");

            for (int i = 0; i < 4; i++)
            {
                _tmpSkillNames[i] = Find<TMP_Text>("Root_Right/Slot_" + i + "/Tmp_SkillName");
                _tmpLocks[i] = Find<TMP_Text>("Root_Right/Slot_" + i + "/Tmp_Lock");
            }

            Hook("Root_TopBar/Btn_Close", CloseSelf);
            if (_btnChange != null) _btnChange.onClick.AddListener(OpenPicker);
            Hook("Root_Picker/Btn_Cancel", ClosePicker);
        }

        private T Find<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private void Hook(string path, System.Action action)
        {
            var btn = Find<Button>(path);
            if (btn == null) { Debug.LogWarning("[SkillLoadout] 缺按钮：" + path); return; }
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }

        // ------------------------------------------------------------ 兽列表
        private void BuildBeastList()
        {
            _beasts.Clear();
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (meta != null && meta.BeastIds != null)
                for (int i = 0; i < meta.BeastIds.Count; i++)
                    if (!string.IsNullOrEmpty(meta.BeastIds[i])) _beasts.Add(meta.BeastIds[i]);

            for (int i = 0; i < _beastRows.Count; i++) if (_beastRows[i] != null) Destroy(_beastRows[i]);
            _beastRows.Clear();
            if (_rowBeastTemplate != null) _rowBeastTemplate.SetActive(false);
            if (_beastContent == null) return;

            for (int i = 0; i < _beasts.Count; i++) BuildBeastRow(i);
            bool empty = _beasts.Count == 0;
            // B5-4+：空态引导卡（builder 产物，缺失则退回旧文案）
            var guide = transform.Find("Root_Right/Empty_Guide");
            if (guide != null)
            {
                guide.gameObject.SetActive(empty);
                var right = transform.Find("Root_Right");
                for (int i = 0; i < 4; i++)
                {
                    var slotT = right != null ? right.Find("Slot_" + i) : null;
                    if (slotT != null) slotT.gameObject.SetActive(!empty);
                }
            }
            if (_beasts.Count > 0) SelectBeast(0);
            else if (_tmpBeastName != null) _tmpBeastName.text = "技能槽";   // 空态标签，不重复标题
        }

        private void BuildBeastRow(int index)
        {
            if (_rowBeastTemplate == null) return;
            var go = Instantiate(_rowBeastTemplate, _beastContent);
            go.name = "Row_" + index;
            go.SetActive(true);
            _beastRows.Add(go);

            string id = _beasts[index];
            BeastProfile prof;
            BeastTable.TryGet(id, out prof);
            SetText(go, "Tmp_Name", prof != null ? prof.Name : id);
            SetText(go, "Tmp_Element", prof != null ? ElementText(prof.Element) : "—");

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                int captured = index;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectBeast(captured));
            }
        }

        private void SelectBeast(int index)
        {
            if (index < 0 || index >= _beasts.Count) return;
            _currentBeast = _beasts[index];

            for (int i = 0; i < _beastRows.Count; i++)
            {
                if (_beastRows[i] == null) continue;
                var bg = _beastRows[i].GetComponent<Image>();
                if (bg != null) bg.color = i == index
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.72f);
            }

            RefreshSlots();
        }

        // ------------------------------------------------------------ 槽位
        private void RefreshSlots()
        {
            BeastProfile prof;
            BeastTable.TryGet(_currentBeast, out prof);
            if (_tmpBeastName != null) _tmpBeastName.text = prof != null ? prof.Name : _currentBeast;

            for (int slot = 0; slot < 4; slot++)
            {
                var ov = WanXiang.Campaign.SkillOverrideCatalog.Get(_currentBeast, slot);
                string baseName = ov != null && !string.IsNullOrEmpty(ov.Name) ? ov.Name : "—";

                if (_tmpSkillNames[slot] == null) continue;

                if (slot == 1)
                {
                    // 战记槽：玩家配置优先
                    string equipped = LoadoutPrefs.SkillOf(_currentBeast, 1);
                    var entry = string.IsNullOrEmpty(equipped) ? null : SkillSlotRules.For(equipped);
                    _tmpSkillNames[1].text = entry != null
                        ? entry.Name + "（已配置）"
                        : baseName;
                }
                else
                {
                    _tmpSkillNames[slot].text = baseName;
                }
            }

            // 觉醒槽：未进化显示"未解锁"（进化标记按下标对齐存在 MetaState.BeastEvolved）
            if (_tmpLocks[3] != null)
            {
                bool evolved = false;
                var meta = WanXiang.Meta.MetaStore.Ensure();
                if (meta != null && meta.BeastIds != null)
                {
                    int i = meta.BeastIds.IndexOf(_currentBeast);
                    evolved = i >= 0 && i < meta.BeastEvolved.Count && meta.BeastEvolved[i];
                }
                _tmpLocks[3].text = evolved ? "固定" : "未解锁";
            }
        }

        // ------------------------------------------------------------ 候选弹层
        private void OpenPicker()
        {
            if (string.IsNullOrEmpty(_currentBeast) || _picker == null) return;
            BeastProfile prof;
            BeastTable.TryGet(_currentBeast, out prof);
            Element el = prof != null ? prof.Element : Element.None;

            var candidates = SkillSlotRules.Candidates(el);
            for (int i = 0; i < _pickRows.Count; i++) if (_pickRows[i] != null) Destroy(_pickRows[i]);
            _pickRows.Clear();
            if (_rowSkillTemplate != null) _rowSkillTemplate.SetActive(false);
            if (_pickContent == null) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                var entry = candidates[i];
                var go = Instantiate(_rowSkillTemplate, _pickContent);
                go.name = "Pick_" + i;
                go.SetActive(true);
                _pickRows.Add(go);
                SetText(go, "Tmp_PickName", entry.Name);
                SetText(go, "Tmp_PickDesc", entry.Desc);

                var btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    var capturedId = entry.Id;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        LoadoutPrefs.SetSkill(_currentBeast, 1, capturedId);
                        ClosePicker();
                        RefreshSlots();
                        Debug.Log("[SkillLoadout] 已配置：" + _currentBeast + " → " + capturedId);
                    });
                }
            }
            _picker.SetActive(true);
        }

        private void ClosePicker()
        {
            if (_picker != null) _picker.SetActive(false);
        }

        // ------------------------------------------------------------ 小工具
        private static void SetText(GameObject row, string path, string text)
        {
            var t = row.transform.Find(path)?.GetComponent<TMP_Text>();
            if (t != null) t.text = text ?? "";
        }

        private static string ElementText(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Earth: return "土";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                default: return "无";
            }
        }
    }
}
