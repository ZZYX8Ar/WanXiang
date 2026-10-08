// ============================================================================
//  万相 · 状态特效叠加层 shader
//  ---------------------------------------------------------------------------
//  给「冻结 / 束缚 / 燃烧…」这类**盖在立绘上**的特效图加生命力：
//    · 正弦横向摆动，且**越靠上摆得越大**（火舌 / 藤蔓往上长的手感）
//    · 纵向"呼吸伸缩"（不用 uv 循环滚动 —— 那样要求贴图无缝，单张特效图会露出接缝）
//    · 明度/透明度脉动（像火焰一闪一闪）
//    · 整体左右轻微摇摆
//  参数全部由 C# 按状态逐条下发（见 BattleStage2D.StatusOverlays），本文件不含任何业务。
//
//  ⚠ 放在 Assets/Resources/Shaders/ 下：BattleStage2D 是运行时 AddComponent 的，
//     Inspector 上存不住引用 ⇒ 只能靠 Resources.Load 取（也顺带保证进包）。
//  ⚠ 与 SpriteRenderer 搭配：贴图给 _MainTex，单位各自颜色走**顶点色**（v.color），
//    所以同一个 Material 可以被多只单位共享，不用每只 new 一份。
// ============================================================================
Shader "WanXiang/StatusOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Tint         ("Tint", Color) = (1,1,1,1)

        _WaveAmp      ("Wave Amp (x 摆动幅度)", Range(0, 0.2))   = 0.015
        _WaveFreq     ("Wave Freq",             Range(0, 60))    = 16
        _WaveSpeed    ("Wave Speed",            Range(0, 12))    = 2.2

        _PulseAmp     ("Pulse Amp (呼吸幅度)",  Range(0, 0.8))   = 0.12
        _PulseSpeed   ("Pulse Speed",           Range(0, 16))    = 3.0

        _RiseAmp      ("Rise Amp (纵向拉伸)",   Range(0, 0.6))   = 0.06
        _RiseSpeed    ("Rise Speed",            Range(0, 16))    = 2.6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Tint;
            float _WaveAmp, _WaveFreq, _WaveSpeed;
            float _PulseAmp, _PulseSpeed;
            float _RiseAmp, _RiseSpeed;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Tint;      // ★ 单位各自的颜色 × 状态色 ⇒ 共享 Material 也没问题
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;

                // ① 纵向呼吸：以中心为轴伸缩（火焰"往上舔一下又落回"）
                float rise = 1.0 + sin(t * _RiseSpeed) * _RiseAmp;
                uv.y = (uv.y - 0.5) / max(rise, 0.05) + 0.5;

                // ② 横向正弦摆动，越靠上越明显（贴图坐标 y 越大 = 越靠上）
                float h = saturate(i.uv.y);
                uv.x += sin(i.uv.y * _WaveFreq + t * _WaveSpeed) * _WaveAmp * h;

                fixed4 c = tex2D(_MainTex, uv) * i.color;

                // ③ 脉动：整体明度与透明一起呼吸（火焰一闪一闪）
                float pulse = 1.0 + sin(t * _PulseSpeed) * _PulseAmp;
                c.rgb *= pulse;
                c.a   *= clamp(pulse, 0.0, 2.0);

                clip(c.a - 0.004);              // 全透明像素直接丢弃，避免叠层边缘发灰
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
