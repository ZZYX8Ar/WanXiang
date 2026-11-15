// 万相 · 骰子演出面板（UIPanel，走 UISystem 管理）
// 派发器用 OpenAsync<DiceRollFx>(payload) 打开；面板在 OnOpenAsync 里播抛骰动画 → 定格点数 → 自动关闭
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI.BossFx
{
    /// <summary>打开骰子演出时传的载荷（表现层只读，不碰战斗状态）。</summary>
    public sealed class DiceRollPayload
    {
        public string Prefix;
        public int Face;
        public int[] Dice;
        public string Text;
    }

    [UIPanel("Panel_DiceRoll", Layer = UILayer.Overlay, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class DiceRollFx : UIPanelBase
    {
        private readonly TMP_Text[] _pips = new TMP_Text[3];
        private readonly RectTransform[] _dice = new RectTransform[3];
        private TMP_Text _title, _result;

        protected override void OnCreate()
        {
            _title = Find<TMP_Text>("Root_Card/Tmp_Title");
            _result = Find<TMP_Text>("Root_Card/Tmp_Result");
            for (int i = 0; i < 3; i++)
            {
                _dice[i] = Find<RectTransform>("Root_Card/Dice_" + i);
                var p = _dice[i] != null ? _dice[i].Find("Tmp_Pips") : null;
                _pips[i] = p != null ? p.GetComponent<TMP_Text>() : null;
            }
        }

        private T Find<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        protected override async UniTask OnOpenAsync(object payload)
        {
            var p = payload as DiceRollPayload;
            await Play(p);
        }

        // ★ Cached 面板第二次打开走 OnRefresh（不是 OnOpenAsync）→ 动画必须两个入口都能播
        protected override void OnRefresh(object payload)
        {
            var p = payload as DiceRollPayload;
            if (p != null) Play(p).Forget();
        }

        private async UniTask Play(DiceRollPayload p)
        {
            if (p == null) { CloseSelf(); return; }
            if (_title != null) _title.text = string.IsNullOrEmpty(p.Prefix) ? "庄家开赌" : p.Prefix;
            if (_result != null) _result.text = "";

            // ① 抛掷：三颗骰子翻点数 + 摇晃 + 缩放
            for (int f = 0; f < 42; f++)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (_dice[i] == null) continue;
                    _dice[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(f * 0.9f + i) * 24f);
                    _dice[i].localScale = Vector3.one * (1f + Mathf.Sin(f * 0.7f + i * 1.3f) * 0.12f);
                    if (_pips[i] != null) _pips[i].text = Pip(1 + ((f * 7 + i * 5) % 6));
                }
                await UniTask.DelayFrame(1);
            }
            // ② 落定：定格真实点数
            for (int i = 0; i < 3; i++)
            {
                if (_dice[i] != null) { _dice[i].localRotation = Quaternion.identity; _dice[i].localScale = Vector3.one; }
                if (_pips[i] != null) _pips[i].text = Pip(p.Dice != null && i < p.Dice.Length ? p.Dice[i] : 1);
            }
            if (_result != null && p.Dice != null && p.Dice.Length >= 3)
                _result.text = "三骰 " + p.Dice[0] + " + " + p.Dice[1] + " + " + p.Dice[2]
                             + "  →  第 " + p.Face + " 点 · " + p.Text;
            await UniTask.Delay(1000);
            Debug.Log("[DiceRollFx] 抛骰完成：第 " + p.Face + " 点");
            CloseSelf();
        }

        /// <summary>数字代替 ⚀ 系列（项目字体缺字形会显示口口）。</summary>
        private static string Pip(int n) => n >= 1 && n <= 6 ? n.ToString() : "?";
    }
}
