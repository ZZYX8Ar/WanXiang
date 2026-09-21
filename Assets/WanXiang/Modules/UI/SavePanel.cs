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

        [SerializeField] private Button _btnClearAll;      // 兜底创建

        protected override UniTask OnOpenAsync(object payload)
        {
            EnsureClearAllButton();
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
            CloseSelf();
            SceneFlow.EnterMain();
        }

        /// <summary>「新的旅程」：挑第一个空档；全满则提示（避免手滑覆盖）。</summary>
        private void OnNewClicked()
        {
            for (int slot = 1; slot <= RunSave.SlotCount; slot++)
            {
                if (!RunSave.Exists(slot))
                {
                    RunSave.StartNew(slot);
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

        /// <summary>「清空全部存档」兜底按钮（代码自建，放屏幕底部）。</summary>
        private void EnsureClearAllButton()
        {
            if (_btnClearAll != null) return;

            var go = new GameObject("Btn_ClearAll", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.85f, 0.62f, 0.58f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 40f);
            r.sizeDelta = new Vector2(260f, 60f);

            var tgo = new GameObject("Tmp_Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var tmp = tgo.AddComponent<TMP_Text>();
            tmp.text = "清空全部存档";
            tmp.fontSize = 24;
            tmp.color = new Color(0.35f, 0.16f, 0.13f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;

            _btnClearAll = btn;
            btn.onClick.AddListener(() =>
            {
                bool ok = awaitClearConfirm();
                if (!ok) return;
                WanXiang.Run.RunSave.ClearAll();
                // 刷新列表显示
                OpenPanelAsync<SavePanel>().Forget();
            });
        }

        private bool _clearConfirming;

        private bool awaitClearConfirm()
        {
            // 双重确认：先弹确认框（异步），这里用一个简单标记避免重复点击
            if (_clearConfirming) return false;
            _clearConfirming = true;
            ConfirmClear();
            return false;
        }

        private async void ConfirmClear()
        {
            try
            {
                bool ok = await Dialog.Confirm("清空全部存档", "所有旅程记录都会被删除，且无法恢复。确定？", "确定清空", "取消");
                if (!ok) return;
                WanXiang.Run.RunSave.ClearAll();
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                if (ui != null) await ui.OpenAsync<SavePanel>();
            }
            finally { _clearConfirming = false; }
        }

    }
}