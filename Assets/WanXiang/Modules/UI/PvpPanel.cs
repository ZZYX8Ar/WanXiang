// ============================================================================
//  Panel_Pvp —— 好友对战（配对码）
//  ---------------------------------------------------------------------------
//  语义（用户定案）：把「我这一局的最终编队」生成配对码发给朋友；
//  朋友粘贴后，用【他的编队】vs【我的编队】跑一场 **AI 自动对战**（无手动），
//  双方各跑一遍必然得到同一份 `PvpReport`（含指纹可对账）。
//
//  底层已具备：ShareCode（编解码）+ PvpMatch.PlayByCode（对战）。
//  本面板只做：取码 / 解码 / 填 PvpContent / 展示战报。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Pvp", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class PvpPanel : UIPanelBase
    {
        [SerializeField] private TMP_InputField _inputOpp;    // Inp_OppCode（粘贴对手码）
        [SerializeField] private TMP_InputField _inputMine;   // Inp_MyCode（我的码，默认最新一局，可改）
        [SerializeField] private TMP_Text _tmpMyCode;         // Tmp_MyCode（我的码，可复制）
        [SerializeField] private Button _btnCopyMine;         // Btn_CopyMine
        [SerializeField] private Button _btnFight;            // Btn_Fight
        [SerializeField] private TMP_Text _tmpResult;         // Tmp_Result（战报）
        [SerializeField] private Button _btnBack;             // Btn_Back

        protected override void OnCreate()
        {
            if (_btnBack != null) _btnBack.onClick.AddListener(CloseSelf);
            if (_btnCopyMine != null)
            {
                _btnCopyMine.onClick.AddListener(OnEditMine);   // 用户定案：复制按钮改为「修改」弹窗
                var lblTxt = _btnCopyMine.GetComponentInChildren<TMP_Text>();
                if (lblTxt != null) lblTxt.text = "修改";
            }
            if (_btnFight != null) _btnFight.onClick.AddListener(OnFight);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // ★ 兜底：prefab 序列化引用丢失（如 Unity 未 reimport 改过的 prefab）时，
            //   按名字找回 Inp_MyCode 节点，保证「我的码」一定是可编辑输入框
            //   （好友的 Inp_OppCode 同理）。—— 否则用户会看到「我的码不可修改」。
            if (_inputMine == null)
            {
                var mineGo = transform.Find("Inp_MyCode");
                if (mineGo != null) _inputMine = mineGo.GetComponent<TMP_InputField>();
            }
            if (_inputMine != null) _inputMine.interactable = true;

            RenderMine();
            // ★ 默认永远是最新一局的码（面板是 Cached 的，旧逻辑「只在为空时填」
            //   会留下上一局的旧码 —— 用户实测「对战码保存的异兽和最后一次战斗的不一样」
            //   就是拿旧码在打）。要打其他阵容：用「修改」弹窗或直接改输入框。
            if (_inputMine != null) _inputMine.text = MyLatestCode();
            if (_tmpResult != null) _tmpResult.text = "确认/修改上方两个配对码，然后点「开始对战」。";
            return UniTask.CompletedTask;
        }

        // ---------------------------------------------------------------- 我的码

        private void RenderMine()
        {
            if (_tmpMyCode == null) return;
            var mine = MyLatestCode();
            _tmpMyCode.text = string.IsNullOrEmpty(mine)
                ? "（还没有可分享的编队 —— 先打一局）"
                : ("我的配对码：\n" + mine);
        }

        /// <summary>取我最近一局的编队码（历程里最新的那条）。</summary>
        private static string MyLatestCode()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (meta == null || meta.HistCodes.Count == 0) return "";
            for (int i = meta.HistCodes.Count - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(meta.HistCodes[i])) return meta.HistCodes[i];
            return "";
        }

        private void OnCopyMine()
        {
            var mine = MyLatestCode();
            if (string.IsNullOrEmpty(mine)) { Debug.LogWarning("[PvpPanel] 没有可复制的码"); return; }
            GUIUtility.systemCopyBuffer = mine;
            Debug.Log("[PvpPanel] 已复制我的配对码：" + mine);
            if (_tmpResult != null) _tmpResult.text = "已复制我的配对码（发给朋友）。\n" + mine;
        }

        // ---------------------------------------------------------------- 对战

        private void OnFight()
        {
            // ★ 我的码：优先用输入框（自己填/改），空则回落到历程最新一条
            var mine = _inputMine != null ? (_inputMine.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(mine)) mine = MyLatestCode();
            if (string.IsNullOrEmpty(mine)) { Show("我还没有配对码 —— 先打一局，或在上面粘贴一个。"); return; }

            string opp = _inputOpp != null ? (_inputOpp.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(opp)) { Show("请先粘贴对方的配对码。"); return; }

            // 内容：异兽表 + 灵魂表 + 技能解析器（与融合管线同一份）
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) { Show("找不到内容目录，无法对战。"); return; }
            var beasts = WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
            if (beasts == null || beasts.Length == 0) { Show("内容目录为空。"); return; }

            var souls = new System.Collections.Generic.List<WanXiang.Fusion.SoulDef>(beasts.Length);
            for (int i = 0; i < beasts.Length; i++) souls.Add(WanXiang.Fusion.SoulForge.Derive(beasts[i], i));

            WanXiang.Fusion.SkillResolver resolver = (skillId) =>
            {
                for (int i = 0; i < beasts.Length; i++)
                {
                    var arr = beasts[i].AllSkills;
                    for (int k = 0; k < arr.Length; k++)
                        if (arr[k] != null && arr[k].Id == skillId) return arr[k];
                }
                return null;
            };

            var content = new WanXiang.Pvp.PvpContent
            {
                Beasts = beasts,
                Souls = souls.ToArray(),
                Resolver = resolver,
            };

            // ★★ 进入战斗场景（AI 自动对战回放）
            // ★ 调试：打印双方 payload 的下标（定位"兽/站位不一样"）
            {
                var decA = new WanXiang.Fusion.SharePayload();
                var decB = new WanXiang.Fusion.SharePayload();
                bool oa = WanXiang.Fusion.ShareCode.TryDecode(mine, beasts.Length, souls.Count, out decA);
                bool ob = WanXiang.Fusion.ShareCode.TryDecode(opp, beasts.Length, souls.Count, out decB);
                System.Func<WanXiang.Fusion.SharePayload, string> dump = (pp) =>
                {
                    if (pp.BeastIndices == null) return "<解码失败>";
                    var b2 = string.Join(",", pp.BeastIndices ?? new int[0]);
                    var s2 = string.Join(",", pp.SoulIndices ?? new int[0]);
                    var c2 = string.Join(",", pp.BoardSlots ?? new int[0]);
                    return "兽[" + b2 + "] 魂[" + s2 + "] 格[" + c2 + "]";
                };
                Debug.Log("[PvpPanel][调试] 我的码解码=" + oa + " " + (oa ? dump(decA) : ""));
                Debug.Log("[PvpPanel][调试] 对方码解码=" + ob + " " + (ob ? dump(decB) : ""));
            }

            var myEntries = WanXiang.Pvp.PvpMatch.BuildSquadOf(mine, content,
                WanXiang.Battle.Core.TeamSide.Player, out string errMine);
            if (myEntries == null) { Show("我方：" + errMine); return; }
            var oppEntries = WanXiang.Pvp.PvpMatch.BuildSquadOf(opp, content,
                WanXiang.Battle.Core.TeamSide.Enemy, out string errOpp);
            if (oppEntries == null) { Show("对方：" + errOpp); return; }

            // ★ 调试：把实际进入对战的双方阵容（名字+格号）打出来，与历程对照
            {
                var s1 = new System.Text.StringBuilder("[PvpPanel][调试] 我方实际上场：");
                for (int i = 0; i < myEntries.Length; i++)
                    s1.Append(myEntries[i].Def.DisplayName).Append('(').Append(myEntries[i].PosIndex).Append(") ");
                var s2 = new System.Text.StringBuilder("[PvpPanel][调试] 对方实际上场：");
                for (int i = 0; i < oppEntries.Length; i++)
                    s2.Append(oppEntries[i].Def.DisplayName).Append('(').Append(oppEntries[i].PosIndex).Append(") ");
                Debug.Log(s1.ToString());
                Debug.Log(s2.ToString());
            }

            ulong seed = WanXiang.Pvp.PvpMatch.SeedOf(mine, opp);

            var req = new WanXiang.Modules.UI.BattleRequest
            {
                Title = "好友对战",
                IsPvpMatch = true,
                WeatherName = "AI 自动对战（双方各自动放技能）",
                Seed = seed,
            };
            // DeployEntry 自带 Def/Side/PosIndex/StatMul —— 直接整组塞给 EnemyEntries
            req.EnemyEntries.AddRange(oppEntries);
            if (req.PlayerCells == null) req.PlayerCells = new System.Collections.Generic.List<int>();
            req.PlayerCells.Clear();
            foreach (var e in myEntries)
            {
                req.Player.Add(e.Def);
                req.PlayerCells.Add(e.PosIndex);   // DeployEntry 的字段名是 PosIndex（不是 BoardSlot）
            }

            CloseSelf();
            SceneFlow.EnterBattle(req);
        }

        // ------------------------------------------ 修改我的码·弹窗（用户定案）
        private RectTransform _editRoot;
        private TMP_InputField _editInput;

        private void OnEditMine()
        {
            EnsureEditDialog();
            if (_editInput == null) { Debug.LogWarning("[PvpPanel] 修改弹窗输入框未建成功"); return; }
            _editInput.text = _inputMine != null ? (_inputMine.text ?? "") : MyLatestCode();
            _editRoot.gameObject.SetActive(true);
            Debug.Log("[PvpPanel][调试] 打开修改弹窗，当前码=" + _editInput.text);
        }

        private void EnsureEditDialog()
        {
            if (_editRoot != null) return;

            // 全屏遮罩：挡住底下所有点击
            var rootGo = new GameObject("EditMineOverlay", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(transform, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var mask = rootGo.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.6f);
            mask.raycastTarget = true;
            _editRoot = root;
            rootGo.SetActive(false);

            // 中央盒子
            var boxGo = new GameObject("Box", typeof(RectTransform));
            var box = (RectTransform)boxGo.transform;
            box.SetParent(root, false);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(960f, 300f);
            var boxImg = boxGo.AddComponent<Image>();
            boxImg.color = new Color(0.96f, 0.93f, 0.86f, 1f);
            boxImg.raycastTarget = true;

            // 标题
            var titleGo = new GameObject("Title", typeof(RectTransform));
            var trt = (RectTransform)titleGo.transform;
            trt.SetParent(box, false);
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -14f);
            trt.sizeDelta = new Vector2(0f, 46f);
            var title = titleGo.AddComponent<TMPro.TextMeshProUGUI>();
            title.text = "输入我的配对码（默认最新一局，可改成其他阵容的码）";
            title.fontSize = 26;
            title.color = new Color(0.15f, 0.12f, 0.08f, 1f);
            title.alignment = TMPro.TextAlignmentOptions.Center;
            title.raycastTarget = false;

            // 输入框：克隆 Inp_OppCode（已知可编辑的输入框结构），拆掉里面的按钮/标签
            if (_inputOpp != null)
            {
                var cloneGo = UnityEngine.Object.Instantiate(_inputOpp.gameObject, box);
                cloneGo.name = "Inp_Edit";
                var crt = (RectTransform)cloneGo.transform;
                crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = new Vector2(0f, 10f);
                crt.sizeDelta = new Vector2(860f, 64f);
                var fight = cloneGo.transform.Find("Btn_Fight");
                if (fight != null) UnityEngine.Object.Destroy(fight.gameObject);
                var lblNode = cloneGo.transform.Find("Tmp_Label");
                if (lblNode != null) UnityEngine.Object.Destroy(lblNode.gameObject);
                _editInput = cloneGo.GetComponent<TMP_InputField>();
                var ph = cloneGo.transform.Find("Viewport/Placeholder");
                if (ph != null)
                {
                    var ptxt = ph.GetComponent<TMPro.TextMeshProUGUI>();
                    if (ptxt != null) ptxt.text = "输入或粘贴我的配对码";
                }
            }

            // 按钮：复制 / 确定 / 取消
            MakeDlgBtn(box, "Btn_Copy", "复制", new Vector2(-300f, -100f), () =>
            {
                GUIUtility.systemCopyBuffer = _editInput.text ?? "";
                Debug.Log("[PvpPanel] 已复制（弹窗）：" + _editInput.text);
            });
            MakeDlgBtn(box, "Btn_Ok", "确定", new Vector2(0f, -100f), () =>
            {
                if (_inputMine != null) _inputMine.text = (_editInput.text ?? "").Trim();
                _editRoot.gameObject.SetActive(false);
                Debug.Log("[PvpPanel][调试] 我的码已改为：" + (_inputMine != null ? _inputMine.text : ""));
            });
            MakeDlgBtn(box, "Btn_Cancel", "取消", new Vector2(300f, -100f), () =>
                _editRoot.gameObject.SetActive(false));
        }

        private void MakeDlgBtn(RectTransform box, string name, string label,
                                Vector2 pos, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(180f, 56f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.72f, 0.55f, 0.32f, 1f);
            var btn = go.AddComponent<Button>();
            var tGo = new GameObject("Text", typeof(RectTransform));
            var trt = (RectTransform)tGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var txt = tGo.AddComponent<TMPro.TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 26;
            txt.color = Color.white;
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            btn.onClick.AddListener(new UnityEngine.Events.UnityAction(onClick));
        }

        private void Show(string text)
        {
            if (_tmpResult != null) _tmpResult.text = text;
        }
    }
}
