// ============================================================================
//  UnitInspectPanel —— 战斗内单位查看面板（2026-10-26，崩铁式）
//  ---------------------------------------------------------------------------
//  双击战场单位时由 `BattlePanel` 打开；打开期间 `_halted = true`（战斗暂停），
//  关闭后恢复。数据全部来自帧快照（由 Stage 打包成 UnitInspectData）。
// ============================================================================
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    public sealed class UnitInspectPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpName;
        [SerializeField] private TMP_Text _tmpStats;
        [SerializeField] private TMP_Text _tmpStatus;
        [SerializeField] private TMP_Text _tmpSkills;
        [SerializeField] private TMP_Text _tmpMech;
        [SerializeField] private Image _accentBar;
        [SerializeField] private Button _btnClose;

        private UnitInspectData _data;

        public System.Action OnClosed;

        protected override void OnCreate() { }

        protected override UniTask OnOpenAsync(object payload)
        {
            _data = payload as UnitInspectData;
            if (_btnClose != null) { _btnClose.onClick.RemoveAllListeners(); _btnClose.onClick.AddListener(CloseSelf); }
            Apply();
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            var cb = OnClosed; OnClosed = null;
            cb?.Invoke();
        }

        private void Apply()
        {
            if (_data == null) return;
            if (_tmpName != null) _tmpName.text = _data.Name;
            if (_tmpStats != null) _tmpStats.text = _data.Stats;
            if (_tmpStatus != null) _tmpStatus.text = string.IsNullOrEmpty(_data.Statuses) ? "状态：无" : "状态：" + _data.Statuses;
            if (_tmpSkills != null) _tmpSkills.text = _data.Skills;
            if (_tmpMech != null) _tmpMech.text = string.IsNullOrEmpty(_data.Mechanic) ? "" : "机制：" + _data.Mechanic;
            if (_accentBar != null) _accentBar.color = _data.Accent;
        }
    }
}
