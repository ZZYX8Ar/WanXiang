// ============================================================================
//  万相 · 存档选择面板
//  ---------------------------------------------------------------------------
//  开始游戏 → 这里。三个槽位，一行一档：
//    · 空档     → 点下去 = 在这个槽位开一段全新旅程（从头开始）
//    · 有档     → 点下去 = 继承这段旅程（境·劫 / 灵卵 / 队伍都接上）
//    · 「新的旅程」大按钮 = 挑第一个空档开新程（全满则提示先覆盖）
//
//  为什么用 Main 层：它是"标题之后的第二个常驻画面"，不遮罩、不弹窗。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;
using WanXiang.Run;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Save", Layer = UILayer.Main, CachePolicy = UICachePolicy.Resident,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class SavePanel : UIPanelBase
    {
        [SerializeField] private Button   _btnNew;                // Btn_New      新的旅程
        [SerializeField] private Button   _btnBack;               // Btn_Back     返回标题
        [SerializeField] private Button[] _btnSlots;              // Btn_Slot0..2
        [SerializeField] private TMP_Text[] _tmpSlots;            // 每档的两行详情
        [SerializeField] private TMP_Text _tmpHint;               // 底部提示

        protected override void OnCreate()
        {
            if (_btnNew != null) _btnNew.onClick.AddListener(OnNewClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(OnBackClicked);
            if (_btnSlots != null)
            {
                for (int i = 0; i < _btnSlots.Length; i++)
                {
                    var idx = i;
                    if (_btnSlots[i] != null) _btnSlots[i].onClick.AddListener(() => OnSlotClicked(idx));
                }
            }
        }

        /// <summary>
        /// 拥有的异兽数 = 图鉴(Collection) ∪ 出战队伍(Team) 去重。
        /// 旧存档没有 Collection 字段（那时买到就直接进队伍）⇒ 只看 Collection 会显示成 0/5。
        /// </summary>
        private static int OwnedBeastCount(WanXiang.Run.RunState st)
        {
            if (st == null) return 0;
            var set = new System.Collections.Generic.HashSet<string>();
            if (st.Collection != null) foreach (var id in st.Collection) if (!string.IsNullOrEmpty(id)) set.Add(id);
            if (st.Team != null) foreach (var id in st.Team) if (!string.IsNullOrEmpty(id)) set.Add(id);
            return set.Count;
        }

        [SerializeField] private Button _btnClearAll;      // 节点已在 prefab 里（Btn_ClearAll）

        protected override UniTask OnOpenAsync(object payload)
        {
            SyncSlotResetButtons();     // 原来叫 Ensure…（代码建按钮）；现改为「切显隐 + 挂监听」
            WireClearAllButton();       // 同上：按钮已在 prefab 里
            RefreshSlots();
            return UniTask.CompletedTask;
        }

        /// <summary>读三个槽位，把每档的状态写成两行字。</summary>
        private void RefreshSlots()
        {
            if (_tmpSlots == null) return;
            for (int i = 0; i < _tmpSlots.Length && i < RunSave.SlotCount; i++)
            {
                var slot = i + 1;
                var state = RunSave.Load(slot);
                if (_tmpSlots[i] == null) continue;

                if (state == null)
                {
                    _tmpSlots[i].text = "存档 " + Cn(slot) + " · 空档\n从这里开始一段新的旅程";
                }
                else
                {
                    _tmpSlots[i].text = "存档 " + Cn(slot) + " · " + state.RealmText +
                                        "    灵卵 " + state.Eggs + " · 异兽 " + OwnedBeastCount(state) + " 只" +
                                        "\n胜 " + state.Wins + " / 败 " + state.Losses +
                                        "    最后旅程 " + (string.IsNullOrEmpty(state.LastSaved) ? "—" : state.LastSaved);
                }
            }
        }

        /// <summary>点槽位：空档 = 开新程；有档 = 继承。</summary>
        private void OnSlotClicked(int index)
        {
            var slot = index + 1;
            var state = RunSave.Load(slot);
            if (state == null) RunSave.StartNew(slot);
            else RunSave.ContinueWith(state);
            WanXiang.Meta.MetaStore.ReloadHistory();   // ★ 切档后按新槽位重读历程，避免跨档污染
            WanXiang.Meta.MetaStore.ReloadMeta();      // ★ 局外数据（墨铊/精魄/等级/觉醒技/图鉴）同样按槽隔离
            CloseSelf();
            SceneFlow.EnterMain();
        }

        /// <summary>
        /// 槽位「重置」按钮：**节点已在 prefab 里**（`Btn_Slot{i}/Btn_Reset`，默认隐藏），
        /// 这里只按"该档有没有存档"切显隐 + 挂监听。
        /// ⛔ 不再用代码 `new GameObject` 建 UI —— UI 一律在 prefab 里做好（工程硬规则）。
        /// </summary>
        private void SyncSlotResetButtons()
        {
            for (int i = 0; i < _btnSlots.Length && i < RunSave.SlotCount; i++)
            {
                var slot = i + 1;
                var slotBtn = _btnSlots[i];
                if (slotBtn == null) continue;

                var reset = slotBtn.transform.Find("Btn_Reset");
                if (reset == null) continue;                 // prefab 里没做就跳过（不报错）
                reset.gameObject.SetActive(RunSave.Exists(slot));

                var btn = reset.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();        // 幂等：避免每次打开重复叠加监听
                    btn.onClick.AddListener(() => ResetAfterConfirm(slot));
                }
            }
        }

        /// <summary>「清空全部存档」按钮：节点已在 prefab 里（`Btn_ClearAll`），这里只挂监听（幂等）。</summary>
        private void WireClearAllButton()
        {
            if (_btnClearAll == null) return;
            _btnClearAll.onClick.RemoveAllListeners();
            _btnClearAll.onClick.AddListener(ConfirmClear);
        }

        private async void ResetAfterConfirm(int slot)
        {
            bool ok = await Dialog.Confirm("重置存档 " + slot, "该存档的进度会被清空并从第一幕重新开始。确定？", "确定重置", "取消");
            if (!ok) return;
            WanXiang.Run.RunSave.ClearSlot(slot);
            WanXiang.Meta.MetaStore.DeleteHistory(slot);   // ★ 顺手删该档历程，否则重进还会显示旧历程
            WanXiang.Meta.MetaStore.DeleteMeta(slot);      // ★ 该档局外数据一并清空（每槽独立）
            WanXiang.Meta.MetaStore.ReloadMeta();          // ★ 内存立即回到"新档"状态（含历程重读）
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null) await ui.OpenAsync<SavePanel>();
        }

        /// <summary>「新的旅程」：挑第一个空档；全满则提示（避免手滑覆盖）。</summary>
        private void OnNewClicked()
        {
            for (int slot = 1; slot <= RunSave.SlotCount; slot++)
            {
                if (!RunSave.Exists(slot))
                {
                    RunSave.StartNew(slot);
                    WanXiang.Meta.MetaStore.ReloadHistory();   // ★ 新档：内存历程应为空
                    WanXiang.Meta.MetaStore.ReloadMeta();      // ★ 新档：局外数据也应是该槽自己的（全新或已有）
                    CloseSelf();
                    SceneFlow.EnterMain();
                    return;
                }
            }
            if (_tmpHint != null)
                _tmpHint.text = "三个槽位都有旅程了 —— 点上面任意一档继承，或先删档再开新程。";
        }

        private void OnBackClicked()
        {
            CloseSelf();
            OpenPanelAsync<StartPanel>().Forget();
        }

        private static string Cn(int n)
        {
            switch (n)
            {
                case 1: return "一";
                case 2: return "二";
                default: return "三";
            }
        }

        private bool _clearConfirming;

        private async void ConfirmClear()
        {
            try
            {
                bool ok = await Dialog.Confirm("清空全部存档", "所有旅程记录都会被删除，且无法恢复。确定？", "确定清空", "取消");
                if (!ok) return;
                WanXiang.Run.RunSave.ClearAll();
                WanXiang.Meta.MetaStore.DeleteAllHistory();   // ★ 连历程一起清
                WanXiang.Meta.MetaStore.DeleteAllMeta();      // ★ 全部槽位的局外数据一并清
                WanXiang.Meta.MetaStore.ReloadMeta();         // ★ 内存回到全新状态
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                if (ui != null) await ui.OpenAsync<SavePanel>();
            }
            finally { _clearConfirming = false; }
        }

    }
}