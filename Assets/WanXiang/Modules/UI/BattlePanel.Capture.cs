// ============================================================================
//  万相 · 战斗内捕获接线（BattlePanel 分部，2026-11-13）
//  点精灵球 → 进入选目标 → 点敌方格 → CaptureFlow 判定 → Presenter 播动画/结果。
// ============================================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    public sealed partial class BattlePanel
    {
        private T FindNode<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private Button _btnCapture;
        private RectTransform _captureBall;
        private Image _captureGlow;
        private TMP_Text _captureResult;
        private BattleCapturePresenter _capturePresenter;
        private bool _captureAiming;
        private bool _captureTipShown;

        private void InitCapture()
        {
            _btnCapture = FindNode<Button>("Root_Action/Btn_Capture");
            _captureBall = FindNode<RectTransform>("Img_CaptureBall");
            _captureGlow = FindNode<Image>("Img_CaptureGlow");
            _captureResult = FindNode<TMP_Text>("Tmp_CaptureResult");
            if (_btnCapture == null) { Debug.LogWarning("[Capture] prefab 缺 Btn_Capture"); return; }
            if (_captureBall == null || _captureGlow == null || _captureResult == null)
            { Debug.LogWarning("[Capture] prefab 缺球/光环/结果节点"); return; }

            _capturePresenter = new BattleCapturePresenter(
                _captureBall, _captureGlow, _captureResult,
                _btnCapture.transform as RectTransform);
            _btnCapture.onClick.RemoveAllListeners();
            _btnCapture.onClick.AddListener(ToggleCaptureAim);

        }

        private void ToggleCaptureAim()
        {
            _captureAiming = !_captureAiming;
            if (!_captureAiming) { _stage?.HighlightCells(null, null); return; }
            // 高亮可捕的敌方格（复用目标预览色）
            var cells = new System.Collections.Generic.List<int>();
            var foes = _play != null && _play.State != null
                ? _play.State.UnitsOf(TeamSide.Enemy) : null;
            if (foes != null)
                for (int i = 0; i < foes.Count; i++)
                {
                    var f = foes[i];
                    if (f != null && f.IsAlive && f.Def != null && CaptureRules.IsCapturable(f.Def)
                        && f.Pos.IsValid) cells.Add(f.Pos.Index);
                }
            _stage?.HighlightCaptureCells(cells);   // 金色：与技能预览的橙红区分
            _capturePresenter?.ShowHint("选择要捕获的敌方异兽（金色格）", "#B8860B");
            if (cells.Count == 0)
            {
                _captureAiming = false;
                var runNow = WanXiang.Run.RunSave.Current;
                string why = runNow != null && runNow.Eggs <= 0
                    ? "灵卵不足，无法捕获"
                    : "当前没有可捕获的目标\n（首领与召唤物不可捕获）";
                _capturePresenter?.ShowHint(why);
                Debug.Log("[Capture] 无法进入捕获：" + why);
            }
        }

        /// <summary>每帧：鼠标悬停捕获按钮 → 显示说明（轮询比 EventTrigger 可靠）。</summary>
        private void PollCaptureHover()
        {
            if (_btnCapture == null) return;
            var rt = _btnCapture.transform as RectTransform;
            if (rt == null) return;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
            if (over && !_captureTipShown)
            {
                _captureTipShown = true;
                var runE = WanXiang.Run.RunSave.Current;
                int eggsNow = runE != null ? runE.Eggs : 0;
                ShowHintTip("捕获",
                    "点击后在九宫格选择一只敌方异兽。\n当前灵卵：" + eggsNow
                    + "（消耗按品级 1~3）\n目标血量越低成功率越高；首领与召唤物不可捕获，失败返还部分灵卵。");
            }
            else if (!over && _captureTipShown)
            {
                _captureTipShown = false;
                HideSkillTip();
            }
        }

        /// <summary>每帧：瞄准中且点了敌方格 → 投球。</summary>
        private void PollCaptureClick()
        {
            if (!_captureAiming || _capturePresenter == null || _stage == null) return;
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            if (!Input.GetMouseButtonDown(0)) return;
            var cam = _stage.Camera != null ? _stage.Camera : Camera.main;
            if (cam == null) return;
            var world = cam.ScreenToWorldPoint(Input.mousePosition);
            string uid, uname; Vector3 anchor;
            if (!_stage.TryPickUnit(world, out uid, out uname, out anchor)) return;

            var st = _play != null ? _play.State : null;
            if (st == null) return;
            BattleUnit target = null;
            var all = st.AllUnitsEver;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].RuntimeId == uid) { target = all[i]; break; }
            if (target == null) return;

            _captureAiming = false;
            _stage.HighlightCells(null, null);
            var o = CaptureFlow.TryInBattle(st, WanXiang.Run.RunSave.Current, target);


            var canvasRt = transform as RectTransform;
            // ★ 落点 = **格子中心**（单位头顶锚点会让球悬在格子上沿，用户 11-13 报障）
            Vector3 cellWorld = target.Pos.IsValid
                ? _stage.CellWorldCenter(TeamSide.Enemy, target.Pos.Index) : anchor;
            Vector2 targetLocal;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRt, cam.WorldToScreenPoint(cellWorld), null, out targetLocal);
            Vector2 startLocal;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRt, RectTransformUtility.WorldToScreenPoint(null, _btnCapture.transform.position),
                null, out startLocal);
            _capturePresenter.Play(o, targetLocal, startLocal, () =>
            {
                // ★ 球定格后再消散（用户：消失太快）；并清掉该单位的状态悬浮面板
                _stage.CaptureVanish(target.RuntimeId);
                _hoverUnitId = null;
                HideStatusTip();
            });
        }

        private void CancelCaptureAim()
        {
            if (!_captureAiming) return;
            _captureAiming = false;
            _stage?.HighlightCells(null, null);
            _capturePresenter?.Cancel();
        }
    }
}
