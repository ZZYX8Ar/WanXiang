// 魇化立绘（2026-11-02）
// 用途：敌方普通/精英异兽的立绘材质。在**同一 mesh/同一 uv** 上叠加：
//   ① 紫黑污染染色  ② Voronoi 裂痕  ③ 脏污斑点  ④ 缓慢脉动的裂痕发光
// _Amount = 0 时与普通精灵完全一致（可一键关闭）。
Shader "WanXiang/CorruptSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color      ("Tint", Color) = (1,1,1,1)
        _Amount     ("Corrupt Amount", Range(0,1)) = 0.85
        _CrackColor ("Crack Color", Color) = (0.95, 0.35, 1.0, 1)
        _CrackScale ("Crack Scale", Range(2,16)) = 4.0
        _CrackWidth ("Crack Width", Range(0.01,0.35)) = 0.16
        _Dirt       ("Dirt", Range(0,1)) = 0.45
        _Pulse      ("Pulse", Range(0,1)) = 0.35
        _Style      ("Crack Style (0=网格 1=主裂纹)", Range(0,1)) = 0
        _Seed       ("Crack Seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color, _CrackColor;
            float _Amount, _CrackScale, _CrackWidth, _Dirt, _Pulse, _Style, _Seed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Voronoi 边界：返回 0~1，越接近边界越亮
            float voronoiEdge(float2 uv)
            {
                float2 g = uv * _CrackScale;
                float2 ip = floor(g), fp = frac(g);
                float d1 = 8.0, d2 = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 r = float2(x, y);
                    float2 p = r + hash21(ip + r) - fp;
                    float d = dot(p, p);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) { d2 = d; }
                }
                return sqrt(d2) - sqrt(d1);
            }

            float fbm(float2 uv)
            {
                float s = 0, a = 0.5;
                for (int i = 0; i < 3; i++) { s += hash21(uv) * a; uv *= 2.03; a *= 0.5; }
                return s;
            }

            // 主裂纹：沿纵向 2~3 条扭曲线（像从伤口裂开的主缝），0~1
            float mainCracks(float2 uv)
            {
                float total = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    float base = 0.28 + 0.22 * i + 0.06 * sin(_Seed + i * 2.1);
                    float wob = 0.05 * sin(uv.y * (5.0 + i * 1.7) + _Seed * 3.1 + i);
                    float d = abs(uv.x - base - wob);
                    float ln = 1.0 - smoothstep(0.0, _CrackWidth * 1.2, d);
                    // 纵向断续：让裂缝有断口
                    float gap = 0.55 + 0.45 * smoothstep(0.25, 0.8, fbm(float2(uv.y * 4.0 + i, _Seed)));
                    total = max(total, ln * gap);
                }
                return total;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                if (_Amount <= 0.001) { float a0 = c.a; return fixed4(c.rgb * a0, a0); }

                // ① 污染染色：压暗 + 偏紫
                float3 corrupt = c.rgb * float3(0.66, 0.52, 0.86) + float3(0.03, 0.0, 0.07);
                float3 col = lerp(c.rgb, corrupt, _Amount);

                // ② 裂痕：两种风格混合（_Style 由材质随机决定，避免所有敌人长一样）
                float crackGrid = 1.0 - smoothstep(0.0, _CrackWidth, voronoiEdge(i.uv));
                float maskGrid = 0.55 + 0.45 * smoothstep(0.30, 0.80, fbm(i.uv * 3.0));
                crackGrid *= maskGrid;

                float crackMain = mainCracks(i.uv);
                float crack = lerp(crackGrid, max(crackGrid, crackMain), _Style);

                // ④ 裂痕脉动（呼吸）
                float pulse = 1.0 - _Pulse * 0.5 * (1.0 - sin(_Time.y * 2.2));
                col = lerp(col, _CrackColor.rgb, saturate(crack * _Amount * pulse * 1.6));

                // ③ 脏污：低频噪点压暗（只在大片区域，不糊脸）
                float dirt = smoothstep(0.55, 0.85, fbm(i.uv * 12.0));
                col *= lerp(1.0, 0.72, dirt * _Dirt * _Amount);

                float a = c.a;
                return fixed4(col * a, a);
            }
            ENDCG
        }
    }
}
