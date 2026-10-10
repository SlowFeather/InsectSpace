Shader "InsectSpace/Local AFK Character"
{
    Properties
    {
        _MainTex("Source diffuse",2D)="white"{}
        _Mask("Source lighting mask",2D)="black"{}
        _MiscMap("Source emission sheen roughness metallic",2D)="black"{}
        _Color("Source color",Color)=(1,1,1,1)
        _DarkTint("Source dark tint",Color)=(.644,.644,.644,1)
        _MidTint("Source mid tint",Color)=(1,1,1,1)
        _ShadowThreshold("Source shadow threshold",Float)=.574
        _DarkThreshold("Source dark threshold",Float)=.973
        _ShadowFeather("Source feather",Float)=.05
        _ShadowIntensity("Source shadow intensity",Float)=1
        _EnvColorWeight("Source environment weight",Float)=.5
        _Metallic("Source metallic",Float)=1
        _Roughness("Source roughness",Float)=1
        _SpecularRange("Source specular range",Float)=.2
        _SpecularFeather("Source specular feather",Float)=.5
        _SpecularColor("Source specular color",Color)=(1,1,1,1)
        _SpecularOffset("Source specular offset",Vector)=(0,0,0,0)
        _LineTint("Source line tint",Color)=(1,1,1,1)
        _LineRange("Source line range",Float)=1
        _LineIntensity("Source line intensity",Float)=1
        _EmissionColor("Source emission color",Color)=(0,0,0,1)
        _EmissionIntensity("Source emission intensity",Float)=0
        _Clamp("Source additional light clamp",Float)=1.5
        _StudyLocalLightSharp("Source local light ramp branch",Float)=0
        _StudyLocalLightGain("Calibrated local light response",Float)=1
        _SDF("Original UV2 face distance field",2D)="black"{}
        _USESDF("Original SDF state",Float)=0
        _SDF_Bias("Original face light bias",Float)=.1
        _USESUBSURFACE("Original skin transmission state",Float)=0
        _Thickness("Original skin thickness",Range(0,1))=0
        _SubsurfaceNormalDistortion("Original skin normal distortion",Float)=.13
        _ScatteringExp("Original transmission exponent",Float)=3
        _ScatteringScale("Original transmission scale",Float)=1
        _SubsurfaceTint("Original transmission tint",Color)=(.97,.02,.03,1)
        _SubsurfaceAmbient("Original skin ambient",Color)=(.2,.032,.016,1)
        _CustomEnvironmentReflection("Decoded source environment reflection",Cube)="black"{}
        _IndirectSpecular("Original indirect specular",Float)=0
        _CharDepthRimState("Original depth rim state",Float)=1
        _StudyRimIntensity("Local depth rim intensity",Float)=.12
        _StudyRimWidth("Local depth rim pixels",Float)=2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Source culling",Float)=2
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "AfkAtmosphere.hlsl"
        TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
        TEXTURE2D(_Mask);SAMPLER(sampler_Mask);
        TEXTURE2D(_MiscMap);SAMPLER(sampler_MiscMap);
        TEXTURE2D(_SDF);SAMPLER(sampler_SDF);
        TEXTURECUBE(_CustomEnvironmentReflection);SAMPLER(sampler_CustomEnvironmentReflection);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST,_SpecularOffset;
        half4 _Color,_DarkTint,_MidTint,_SpecularColor,_LineTint,_EmissionColor;
        float _ShadowThreshold,_DarkThreshold,_ShadowFeather,_ShadowIntensity,_EnvColorWeight;
        float _Metallic,_Roughness,_SpecularRange,_SpecularFeather,_LineRange,_LineIntensity;
        float _EmissionIntensity,_Clamp,_StudyLocalLightSharp,_StudyLocalLightGain;
        float _USESDF,_SDF_Bias,_USESUBSURFACE,_Thickness,_SubsurfaceNormalDistortion,_ScatteringExp,_ScatteringScale;
        half4 _SubsurfaceTint,_SubsurfaceAmbient;
        float _IndirectSpecular,_CharDepthRimState,_StudyRimIntensity,_StudyRimWidth;
        CBUFFER_END
        half4 _StudyAmbient;
        half4 _StudyZoneLightColor;
        float _StudyZoneLightIntensity;
        float3 _StudyFaceForward;
        struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float2 uv2:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;float2 uv2:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID};
        V vert(A a) {
            UNITY_SETUP_INSTANCE_ID(a);V o;UNITY_TRANSFER_INSTANCE_ID(a,o);
            o.w=TransformObjectToWorld(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);
            o.p=TransformWorldToHClip(o.w);o.uv=TRANSFORM_TEX(a.uv,_MainTex);o.uv2=a.uv2;o.fog=ComputeFogFactor(o.p.z);return o;
        }
        half faceShadow(float2 uv,half3 light) {
            float angle=_SDF_Bias*PI;
            float2 direction=normalize(float2(cos(angle)*light.x-sin(angle)*light.z,sin(angle)*light.x+cos(angle)*light.z));
            float2 forward=normalize(_StudyFaceForward.xz+float2(0,.00001));
            float signedAngle=acos(clamp(dot(forward,direction),-1,1))/PI;
            bool mirror=forward.x*direction.y-forward.y*direction.x<0;
            float mirrored=uv.x<1?1-uv.x:uv.x<1.5?.5-uv.x:-.5-uv.x;
            half distance=SAMPLE_TEXTURE2D(_SDF,sampler_SDF,float2(mirror?mirrored:uv.x,uv.y)).r;
            half lit=smoothstep(0,1,saturate((distance-(clamp(signedAngle,.000061,.99994)-.25))*2))*step(.001,distance);
            return uv.x>1&&uv.x<1.5?1-lit:lit;
        }
        // CharShow 9-100: the primary light wraps N dot L; local lights use N dot L.
        half3 ramp(half3 diffuse,half facing,half shadow,half threshold,half feather,out half bright) {
            half divisor=max(feather,.000061);
            bright=min(saturate((facing-threshold+feather)/divisor),shadow);
            half dark=saturate((facing-threshold*_DarkThreshold+feather)/divisor);
            half3 shade=lerp(diffuse*_DarkTint.rgb,diffuse,bright);
            shade=lerp(shade,diffuse*_MidTint.rgb,bright*(1-bright));
            return lerp(diffuse*_DarkTint.rgb,shade,dark);
        }
        half3 specular(half3 n,half3 view,Light light,half3 albedo,half roughness,half metallic) {
            half3 h=SafeNormalize(view+light.direction);
            half nh=saturate(dot(n,h)),lh=saturate(dot(light.direction,h));
            half r2=max(roughness*roughness,.000061),r4=r2*r2;
            half d=nh*nh*(r4-1)+1.00001;
            half energy=saturate(r4/((r2*4+2)*max(lh*lh,.1)*d*d)-.000061);
            half feather=_SpecularFeather*(_SpecularRange-.001)+.001;
            half highlight=smoothstep(saturate(_SpecularRange-feather),max(saturate(_SpecularRange+feather),.0001),energy);
            return lerp(half3(.04,.04,.04),albedo,metallic)*highlight*_SpecularColor.rgb*saturate(dot(n,light.direction))*(1-roughness);
        }
        ENDHLSL
        Pass
        {
            Name "RecoveredCharacter" Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
            half4 frag(V i):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).rgb*_Color.rgb;
                half4 misc=SAMPLE_TEXTURE2D(_MiscMap,sampler_MiscMap,i.uv);
                half2 mask=SAMPLE_TEXTURE2D(_Mask,sampler_Mask,i.uv).rg;
                half roughness=saturate(misc.b*_Roughness),metallic=saturate(misc.a*_Metallic);
                half3 diffuse=max(albedo*(1-metallic),.000061);
                half3 n=normalize(i.n),view=GetWorldSpaceNormalizeViewDir(i.w);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                Light main=GetMainLight(ComputeScreenPos(TransformWorldToHClip(i.w)));
                #else
                Light main=GetMainLight(TransformWorldToShadowCoord(i.w));
                #endif
                main.shadowAttenuation*=StudyCloudShadow(i.w);
                half bright;
                half shadow=lerp(1,main.shadowAttenuation,saturate(_ShadowIntensity));
                half facing=dot(n,main.direction)*.5+.5;
                if(_USESDF>.5&&i.uv2.x>0) facing=lerp(_ShadowThreshold-_ShadowFeather,_ShadowThreshold,faceShadow(i.uv2,main.direction));
                half3 shade=ramp(diffuse,facing,shadow,_ShadowThreshold,_ShadowFeather,bright);
                half influence=bright+(1-bright)*(1-_EnvColorWeight);
                half3 ambient=diffuse*lerp(half3(1,1,1),_StudyAmbient.rgb,_StudyAmbient.a);
                // CharShow blends white and zone light before applying its capped intensity.
                half3 primary=lerp(half3(1,1,1),_StudyZoneLightColor.rgb,_EnvColorWeight)*min(_StudyZoneLightIntensity,1.5);
                half3 direct=shade*primary+specular(n,SafeNormalize(view+_SpecularOffset.xyz),main,albedo,roughness,metallic)*primary;
                half sheen=pow(abs(dot(n,SafeNormalize(view+main.direction))),max(_LineRange,0))*misc.g;
                half3 additional=0;
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData=(InputData)0;inputData.positionWS=i.w;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
                uint lightCount=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light local=GetAdditionalLight(lightIndex,i.w,half4(1,1,1,1));
                    half localBright;
                    half localFeather=_StudyLocalLightSharp>.5?.5:_ShadowFeather;
                    half localThreshold=_StudyLocalLightSharp>.5?0:_ShadowThreshold;
                    half3 localShade=ramp(diffuse,dot(n,local.direction),local.shadowAttenuation,localThreshold,localFeather,localBright);
                    half localWeight=localBright+(1-localBright)*(1-_EnvColorWeight);
                    half3 irradiance=min(local.color*local.distanceAttenuation*_StudyLocalLightGain,max(_Clamp/3,0));
                    additional+=(localShade*localWeight+specular(n,view,local,albedo,roughness,metallic))*irradiance;
                    influence=max(influence,saturate(local.distanceAttenuation*localWeight));
                LIGHT_LOOP_END
                #endif
                half maskInfluence=saturate(max(min(influence,influence*(1-mask.r)),mask.r*(1-influence)));
                half3 rgb=lerp(ambient*primary,direct,maskInfluence);
                rgb*=lerp(half3(1,1,1),_LineTint.rgb*max(_LineIntensity,1)*primary,saturate(sheen));
                rgb+=additional+misc.r*_EmissionColor.rgb*_EmissionIntensity;
                if(_USESUBSURFACE>.5) {
                    half transmission=pow(saturate(dot(view,-(main.direction+n*_SubsurfaceNormalDistortion))),max(_ScatteringExp,.01))*_ScatteringScale;
                    rgb+=(transmission*_SubsurfaceTint.rgb+_SubsurfaceAmbient.rgb)*(1-_Thickness)*(1-mask.g)*primary;
                }
                half nv=saturate(dot(n,view));
                half3 f0=lerp(half3(.04,.04,.04),albedo,metallic);
                half3 ibl=SAMPLE_TEXTURECUBE_LOD(_CustomEnvironmentReflection,sampler_CustomEnvironmentReflection,reflect(-view,n),roughness*6).rgb;
                rgb+=ibl*(f0+(1-f0)*pow(1-nv,5))*_IndirectSpecular/(1+roughness*roughness)*lerp(.35,1,saturate(_StudyZoneLightIntensity));
                if(_CharDepthRimState>.5&&_StudyRimIntensity>0) {
                    float2 normalVS=mul((float3x3)UNITY_MATRIX_V,n).xy;
                    float2 screen=GetNormalizedScreenSpaceUV(i.p);
                    float2 offset=normalVS*_StudyRimWidth/_ScaledScreenParams.xy;
                    float raw=SampleSceneDepth(saturate(screen+offset));
                    float3 behind=ComputeWorldSpacePosition(saturate(screen+offset),raw,UNITY_MATRIX_I_VP);
                    half gap=saturate((length(behind-_WorldSpaceCameraPos)-length(i.w-_WorldSpaceCameraPos))/.4);
                    rgb+=gap*(1-nv)*primary*_StudyRimIntensity;
                }
                return half4(MixFog(max(rgb,0),i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            V shadowVert(A a) {V o=vert(a);
                float3 lightDirection=_LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                lightDirection=normalize(_LightPosition-o.w);
            #endif
                o.p=TransformWorldToHClip(ApplyShadowBias(o.w,o.n,lightDirection));
                #if UNITY_REVERSED_Z
                o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE);
                #else
                o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;}
            half4 shadowFrag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            half4 depthFrag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);return i.p.z;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormals"}
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment normalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 normalsFrag(V i):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 normal=NormalizeNormalPerPixel(i.n);
                #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*.5+.5)),0);
                #else
                return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
}
