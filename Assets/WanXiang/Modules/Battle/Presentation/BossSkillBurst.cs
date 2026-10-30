// ============================================================================
//  BossSkillBurst —— 首领技能爆发（2026-10-29 池化版）
//  ---------------------------------------------------------------------------
//  ⛔ 之前每次 `Play` 都 `new Material` + `CreatePrimitive(Quad)`（新 mesh），
//     战斗越久越卡：材质/网格/变体不断创建销毁，GC 与显存都在抖。
//  现在改成**固定小池**（4 个实例，材质与 mesh 只建一次），Play 只做参数重置：
//     · 池里找一个空闲实例；全忙就复用最老的那个（视觉上勉强叠一下，不新增对象）
//     · 材质仍用 `WanXiang/BossSkillBurst`，只是重复设置 float/color
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class BossSkillBurst : MonoBehaviour
    {
        private const int PoolSize = 4;
        private static BossSkillBurst[] _pool;
        private static int _cursor;

        private Material _mat;
        private MeshRenderer _mr;
        private float _t, _dur;
        private bool _active;

        private static Material _sharedMatTemplate;

        public static void Play(Vector3 center, Color color, float radius = 2.2f,
                                int streaks = 16, float dur = 0.45f, int shape = 0)
        {
            EnsurePool();
            if (_pool == null) return;

            // 找一个空闲；全忙则复用最老（cursor 轮转）
            BossSkillBurst inst = null;
            for (int i = 0; i < _pool.Length; i++)
            {
                int idx = (_cursor + i) % _pool.Length;
                if (_pool[idx] != null && !_pool[idx]._active) { inst = _pool[idx]; _cursor = idx + 1; break; }
            }
            if (inst == null)
            {
                inst = _pool[_cursor % _pool.Length];
                _cursor++;
                if (inst == null) return;
            }

            inst.transform.position = center;
            inst.transform.localScale = Vector3.one * radius * 2f;
            inst.transform.rotation = Quaternion.identity;

            inst._mat.SetColor("_Color", color);
            inst._mat.SetFloat("_Progress", 0f);
            inst._mat.SetFloat("_Streaks", Mathf.Max(4f, streaks));
            inst._mat.SetFloat("_Intensity", 1.15f);
            inst._mat.SetFloat("_Shape", Mathf.Clamp(shape, 0, 3));

            inst._dur = Mathf.Max(0.15f, dur);
            inst._t = 0f;
            inst._active = true;
            inst.gameObject.SetActive(true);
        }

        private static void EnsurePool()
        {
            if (_pool != null) return;
            var sh = Shader.Find("WanXiang/BossSkillBurst");
            if (sh == null) return;                        // 没有 shader 就静默跳过

            if (_sharedMatTemplate == null)
                _sharedMatTemplate = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };

            _pool = new BossSkillBurst[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("BossSkillBurst#" + i);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(go.transform, false);
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _sharedMatTemplate;    // ⚠ 共用材质模板会互相串参数，见下
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // 每个实例一份自己的材质（只 4 份，一次创建，永不销毁）
                var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                mr.sharedMaterial = mat;

                var comp = go.AddComponent<BossSkillBurst>();
                comp._mat = mat;
                comp._mr = mr;
                go.SetActive(false);
                _pool[i] = comp;
            }
        }

        private void Update()
        {
            if (!_active) return;
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _dur);
            if (_mat != null) _mat.SetFloat("_Progress", k);
            if (k >= 1f) { _active = false; gameObject.SetActive(false); }
        }

        private void OnDestroy()
        {
            // 池是长生命周期对象，材质随实例一起释放
            if (_mat != null) Destroy(_mat);
        }
    }
}
