// ============================================================================
//  万相 · 分队选择面板（S1）
//  ---------------------------------------------------------------------------
//  一屏完成「选分队 + 选难度」：
//    · 列表来自 SquadTable.Available(已满足解锁键)；未解锁的显示条件、按钮置灰
//    · 难度 ± 夹在 [1, DifficultyTable.MaxLevel]，右侧显示该档新规则的人话
//    · 底部预览"起手 3/5 只 + 灵卵 + 初始遗物"
//
//  ⚠ prefab 由 Editor 工具生成（菜单「万相/UI/生成 分队选择面板（S1）」），
//    本脚本用 transform.Find 取控件（项目允许：Find 取控件 + 填数据，不运行时拼结构）。
//  ⛔ 不写存档：选择结果放在静态 PendingSquadId / PendingDifficulty，由开局流程读取后清理。
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
    [UIPanel("Panel_SquadSelect", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = true, CloseOnMaskClick = false)]
    public sealed class SquadSelectPanel : UIPanelBase
    {
        /// <summary>玩家选择的分队 id（空 = 默认分队）。开局流程读取后请置空。</summary>
        public static string PendingSquadId = "";
        /// <summary>玩家选择的难度（1~18）。开局流程读取后请置 1。</summary>
        public static int PendingDifficulty = 1;

        private RectTransform _content;
        private GameObject _rowTemplate;
        private TMP_Text _tmpLevel, _tmpRule, _tmpPreview, _tmpEmpty;

        private readonly List<SquadDef> _squads = new List<SquadDef>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private int _selected = -1;
        private int _level = 1;
        /// <summary>继续出征回调（payload 注入，由 HomePanel 出征流程提供）。用完即清。</summary>
        private System.Action _onStart;

        protected override void OnCreate() { }   // 控件在 OnOpenAsync 里按名字取；prefab 结构由编辑器工具生成

        protected override UniTask OnOpenAsync(object payload)
        {
            WanXiang.Modules.UI.DataTables.EnsureInstalled();   // 幂等：直接从主界面打开也不会空表
            _onStart = payload as System.Action;
            // B5-1：挑战分享码（按钮由 builder 增量生成，缺失则跳过）
            var bCopy = transform.Find("Root_Bottom/Btn_CopyCode");
            if (bCopy != null) { var bb = bCopy.GetComponent<Button>(); if (bb != null) bb.onClick.AddListener(OnCopyChallenge); }
            var bImp = transform.Find("Root_Bottom/Btn_ImportCode");
            if (bImp != null) { var bi = bImp.GetComponent<Button>(); if (bi != null) bi.onClick.AddListener(OnImportChallenge); }
            Bind();
            BuildDifficulty();
            BuildList();
            return UniTask.CompletedTask;
        }

        // ------------------------------------------------------------ 取控件
        private void Bind()
        {
            _content = Find<RectTransform>("Scroll_Grid/Viewport/Content");
            _rowTemplate = Find<Transform>("Scroll_Grid/Viewport/Content/Row_Squad_Template")?.gameObject;
            _tmpLevel = Find<TMP_Text>("Root_Difficulty/Tmp_Level");
            _tmpRule = Find<TMP_Text>("Root_Difficulty/Tmp_Rule");
            _tmpPreview = Find<TMP_Text>("Root_Bottom/Tmp_TeamPreview");
            _tmpEmpty = Find<TMP_Text>("Scroll_Grid/Viewport/Content/Tmp_Empty");

            Hook("Root_TopBar/Btn_Close", CloseSelf);
            Hook("Root_Difficulty/Btn_Minus", () => ChangeLevel(-1));
            Hook("Root_Difficulty/Btn_Plus", () => ChangeLevel(+1));
            Hook("Root_Bottom/Btn_Start", StartRun);
        }

        private T Find<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private void Hook(string path, System.Action action)
        {
            var btn = Find<Button>(path);
            if (btn == null) { Debug.LogWarning("[SquadSelect] 缺按钮：" + path); return; }
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }

        // ------------------------------------------------------------ 难度
        private void BuildDifficulty()
        {
            int max = DifficultyTable.MaxLevel > 0 ? DifficultyTable.MaxLevel : 18;
            if (_level < 1) _level = 1;
            if (_level > max) _level = max;
            if (_tmpLevel != null) _tmpLevel.text = "N" + _level;

            var row = DifficultyTable.Row(_level);
            string rule = row == null ? "难度表缺失" : HumanRule(row.NewRule);
            if (_tmpRule != null) _tmpRule.text = rule;

            PendingDifficulty = _level;
        }

        private void ChangeLevel(int delta)
        {
            int max = DifficultyTable.MaxLevel > 0 ? DifficultyTable.MaxLevel : 18;
            int next = Mathf.Clamp(_level + delta, 1, max);
            if (next == _level) return;
            _level = next;
            BuildDifficulty();
        }

        /// <summary>里程碑规则的"人话"（表里是键，玩家看不懂）。</summary>
        private static string HumanRule(string key)
        {
            if (string.IsNullOrEmpty(key)) return "—";
            switch (key)
            {
                case "elite_extra_add": return "精英位多 1 名随从";
                case "boss_phase2": return "首领进入第二阶段";
                case "ai_aggressive": return "敌方 AI 更凶";
                case "turns_minus6": return "回合上限 −6";
                case "enemy_calamity": return "敌方获得劫象";
                case "boss_shield_all": return "首领全队获得护盾";
                default: return key;
            }
        }

        // ------------------------------------------------------------ 分队列表
        private void BuildList()
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
            _squads.Clear();

            if (_rowTemplate != null) _rowTemplate.SetActive(false);
            if (_content == null) { Debug.LogError("[SquadSelect] 找不到 Content"); return; }

            // 解锁键：目前只有默认分队（后续由通关进度补充）
            var satisfied = new HashSet<string>();
            var list = SquadTable.Available(satisfied);
            if (list == null || list.Count == 0)
            {
                if (_tmpEmpty != null) _tmpEmpty.gameObject.SetActive(true);
                if (_tmpPreview != null) _tmpPreview.text = "起手：—";
                return;
            }
            if (_tmpEmpty != null) _tmpEmpty.gameObject.SetActive(false);
            _squads.AddRange(list);

            for (int i = 0; i < _squads.Count; i++) BuildRow(i);

            int pick = 0;
            var def = SquadTable.Default();
            if (def != null) pick = Mathf.Max(0, _squads.FindIndex(s => s.Id == def.Id));
            Select(pick);
        }

        private void BuildRow(int index)
        {
            if (_rowTemplate == null) return;
            var go = Instantiate(_rowTemplate, _content);
            go.name = "Row_" + index;
            go.SetActive(true);
            _rows.Add(go);

            var d = _squads[index];
            SetText(go, "Tmp_Name", d.Name);
            SetText(go, "Tmp_Starter", StarterText(d));
            SetText(go, "Tmp_Desc", d.Desc);

            bool locked = IsLocked(d);
            var unlock = go.transform.Find("Tmp_Unlock");
            if (unlock != null)
            {
                unlock.gameObject.SetActive(locked);
                var t = unlock.GetComponent<TMP_Text>();
                if (t != null) t.text = UnlockText(d.Unlock);
            }

            var btn = go.transform.Find("Btn_Pick")?.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = !locked;
                int captured = index;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Select(captured));
            }

            var accent = go.transform.Find("Img_Accent")?.GetComponent<Image>();
            if (accent != null) accent.color = ElementColor(d.Element);
        }

        private void Select(int index)
        {
            if (index < 0 || index >= _squads.Count) return;
            _selected = index;
            var d = _squads[index];
            PendingSquadId = d.Id;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] == null) continue;
                var bg = _rows[i].GetComponent<Image>();
                if (bg != null) bg.color = i == index
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.72f);
            }

            if (_tmpPreview != null)
            {
                string relic = string.IsNullOrEmpty(d.Relic) ? "无" : d.Relic;
                _tmpPreview.text = "起手：" + StarterText(d) + "　灵卵 " + d.Eggs + "　遗物 " + relic;
            }
        }

        /// <summary>B5-1：把当前选中的分队+难度编成 WX1C- 挑战码进剪贴板。</summary>
        private void OnCopyChallenge()
        {
            if (_selected < 0)
            {
                if (_tmpPreview != null) _tmpPreview.text = "先选一个分队，再复制挑战";
                return;
            }
            string code = WanXiang.Battle.Core.ChallengeCode.Encode(_squads[_selected].Id, _level);
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;
            if (_tmpPreview != null) _tmpPreview.text = "挑战码已复制：" + code;
            Debug.Log("[SquadSelect] 挑战码已复制：" + code);
        }

        /// <summary>B5-1：从剪贴板读 WX1C- 挑战码，预填分队与难度。</summary>
        private void OnImportChallenge()
        {
            string code = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(code))
            {
                if (_tmpPreview != null) _tmpPreview.text = "剪贴板是空的——先复制一条挑战码。";
                return;
            }
            string sid; int lv;
            if (!WanXiang.Battle.Core.ChallengeCode.TryDecode(code, out sid, out lv))
            {
                if (_tmpPreview != null) _tmpPreview.text = "挑战码无效（格式不对或难度超出范围）。";
                return;
            }
            WanXiang.Modules.UI.SquadSelectPanel.PendingSquadId = sid;
            WanXiang.Modules.UI.SquadSelectPanel.PendingDifficulty = lv;
            _level = lv;
            BuildDifficulty();
            int idx = _squads.FindIndex(x => x.Id == sid);
            if (idx >= 0) Select(idx);   // 列表选中 + 预览刷新
            if (_tmpPreview != null) _tmpPreview.text = "已导入好友挑战：" + sid + " · N" + lv;
            Debug.Log("[SquadSelect] 挑战码导入：" + code + " → " + sid + " N" + lv);
        }

        private void StartRun()
        {
            if (_selected < 0)
            {
                if (_tmpPreview != null) _tmpPreview.text = "先选一个分队";
                return;
            }
            var d = _squads[_selected];
            Debug.Log("[SquadSelect] 开始远征：分队=" + d.Id + " 难度=N" + _level);
            var start = _onStart; _onStart = null;   // 用完即清（依赖注入纪律）
            CloseSelf();
            if (start != null) start();              // 回到 HomePanel 出征流程（转场 + 进图）
        }

        // ------------------------------------------------------------ 小工具
        private static void SetText(GameObject row, string path, string text)
        {
            var t = row.transform.Find(path)?.GetComponent<TMP_Text>();
            if (t != null) t.text = text ?? "";
        }

        private static string StarterText(SquadDef d)
        {
            if (d.Starters != null && d.Starters.Count > 0)
                return string.Join(" / ", d.Starters);
            if (!string.IsNullOrEmpty(d.StarterExpr) && d.StarterExpr.StartsWith("random"))
                return d.StarterExpr.StartsWith("random_same_element")
                    ? "同元素随机 " + d.StarterExpr.Substring(d.StarterExpr.IndexOf(':') + 1) + " 只"
                    : "随机 " + d.StarterExpr.Substring(d.StarterExpr.IndexOf(':') + 1) + " 只";
            return "—";
        }

        private static bool IsLocked(SquadDef d)
            => !string.IsNullOrEmpty(d.Unlock)
               && !string.Equals(d.Unlock, "default", System.StringComparison.OrdinalIgnoreCase);

        private static string UnlockText(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            switch (key)
            {
                case "act1_clear": return "通关第 1 幕后解锁";
                case "act3_clear": return "通关第 3 幕后解锁";
                case "clear": return "通关后解锁";
                default: return key.StartsWith("end_") ? "达成特定结局后解锁" : key;
            }
        }

        private static Color ElementColor(Element e)
        {
            switch (e)
            {
                case Element.Wood: return new Color(0.55f, 0.70f, 0.45f, 1f);
                case Element.Fire: return new Color(0.78f, 0.42f, 0.32f, 1f);
                case Element.Earth: return new Color(0.72f, 0.62f, 0.42f, 1f);
                case Element.Metal: return new Color(0.70f, 0.70f, 0.74f, 1f);
                case Element.Water: return new Color(0.42f, 0.60f, 0.72f, 1f);
                default: return new Color(0.62f, 0.60f, 0.56f, 1f);
            }
        }
    }
}
