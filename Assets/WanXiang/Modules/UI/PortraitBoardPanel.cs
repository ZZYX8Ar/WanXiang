// ============================================================================
//  Panel_PortraitBoard —— 看板库（动态立绘轮换管理）
//  ---------------------------------------------------------------------------
//  · 列出所有已解锁动态立绘；点击加入/移出轮换，最多 5 个，按点击顺序轮换；
//    第 1 个 = 返回主界面时显示的看板兽（写 MetaState.HomePortraitId）。
//  · 自动轮换开关 + 轮换间隔（60/120/180/240 秒；默认 180 ≈ 一首歌的时间，
//    后续每只立绘配专属 BGM 后，轮换间隔即跟着歌长走）。
//  · 选择与开关存 PlayerPrefs（展示偏好，PortraitBoardPrefs 统一读写）；
//    解锁状态在 MetaState（残卷阁领取）。
//  ⛔ 铁律：所有节点在 prefab 里（UIBuildPortraitPatch 建），本类只填数据/显隐。
// ============================================================================

using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_PortraitBoard", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class PortraitBoardPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpHint;           // Tmp_Hint
        [SerializeField] private RectTransform _scrollContent; // Scroll_Portraits/Viewport/Content
        [SerializeField] private RectTransform _cardTemplate;  // Item_PortraitCard（默认隐藏）
        [SerializeField] private Button _btnAutoRotate;        // Btn_AutoRotate
        [SerializeField] private Button _btnInterval;          // Btn_Interval
        [SerializeField] private Button _btnClose;             // Btn_Close

        private static readonly Color C_Gold = new Color(0.79f, 0.63f, 0.39f, 1f);
        private static readonly Color C_Sel  = new Color(0.79f, 0.63f, 0.39f, 1f);   // 选中：金黄（用户 2026-10-19：青色太丑，换回金黄）
        private static readonly Color C_Ink  = new Color(0.165f, 0.129f, 0.094f, 1f);
        private static readonly Color C_Ink2 = new Color(0.42f, 0.38f, 0.34f, 1f);
        private static readonly Color C_Off  = new Color(0.86f, 0.85f, 0.83f, 1f);

        private readonly List<string> _unlocked = new List<string>(50);  // 已解锁（MetaState 顺序）
        private readonly List<string> _selected = new List<string>(PortraitBoardPrefs.MaxSelected);
        private SpriteCatalog _catalog;

        protected override void OnCreate()
        {
            if (_btnClose != null) _btnClose.onClick.AddListener(CloseSelf);
            if (_btnAutoRotate != null) _btnAutoRotate.onClick.AddListener(OnToggleAuto);
            if (_btnInterval != null) _btnInterval.onClick.AddListener(OnCycleInterval);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            _unlocked.Clear();
            if (meta != null) _unlocked.AddRange(meta.UnlockedLivePortraits);
            _catalog = FindCatalog();

            _selected.Clear();
            _selected.AddRange(PortraitBoardPrefs.LoadSelected());
            _selected.RemoveAll(id => !_unlocked.Contains(id));   // 防脏：只留仍解锁的

            Rebuild();
            RefreshSettingButtons();
            return UniTask.CompletedTask;
        }

        // ==================================================================== 卡片
        private void Rebuild()
        {
            if (_scrollContent == null || _cardTemplate == null) return;

            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                var c = _scrollContent.GetChild(i);
                if (c == _cardTemplate) continue;
                Destroy(c.gameObject);
            }

            for (int i = 0; i < _unlocked.Count; i++)
            {
                var id = _unlocked[i];
                var card = Instantiate(_cardTemplate, _scrollContent);
                card.name = "Card_" + id;
                card.gameObject.SetActive(true);

                int order = _selected.IndexOf(id);            // -1 = 未选
                bool hasVideo = File.Exists(Path.Combine(
                    Application.streamingAssetsPath, "LivePortraits", "live_" + id + ".mp4"));

                // 头像（战斗立绘头图，与残卷阁同源）
                var head = card.Find("Img_Head") as RectTransform;
                if (head != null)
                {
                    var img = head.GetComponent<Image>();
                    var sp = _catalog != null ? _catalog.GetHead(id) : null;
                    if (img != null) { img.sprite = sp; img.color = sp != null ? Color.white : C_Off; }
                    if (img != null) img.preserveAspect = true;
                }

                var name = FindTmp(card, "Tmp_Name");
                if (name != null)
                {
                    name.text = hasVideo ? DisplayNameOf(id) : DisplayNameOf(id) + "\n（视频未制作）";
                    name.color = hasLiveVideoColor(order, hasVideo);
                }

                var orderTxt = FindTmp(card, "Tmp_Order");
                if (orderTxt != null)
                {
                    orderTxt.text = order >= 0 ? "第" + (order + 1) + "位" : "";
                    orderTxt.color = C_Ink;
                }

                var cardImg = card.GetComponent<Image>();
                if (cardImg != null)
                {
                    // ★ 只有颜色真的变了才写：整排无条件重涂会把所有 Image 标脏 ⇒
                    //   Canvas 全量重建，点一下"所有格子都闪一下"（用户 2026-10-19）。
                    var target = order >= 0 ? C_Sel : (hasVideo ? Color.white : C_Off);
                    if (cardImg.color != target) cardImg.color = target;
                }

                var btn = card.GetComponent<Button>();
                if (btn != null)
                {
                    btn.interactable = hasVideo;
                    btn.onClick.RemoveAllListeners();
                    if (hasVideo)
                    {
                        var captured = id;
                        btn.onClick.AddListener(() => OnCardClicked(captured));
                    }
                }
            }
        }

        private Color hasLiveVideoColor(int order, bool hasVideo)
        {
            if (!hasVideo) return C_Ink2;
            return order >= 0 ? C_Ink : C_Ink2;
        }

        private void OnCardClicked(string id)
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            int at = _selected.IndexOf(id);
            if (at >= 0)
            {
                _selected.RemoveAt(at);
            }
            else
            {
                if (_selected.Count >= PortraitBoardPrefs.MaxSelected)
                {
                    if (_tmpHint != null)
                        _tmpHint.text = "轮换最多 " + PortraitBoardPrefs.MaxSelected +
                                        " 只：先点选中的取消一只，再加新的。";
                    return;
                }
                _selected.Add(id);
            }

            PortraitBoardPrefs.SaveSelected(_selected);
            // 第 1 个 = 返回主界面时显示的看板兽
            if (meta != null && _selected.Count > 0 && meta.HomePortraitId != _selected[0])
            {
                meta.HomePortraitId = _selected[0];
                WanXiang.Meta.MetaStore.Save();
            }
            RefreshSettingButtons();
            Rebuild();
        }

        // ==================================================================== 设置
        private void OnToggleAuto()
        {
            PortraitBoardPrefs.SetAutoRotate(!PortraitBoardPrefs.AutoRotateOn);
            RefreshSettingButtons();
        }

        private void OnCycleInterval()
        {
            float cur = PortraitBoardPrefs.LoadSeconds();
            int idx = 0;
            for (int i = 0; i < PortraitBoardPrefs.IntervalSteps.Length; i++)
                if (Mathf.Approximately(PortraitBoardPrefs.IntervalSteps[i], cur)) { idx = i; break; }
            PortraitBoardPrefs.SetSeconds(PortraitBoardPrefs.IntervalSteps[(idx + 1) % PortraitBoardPrefs.IntervalSteps.Length]);
            RefreshSettingButtons();
        }

        private void RefreshSettingButtons()
        {
            if (_btnAutoRotate != null)
            {
                var lbl = FindTmp(_btnAutoRotate.transform, "Tmp_Label");
                bool on = PortraitBoardPrefs.AutoRotateOn;
                if (lbl != null) lbl.text = "自动轮换：" + (on ? "开" : "关");
                var img = _btnAutoRotate.GetComponent<Image>();
                if (img != null) img.color = on ? C_Gold : C_Off;
            }
            if (_btnInterval != null)
            {
                var lbl = FindTmp(_btnInterval.transform, "Tmp_Label");
                if (lbl != null) lbl.text = "轮换间隔：" + PortraitBoardPrefs.LoadSeconds().ToString("0") + " 秒（约一首歌）";
            }
            if (_tmpHint != null)
                _tmpHint.text = "点击立绘加入轮换（最多 " + PortraitBoardPrefs.MaxSelected +
                                " 个，按点击顺序轮换；第 1 个 = 返回主界面显示的看板兽）。";
        }

        private string DisplayNameOf(string id)
        {
            var beasts = LoadBeasts();
            if (beasts != null)
                for (int i = 0; i < beasts.Length; i++)
                    if (beasts[i] != null && beasts[i].Id == id)
                        return beasts[i].DisplayName;
            return id;
        }

        private static WanXiang.Battle.Core.BeastDef[] LoadBeasts()
        {
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) return null;
            return WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
        }

        private static SpriteCatalog FindCatalog()
        {
            var all = Resources.FindObjectsOfTypeAll<SpriteCatalog>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Entries.Count > 0) return all[i];
            return null;
        }

        private static TMP_Text FindTmp(Transform root, string name)
        {
            var t = root.Find(name);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }
    }
}
