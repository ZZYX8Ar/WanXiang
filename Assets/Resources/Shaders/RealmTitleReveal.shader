// ============================================================================
//  万相 · 艺术字「笔锋写入」揭示 Shader（2026-10-23 重做）
//  ---------------------------------------------------------------------------
//  用户："写字转场不太行" —— 原来的实现是**径向涟漪**（从中心一圈圈洇开），
//  对"毛笔写字"这种**单向运笔**的意象是错的：字应该像被一笔扫出来，
//  而不是从中间泡开。
//
//  本版做法（仍然是 UI 用的内置管线 CGPROGRAM，与 Resources/Shaders 其它 UI shader 一致）：
//   ① 单向扫描：沿 X（或 _Dir 指定的方向）推一条**带噪声的竖边**，
//      边缘用双频噪声打碎 ⇒ 像笔锋走纸，而不是直尺推过来；
//   ② 湿边：边界处**墨色加深 + 加宽 alpha**（湿墨在纸上洇开的那一圈）；
//   ③ 前渗：边界前方一小段半透明"洇痕"，跟随 _Progress 出现、结束收掉；
//   ④ 运笔微颤：边界位置叠一个低频 sin，手写感；
//   ⑤ 墨色反相 _Invert 保留（黑墨字在暗底上要翻成白墨）。
//
//  兼容：仍然只吃 `_Progress`，RealmTransition 不需要改代码。
// ============================================================================
Shader "WanXiang/UI/RealmTitleReveal"
{
    Properties
    {
        _MainTex      ("Title Art (RGBA)", 2D) = "white" {}
        _Color        ("Tint", Color) = (1,1,1,1)
        _NoiseTex     ("Noise (Grayscale)", 2D) = "gray" {}

        _Progress     ("Progress", Range(0,1)) = 0
        _RippleCenter ("Legacy Center (unused)", Vector) = (0.5,0.5,0,0)
        _Feather      ("Edge Feather", Range(0.005,0.4)) = 0.055
        _MaxRadius    ("Legacy Radius (unused)", Range(0.5,2.5)) = 1.5

        _NoiseStrength("Edge Noise", Range(0,0.4)) = 0.06
        _EdgeColor    ("Wet Edge Color", Color) = (0.92,0.90,0.84,1)
        _EdgeWidth    ("Wet Edge Width", Range(0,0.2)) = 0.03

        _SweepStart   ("Sweep Start X", Range(-0.4,0.4)) = -0.12
        _SweepEnd     ("Sweep End X", Range(0.6,1.4)) = 1.12
        _Bleed        ("Bleed Ahead", Range(0,0.15)) = 0.05
        _Tremble      ("Hand Tremble", Range(0,0.01)) = 0.0025
        _Invert       ("Invert Ink (0=黑墨, 1=白墨)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "Default"
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
            float  _Progress;
            float4 _RippleCenter;
            float  _Feather;
            float  _MaxRadius;
            float  _NoiseStrength;
            fixed4 _EdgeColor;
            float  _EdgeWidth;
            float  _SweepStart;
            float  _SweepEnd;
            float  _Bleed;
            float  _Tremble;
            float  _Invert;

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
                // ---- 边界位置（沿 X 推进；-0.12 起步让边缘不会一开始就"整片"出现）----
                float edgeX = lerp(_SweepStart, _SweepEnd, _Progress);

                // ---- 双频噪声：低频做大起伏（笔锋），高频做毛边（纸纤维）----
                float n1 = tex2D(_NoiseTex, i.uvNoise).r;
                float n2 = tex2D(_NoiseTex, i.uvNoise * 4.7 + float2(0.37, 0.21)).r;
                float n = n1 * 0.65 + n2 * 0.35;

                // ---- 运笔微颤：低频 sin，随时间轻摆（幅度很小，不能像"抖动故障"）----
                float trem = sin(i.uv.y * 26.0 + _Time.y * 2.1) * _Tremble;
                float localEdge = edgeX + (n - 0.5) * _NoiseStrength + trem;

                float d = i.uv.x - localEdge;               // d<0 = 已写到的地方

                // ---- 可见遮罩：d<0 显示；边缘 feather 内柔和过渡 ----
                float mask = 1.0 - smoothstep(-_Feather, _Feather, d);
                mask *= step(0.0001, _Progress);            // Progress=0 不许露残影

                // ---- 字图 + 反相 + 顶点色 ----
                fixed4 col = tex2D(_MainTex, i.uv);
                col.rgb = lerp(col.rgb, 1.0 - col.rgb, _Invert);
                col *= i.color;

                // ---- ② 湿边：只在**有墨的像素**上做（⛔ 否则空白处会被染成一条竖带）----
                //   墨在纸上洇开：颜色往湿墨色偏一点、alpha 略微加厚。
                float hasInk = step(0.04, col.a);
                float wet = saturate(1.0 - abs(d) / (_EdgeWidth + 0.0001));
                wet *= step(0.0001, _Progress) * (1.0 - step(0.999, _Progress)) * hasInk;
                col.rgb = lerp(col.rgb, _EdgeColor.rgb, wet * _EdgeColor.a * 0.55);
                col.a = saturate(col.a + wet * 0.12);

                // ---- ③ 前渗：边界前方的洇痕，同样只作用在有墨处 ----
                float ahead = saturate(1.0 - abs(d - _Bleed) / (_Bleed + 0.0001));
                ahead *= step(0.0001, _Progress) * (1.0 - step(0.999, _Progress)) * hasInk;
                col.a = saturate(col.a + ahead * 0.06);

                col.a *= mask;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
