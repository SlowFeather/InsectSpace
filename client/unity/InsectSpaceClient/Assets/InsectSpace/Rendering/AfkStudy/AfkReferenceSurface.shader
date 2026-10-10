Shader "InsectSpace/Local Reference Surface"
{
    Properties
    {
        _BaseMap("Albedo",2D)="white"{}
        _NormalMap("Normal detail",2D)="bump"{}
        _BaseColor("Tint",Color)=(1,1,1,1)
        _ShadowTint("Shadow palette",Color)=(.18,.40,.43,1)
        _Cutoff("Alpha cutoff",Range(0,1))=.03
        _Ground("Painted ground",Float)=0
        _Wind("Wind",Float)=0
        _VertexColor("Vertex palette",Float)=0
        _Rock("Layered rock",Float)=0
        _RockColor("Rock color",Color)=(.22,.35,.39,1)
        _RockTop("Rock top color",Color)=(.22,.38,.27,1)
        _Foliage("Foliage palette",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;half4 _BaseColor,_ShadowTint,_RockColor,_RockTop;float _Cutoff,_Ground,_Wind,_VertexColor,_Rock,_Foliage;
        CBUFFER_END
        struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;half4 c:COLOR;float4 t:TANGENT;};
        struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;float2 uv:TEXCOORD2;half4 c:COLOR;half fog:TEXCOORD3;half4 t:TEXCOORD4;};
        float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
        float3 wind(A a){float3 p=TransformObjectToWorld(a.p.xyz);p.x+=sin(p.x*1.7+p.z+_Time.y*1.6)*_Wind*a.uv.y*a.uv.y;return p;}
        V vert(A a){V o;o.w=wind(a);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.t=half4(TransformObjectToWorldDir(a.t.xyz),a.t.w*GetOddNegativeScale());o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.c=lerp(half4(1,1,1,1),a.c,_VertexColor);o.fog=ComputeFogFactor(o.p.z);return o;}
        ENDHLSL
        Pass
        {
            Name "ReferenceForward" Tags{"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 frag(V i):SV_Target
            {
                half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);clip(tex.a-_Cutoff);
                half3 albedo=tex.rgb*_BaseColor.rgb*i.c.rgb;
                half3 normalWS=normalize(i.n);
                if(_Foliage>.5)
                {
                    half luminance=dot(tex.rgb,half3(.28,.60,.12));
                    albedo=lerp(half3(.045,.12,.105),half3(.22,.40,.28),smoothstep(.015,.65,luminance));
                }
                if(_Ground>.5)
                {
                    float broad=noise(i.w.xz*.19),brush=noise(i.w.xz*float2(.85,1.4));
                    albedo=lerp(half3(.18,.34,.25),half3(.43,.54,.31),smoothstep(.15,.87,broad));
                    albedo*=lerp(.92,1.06,brush);
                    float track=abs(i.w.x-(4.3+sin(i.w.z*.22)*2.4));
                    albedo=lerp(albedo,half3(.56,.53,.35),saturate(1-track/1.2)*.48);
                }
                if(_Rock>.5)
                {
                    // Recovered EnvWorldMountain: diffuse alpha blends the
                    // world terrain page, it never cuts out rock fragments.
                    // The local palette stands in for the unlocated VT page.
                    half3 terrain=_RockTop.rgb*lerp(.84,1.08,noise(i.w.xz*.24));
                    albedo=lerp(tex.rgb*_RockColor.rgb*2.3,terrain,tex.a);
                    // Original mobile program adds RG tangent perturbation to
                    // the geometric normal, then normalizes (no sqrt Z term).
                    half2 xy=SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,i.uv).rg*2-1;
                    half3 tangent=normalize(i.t.xyz);
                    half3 bitangent=cross(normalWS,tangent)*i.t.w;
                    normalWS=normalize(normalWS+tangent*xy.x+bitangent*xy.y);
                }
                Light light=GetMainLight(TransformWorldToShadowCoord(i.w));
                half wrapped=dot(normalWS,light.direction)*.5+.5;
                half band=lerp(_Rock>.5?.36:.76,1,smoothstep(.40,.68,wrapped));
                half shadow=lerp(.32,1,smoothstep(.04,.94,light.shadowAttenuation));
                half3 daylight=light.color*.82+SampleSH(half3(0,1,0))*.46;
                half3 rgb=albedo*lerp(_ShadowTint.rgb,daylight,shadow)*band;
                return half4(MixFog(rgb,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            float3 _LightDirection;
            struct S{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            S shadowVert(A a){S o;float3 w=wind(a);o.p=TransformWorldToHClip(ApplyShadowBias(w,TransformObjectToWorldNormal(a.n),_LightDirection));
            #if UNITY_REVERSED_Z
                o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE);
            #else
                o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE);
            #endif
                o.uv=TRANSFORM_TEX(a.uv,_BaseMap);return o;}
            half4 shadowFrag(S i):SV_Target{clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags{"LightMode"="DepthOnly"}
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment depthFrag
            half4 depthFrag(V i):SV_Target{clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);return i.p.z;}
            ENDHLSL
        }
    }
}
