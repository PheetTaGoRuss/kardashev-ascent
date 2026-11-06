// Planet tiles drawn straight from vertex colours, with no lighting: for data maps that must read
// the same on the day and night side.
Shader "KardashevAscent/PlanetTilesUnlit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
