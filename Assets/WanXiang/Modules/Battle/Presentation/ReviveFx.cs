// ============================================================================
//  ReviveFx —— 复活光柱播放（2026-10-25）
//  ---------------------------------------------------------------------------
//  一次性组件：Spawn(pos, color) 后 0.9s 播完自毁。
//  · 尺寸按**单位世界尺寸**给（调用方传 scale），不要写死像素；
//  · 真实时间推进（演出不吃倍速）；
//  · shader/贴图不可用 ⇒ 静默不生成，绝不留在场上成白块。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class ReviveFx : MonoBehaviour
    {
        private Material _mat;
        private float _t;
        private const float Life = 0.9f;

        public static void Spawn(Vector3 worldPos, Color color, float worldHeight = 1.4f)
        {
            var sh = Resources.Load<Shader>("Battle/ReviveRise");
            var sp = Resources.Load<Sprite>("Battle/FX_shield_hex");
            var noise = Resources.Load<Sprite>("Battle/FX_motion_fog");
            if (sh == null || sp == null || !sh.isSupported) return;

            var go = new GameObject("ReviveFx");
            go.transform.position = worldPos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = 20;               // 立绘之下、HUD 之上

            // 尺寸：宽 = 高 × 0.72（光柱+环共用一个矩形）
            var b = sp.bounds.size;
            float h = Mathf.Max(0.4f, worldHeight);
            go.transform.localScale = new Vector3(h * 0.72f / Mathf.Max(0.001f, b.x),
                                                  h / Mathf.Max(0.001f, b.y), 1f);

            var mat = new Material(sh);
            if (noise != null) mat.SetTexture("_NoiseTex", noise.texture);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Progress", 0f);
            mat.SetFloat("_RingStrength", 0.9f);
            mat.SetFloat("_PillarStrength", 0.85f);
            sr.material = mat;

            var fx = go.AddComponent<ReviveFx>();
            fx._mat = mat;
        }

        private void Update()
        {
            if (_mat == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime;               // 演出用真实时间
            float k = Mathf.Clamp01(_t / Life);
            _mat.SetFloat("_Progress", Mathf.SmoothStep(0f, 1f, k));
            if (k >= 1f)
            {
                Destroy(_mat);
                Destroy(gameObject);
            }
        }
    }
}
