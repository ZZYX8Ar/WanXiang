// ============================================================================
//  万相 · 道痕面板（S2）
//  ---------------------------------------------------------------------------
//  仓库列表（全部 7 条，未获得的置灰）+ 详情 + 装备/卸下 + 分享码导出。
//  prefab 由 DaoMarkPanelBuilder 生成；控件用 transform.Find 取（与 prefab 解耦）。
//
//  交互：
//    · 点行 → 右侧详情（名称/星级/效果/来源阈值）
//    · Btn_Equip：未装备 → 装到第一个空槽；已装备 → 卸下（文案随之切换）
//    · Btn_Copy → GUIUtility.systemCopyBuffer = ExportCode()
//  ⛔ 不做导入输入框（S3）；不做删除（道痕只升不降）。
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
    [UIPanel("Panel_DaoMark", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class DaoMarkPanel : UIPanelBase
    {
        private RectTransform _content;
        private GameObject _rowTemplate;
        private TMP_Text _tmpCount, _tmpDetailName, _tmpDetailStar, _tmpDetailDesc, _tmpDetailSource, _tmpCode;
        private Button _btnEquip, _btnCopy;
        private TMP_Text _slot0, _slot1;

        private readonly List<DaoMarkDef> _marks = new List<DaoMarkDef>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private int _selected = -1;

        protected override void OnCreate() { }

        protected override UniTask OnOpenAsync(object payload)
        {
            WanXiang.Modules.UI.DataTables.EnsureInstalled();
            Bind();
            Refresh();
            return UniTask.CompletedTask;
        }

        private void Bind()
        {
            _content = Find<RectTransform>("Scroll_Grid/Viewport/Content");
            _rowTemplate = Find<Transform>("Scroll_Grid/Viewport/Content/Row_Dao_Template")?.gameObject;
            _tmpCount = Find<TMP_Text>("Root_TopBar/Tmp_Count");
            _tmpDetailName = Find<TMP_Text>("Root_Right/Tmp_DetailName");
            _tmpDetailStar = Find<TMP_Text>("Root_Right/Tmp_DetailStar");
            _tmpDetailDesc = Find<TMP_Text>("Root_Right/Tmp_DetailDesc");
            _tmpDetailSource = Find<TMP_Text>("Root_Right/Tmp_DetailSource");
            _tmpCode = Find<TMP_Text>("Root_Share/Tmp_Code");
            _btnEquip = Find<Button>("Root_Right/Btn_Equip");
            _btnCopy = Find<Button>("Root_Share/Btn_Copy");
            _slot0 = Find<TMP_Text>("Root_Right/Root_Equipped/Slot_0/Tmp_SlotName");
            _slot1 = Find<TMP_Text>("Root_Right/Root_Equipped/Slot_1/Tmp_SlotName");

            Hook("Root_TopBar/Btn_Close", CloseSelf);
            if (_btnEquip != null) _btnEquip.onClick.AddListener(OnEquipClicked);
            if (_btnCopy != null) _btnCopy.onClick.AddListener(OnCopyClicked);
            Hook("Root_Share/Btn_Import", OnImportClicked);
        }

        private T Find<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private void Hook(string path, System.Action action)
        {
            var btn = Find<Button>(path);
            if (btn == null) { Debug.LogWarning("[DaoMark] 缺按钮：" + path); return; }
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }

        // ------------------------------------------------------------ 刷新
        private void Refresh()
        {
            _marks.Clear();
            var all = DaoMarkRules.All;
            for (int i = 0; i < all.Count; i++) _marks.Add(all[i]);

            for (int i = 0; i < _rows.Count; i++) if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
            if (_rowTemplate != null) _rowTemplate.SetActive(false);
            if (_content == null) return;

            for (int i = 0; i < _marks.Count; i++) BuildRow(i);
            UpdateCount();
            UpdateEquippedSlots();
            Select(-1);      // 详情清空
        }

        private void BuildRow(int index)
        {
            if (_rowTemplate == null) return;
            var go = Instantiate(_rowTemplate, _content);
            go.name = "Row_" + index;
            go.SetActive(true);
            _rows.Add(go);

            var d = _marks[index];
            int star = DaoMarkPrefs.StarOf(d.Id);
            SetText(go, "Tmp_Name", d.Name);
            SetText(go, "Tmp_Star", StarString(star));

            var lk = go.transform.Find("Tmp_Locked");
            if (lk != null) lk.gameObject.SetActive(star <= 0);

            var btn = go.transform.Find("Btn_Pick")?.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = true;
                int captured = index;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Select(captured));
            }
            // 整行可点：行自身加 Button 更好，但模板里没放——用文本/图标区域即可，
            // 上面 Btn_Pick 仅在模板有该节点时生效。
        }

        private void Select(int index)
        {
            _selected = index;
            if (index < 0 || index >= _marks.Count)
            {
                if (_tmpDetailName != null) _tmpDetailName.text = "—";
                if (_tmpDetailStar != null) _tmpDetailStar.text = "—";
                if (_tmpDetailDesc != null) _tmpDetailDesc.text = "选择左侧道痕查看详情";
                if (_tmpDetailSource != null) _tmpDetailSource.text = "—";
                if (_btnEquip != null) _btnEquip.interactable = false;
                return;
            }

            var d = _marks[index];
            int star = DaoMarkPrefs.StarOf(d.Id);
            if (_tmpDetailName != null) _tmpDetailName.text = d.Name;
            if (_tmpDetailStar != null) _tmpDetailStar.text = StarString(star);
            if (_tmpDetailDesc != null)
                _tmpDetailDesc.text = string.IsNullOrEmpty(d.EffectDesc) ? "—" : d.EffectDesc;
            if (_tmpDetailSource != null)
                _tmpDetailSource.text = "来源：" + SourceText(d.Source) +
                    "　阈值 " + d.T1 + " / " + d.T2 + " / " + d.T3;

            if (_btnEquip != null)
            {
                _btnEquip.interactable = star > 0;                    // 未获得的不能装备
                var label = _btnEquip.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = IsEquipped(d.Id) ? "卸下" : "装备";
            }
        }

        private void OnEquipClicked()
        {
            if (_selected < 0 || _selected >= _marks.Count) return;
            var d = _marks[_selected];
            if (DaoMarkPrefs.StarOf(d.Id) <= 0) return;

            if (IsEquipped(d.Id))
            {
                for (int s = 0; s < 2; s++)
                    if (DaoMarkPrefs.EquippedAt(s) == d.Id) DaoMarkPrefs.SetEquipped(s, "");
                Debug.Log("[DaoMark] 卸下：" + d.Name);
            }
            else
            {
                int slot = -1;
                for (int s = 0; s < 2; s++) if (string.IsNullOrEmpty(DaoMarkPrefs.EquippedAt(s))) { slot = s; break; }
                if (slot < 0)
                {
                    if (_tmpDetailDesc != null) _tmpDetailDesc.text = "两个装备槽都满了，先卸下一条。";
                    return;
                }
                DaoMarkPrefs.SetEquipped(slot, d.Id);
                Debug.Log("[DaoMark] 装备到槽 " + slot + "：" + d.Name);
            }
            UpdateEquippedSlots();
            Select(_selected);      // 刷新按钮文案
        }

        /// <summary>S3：从剪贴板读取分享码导入（避免 prefab 里再摆 TMP_InputField）。</summary>
        private void OnImportClicked()
        {
            string code = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(code))
            {
                if (_tmpDetailDesc != null) _tmpDetailDesc.text = "剪贴板是空的——先复制一条分享码。";
                return;
            }
            int n = DaoMarkPrefs.ImportCode(code);
            if (n < 0)
            {
                if (_tmpDetailDesc != null) _tmpDetailDesc.text = "代码无效（格式不对或包含未知道痕）。";
                return;
            }
            if (_tmpDetailDesc != null) _tmpDetailDesc.text = "导入成功：" + n + " 条（重复的取最高星）。";
            Debug.Log("[DaoMark] 导入 " + n + " 条道痕：" + code);
            Refresh();
        }

        private void OnCopyClicked()
        {
            string code = DaoMarkPrefs.ExportCode();
            if (string.IsNullOrEmpty(code))
            {
                if (_tmpCode != null) _tmpCode.text = "没有可分享的道痕";
                return;
            }
            GUIUtility.systemCopyBuffer = code;
            if (_tmpCode != null) _tmpCode.text = code;
            Debug.Log("[DaoMark] 分享码已复制：" + code);
        }

        // ------------------------------------------------------------ 小工具
        private void UpdateCount()
        {
            if (_tmpCount != null)
                _tmpCount.text = DaoMarkPrefs.Count + " / " + _marks.Count;
        }

        private void UpdateEquippedSlots()
        {
            string e0 = DaoMarkPrefs.EquippedAt(0);
            string e1 = DaoMarkPrefs.EquippedAt(1);
            if (_slot0 != null) _slot0.text = SlotText(e0);
            if (_slot1 != null) _slot1.text = SlotText(e1);
        }

        private static string SlotText(string id)
        {
            if (string.IsNullOrEmpty(id)) return "空槽位";
            var d = DaoMarkRules.For(id);
            return (d != null ? d.Name : id) + " " + StarString(DaoMarkPrefs.StarOf(id));
        }

        private static bool IsEquipped(string id)
            => DaoMarkPrefs.EquippedAt(0) == id || DaoMarkPrefs.EquippedAt(1) == id;

        private static string StarString(int star)
        {
            if (star <= 0) return "—";     // "未获得"已由右侧 Tmp_Locked 角标表达，不重复
            return star >= 3 ? "★★★" : star == 2 ? "★★☆" : "★☆☆";
        }

        private static string SourceText(DaoMarkSource s)
        {
            switch (s)
            {
                case DaoMarkSource.Reactions: return "触发元素反应";
                case DaoMarkSource.SameElementActs: return "纯色队通关幕数";
                case DaoMarkSource.Deaths: return "零阵亡";
                case DaoMarkSource.FastWins: return "速通场次";
                case DaoMarkSource.BossKills: return "击败首领";
                case DaoMarkSource.Captures: return "捕获异兽";
                case DaoMarkSource.MaxHit: return "单次最大伤害";
                default: return "—";
            }
        }

        private static void SetText(GameObject row, string path, string text)
        {
            var t = row.transform.Find(path)?.GetComponent<TMP_Text>();
            if (t != null) t.text = text ?? "";
        }
    }
}
