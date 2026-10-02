Shader "InsectSpace/Meadow Painterly"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _ShadowTint("Cool shadow", Color) = (.46,.64,.68,1)
        _Wind("Wind amplitude", Float) = 0
        _VertexColor("Use vertex palette", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; half4 _BaseColor; half4 _ShadowTint; float _Wind; float _VertexColor;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half4 color : COLOR; half fog : TEXCOORD3; };
        float3 WindPosition(float3 p, float2 uv) { float3 w=TransformObjectToWorld(p); float phase=dot(w.xz,float2(.82,.64))+_Time.y*1.65; return p+float3(sin(phase),0,cos(phase*.83)) * _Wind * uv.y*uv.y; }
        Varyings Vert(Attributes a) { Varyings o; float3 p=WindPosition(a.positionOS.xyz,a.uv); o.positionWS=TransformObjectToWorld(p); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(a.normalOS); o.uv=TRANSFORM_TEX(a.uv,_BaseMap); o.color=lerp(half4(1,1,1,1),a.color,_VertexColor); o.fog=ComputeFogFactor(o.positionCS.z); return o; }
        ENDHLSL
        Pass
        {
            Name "PainterlyForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 Frag(Varyings i) : SV_Target
            {
                Light main=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl=dot(normalize(i.normalWS),main.direction)*.5+.5;
                half band=lerp(.52,1.0,smoothstep(.43,.62,ndl));
                half shade=lerp(.66,1.0,main.shadowAttenuation)*band;
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb*i.color.rgb;
                half3 lighting=lerp(_ShadowTint.rgb,half3(1.10,1.06,.94),shade);
                half3 rgb=albedo*lighting;
                return half4(MixFog(rgb,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            float3 _LightDirection;
            float4 ShadowVert(Attributes a) : SV_POSITION { float3 p=TransformObjectToWorld(WindPosition(a.positionOS.xyz,a.uv)); float3 n=TransformObjectToWorldNormal(a.normalOS); float4 c=TransformWorldToHClip(ApplyShadowBias(p,n,_LightDirection));
            #if UNITY_REVERSED_Z
            c.z=min(c.z,UNITY_NEAR_CLIP_VALUE);
            #else
            c.z=max(c.z,UNITY_NEAR_CLIP_VALUE);
            #endif
            return c; }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
