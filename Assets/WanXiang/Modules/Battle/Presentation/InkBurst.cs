// ============================================================================
//  InkBurst —— 死亡时炸开的墨点（T13 补充，2026-10-24）
//  ---------------------------------------------------------------------------
//  用户："死亡 shader 感觉没有，看着一般" —— 光有溶解不够，还需要一个**瞬间的爆发**：
//  死亡那一刻从身上炸出几颗墨点（带重力、带拖尾感），配合单位溶解 ⇒ 有"被打散"的实感。
//
//  设计：
//   · 一次性组件：StartBurst(pos, count, size, color) 后自行在 Update 里推进、结束后 Destroy 自己；
//   · 不用 ParticleSystem（本工程禁粒子系统，材质/管线坑多），纯 SpriteRenderer；
//   · 墨点大小/速度/寿命随机，但用**确定性随机**（固定种子）便于复现。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class InkBurst : MonoBehaviour
    {
        private struct Dot
        {
            public Transform T;
            public SpriteRenderer Sr;
            public Vector2 Vel;
            public float Life;
            public float Age;
            public float Size;
            public Color Base;
        }

        private Dot[] _dots;
        private Transform _halo;
        private SpriteRenderer _haloSr;
        private float _haloBase = 1f;
        private float _haloAge;
        private float _gravity = -6.5f;
        private float _drag = 1.6f;

        /// <summary>在指定世界坐标炸开一团墨点。</summary>
        public static void Spawn(Vector3 worldPos, int count, float sizeScale, Color color,
                                 int sortingOrder = 26)
        {
            var go = new GameObject("InkBurst");
            go.transform.position = worldPos;
            var burst = go.AddComponent<InkBurst>();
            burst.Build(count, sizeScale, color, sortingOrder);
        }

        private void Build(int count, float sizeScale, Color color, int sortingOrder)
        {
            var sp = Resources.Load<Sprite>("Battle/FX_ink_dot");
            if (sp == null)
            {
                Destroy(gameObject);
                return;
            }

            count = Mathf.Clamp(count, 3, 14);

            // 瞬间墨晕：一下溅开的暗斑，0.22s 放大淡出（给"爆发"一个视觉锚点）
            var haloGo = new GameObject("halo");
            haloGo.transform.SetParent(transform, false);
            var haloSr = haloGo.AddComponent<SpriteRenderer>();
            haloSr.sprite = sp;
            haloSr.sortingOrder = sortingOrder - 1;
            haloSr.color = new Color(color.r, color.g, color.b, 0.55f);
            _halo = haloGo.transform;
            _haloSr = haloSr;
            _haloBase = sizeScale;
            _dots = new Dot[count];
            var rng = new System.Random(Mathf.RoundToInt(transform.position.x * 100f) ^ Mathf.RoundToInt(transform.position.y * 100f));

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("dot" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.color = color;
                sr.sortingOrder = sortingOrder + (i % 3);

                float size = sizeScale * Mathf.Lerp(0.26f, 0.62f, (float)rng.NextDouble());
                go.transform.localScale = Vector3.one * size;

                // 方向：以"向上/斜上"为主（被打飞），少量向下作碎屑
                float ang = Mathf.Lerp(20f, 160f, (float)rng.NextDouble()) * Mathf.Deg2Rad;
                float spd = Mathf.Lerp(2.2f, 5.4f, (float)rng.NextDouble());
                if (i % 4 == 3) ang = -ang * 0.6f;      // 少量往下飞

                _dots[i] = new Dot
                {
                    T = go.transform,
                    Sr = sr,
                    Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                    Life = Mathf.Lerp(0.45f, 0.8f, (float)rng.NextDouble()),
                    Age = 0f,
                    Size = size,
                    Base = color,
                };
            }
        }

        private void Update()
        {
            if (_dots == null) return;
            float dt = Time.deltaTime;

            // 墨晕：0.22s 内放大到 3.2 倍并淡出
            if (_halo != null)
            {
                _haloAge += dt;
                float k = Mathf.Clamp01(_haloAge / 0.22f);
                _halo.localScale = Vector3.one * _haloBase * Mathf.Lerp(1.0f, 3.2f, k);
                var hc = _haloSr.color;
                hc.a = Mathf.Lerp(0.55f, 0f, k);
                _haloSr.color = hc;
                if (k >= 1f) { Destroy(_halo.gameObject); _halo = null; }
            }
            bool alive = false;

            for (int i = 0; i < _dots.Length; i++)
            {
                var d = _dots[i];
                if (d.T == null) continue;
                d.Age += dt;
                if (d.Age >= d.Life)
                {
                    if (d.T.gameObject.activeSelf) d.T.gameObject.SetActive(false);
                    _dots[i] = d;
                    continue;
                }
                alive = true;

                // 抛物线 + 空气阻力（墨点会被"拖住"，不像火花那样直线飞）
                d.Vel.y += _gravity * dt;
                d.Vel -= d.Vel * (1f - Mathf.Exp(-_drag * dt));
                d.T.localPosition += (Vector3)(d.Vel * dt);

                float k = d.Age / d.Life;
                d.T.localScale = Vector3.one * d.Size * Mathf.Lerp(1.15f, 0.55f, k);
                var c = d.Base;
                c.a = Mathf.Lerp(d.Base.a, 0f, k * k);     // 后段快速淡出
                d.Sr.color = c;

                _dots[i] = d;
            }

            if (!alive) Destroy(gameObject, 0.02f);
        }
    }
}
