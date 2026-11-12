// ============================================================================
//  万相 · 灵感树面板（B5-4，Modules/UI）
//  ---------------------------------------------------------------------------
//  三支 × 5 层 + 悟道点余额。购买：扣悟道点 → InspirationIds + Save → 刷新。
//  节点状态：已解锁（亮）/ 可买（白 + 显示名与消耗）/ 锁定（灰，上一级未解锁）。
// ============================================================================
using Cysharp.Threading.Tasks;
using WanXiang.Framework.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Inspiration", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class InspirationPanel : UIPanelBase
    {
        private static readonly string[] BranchNodes = { "Branch_travel", "Branch_battle", "Branch_forge" };
        private static readonly string[] BranchKeys = { "travel", "battle", "forge" };

        protected override void OnCreate() { }

        /// <summary>路径取控件（transform.Find 包装，DaoMarkPanel 同款）。</summary>
        private T Find<T>(string path) where T : Component
        {
            var tr = transform.Find(path);
            return tr != null ? tr.GetComponent<T>() : null;
        }

        /// <summary>路径取控件 + 绑点击（DaoMarkPanel 同款）。</summary>
        private void Hook(string path, System.Action action)
        {
            var btn = Find<Button>(path);
            if (btn == null) { Debug.LogWarning("[Inspiration] 缺按钮：" + path); return; }
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            WanXiang.Modules.UI.DataTables.EnsureInstalled();
            Hook("Root_TopBar/Btn_Close", CloseSelf);
            Refresh();
            return UniTask.CompletedTask;
        }

        private void Refresh()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            var pts = transform.Find("Root_TopBar/Tmp_Points");
            if (pts != null) pts.GetComponent<TMP_Text>().text = "悟道点 " + (meta != null ? meta.WudaoPoints : 0);

            for (int b = 0; b < 3; b++)
            {
                var col = transform.Find(BranchNodes[b]);
                if (col == null) continue;
                for (int t = 1; t <= 5; t++)
                {
                    var btnTr = col.Find("Grid/Btn_T" + t);
                    if (btnTr == null) continue;
                    RefreshNode(btnTr, meta, BranchKeys[b], t);
                }
            }
        }

        private void RefreshNode(Transform btnTr, WanXiang.Meta.MetaState meta, string branch, int tier)
        {
            var def = FindDef(branch, tier);
            var btn = btnTr.GetComponent<Button>();
            var img = btnTr.GetComponent<Image>();
            var label = btnTr.Find("Tmp_Label") != null ? btnTr.Find("Tmp_Label").GetComponent<TMP_Text>() : null;
            if (def == null || btn == null) return;

            bool unlocked = meta != null && meta.InspirationIds.IndexOf(def.Id) >= 0;
            bool canBuy = meta != null && WanXiang.Battle.Core.InspirationRules.CanBuy(meta.InspirationIds, def.Id)
                          && meta.WudaoPoints >= def.Cost;

            btn.interactable = unlocked || canBuy;
            if (img != null)
                img.color = unlocked ? new Color(0.72f, 0.90f, 0.72f, 0.95f)
                          : canBuy ? new Color(1f, 1f, 1f, 0.9f)
                          : new Color(0.75f, 0.74f, 0.72f, 0.7f);
            if (label != null)
            {
                label.fontSize = 20;
                label.text = unlocked
                    ? def.Name + "（已解锁）\n" + def.Desc
                    : (canBuy || tierOk(meta, def)) ? def.Name + " · " + def.Cost + " 点\n" + def.Desc
                    : def.Name + "\n需先解锁上一层";   // 显示名称与解锁条件，不再"？？？"
            }
            btn.onClick.RemoveAllListeners();
            var id = def.Id;                       // 闭包捕获
            btn.onClick.AddListener(() => OnNodeClicked(id));
        }

        private bool tierOk(WanXiang.Meta.MetaState meta, WanXiang.Battle.Core.InspirationDef def)
        {
            return meta != null && WanXiang.Battle.Core.InspirationRules.CanBuy(meta.InspirationIds, def.Id);
        }

        private WanXiang.Battle.Core.InspirationDef FindDef(string branch, int tier)
        {
            var all = WanXiang.Battle.Core.InspirationRules.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Branch == branch && all[i].Tier == tier) return all[i];
            return null;
        }

        private void OnNodeClicked(string id)
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            var def = WanXiang.Battle.Core.InspirationRules.DefOf(id);
            if (meta == null || def == null) return;
            if (meta.InspirationIds.IndexOf(id) >= 0) { Dialog.Tip("已解锁", "「" + def.Name + "」已生效。").Forget(); return; }
            if (!WanXiang.Battle.Core.InspirationRules.CanBuy(meta.InspirationIds, id))
            { Dialog.Tip("尚未解锁", "先解锁「" + def.Branch + "」支的上一级灵感。").Forget(); return; }
            if (meta.WudaoPoints < def.Cost)
            {
                Dialog.Tip("悟道点不足",
                    "「" + def.Name + "」需要 " + def.Cost + " 点，你现在有 " + meta.WudaoPoints + " 点。\n"
                    + "悟道点在局末结算获得（难度 ×2 + 通关 +10 + 首通幕 ×5）。").Forget();
                return;
            }
            meta.WudaoPoints -= def.Cost;
            meta.InspirationIds.Add(id);
            WanXiang.Meta.MetaStore.Save();
            SfxThrottled("sfx_ui_confirm");
            Debug.Log("[Inspiration] 解锁灵感：" + id + "（" + def.Cost + " 点，余 " + meta.WudaoPoints + "）");
            Dialog.Tip("灵感已至", "「" + def.Name + "」已生效。\n" + def.Desc).Forget();
            Refresh();
        }
    }
}
