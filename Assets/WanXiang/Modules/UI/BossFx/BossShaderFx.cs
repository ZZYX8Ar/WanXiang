// 万相 · Boss 机制 Shader 演出（注册表驱动）
// 用现成的 WanXiang/BossSkillBurst：_Progress 0→1 播一圈爆发，结束自动销毁。
// 加特效 = BossFxRegistry.Register 一行 + 一行参数表；不写死在 Hook 里。
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Modules.UI.BossFx
{
    /// <summary>爆发参数（按机制族给不同颜色/形状，表驱动）。</summary>
    public readonly struct BurstStyle
    {
        public readonly Color Tint;
        public readonly float Shape;       // BossSkillBurst._Shape：0 圆环 / 1 刃 / 2 晶 / 3 眼
        public readonly float Intensity;
        public BurstStyle(Color tint, float shape, float intensity)
        { Tint = tint; Shape = shape; Intensity = intensity; }
    }

    public static class BurstStyles
    {
        public static readonly BurstStyle Default = new BurstStyle(new Color(0.95f, 0.72f, 0.30f, 1f), 0f, 1.2f);
        public static readonly BurstStyle Dice    = new BurstStyle(new Color(0.85f, 0.62f, 0.30f, 1f), 2f, 1.4f);
        public static readonly BurstStyle Control = new BurstStyle(new Color(0.48f, 0.30f, 0.62f, 1f), 3f, 1.3f);
        public static readonly BurstStyle Damage  = new BurstStyle(new Color(0.85f, 0.28f, 0.22f, 1f), 1f, 1.5f);
        public static readonly BurstStyle Heal    = new BurstStyle(new Color(0.40f, 0.72f, 0.42f, 1f), 0f, 1.1f);
    }

    public sealed class BossShaderFx : IBossFx
    {
        private readonly BurstStyle _style;
        public BossShaderFx(BurstStyle style) { _style = style; }

        public void Play(FxContext ctx)
        {
            var sh = Shader.Find("WanXiang/BossSkillBurst");
            if (sh == null) { Debug.LogWarning("[BossFx] 找不到 BossSkillBurst shader"); return; }
            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null) return;
            var go = new GameObject("Fx_BossBurst", typeof(RectTransform), typeof(Image));
            var rt = go.transform as RectTransform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var mat = new Material(sh);
            mat.SetColor("_Color", _style.Tint);
            mat.SetFloat("_Shape", _style.Shape);
            mat.SetFloat("_Intensity", _style.Intensity);
            img.material = mat;
            Animate(img, mat, go).Forget();
        }

        private static async UniTaskVoid Animate(Image img, Material mat, GameObject go)
        {
            const int frames = 30;
            for (int i = 0; i <= frames; i++)
            {
                if (go == null) return;
                mat.SetFloat("_Progress", i / (float)frames);
                await UniTask.DelayFrame(1);
            }
            if (go != null) Object.Destroy(go);
            if (mat != null) Object.Destroy(mat);
        }
    }
}
