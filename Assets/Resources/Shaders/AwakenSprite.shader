// 觉醒立绘 v2（B5-2：品级表现 + 异色表现；2026-11-02 初版）
// 品级：_TierColor 描边 + 呼吸光晕 + 神品斜流光；异色：色相偏移 + 周期扫光。
// _Amount=0 且 _ShinySweep=0 时与普通精灵一致（向后兼容旧用法）。
Shader "WanXiang/AwakenSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color    ("Tint", Color) = (1,1,1,1)
        _Amount   ("Awaken", Range(0,1)) = 1
        _Gold     ("Gold", Color) = (1.0, 0.82, 0.38, 1)
        _Rim      ("Rim Width (uv)", Range(0.001,0.02)) = 0.004
        _Inner    ("Inner Glow", Range(0,1)) = 0.22
        _TierColor   ("Tier Rim Color", Color) = (1, 0.82, 0.38, 1)   // 灵=冷灰 玄=冷青 神=暖金（BeastVisual 设）
        _RimWidth    ("Tier Rim Width", Range(0.001,0.02)) = 0.004
        _GlowBreath  ("Breath Glow", Range(0,0.3)) = 0.0              // 呼吸强度（BeastVisual 设）
        _BreathSpeed ("Breath Speed", Range(0.1,4)) = 0.8
        _SweepSpeed  ("Sweep Speed", Range(0,1)) = 0.0                // 神品流光；0=关闭（灵/玄）
        _HueShift    ("Hue Shift", Range(-0.15,0.15)) = 0.0           // 异色 ±18°（0.06 为默认值）
        _ShinySweep  ("Shiny Sweep", Range(0,1)) = 0.0                // 异色周期扫光强度
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
            fixed4 _Color, _Gold, _TierColor;
            float _Amount, _Rim, _Inner;
            float _RimWidth, _GlowBreath, _BreathSpeed, _SweepSpeed;
            float _HueShift, _ShinySweep;

            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; o.color = v.color * _Color; return o;
            }

            // rgb → hue 平移（YIQ 空间近似旋转，够异色用且不引入完整 HSV 转换）
            fixed3 HueShift(fixed3 c, float shift)
            {
                fixed3 k = fixed3(0.57735, 0.57735, 0.57735);   // 1/sqrt(3) 灰轴（const 不接受单参构造）
                float cosA = cos(shift * 6.2831853);
                return c * cosA + cross(k, c) * sin(shift * 6.2831853) + k * dot(k, c) * (1.0 - cosA);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float a = c.a;
                if (_Amount <= 0.001 && _ShinySweep <= 0.001 && abs(_HueShift) <= 0.001)
                    return fixed4(c.rgb * a, a);

                // ---- 异色：色相偏移（先偏再叠光——扫光用原亮度，不跟着偏）----
                float3 rgb = c.rgb;
                if (abs(_HueShift) > 0.001) rgb = saturate(HueShift(c.rgb, _HueShift));

                // ---- 边缘（4 邻域 alpha 差，同一张图不会错位）----
                float rimW = max(_RimWidth, 0.0005);
                float a1 = tex2D(_MainTex, i.uv + float2( rimW, 0)).a;
                float a2 = tex2D(_MainTex, i.uv + float2(-rimW, 0)).a;
                float a3 = tex2D(_MainTex, i.uv + float2(0,  rimW)).a;
                float a4 = tex2D(_MainTex, i.uv + float2(0, -rimW)).a;
                float edge = saturate((a - min(min(a1,a2), min(a3,a4))) * 5.0);

                // ---- 品级描边 + 内辉光（觉醒通道 _Amount；描边色 = _TierColor）----
                float flow = 0.5 + 0.5 * sin((i.uv.y - _Time.y * 0.25) * 12.566);
                float inner = a * _Inner * (0.55 + 0.45 * flow);
                float breath = _GlowBreath * (0.5 + 0.5 * sin(_Time.y * _BreathSpeed));   // 呼吸光晕
                float g = saturate((edge + inner) * _Amount + breath * a);
                rgb = lerp(rgb, _TierColor.rgb, g);

                // ---- 神品斜流光（_SweepSpeed>0）：斜 45° 窄带自左下向右上扫 ----
                if (_SweepSpeed > 0.001)
                {
                    float band = frac(i.uv.x * 0.7 + i.uv.y * 0.7 - _Time.y * _SweepSpeed * 0.35);
                    rgb += _TierColor.rgb * saturate(1.0 - abs(band - 0.5) * 8.0) * 0.35 * a;
                }

                // ---- 异色扫光（_ShinySweep>0）：2.5s 周期斜 45° 窄带，比神品流光更"闪"更短 ----
                if (_ShinySweep > 0.001)
                {
                    float band2 = frac(i.uv.x * 0.7 + i.uv.y * 0.7 - _Time.y * 0.4);
                    rgb += fixed3(1, 1, 0.9) * saturate(1.0 - abs(band2 - 0.5) * 10.0) * _ShinySweep * a;
                }

                return fixed4(rgb * a, a);
            }
            ENDCG
        }
    }
}
