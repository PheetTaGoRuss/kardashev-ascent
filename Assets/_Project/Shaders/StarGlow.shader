// The star as seen in the sky: a bright disc with a soft glow around it, added on top of the scene.
// Drawn on a quad; _DiscFraction is the disc's radius as a fraction of the quad's half-width.
Shader "KardashevAscent/StarGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.95, 0.9, 1)
        _Intensity ("Disc Intensity", Range(0, 8)) = 3
        _GlowIntensity ("Glow Intensity", Range(0, 4)) = 0.9
        _DiscFraction ("Disc Radius (fraction of quad)", Range(0.01, 1)) = 0.1667
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Intensity;
            half _GlowIntensity;
            half _DiscFraction;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2 - 1;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r = length(i.uv);
                float edge = fwidth(r) * 1.5;
                float disc = 1 - smoothstep(_DiscFraction - edge, _DiscFraction + edge, r);
                float halo = saturate(1 - r);
                float glow = halo * halo * halo * _GlowIntensity;
                float3 col = _Color.rgb * (disc * _Intensity + glow);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
