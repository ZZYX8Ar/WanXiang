// ============================================================================
//  Panel_Forge —— 铸魂台（融合）：宿主 | 漩涡 | 灵魂 三栏
//  ---------------------------------------------------------------------------
//  v1.2 填充：此前只有骨架（OnFuseClicked 是 TODO），打开后三栏全空。
//
//  数据流：
//    宿主列表 = 旅程存档的队伍（RunSave.Team），空则退化为图鉴前五只
//    灵魂列表 = 对每只宿主用 SoulForge.Derive(beast, 1) 派生的灵魂
//    选中宿主/灵魂 → 中央预览（立绘 / 灵球色块）+ 五行覆盖与五色区
//    点「熔炼」 → FusionRules.Fuse(host, soul, resolveSkill) 产出新异兽
//                 → Dialog 告知结果，并把队伍里的宿主替换为融合体（写入存档）
//
//  ⚠ 列表项是**代码自建**的（prefab 没有行项模板），与 DialogPanel 同一策略；
//    美术稿到位后把 BuildRows() 换成读 prefab 行项即可，数据流不变。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;
using WanXiang.Battle.Presentation;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Forge", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class ForgePanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpEggs;            // Tmp_Eggs      灵卵余额
        [SerializeField] private ScrollRect _scrollHosts;      // Scroll_Hosts  宿主列表
        [SerializeField] private Image _imgHost;               // Img_HostPreview 宿主立绘
        [SerializeField] private Image _imgVortex;             // Img_Vortex    墨色漩涡
        [SerializeField] private ScrollRect _scrollSouls;      // Scroll_Souls  灵魂列表
        [SerializeField] private Image _imgSoul;               // Img_SoulPreview 灵魂球体
        [BindArray("Img_Cover_{0}", 5)]
        [SerializeField] private Image[] _imgCover;            // 五行覆盖（木火土金水，未覆盖灰置）
        [BindArray("Img_Sw_{0}", 5)]
        [SerializeField] private Image[] _imgSwatch;           // 五色区预览（bodyMain/bodyAccent/energyGlow/eyeCore/outline）
        [SerializeField] private TMP_Text _tmpResult;          // Tmp_ResultName 融合预览名
        [SerializeField] private Button _btnFuse;              // Btn_Fuse
        [SerializeField] private Button _btnBack;              // Btn_Back  返回节点地图
        [SerializeField] private WanXiang.Fusion.ContentCatalogSO _contentCatalog;  // 由生成器注入
        [SerializeField] private WanXiang.Battle.Presentation.SpriteCatalog _sprites;  // 同上

        // ---- 选中态与列表行 ----
        private BeastDef _selectedHost;
        private SoulDef _selectedSoul;
        private readonly List<BeastDef> _hosts = new List<BeastDef>();
        private readonly List<SoulDef> _souls = new List<SoulDef>();
        private readonly List<Button> _rows = new List<Button>();
        private int _pickedHost = -1;
        private int _pickedSoul = -1;
        private static readonly Color RowIdle = new Color(0.96f, 0.94f, 0.89f, 1f);
        private static readonly Color RowPicked = new Color(0.85f, 0.76f, 0.56f, 1f);
        private static readonly Color CoverOff = new Color(0.62f, 0.60f, 0.55f, 0.45f);
        private static readonly Color CoverOn = new Color(0.42f, 0.55f, 0.36f, 1f);

        private TMP_Text _tmpDiff;      // 代码自建：把"灵魂到底改了什么"一栏一栏列出来

        protected override void OnCreate()
        {
            if (_btnFuse != null) _btnFuse.onClick.AddListener(OnFuseClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(() => {
                CloseSelf();
                var __ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                if (__ui != null) Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                    await __ui.OpenAsync<CampaignPanel>();
                });
            });   // 回到节点地图（继续探索）
            BuildDiff();
        }

        /// <summary>
        /// 玩家不知道"灵魂有什么用" —— 因为界面从没告诉他。
        /// 这一块按《暗黑破坏神》镶嵌宝石的对比写法（绿色=变、灰=不变）把差异列清楚：
        /// 五行 / 技能 / 特性 / 辉光色，四栏。
        /// </summary>
        private void BuildDiff()
        {
            if (_tmpDiff != null) return;
            var rt = new GameObject("Tmp_SoulDiff", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(980f, 210f);
            rt.anchoredPosition = new Vector2(0f, -300f);      // 漩涡下方
            _tmpDiff = rt.gameObject.AddComponent<TextMeshProUGUI>();
            _tmpDiff.fontSize = 24;
            _tmpDiff.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            _tmpDiff.alignment = TextAlignmentOptions.TopLeft;
            _tmpDiff.raycastTarget = false;
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            EnsureBackButton();
            Fill();
            return UniTask.CompletedTask;
        }

        // ================================================================
        //  填充
        // ================================================================

        private void Fill()
        {
            var run = WanXiang.Run.RunSave.Current;
            var all = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;
            if (all == null) all = new BeastDef[0];

            // 宿主：优先旅程队伍；空则用图鉴前五（铸魂台不该开成白板）
            _hosts.Clear();
            if (run != null && run.Team != null)
            {
                var byId = new Dictionary<string, BeastDef>();
                foreach (var b in all) byId[b.Id] = b;
                foreach (var id in run.Team)
                    if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var def) && !_hosts.Contains(def))
                        _hosts.Add(def);
            }
            if (_hosts.Count == 0)
                for (int i = 0; i < all.Length && _hosts.Count < 5; i++) _hosts.Add(all[i]);

            // 灵魂：从宿主派生（v1.2 简化 —— 每只宿主一条「魂」；孵蛋/掉落后续再接）
            _souls.Clear();
            foreach (var h in _hosts) _souls.Add(SoulForge.Derive(h, 1));

            if (_tmpTitle != null) _tmpTitle.text = "铸魂台";
            if (_tmpEggs != null) _tmpEggs.text = run != null ? "灵卵 " + run.Eggs : "灵卵 0";

            _rows.Clear();
            _pickedHost = _hosts.Count > 0 ? 0 : -1;
            _pickedSoul = _souls.Count > 0 ? 0 : -1;
            _selectedHost = _pickedHost >= 0 ? _hosts[0] : null;
            _selectedSoul = _pickedSoul >= 0 ? _souls[0] : null;

            BuildRows(_scrollHosts, _hosts.ConvertAll(h => h.DisplayName), i =>
            {
                _pickedHost = i;
                _selectedHost = _hosts[i];
                MarkPicked();
                if (_imgHost != null && _sprites != null)
                    _imgHost.sprite = _sprites.Get(_selectedHost.Id);
                RefreshPreview();
            });

            BuildRows(_scrollSouls, _souls.ConvertAll(s => s.DisplayName + " · " + s.Epithet), i =>
            {
                _pickedSoul = i;
                _selectedSoul = _souls[i];
                MarkPicked();
                if (_imgSoul != null && _selectedSoul.GlowHex != null &&
                    ColorUtility.TryParseHtmlString(
                        _selectedSoul.GlowHex.StartsWith("#") ? _selectedSoul.GlowHex : "#" + _selectedSoul.GlowHex,
                        out var c))
                    _imgSoul.color = c;
                RefreshPreview();
            });

            if (_imgHost != null && _selectedHost != null && _sprites != null)
                _imgHost.sprite = _sprites.Get(_selectedHost.Id);
            RefreshPreview();
        }

        private void MarkPicked()
        {
            // 行序：前 _hosts.Count 行是宿主列表，其后是灵魂列表
            for (int i = 0; i < _rows.Count; i++)
            {
                var img = _rows[i] != null && _rows[i].targetGraphic != null
                    ? _rows[i].targetGraphic as Image : null;
                if (img == null) continue;
                bool hostList = i < _hosts.Count;
                int dataIdx = hostList ? i : i - _hosts.Count;
                bool picked = hostList ? dataIdx == _pickedHost : dataIdx == _pickedSoul;
                img.color = picked ? RowPicked : RowIdle;
            }
        }

        private BeastDef PreviewFuse()
        {
            if (_selectedHost == null || _selectedSoul == null) return null;
            try { return FusionRules.Fuse(_selectedHost, _selectedSoul, id => null); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Forge] 预览融合失败：" + ex.Message);
                return null;
            }
        }

        private void RefreshPreview()
        {
            if (_selectedHost == null || _selectedSoul == null)
            {
                if (_tmpResult != null) _tmpResult.text = "选中宿主与灵魂后开始熔炼";
                return;
            }

            var fused = PreviewFuse();
            if (_tmpResult != null)
                _tmpResult.text = fused != null
                    ? "预览：" + fused.DisplayName
                    : "无法预览（数据缺失，熔炼可能失败）";

            // 五色区：融合体的外观（预览失败时用宿主原色）
            var def = fused ?? _selectedHost;
            string[] hexes =
            {
                def.Palette.BodyMain, def.Palette.BodyAccent,
                def.Palette.EnergyGlow, def.Palette.EyeCore, def.Palette.Outline,
            };
            if (_imgSwatch != null)
                for (int i = 0; i < _imgSwatch.Length && i < hexes.Length; i++)
                    if (_imgSwatch[i] != null && !string.IsNullOrEmpty(hexes[i]) &&
                        ColorUtility.TryParseHtmlString(
                            hexes[i].StartsWith("#") ? hexes[i] : "#" + hexes[i], out var c))
                        _imgSwatch[i].color = c;

            // 五行覆盖：灵魂的元素覆盖点亮，其余灰置
            if (_imgCover != null)
            {
                var covered = _selectedSoul.ElementOverride;
                for (int i = 0; i < _imgCover.Length; i++)
                {
                    if (_imgCover[i] == null) continue;
                    bool on = covered != Element.None && (int)covered == i;
                    _imgCover[i].color = on ? CoverOn : CoverOff;
                }
            }

            WriteDiff(fused ?? _selectedHost);
        }

        /// <summary>把融合前后的差异写成四行（这是"灵魂有什么用"的答案）。</summary>
        private void WriteDiff(BeastDef after)
        {
            if (_tmpDiff == null || _selectedHost == null || _selectedSoul == null) return;
            var before = _selectedHost;
            var sb = new System.Text.StringBuilder();

            // 五行
            sb.Append("五行　").Append(Cn(before.Element)).Append(" → ")
              .Append(Cn(after.Element))
              .Append(after.Element != before.Element ? "　（克制关系改变）" : "　（未变）").Append("\n");

            // 技能：逐个槽位比对，只列变化
            sb.Append("技能　");
            bool anySkill = false;
            anySkill |= SkillSlot(sb, "普攻", before.Basic, after.Basic, ref anySkill);
            SkillSlot(sb, "主动", before.Active, after.Active, ref anySkill);
            SkillSlot(sb, "绝技", before.Ultimate, after.Ultimate, ref anySkill);
            if (!anySkill) sb.Append("未变");
            sb.Append("\n");

            // 特性
            sb.Append("特性　").Append(string.IsNullOrEmpty(before.Trait.Name) ? "无" : before.Trait.Name)
              .Append(" → ").Append(string.IsNullOrEmpty(after.Trait.Name) ? "无" : after.Trait.Name).Append("\n");

            // 辉光 / 睛色
            sb.Append("辉光　").Append(string.IsNullOrEmpty(before.Palette.EnergyGlow) ? "默认" : before.Palette.EnergyGlow)
              .Append(" → ").Append(string.IsNullOrEmpty(after.Palette.EnergyGlow) ? "默认" : after.Palette.EnergyGlow)
              .Append("　睛色　").Append(string.IsNullOrEmpty(before.Palette.EyeCore) ? "默认" : before.Palette.EyeCore)
              .Append(" → ").Append(string.IsNullOrEmpty(after.Palette.EyeCore) ? "默认" : after.Palette.EyeCore);

            _tmpDiff.text = sb.ToString();
        }

        private static bool SkillSlot(System.Text.StringBuilder sb, string label, SkillDef b, SkillDef a, ref bool any)
        {
            string bn = b != null ? b.Name : "无";
            string an = a != null ? a.Name : "无";
            if (bn == an) return false;
            sb.Append(label).Append(" ").Append(bn).Append("→").Append(an).Append("　");
            any = true;
            return true;
        }

        private static string Cn(Element e)
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

        // ================================================================
        //  熔炼
        // ================================================================

        private async void OnFuseClicked()
        {
            if (_selectedHost == null || _selectedSoul == null)
            {
                await Dialog.Tip("铸魂台", "先在两侧各选中一个宿主与灵魂。");
                return;
            }

            BeastDef fused;
            try { fused = FusionRules.Fuse(_selectedHost, _selectedSoul, id => null); }
            catch (Exception ex)
            {
                await Dialog.Tip("融合失败", ex.Message);
                return;
            }

            // 结果写回存档：队伍里对应宿主替换为融合体（融合是"改写"不是凭空造兽）
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && run.Team != null)
            {
                int idx = run.Team.IndexOf(_selectedHost.Id);
                if (idx >= 0) run.Team[idx] = fused.Id;
                WanXiang.Run.RunSave.SaveCurrent();
            }

            await Dialog.Tip("熔炼完成",
                fused.DisplayName + " 诞生了！\n魂：" + _selectedSoul.Epithet +
                "　五行覆盖：" + (_selectedSoul.ElementOverride == Element.None ? "无" : _selectedSoul.ElementOverride.ToString()));

            Fill();      // 重灌：队伍已变化
        }

        // ================================================================
        //  自建列表行（prefab 没有行项模板）
        // ================================================================

        private void BuildRows(ScrollRect scroll, List<string> labels, Action<int> onPick)
        {
            if (scroll == null || scroll.content == null) return;
            var content = scroll.content;

            // ⚠ ScrollVertical 生成的 content 自带 VerticalLayoutGroup + ContentSizeFitter，
            //   会覆盖我们手动设的 sizeDelta 与行位置（实测 content 高被算成别的值）—— 先关掉。
            var vlg = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;
            var fitter = content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var old = content.GetChild(i).gameObject;
                old.SetActive(false);      // Destroy 帧末才生效，先失活避免同帧重叠
                Destroy(old);
            }

            for (int i = 0; i < labels.Count; i++)
            {
                var go = new GameObject("Row_" + i, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(content, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -i * 92f);
                // ★★ 这里原来是 sizeDelta = (-8, 84) —— 但上面用的是【水平单点锚】
                //    (anchorMin.x == anchorMax.x == 0.5)，此时 sizeDelta.x 就是**实际宽度**，
                //    写成负数 ⇒ 行宽 -8 ⇒ 行内文本更窄(-36) ⇒ 文字被挤成一列（竖排乱码）。
                //    负边距只在【双向锚】下才有意义。这里用视口宽度算真实行宽。
                float rowW = (scroll.viewport != null ? scroll.viewport.rect.width : 420f) - 8f;
                if (rowW < 120f) rowW = 420f;      // 布局尚未算完时的兜底
                rt.sizeDelta = new Vector2(rowW, 84f);

                var img = go.AddComponent<Image>();
                img.color = RowIdle;
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;

                var lrt = new GameObject("Tmp", typeof(RectTransform)).GetComponent<RectTransform>();
                lrt.SetParent(rt, false);
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(14f, 6f);
                lrt.offsetMax = new Vector2(-14f, -6f);
                var tmp = lrt.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.text = labels[i];
                tmp.fontSize = 26;
                tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.raycastTarget = false;

                int idx = i;
                btn.onClick.AddListener(() => onPick(idx));
                _rows.Add(btn);
            }

            float totalH = labels.Count * 92f + 40f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(totalH, 300f));
        }

        /// <summary>返回地图按钮兜底（prefab 没有时代码创建，保证能返回继续探索）。</summary>
        private void EnsureBackButton()
        {
            if (_btnBack != null) return;

            var go = new GameObject("Btn_Back", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.94f, 0.92f, 0.88f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(96f, -52f);
            r.sizeDelta = new Vector2(152f, 64f);

            var tgo = new GameObject("Tmp_Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = "返回地图";
            tmp.fontSize = 26;
            tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            _btnBack = btn;
            btn.onClick.AddListener(() =>
            {
                CampaignPanel.PendingCommit = -1;      // 返回 = 未完成，节点不通过
                CloseSelf();
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                    await ui.OpenAsync<CampaignPanel>();
                });
            });
        }

    }
}
