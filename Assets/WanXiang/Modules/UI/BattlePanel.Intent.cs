// ============================================================================
//  万相 · 敌人意图徽章（2026-11-16，partial: BattlePanel）
//  ---------------------------------------------------------------------------
//  UI 全在 Panel_Battle.prefab：Root_EnemyIntent / Intent_0..8 / Tmp_Text。
//  代码只做：绑定 → 按敌人格号填文本/颜色 → 显隐。**禁止运行时建 UI**。
//  ⚠ 徽章钉在棋盘格上方（用户 2026-11-16 定案），不跟随单位移动，避免冲锋乱飘。
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
        private const int IntentSlotCount = 9;
        // 徽章相对格子中心上移的距离（像素）。抬高到 132 是因为两行文字（数值+目标）后
        // 96 会压住敌人立绘头部（第 4 幕 3+3 密排时实测）。
        private const float IntentYOffset = 132f;
        private RectTransform _intentHolder;
        private RectTransform[] _intentNodes;
        private TMP_Text[] _intentTexts;
        private Image[] _intentBg;
        private readonly IntentInfo[] _intentBuf = new IntentInfo[IntentSlotCount];

        private void BuildEnemyIntent()
        {
            _intentNodes = new RectTransform[IntentSlotCount];
            _intentTexts = new TMP_Text[IntentSlotCount];
            _intentBg = new Image[IntentSlotCount];
            var holder = transform.Find("Root_Stage/Root_EnemyIntent") as RectTransform;
            _intentHolder = holder;
            if (holder == null) return;
            for (int i = 0; i < IntentSlotCount; i++)
            {
                var n = holder.Find("Intent_" + i) as RectTransform;
                if (n == null) continue;
                _intentNodes[i] = n;
                _intentTexts[i] = n.Find("Tmp_Text")?.GetComponent<TMP_Text>();
                _intentBg[i] = n.GetComponent<Image>();
                n.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 刷新敌人意图：读 Core 的同源预告，按敌人当前格号填徽章。
        /// </summary>
        // ★ 2026-11-17 用户："还是不要这个敌人的攻击意图了，都把敌人遮挡完了"
        //   Core 的 EnemyIntentReader 保留（自检/以后要用），但 UI 层整体关闭。
        //   想重新开启时：把本常量改 true 即可，其余逻辑无需动。
        private const bool ShowEnemyIntent = false;

        private void RefreshEnemyIntent()
        {
            if (!ShowEnemyIntent) { HideAllIntents(); return; }
            if (_intentNodes == null || _play == null || _play.State == null) return;
            var st = _play.State;
            var foes = st.UnitsOf(TeamSide.Enemy);
            // ⚠ 未部署（战斗还没开始）时不清空，否则最后空刷新会灭掉已填的意图。
            //   已开局但敌人全灭 → 照常清空（不留残影）。
            bool deploying = st.Turn <= 0;
            if (foes == null || (foes.Count == 0 && deploying)) return;

            // 先全隐藏（回合切换/敌人死亡时不能留残影）
            HideAllIntents();

            for (int i = 0; i < foes.Count; i++)
            {
                var u = foes[i];
                if (u == null || !u.IsAlive) continue;
                int cell = u.Pos.Index;   // GridPos 是 struct，不能用 != null 判断（永远为真）
                if (cell < 0 || cell >= IntentSlotCount || _intentNodes[cell] == null) continue;

                var info = EnemyIntentReader.Read(st, u);
                var tx = _intentTexts[cell];
                if (tx != null)
                {
                    // ★ 2026-11-16：副行显示"要打谁" —— 玩家知道核心输出被盯上才会去保护/换位。
                    //   群攻/无目标时只显示主行（不给假信息）。
                    tx.text = string.IsNullOrEmpty(info.TargetLabel)
                        ? "<size=24>" + info.Label + "</size>"
                        : "<size=24>" + info.Label + "</size>\n<size=13>→ " + info.TargetLabel + "</size>";
                    tx.color = IntentInk(info.Kind);
                }

                var node = _intentNodes[cell];
                node.gameObject.SetActive(true);
                // ★ 2026-11-16 用户："意图位置怎么偏左" ——
                //   原来靠 prefab 里硬编码的 anchoredPosition，与相机/棋盘换算有系统偏差。
                //   改为按敌人格子的世界中心换算屏幕坐标，再转 holder 本地坐标，永不偏。
                PlaceIntentAtCell(node, cell);
                // 终结技加高亮底（其余保持浅纸卡）
                if (_intentBg[cell] != null)
                    _intentBg[cell].color = info.IsUltimate ? new Color(1f, 0.92f, 0.70f, 1f) : Color.white;
            }
        }

        private void HideAllIntents()
        {
            if (_intentNodes == null) return;
            for (int i = 0; i < IntentSlotCount; i++)
                if (_intentNodes[i] != null) _intentNodes[i].gameObject.SetActive(false);
        }

        /// <summary>把徽章钉到指定敌人格子的正上方（世界坐标 → 屏幕 → holder 本地）。</summary>
        private void PlaceIntentAtCell(RectTransform node, int cell)
        {
            if (node == null || _intentHolder == null || _stage == null) return;
            var cam = _stage.Camera;
            if (cam == null) return;

            Vector3 world = _stage.CellWorldCenter(TeamSide.Enemy, cell);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;   // 在相机背后

            Vector2 local;
            // Overlay Canvas：cam 参数必须传 null
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _intentHolder, screen, null, out local)) return;

            node.anchoredPosition = local + new Vector2(0f, IntentYOffset);
        }

        private Color IntentInk(IntentKind k)
        {
            // 浅纸卡底（白）→ 深墨字为主；终结技用深红强调（对比度仍足够）
            if (k == IntentKind.Ultimate)
            {
                var c = new Color(0.55f, 0.12f, 0.10f, 1f);
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            }
            var ink = new Color(0.165f, 0.129f, 0.094f, 1f);   // 与 Tmp_WeatherName 同墨色
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? ink.linear : ink;
        }
    }
}
