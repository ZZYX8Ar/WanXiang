// ============================================================================
//  BossSkillBurst —— 首领技能爆发（2026-10-26）
//  ---------------------------------------------------------------------------
//  用户："首领跟小兵没区别，给他们做点特效"。
//  这个组件只做一件事：在目标位置放一张**加色**四边形，跑 `WanXiang/BossSkillBurst`，
//  0.45s 内把 _Progress 从 0 推到 1 然后自毁。
//  ⚠ 不改战斗数据、不碰 SkillFxPlayer（终结技那套保持原样）。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class BossSkillBurst : MonoBehaviour
    {
        private Material _mat;
        private float _t;
        private float _dur = 0.45f;
        private Color _color;
        private float _scale;
        private Vector3 _from, _to;

        public static void Play(Vector3 center, Color color, float radius = 2.2f,
                                int streaks = 16, float dur = 0.45f, int shape = 0)
        {
            var sh = Shader.Find("WanXiang/BossSkillBurst");
            if (sh == null) return;                       // 没有 shader 就静默跳过（不影响战斗）

            var go = new GameObject("BossSkillBurst");
            go.transform.position = center;

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(go.transform, false);
            q.transform.localScale = Vector3.one * radius * 2f;
            var mr = q.GetComponent<MeshRenderer>();
            var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetColor("_Color", color);
            mat.SetFloat("_Progress", 0f);
            mat.SetFloat("_Streaks", Mathf.Max(4f, streaks));
            mat.SetFloat("_Intensity", 1.15f);
            mat.SetFloat("_Shape", Mathf.Clamp(shape, 0, 3));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var fx = go.AddComponent<BossSkillBurst>();
            fx._mat = mat;
            fx._dur = Mathf.Max(0.15f, dur);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _dur);
            if (_mat != null) _mat.SetFloat("_Progress", k);
            if (k >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
