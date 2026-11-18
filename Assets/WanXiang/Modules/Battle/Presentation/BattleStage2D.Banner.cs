// ============================================================================
//  万相 · 回合横幅 / 护盾表现（2026-11-18 代码骨架重构）
//  从 BattleStage2D.cs 原样搬入，逻辑零改动。
//  ⚠ 字段（TbCol*/_shield*）保留在主文件，partial 直接访问。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {

        private TextMesh TbText(Transform parent, string name, int fontSize, float charSize, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var f = LegacyFont();
            var tm = go.AddComponent<TextMesh>();
            if (f != null) { tm.font = f; go.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
            tm.fontSize = fontSize; tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sortingOrder = order;
            return tm;
        }

        private void ShowTurnBanner(int turn, string sideCn, Color col)
        {
            if (_tbGroup == null)
            {
                var go = new GameObject("TurnBanner");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3((PlayerX + EnemyX) * 0.5f, BoardY + 2.55f, -3f);
                _tbGroup = go.transform;

                _tbBack = new GameObject("Back").AddComponent<SpriteRenderer>();
                _tbBack.transform.SetParent(_tbGroup, false);
                _tbBack.sprite = SolidSprite(Color.white, 100, 48, 0, Color.white);
                _tbBack.transform.localScale = new Vector3(9f, 0.78f, 1f);
                _tbBack.color = new Color(0f, 0f, 0f, 0.55f);
                _tbBack.sortingOrder = 58;

                _tbRuleL = new GameObject("RuleL").AddComponent<SpriteRenderer>();
                _tbRuleL.transform.SetParent(_tbGroup, false);
                _tbRuleL.sprite = SolidSprite(Color.white, 100, 4, 0, Color.white);
                _tbRuleL.transform.localScale = new Vector3(1.8f, 0.04f, 1f);
                _tbRuleL.color = new Color(0.92f, 0.88f, 0.75f, 0.9f);
                _tbRuleL.sortingOrder = 59;

                _tbRuleR = new GameObject("RuleR").AddComponent<SpriteRenderer>();
                _tbRuleR.transform.SetParent(_tbGroup, false);
                _tbRuleR.sprite = SolidSprite(Color.white, 100, 4, 0, Color.white);
                _tbRuleR.transform.localScale = new Vector3(1.8f, 0.04f, 1f);
                _tbRuleR.color = new Color(0.92f, 0.88f, 0.75f, 0.9f);
                _tbRuleR.sortingOrder = 59;

                _tbBig = TbText(_tbGroup, "Big", 64, 0.082f, 60);
                _tbSub = TbText(_tbGroup, "Sub", 36, 0.042f, 60);
                _tbSub.transform.localPosition = new Vector3(0f, 0.34f, 0f);
                _tbBig.transform.localPosition = Vector3.zero;
            }

            _tbTurn = turn;
            _tbBig.text = sideCn != null ? sideCn : ("第 " + turn + " 回合");
            _tbBig.color = col;
            _tbSub.gameObject.SetActive(sideCn == null);
            if (sideCn == null) _tbSub.text = "ROUND " + turn;
            _tbGroup.gameObject.SetActive(true);
            _tbT = TbLife;
            // 出场：大字轻微回弹
            _tbBig.transform.localScale = Vector3.one * 1.1f;
        }

        /// <summary>入场落地尘：6 片小方块向外弹开（复用 _bursts，不用粒子系统）。</summary>
        private void SpawnDust(UnitView2D v)
        {
            if (v == null || v.Root == null) return;
            Vector3 c = v.Root.transform.position + new Vector3(0f, 0.12f, -1.05f);
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI * 2f * i / 6f;
                var col = new Color(0.78f, 0.72f, 0.60f, 0.55f);
                var burst = AcquireBurst(SolidSprite(col, 10, 10, 0, col), 30, c, 0.12f);
                burst.Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.35f) * 1.2f;
                burst.Life = 0.32f; burst.Max = 0.32f;
                _bursts.Add(burst);
            }
        }

        private void StepTurnBanner(float dt)
        {
            if (_tbGroup == null || !_tbGroup.gameObject.activeSelf) return;
            _tbT -= dt;
            if (_tbT <= 0f) { _tbGroup.gameObject.SetActive(false); return; }

            // 两侧细线滑入（TbSlide 秒内从 ±5 → ±3.05）
            float k = Mathf.Clamp01(1f - _tbT / (TbLife - TbSlide));
            float ease = Mathf.SmoothStep(0f, 1f, k);
            float x = Mathf.Lerp(5f, 2.45f, ease);
            if (_tbRuleL != null) _tbRuleL.transform.localPosition = new Vector3(-x, 0f, 0f);
            if (_tbRuleR != null) _tbRuleR.transform.localPosition = new Vector3(x, 0f, 0f);

            // 大字回弹 1.1 → 1
            if (_tbBig != null)
            {
                float sc = Mathf.MoveTowards(_tbBig.transform.localScale.x, 1f, dt * 0.5f);
                _tbBig.transform.localScale = Vector3.one * sc;
            }

            // 尾部淡出
            float a = _tbT < TbFade ? Mathf.Clamp01(_tbT / TbFade) : 1f;
            if (_tbBig != null) { var c = _tbBig.color; c.a = a; _tbBig.color = c; }
            if (_tbSub != null) { var c = _tbSub.color; c.a = a * 0.8f; _tbSub.color = c; }
            if (_tbBack != null) { var c = _tbBack.color; c.a = 0.55f * a; _tbBack.color = c; }
            if (_tbRuleL != null) { var c = _tbRuleL.color; c.a = 0.9f * a; _tbRuleL.color = c; }
            if (_tbRuleR != null) { var c = _tbRuleR.color; c.a = 0.9f * a; _tbRuleR.color = c; }
        }

        /// <summary>程序化圆形护盾贴图（128×128，边缘 2px 柔化）。</summary>
        private static Sprite ShieldCircleSprite()
        {
            if (_shieldCircle != null) return _shieldCircle;
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            float r = N * 0.5f - 1f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - N * 0.5f + 0.5f, dy = y - N * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 只画**环**：外圈实、内侧 7px 渐隐到透明（中心全透，不盖立绘）
                    float outer = Mathf.Clamp01((r - d) / 2f);
                    float inner = Mathf.Clamp01((d - (r - 7f)) / 5f);
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Min(outer, inner));
                }
            tex.SetPixels(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
            _shieldCircle = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            _shieldCircle.name = "ShieldCircle";
            return _shieldCircle;
        }

        /// <summary>全圆填充贴图（软边 4px）——护盾膜的"里面"。</summary>
        private static Sprite ShieldFillSprite()
        {
            if (_shieldFill != null) return _shieldFill;
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            float r = N * 0.5f - 1f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - N * 0.5f + 0.5f, dy = y - N * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((r - d) / 4f));
                }
            tex.SetPixels(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
            _shieldFill = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            _shieldFill.name = "ShieldFill";
            return _shieldFill;
        }

        private void SetShield2D(UnitView2D v, int shield)
        {
            bool on = shield > 0;

            // ★ 2026-11-02：要的是**正圆环**，不是删掉（用户原话）。
            if (on) EnsureShieldField(v);
            if (v.ShieldField != null)
            {
                if (!on)
                {
                    // ⛔ 同光环：破盾只隐藏、**不销毁材质**，否则护盾再回来时
                    //   EnsureShieldField 会因为 ShieldField != null 提前 return ⇒ 再也不显示。
                    if (v.ShieldField.gameObject.activeSelf) v.ShieldField.gameObject.SetActive(false);
                }
                else if (on && v.ShieldMat != null)
                {
                    float k = Mathf.Clamp01(shield / Mathf.Max(1f, v.MaxHp * 0.35f));
                    v.ShieldMat.SetFloat("_Strength", Mathf.Lerp(0.45f, 1f, k));
                    // 受击涟漪衰减（每帧 -2.8/s，约 0.36s 收完）
                    float hp = v.ShieldMat.GetFloat("_HitPhase");
                    if (hp > 0.001f) v.ShieldMat.SetFloat("_HitPhase", Mathf.Max(0f, hp - Time.deltaTime * 2.8f));
                }
            }
            if (v.ShieldPlate != null && v.ShieldPlate.gameObject.activeSelf != on)
                v.ShieldPlate.gameObject.SetActive(on);
            if (v.ShieldText != null)
            {
                if (v.ShieldText.gameObject.activeSelf != on) v.ShieldText.gameObject.SetActive(on);
                string t = on ? shield.ToString() : "";
                if (v.ShieldText.text != t) v.ShieldText.text = t;
            }
        }
    }
}
