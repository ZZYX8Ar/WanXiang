// ============================================================================
//  万相 · 连携阵型清单（2026-11-16，partial: BattlePanel）
//  ---------------------------------------------------------------------------
//  为什么做：原来点"连携"直接发第一条可用阵型（代码注释自己写 v1 待补），
//  玩家不知道有 10 个阵型、也不知道"我差一格就能成阵" —— 没有配合/构筑感。
//  现在：居中弹窗列出全部阵型 + 3×3 迷你格（哪些格有人、缺哪格）+ 效果 + 发动。
//
//  UI 全在 Panel_Battle.prefab：Root_ComboPanel / Panel_Combo / Rows / Row_0..9
//  （每行含 Grid_0..8 迷你格 / Tmp_Name / Tmp_Effect / Tmp_Missing / Btn_Fire）
//  ⛔ 禁运行时建 UI；本文件只做绑定、填字、上色、显隐。
// ============================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    public sealed partial class BattlePanel
    {
        private const int ComboRowCount = 10;

        private GameObject _rootComboPanel;
        private Button _comboCloseBtn;
        private TMP_Text _comboCountText;
        private readonly RectTransform[] _comboRows = new RectTransform[ComboRowCount];
        private readonly Image[] _comboRowBg = new Image[ComboRowCount];
        private readonly Image[,] _comboCells = new Image[ComboRowCount, 9];
        private readonly TMP_Text[] _comboNames = new TMP_Text[ComboRowCount];
        private readonly TMP_Text[] _comboEffects = new TMP_Text[ComboRowCount];
        private readonly TMP_Text[] _comboMissing = new TMP_Text[ComboRowCount];
        private readonly Button[] _comboFireBtns = new Button[ComboRowCount];
        private readonly List<ComboRules.ComboStatus> _comboStatus = new List<ComboRules.ComboStatus>();
        private bool _comboPanelOpen;

        private void BuildComboPanel()
        {
            var rootT = transform.Find("Root_ComboPanel");
            if (rootT == null) return;
            _rootComboPanel = rootT.gameObject;

            var closeT = rootT.Find("Panel_Combo/Btn_Close");
            if (closeT != null)
            {
                _comboCloseBtn = closeT.GetComponent<Button>();
                if (_comboCloseBtn != null) _comboCloseBtn.onClick.AddListener(CloseComboPanel);
            }
            var cnt = rootT.Find("Panel_Combo/Tmp_Count");
            if (cnt != null) _comboCountText = cnt.GetComponent<TMP_Text>();

            var rows = rootT.Find("Panel_Combo/Rows");
            if (rows == null) return;
            for (int i = 0; i < ComboRowCount; i++)
            {
                var r = rows.Find("Row_" + i) as RectTransform;
                if (r == null) continue;
                _comboRows[i] = r;
                _comboRowBg[i] = r.GetComponent<Image>();
                _comboNames[i] = r.Find("Tmp_Name")?.GetComponent<TMP_Text>();
                _comboEffects[i] = r.Find("Tmp_Effect")?.GetComponent<TMP_Text>();
                _comboMissing[i] = r.Find("Tmp_Missing")?.GetComponent<TMP_Text>();
                var fb = r.Find("Btn_Fire");
                if (fb != null)
                {
                    _comboFireBtns[i] = fb.GetComponent<Button>();
                    int captured = i;
                    if (_comboFireBtns[i] != null)
                        _comboFireBtns[i].onClick.AddListener(() => OnComboFireClicked(captured));
                }
                for (int g = 0; g < 9; g++)
                {
                    var cell = r.Find("Grid_" + g);
                    if (cell != null) _comboCells[i, g] = cell.GetComponent<Image>();
                }
            }
            _rootComboPanel.SetActive(false);
        }

        /// <summary>
        /// 打开连携清单（替代原来的"直接发第一条"）。
        /// 暂停战场：复用 _halted（与"重新挑战确认框"同一套冻结机制）。
        /// </summary>
        private void OpenComboPanel()
        {
            if (_play == null || !_play.AwaitingCommand || _comboPanelOpen) return;
            if (_rootComboPanel == null) return;
            _autoBattle = false;
            _comboPanelOpen = true;
            _halted = true;
            RefreshComboPanel();
            _rootComboPanel.SetActive(true);
            Sfx("sfx_bt_cmd_select");
        }

        private void CloseComboPanel()
        {
            if (!_comboPanelOpen) return;
            _comboPanelOpen = false;
            _halted = false;
            if (_rootComboPanel != null) _rootComboPanel.SetActive(false);
            RefreshActionBar();
        }

        private void OnComboFireClicked(int row)
        {
            if (!_comboPanelOpen || _play == null) return;
            if (row < 0 || row >= _comboStatus.Count) return;
            var st = _comboStatus[row];
            if (st == null || !st.Ready || st.Def == null) return;

            // ⛔ 不要在这里 SelectActor 切换待令者：PendingActorSelect 要到下一个决策点才生效，
            //   而 SubmitCombo 立刻消费指令 ⇒ CanFire 检的仍是旧待令单位，必然失败
            //   （实测：点了发动、面板关了、灵力没扣、连携没打出去）。
            //   正确语义：连携由**当前待令单位**发动；它不在中宫时面板根本不会显示可发动。
            bool ok = _play.SubmitCombo(st.Def.Id);
            Debug.Log("[Combo] 发动 " + st.Def.Name + " → " + (ok ? "成功" : "失败"));
            if (ok)
            {
                CloseComboPanel();
                Sfx("sfx_bt_cmd_confirm");
            }
        }

        /// <summary>填清单：名称 / 效果 / 迷你格 / 缺格 / 可发动。</summary>
        private void RefreshComboPanel()
        {
            if (_play == null || _play.State == null) return;
            // ★ 可发动判定必须用**当前待令单位**（与实战 CanFire 同一个人），
            //   否则会出现"面板说能发动、点了却失败"。
            var actor = _play.PendingUnit;
            _comboStatus.Clear();
            _comboStatus.AddRange(ComboRules.Overview(_play.State, actor));


            int ready = 0;
            for (int i = 0; i < ComboRowCount; i++)
            {
                if (_comboRows[i] == null) continue;
                if (i >= _comboStatus.Count)
                {
                    _comboRows[i].gameObject.SetActive(false);
                    continue;
                }
                _comboRows[i].gameObject.SetActive(true);
                var st = _comboStatus[i];
                var def = st.Def;
                if (st.Ready) ready++;

                if (_comboNames[i] != null) _comboNames[i].text = def != null ? def.Name : "?";
                if (_comboEffects[i] != null) _comboEffects[i].text = def != null ? def.Note : "";
                if (_comboMissing[i] != null)
                {
                    if (st.Ready) { _comboMissing[i].text = "可发动"; _comboMissing[i].color = ComboOk(); }
                    else
                    {
                        string txt = MissingText(st, def);
                        _comboMissing[i].text = txt;
                        _comboMissing[i].color = ComboWarn();
                    }
                }

                var bg = _comboRowBg[i];
                if (bg != null)
                    bg.color = st.Ready ? ComboReadyBg() : (NearReady(st) ? ComboNearBg() : Color.white);

                if (_comboFireBtns[i] != null)
                    _comboFireBtns[i].gameObject.SetActive(st.Ready);

                PaintComboCells(i, st, def);
            }

            if (_comboCountText != null)
                _comboCountText.text = ready + " / " + _comboStatus.Count + " 可发动";
        }

        /// <summary>迷你格上色：红边=阵型需要但这格没人；绿=已占且需要；浅灰=与阵型无关。</summary>
        private void PaintComboCells(int row, ComboRules.ComboStatus st, ComboDef def)
        {
            int[] need = def != null ? def.Cells : null;
            int[] needAlt = def != null ? def.CellsAlt : null;
            // 若备用阵型更接近，用它来展示（与 MissingCells 取最优组一致）
            if (needAlt != null && needAlt.Length > 0 && st.MissingCells != null)
            {
                int altMiss = CountMissing(needAlt, st.MissingCells);
                int mainMiss = need != null ? CountMissing(need, st.MissingCells) : int.MaxValue;
                if (altMiss < mainMiss) need = needAlt;
            }

            for (int g = 0; g < 9; g++)
            {
                var img = _comboCells[row, g];
                if (img == null) continue;
                bool wanted = Contains(need, g);
                bool occupied = IsOurCellOccupied(g);
                if (wanted && occupied) img.color = ComboCellOn();
                else if (wanted) img.color = ComboCellMiss();
                else img.color = ComboCellIdle();
            }
        }

        private bool _comboCellBufValid;
        private readonly bool[] _comboCellOcc = new bool[9];

        /// <summary>当前我方存活单位占了哪些格（每次刷新算一遍，避免 9×5 次重复遍历）。</summary>
        private bool IsOurCellOccupied(int cell)
        {
            var st = _play != null ? _play.State : null;
            if (st == null) return false;
            var mine = st.UnitsOf(TeamSide.Player);
            if (mine == null) return false;
            for (int i = 0; i < mine.Count; i++)
            {
                var u = mine[i];
                if (u != null && u.IsAlive && u.Pos.IsValid && u.Pos.Index == cell) return true;
            }
            return false;
        }

        private static bool Contains(int[] arr, int v)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return true;
            return false;
        }

        private static int CountMissing(int[] cells, int[] missing)
        {
            if (cells == null || missing == null) return 0;
            int n = 0;
            for (int i = 0; i < cells.Length; i++) if (Contains(missing, cells[i])) n++;
            return n;
        }

        private static bool NearReady(ComboRules.ComboStatus st)
            => st.MissingCells != null && st.MissingCells.Length > 0 && st.MissingCells.Length <= 2;

        private static string MissingText(ComboRules.ComboStatus st, ComboDef def)
        {
            if (st.MissingCells != null && st.MissingCells.Length > 0)
            {
                var sb = new System.Text.StringBuilder("还差：");
                for (int i = 0; i < st.MissingCells.Length; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append(ComboRules.CellName(st.MissingCells[i]));
                }
                return sb.ToString();
            }
            return string.IsNullOrEmpty(st.Reason) ? "条件不足" : st.Reason;
        }

        private static Color Tune(Color c)
            => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        private static Color ComboOk() => Tune(new Color(0.06f, 0.36f, 0.27f, 1f));
        private static Color ComboWarn() => Tune(new Color(0.55f, 0.18f, 0.12f, 1f));
        private static Color ComboReadyBg() => Tune(new Color(0.88f, 0.95f, 0.90f, 1f));
        private static Color ComboNearBg() => Tune(new Color(0.98f, 0.95f, 0.87f, 1f));
        private static Color ComboCellOn() => Tune(new Color(0.37f, 0.72f, 0.60f, 1f));
        private static Color ComboCellMiss() => Tune(new Color(0.90f, 0.52f, 0.45f, 1f));
        private static Color ComboCellIdle() => Tune(new Color(0.84f, 0.82f, 0.78f, 1f));
    }
}
