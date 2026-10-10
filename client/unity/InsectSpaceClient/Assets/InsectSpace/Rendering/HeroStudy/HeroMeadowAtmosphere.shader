Shader "InsectSpace/Hero Meadow Atmosphere"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _ShadowTint("Cool shadow", Color) = (.46,.64,.68,1)
        _Wind("Wind amplitude", Float) = 0
        _VertexColor("Use vertex palette", Float) = 1
        _MistIntensity("Mist palette blend",Range(0,1)) = 0
        _CloudShadow("Moving cloud shade",Range(0,1)) = .18
        _Cutoff("Alpha cutoff",Range(0,1)) = 0
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
        float4 _BaseMap_ST; half4 _BaseColor; half4 _ShadowTint; float _Wind; float _VertexColor; float _MistIntensity; float _CloudShadow; float _Cutoff;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half4 color : COLOR; half fog : TEXCOORD3; };
        float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float Noise(float2 p) { float2 cell=floor(p),f=frac(p); f=f*f*(3-2*f); return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y); }
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
                half4 textureColor=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);clip(textureColor.a-_Cutoff);
                Light main=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl=dot(normalize(i.normalWS),main.direction)*.5+.5;
                half band=lerp(.52,1.0,smoothstep(.43,.62,ndl));
                half shade=lerp(.48,1.0,main.shadowAttenuation)*band;
                half3 albedo=textureColor.rgb*_BaseColor.rgb*i.color.rgb;
                half3 lighting=lerp(_ShadowTint.rgb,half3(1.10,1.06,.94),shade);
                half3 environment=max(half3(.07,.09,.13),main.color*.68+SampleSH(normalize(i.normalWS))*.55); half3 rgb=albedo*lighting*environment;
                half cloud=smoothstep(.36,.70,Noise(i.positionWS.xz*.095+float2(_Time.y*.012,_Time.y*.008)));
                rgb*=lerp(1, .78,cloud*_CloudShadow*(1-_MistIntensity));
                // Graded world-space ground treatment, independent of camera framing.
                half luma=dot(rgb,half3(.2126,.7152,.0722));
                half3 overcast=lerp(rgb,luma*half3(.83,.97,.91),.30);
                half nearDark=lerp(.33,1.0,smoothstep(-1,6,i.positionWS.z));
                half meadowBands=Noise(i.positionWS.xz*float2(.14,.25));
                half patchLight=lerp(.75,1.18,smoothstep(.17,.83,meadowBands));
                rgb=lerp(rgb,overcast*nearDark*patchLight,_MistIntensity);
                float distanceToCamera=distance(_WorldSpaceCameraPos,i.positionWS);
                half bank=smoothstep(8,32,distanceToCamera)*saturate(1.25-i.positionWS.y*.24);
                half drift=.74+.18*sin(i.positionWS.z*.65+i.positionWS.x*.10+_Time.y*.08)+.08*sin(i.positionWS.x*.32-i.positionWS.z*.14);
                rgb=lerp(rgb,unity_FogColor.rgb,saturate(bank*drift)*_MistIntensity*.14);
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
            struct ShadowVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            ShadowVaryings ShadowVert(Attributes a) { float3 p=TransformObjectToWorld(WindPosition(a.positionOS.xyz,a.uv)); float3 n=TransformObjectToWorldNormal(a.normalOS); float4 c=TransformWorldToHClip(ApplyShadowBias(p,n,_LightDirection));
            #if UNITY_REVERSED_Z
            c.z=min(c.z,UNITY_NEAR_CLIP_VALUE);
            #else
            c.z=max(c.z,UNITY_NEAR_CLIP_VALUE);
            #endif
            ShadowVaryings o; o.positionCS=c; o.uv=TRANSFORM_TEX(a.uv,_BaseMap); return o; }
            half4 ShadowFrag(ShadowVaryings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff); return i.positionCS.z; }
            ENDHLSL
        }
    }
}
