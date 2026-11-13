// ============================================================================
//  万相 · 战斗内捕获表现层（2026-11-13）
//  ---------------------------------------------------------------------------
//  ⛔ 禁用 ParticleSystem：全部用 prefab 里的 Image + DOTween 做程序化动画。
//  四帧：投掷弧线 → 目标吸入/球合拢 → 晃动 3 次 → 成功金光 / 失败红光+弹回。
//  结果文字明确写：成功率、消耗、返还、成功与否（用户："成没成功你也没有说"）。
// ============================================================================
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI
{
    public sealed class BattleCapturePresenter
    {
        private readonly RectTransform _ball;
        private readonly Image _ballImg;
        private readonly Image _glow;
        private readonly TMP_Text _result;
        private readonly RectTransform _button;
        private readonly CanvasGroup _ballGroup;
        private readonly CanvasGroup _glowGroup;
        private readonly CanvasGroup _resultGroup;
        private readonly Material _glowMat;      // CaptureBurst 材质实例（_Progress 驱动）
        private Sequence _seq;

        public BattleCapturePresenter(RectTransform ball, Image glow, TMP_Text result, RectTransform button)
        {
            _ball = ball; _ballImg = ball != null ? ball.GetComponent<Image>() : null;
            _glow = glow; _result = result; _button = button;
            _ballGroup = ball != null ? (ball.GetComponent<CanvasGroup>() ?? ball.gameObject.AddComponent<CanvasGroup>()) : null;
            _glowGroup = glow != null ? (glow.GetComponent<CanvasGroup>() ?? glow.gameObject.AddComponent<CanvasGroup>()) : null;
            _resultGroup = result != null ? (result.GetComponent<CanvasGroup>() ?? result.gameObject.AddComponent<CanvasGroup>()) : null;
            // 光圈材质实例化一份（避免污染共享材质，多个面板/多场战斗互不影响）
            if (glow != null && glow.material != null && glow.material.shader != null)
            {
                _glowMat = new Material(glow.material);
                glow.material = _glowMat;
            }
        }

        /// <summary>播一次投球。targetLocal = 目标格在 Canvas 局部坐标里的位置。</summary>
        public void Play(CaptureOutcome o, Vector2 targetLocal, Vector2 startLocal)
        {
            if (_ball == null) return;
            _seq?.Kill();
            _ball.gameObject.SetActive(true);
            if (_glow != null) _glow.gameObject.SetActive(true);
            if (_result != null) _result.gameObject.SetActive(true);
            _ball.localScale = Vector3.one * 0.9f;
            _ball.anchoredPosition = startLocal;
            if (_ballGroup != null) _ballGroup.alpha = 1f;
            if (_glowGroup != null) _glowGroup.alpha = 0f;
            if (_resultGroup != null) _resultGroup.alpha = 0f;
            _glow.rectTransform.anchoredPosition = targetLocal;
            _glow.rectTransform.localScale = Vector3.one * 0.3f;
            if (_glowMat != null && _glowMat.HasProperty("_Progress")) _glowMat.SetFloat("_Progress", 0f);

            // ① 弧线投掷（自定义插值：直线 + sin 抬高）
            var seq = DOTween.Sequence();
            float flight = 0.52f;
            seq.Append(DOTween.To(t =>
            {
                float k = Mathf.Clamp01(t);
                Vector2 p = Vector2.Lerp(startLocal, targetLocal, k);
                p.y += Mathf.Sin(Mathf.PI * k) * 110f;
                _ball.anchoredPosition = p;
                _ball.localRotation = Quaternion.Euler(0f, 0f, -720f * k);
            }, 0f, 1f, flight).SetEase(Ease.InOutSine));

            // ② 合拢 + 目标吸入感（球缩小再弹回）
            seq.Append(_ball.DOScale(0.55f, 0.12f).SetEase(Ease.InQuad));
            seq.Append(_ball.DOScale(1f, 0.16f).SetEase(Ease.OutBack));

            // ③ 晃动 3 次
            for (int i = 0; i < 3; i++)
            {
                seq.Append(_ball.DORotate(new Vector3(0f, 0f, 22f), 0.13f).SetEase(Ease.InOutSine));
                seq.Append(_ball.DORotate(new Vector3(0f, 0f, -22f), 0.13f).SetEase(Ease.InOutSine));
            }
            seq.Append(_ball.DORotate(Vector3.zero, 0.10f));

            // ④ 结果：光环扩散 + 文字；失败球弹回按钮
            if (o.Ok && o.Success)
            {
                seq.AppendCallback(() =>
                {
                    _glow.color = new Color(1f, 0.84f, 0.35f, 0.9f);
                    if (_result != null) _result.text = "<color=#B8860B>捕获成功！</color>\n「" + o.Name
                        + "」已收录图鉴　（成功率 " + o.Chance + "% · 消耗 " + o.Cost + " 灵卵）";
                });
                seq.Append(_glow.rectTransform.DOScale(2.1f, 0.46f).SetEase(Ease.OutCubic));
                seq.Join(_glowGroup.DOFade(1f, 0.10f));
                if (_glowMat != null && _glowMat.HasProperty("_Progress"))
                    seq.Join(DOTween.To(() => _glowMat.GetFloat("_Progress"),
                                        v => _glowMat.SetFloat("_Progress", v), 1f, 0.46f));
                seq.Join(_glowGroup.DOFade(0f, 0.32f).SetDelay(0.30f));
                seq.Join(_resultGroup.DOFade(1f, 0.18f));
            }
            else
            {
                string why = o.Ok ? "挣脱了！返还 " + o.Refund + " 灵卵　（成功率 " + o.Chance + "%）" : o.Reason;
                seq.AppendCallback(() =>
                {
                    _glow.color = new Color(0.88f, 0.35f, 0.30f, 0.85f);
                    if (_result != null) _result.text = "<color=#A33A2D>捕获失败</color>\n" + why;
                });
                seq.Append(_glow.rectTransform.DOScale(1.5f, 0.34f));
                seq.Join(_glowGroup.DOFade(1f, 0.10f));
                if (_glowMat != null && _glowMat.HasProperty("_Progress"))
                    seq.Join(DOTween.To(() => _glowMat.GetFloat("_Progress"),
                                        v => _glowMat.SetFloat("_Progress", v), 1f, 0.34f));
                seq.Join(_glowGroup.DOFade(0f, 0.30f).SetDelay(0.16f));
                seq.Join(_resultGroup.DOFade(1f, 0.18f));
                // 失败：球原地弹跳一下后消散（飞回按钮会导致"球残留在按钮旁边"，用户 11-13 报障）
                seq.Join(_ball.DOPunchPosition(new Vector3(0f, 18f, 0f), 0.28f, 6, 0.6f));
            }

            // 收尾：结果文字停留 1.8s 后淡出，球隐藏
            if (_resultGroup != null) seq.Append(_resultGroup.DOFade(0f, 0.35f).SetDelay(1.8f));
            if (_ballGroup != null) seq.Append(_ballGroup.DOFade(0f, 0.22f));
            seq.OnComplete(() =>
            {
                // 兜底：任何分支 Killed/Completed 都强制收干净，避免球残留在按钮旁
                _ball.gameObject.SetActive(false);
                if (_glow != null) _glow.gameObject.SetActive(false);
                if (_result != null) _result.gameObject.SetActive(false);
                _ball.localRotation = Quaternion.identity;
                if (_ballGroup != null) _ballGroup.alpha = 1f;
            });
            seq.OnKill(() =>
            {
                _ball.gameObject.SetActive(false);
                if (_glow != null) _glow.gameObject.SetActive(false);
                if (_result != null) _result.gameObject.SetActive(false);
            });
            _seq = seq;
        }

        public void Cancel()
        {
            _seq?.Kill();
            if (_ball != null) _ball.gameObject.SetActive(false);
            if (_glow != null) _glow.gameObject.SetActive(false);
            if (_result != null) _result.gameObject.SetActive(false);
        }
    }
}
