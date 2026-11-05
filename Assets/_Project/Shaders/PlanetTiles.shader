// Planet tiles: colour from the mesh's vertex colours; vertex alpha = smoothness (oceans shine, land is matte).
Shader "KardashevAscent/PlanetTiles"
{
    Properties
    {
        _SmoothnessScale ("Smoothness Scale", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        struct Input
        {
            float4 color : COLOR;
        };

        half _SmoothnessScale;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = IN.color.rgb;
            o.Metallic = 0;
            o.Smoothness = IN.color.a * _SmoothnessScale;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
