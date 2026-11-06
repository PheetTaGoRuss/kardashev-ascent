// See-through tint for placement previews (the panel ghost). Unlit, alpha-blended, colour from _Color.
Shader "KardashevAscent/GhostTint"
{
    Properties
    {
        _Color ("Color", Color) = (0.45, 0.85, 1, 0.45)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float shade : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // A little view-facing shading so the box edges read without scene lighting.
                float3 n = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                o.shade = 0.65 + 0.35 * saturate(n.z);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(_Color.rgb * i.shade, _Color.a);
            }
            ENDCG
        }
    }
}
