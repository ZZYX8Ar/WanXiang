// ============================================================================
//  万相 · 技能特效 · 全屏闪 shader（SkillFlash）
//  ---------------------------------------------------------------------------
//  终结技 / 觉醒技演出的「全屏闪」：以角色屏幕位置为中心的一次性高光爆闪。
//    · _Center（0..1 屏幕 uv）为爆心，径向衰减
//    · 全局也提一层亮度（白边），命中"整个画面亮一下"的大招手感
//    · 起手 0.08 内冲到峰值、随后快速衰减 —— _FlashK 控制衰减曲线
//  ⚠ 贴图用 1×1 白块（_MainTex），颜色/中心全解析；SpriteRenderer 铺满相机视口。
// ============================================================================
Shader "WanXiang/Skill/Flash"
{
    Properties
    {
        [PerRendererData] _MainTex   ("Sprite Texture", 2D) = "white" {}
        _Color        ("Flash Color", Color) = (1,1,1,1)
        _Progress     ("Progress 0..1", Range(0,1)) = 0
        _Intensity    ("Intensity", Range(0,1)) = 0.6
        _Center       ("Center (xy uv)", Vector) = (0.5,0.5,0,0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
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
            float4 _Color;
            float _Progress;
            float _Intensity;
            float4 _Center;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float prog = saturate(_Progress);

                // 峰值：起手瞬间冲到 1，再快速衰减（pow 越大衰减越快）
                float rise = smoothstep(0.0, 0.10, prog);      // 0.1 内拉满
                float fall = pow(1.0 - prog, 2.2);
                float env = rise * fall;

                // 以 _Center 为爆心的径向衰减（保留一点方向感）
                float2 dv = (i.uv - _Center.xy) * float2(1.7, 1.0);   // 横向稍宽，贴合宽屏
                float radial = 1.0 - smoothstep(0.0, 1.0, length(dv));

                float a = env * _Intensity * (0.45 + 0.55 * radial);
                fixed4 c = fixed4(i.color.rgb, a);
                clip(a - 0.004);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
