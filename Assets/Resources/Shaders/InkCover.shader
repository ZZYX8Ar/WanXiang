// ============================================================================
//  万相 · 转场遮罩「墨从四边洇入」Shader（T2，2026-10-24）
//  ---------------------------------------------------------------------------
//  现状：RealmTransition 的遮罩是一块**纯色 Image** 淡入淡出（alpha 0→1）。
//  用户要的是"墨从四边洇进来"，而不是整屏均匀变黑。
//
//  做法（UI 用内置管线 CGPROGRAM，与 Resources/Shaders 其它 UI shader 一致）：
//   · `_Progress` 0→1：墨从**四条边**向中心推进（dEdge = 到最近边的距离）；
//   · 边缘用噪声打碎（双频），不是直尺推；
//   · 前沿一道更深/更实的"湿墨边"（edge），让推进有重量；
//   · 顶点色 × _Color 照乘（UGUI 靠这个改色，漏了就"改色没反应"）。
//  ⚠ 与旧行为兼容：alpha 仍由 Image.color 控制，`_Progress` 只决定"墨盖到哪"。
// ============================================================================
Shader "WanXiang/UI/InkCover"
{
    Properties
    {
        _MainTex  ("Mask", 2D) = "white" {}
        _Color    ("Tint", Color) = (0,0,0,1)
        _NoiseTex ("Noise (Grayscale)", 2D) = "gray" {}

        _Progress ("Progress", Range(0,1)) = 0
        _Soft     ("Edge Softness", Range(0.01,0.5)) = 0.10
        _NoiseAmt ("Edge Noise", Range(0,0.4)) = 0.16
        _EdgeDark ("Wet Edge Strength", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "InkCover"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float2 uv      : TEXCOORD0;
                float2 uvNoise : TEXCOORD1;
                fixed4 color   : COLOR;
            };

            sampler2D _MainTex;  float4 _MainTex_ST;
            sampler2D _NoiseTex; float4 _NoiseTex_ST;
            fixed4 _Color;
            float _Progress;
            float _Soft;
            float _NoiseAmt;
            float _EdgeDark;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.uvNoise = TRANSFORM_TEX(v.texcoord, _NoiseTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ---- 到最近边的距离：0 = 边上，1 = 正中心 ----
                // ⛔ 纯 `min(dx,dy)` 会形成菱形等值线 ⇒ 中心出现一个 X 形接缝（实测截图可见）。
                //   用 平滑 min + 径向混合，等值线变成圆角方块，墨缘才自然。
                float2 d = min(i.uv, 1.0 - i.uv);
                float mn = min(d.x, d.y);
                float soft = 0.10;
                float mnSoft = mn - (soft - mn) * (soft - mn) / (4.0 * soft);   // smooth-min 近似
                mnSoft = mn < soft ? mnSoft : mn;
                float rad = length(d) * 0.78;
                float dEdge = (mnSoft * 0.62 + rad * 0.38) * 2.0;

                // ---- 双频噪声：低频做大块墨晕、高频做纸纤维毛边 ----
                float n1 = tex2D(_NoiseTex, i.uvNoise).r;
                float n2 = tex2D(_NoiseTex, i.uvNoise * 3.7 + float2(0.31, 0.17)).r;
                float n = n1 * 0.7 + n2 * 0.3;

                // ---- 前沿推进：0 时全透，1 时全盖 ----
                float front = _Progress * 1.35 - 0.18;
                float local = dEdge + (n - 0.5) * _NoiseAmt;
                float mask = 1.0 - smoothstep(front - _Soft, front + _Soft, local);
                mask *= step(0.0001, _Progress);

                // ---- 湿墨边：贴着前沿的一圈更实（让"墨在推"有重量）----
                float edge = 1.0 - saturate(abs(local - front) / (_Soft * 1.4 + 0.0001));
                edge *= step(0.0001, _Progress) * (1.0 - step(0.999, _Progress));

                fixed4 col = i.color;
                // 前沿压暗一点（墨越湿越黑），其余按传入色
                col.rgb = lerp(col.rgb, col.rgb * 0.55, edge * _EdgeDark);
                col.a = saturate(col.a * mask);

                // 边上的顶点色 alpha 若为 0（演出结束），整块自然不可见
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
