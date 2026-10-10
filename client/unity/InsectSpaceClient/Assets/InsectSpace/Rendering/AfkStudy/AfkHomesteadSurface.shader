Shader "InsectSpace/Local Homestead Surface"
{
    Properties
    {
        _MainTex("Source diffuse",2D)="white"{}
        _DetailTex("Source reed detail",2D)="white"{}
        _NoiseTex("Source reed color noise",2D)="black"{}
        _WaveNoise("Source reed wave noise",2D)="black"{}
        _NoiseColor("Source reed noise color",Color)=(0,0,0,1)
        _NoiseRotation("Source reed noise rotation",Float)=0
        _WaveColor("Source reed wave color",Color)=(0,0,0,1)
        _WaveSpeed("Source reed wave speed",Float)=.16
        _NormalMap("Source normal RG",2D)="gray"{}
        _WorldMap("Recovered world VT",2D)="white"{}
        _WorldMapMip("Local coarse VT smoothing",Float)=0
        _TerrainDetail("Recovered template ground tile",2D)="gray"{}
        _TerrainDetailStrength("Template detail contribution",Range(0,1))=0
        _TerrainDetailTiling("Local template ground scale",Float)=.12
        _TerrainDetailContrast("Local template ground contrast",Float)=1
        _WorldMapRect("World rectangle",Vector)=(0,0,408,544)
        _Color("Source color",Color)=(1,1,1,1)
        _BaseColor("Source grass diffuse multiplier",Color)=(1,1,1,1)
        _Tint("Source foliage tint",Color)=(1,1,1,1)
        _BottomColor("Source bottom",Color)=(1,1,1,1)
        _TopColor("Source top",Color)=(0,0,0,1)
        _BottomValue("Source bottom value",Float)=.7
        _TopSatOffset("Source top saturation",Float)=.284
        _GradientPos("Source gradient position",Float)=0
        _GradientOffsetSilver("Source reed gradient offset",Float)=.26
        _RevertGradient("Source reverse gradient",Float)=0
        _AOInstensity("Source grass AO",Float)=.231
        _TYPE_FLOWER("Source flower mode",Float)=0
        _GrassHeight("Source grass height",Float)=1
        _GrassWidth("Source grass width",Float)=1
        _Scale("Source flower scale",Float)=.1
        _FlowerNoRot("Source flower rotation bypass",Float)=0
        _TYPE_SLIVER("Source reed mode",Float)=0
        _HasDetail("Detail stream",Float)=0
        _UseBillboard("Source reed billboard",Float)=0
        _NoZBillboard("Source upright blend",Float)=0
        _Width("Source billboard width",Float)=1
        _Height("Source billboard height",Float)=1
        _OffsetY("Source vertical offset",Float)=0
        _ShadowLength("Source billboard shadow length",Float)=1.2
        _ShadowOffset("Source billboard shadow offset",Float)=0
        _ShadowDepthOffset("Source foliage shadow receiver offset",Float)=0
        _CameraForwardOffsetXZ("Source billboard depth offset",Float)=0
        _DarkTint("Source dark tint",Color)=(.3,.3,.3,1)
        _ShadowTint("Source grass shadow tint",Color)=(.3,.3,.3,1)
        _MidTint("Source mid tint",Color)=(.5,.5,.5,1)
        _ShadowMapTint("Source shadow tint",Color)=(.3,.3,.3,1)
        _ShadowThreshold("Source shadow threshold",Float)=.75
        _DarkThreshold("Source dark threshold",Float)=.75
        _ShadowFeather("Source feather",Float)=.06
        _InShadowIntensity("Source shadow light",Float)=0
        _NormalScale("Source normal scale",Float)=1
        _AOIntensity("Source cliff VT AO",Float)=0
        _NormalIntensity("Source foliage normal intensity",Float)=1
        _ShadowIntensity("Source shadow intensity",Float)=1
        _Cutoff("Source alpha cutoff",Float)=.5
        _AlphaTest("Alpha test",Float)=0
        _HasNormal("Normal stream",Float)=0
        _SourceTangentBasis("Source interpolated tangent basis",Range(0,1))=1
        _Kind("Surface kind",Float)=0
        _UseSourceZoneLight("Recovered zone light values",Range(0,1))=0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Source face culling",Float)=0
        _TERRAINFRESNEL("Source terrain fill light",Float)=0
        _Wind("Wind amplitude",Float)=0
        _StudyDayTint("Calibrated day tint",Color)=(1,1,1,1)
        _StudyDayTopTint("Calibrated day upper surface tint",Color)=(1,1,1,1)
        _StudyNightTint("Calibrated night tint",Color)=(1,1,1,1)
        _StudyNightExposure("Calibrated night exposure",Float)=1
        _StudyNightTopExposure("Calibrated night upper surface exposure",Float)=1
        _StudyNightTopSaturation("Calibrated night upper surface saturation",Range(0,1))=1
        _StudyNightTopTint("Calibrated night upper surface tint",Color)=(1,1,1,1)
        _StudyNightShadowTint("Calibrated night shadow tint",Color)=(1,1,1,1)
        _StudyLocalLightWeight("Calibrated local light response",Float)=1
        _StudyLocalShadowWeight("Calibrated local fill occlusion",Range(0,1))=0
    }
    SubShader
    {
        // Billboard and GPU-grass deformation needs the original object-space vertices.
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest" "DisableBatching"="True"}
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "AfkAtmosphere.hlsl"
        TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
        TEXTURE2D(_DetailTex);SAMPLER(sampler_DetailTex);
        TEXTURE2D(_NoiseTex);SAMPLER(sampler_NoiseTex);
        TEXTURE2D(_WaveNoise);SAMPLER(sampler_WaveNoise);
        TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);
        TEXTURE2D(_WorldMap);SAMPLER(sampler_WorldMap);
        TEXTURE2D(_TerrainDetail);SAMPLER(sampler_TerrainDetail);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST,_DetailTex_ST,_NoiseTex_ST,_WorldMapRect;
        float _TerrainDetailStrength,_TerrainDetailTiling,_TerrainDetailContrast,_WorldMapMip;
        float _UseSourceZoneLight,_SourceTangentBasis;
        half4 _Color,_BaseColor,_Tint,_BottomColor,_TopColor,_DarkTint,_MidTint,_ShadowMapTint,_ShadowTint,_NoiseColor,_WaveColor;
        float _NoiseRotation,_WaveSpeed;
        float _BottomValue,_TopSatOffset,_GradientPos,_RevertGradient,_AOInstensity,_TYPE_FLOWER,_TYPE_SLIVER,_HasDetail;
        float _UseBillboard,_NoZBillboard,_Width,_Height,_OffsetY,_GrassHeight,_GrassWidth,_AOIntensity;
        float _ShadowLength,_ShadowOffset,_ShadowDepthOffset,_CameraForwardOffsetXZ,_GradientOffsetSilver;
        float _Scale,_FlowerNoRot,_TERRAINFRESNEL;
        half4 _StudyDayTint,_StudyDayTopTint,_StudyNightTint,_StudyNightTopTint,_StudyNightShadowTint;
        float _StudyNightExposure,_StudyNightTopExposure,_StudyNightTopSaturation,_StudyLocalLightWeight,_StudyLocalShadowWeight;
        float _ShadowThreshold,_DarkThreshold,_ShadowFeather,_InShadowIntensity,_NormalScale,_NormalIntensity,_ShadowIntensity,_Cutoff,_AlphaTest,_HasNormal,_Kind,_Wind;
        CBUFFER_END
        half4 _StudyAmbient;
        half4 _StudyZoneLightColor;
        float _StudyZoneLightIntensity;
        float _StudyTimeOfDay;
        float4 _StudyTerrainFresnel;
        struct A {float4 p:POSITION;float3 n:NORMAL;float4 t:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half4 t:TEXCOORD2;float4 uv:TEXCOORD3;half fog:TEXCOORD4;float height:TEXCOORD5;half wave:TEXCOORD6;half3 b:TEXCOORD7;UNITY_VERTEX_INPUT_INSTANCE_ID};
        float3 billboard(float3 p,float3 backward,bool shadow) {
            float3 up=normalize(TransformObjectToWorldDir(float3(0,1,0)));
            // Recovered EnvWorldFoliageBillboard uses cross(view backward, up).
            float3 right=normalize(cross(backward,up));
            float3 tilted=normalize(normalize(cross(right,backward))+up);
            tilted=lerp(tilted,up,_NoZBillboard);
            float sx=length(float3(unity_ObjectToWorld._m00,unity_ObjectToWorld._m01,unity_ObjectToWorld._m02));
            float sy=length(float3(unity_ObjectToWorld._m10,unity_ObjectToWorld._m11,unity_ObjectToWorld._m12));
            float3 vertical=shadow?up*_ShadowLength:tilted;
            float3 result=TransformObjectToWorld(float3(0,0,0))+right*p.x*sx*_Width+vertical*p.y*sy*_Height;
            result.xz+=backward.xz/max(length(backward.xz),.0001)*_CameraForwardOffsetXZ;
            result.y+=_OffsetY+(shadow?_ShadowOffset:0);
            // The source shadow pass offsets one unit toward the light to avoid self-shadow stripes.
            return result+(shadow?backward:float3(0,0,0));
        }
        V vert(A a) {
            UNITY_SETUP_INSTANCE_ID(a);V o;UNITY_TRANSFER_INSTANCE_ID(a,o);o.w=TransformObjectToWorld(a.p.xyz);
            if(_Kind==4 && _UseBillboard<.5) {
                float3 p=a.p.xyz;
                p.y*=_GrassHeight;p.xz*=clamp(_GrassHeight*.8,.3,1)*_GrassWidth;
                // The recovered GPU grass program ignores instance rotation.
                float uniformScale=length(float3(unity_ObjectToWorld._m00,unity_ObjectToWorld._m10,unity_ObjectToWorld._m20));
                float3 pivot=TransformObjectToWorld(float3(0,0,0));
                p*=uniformScale;
                if(_TYPE_FLOWER>.5) {
                    // EnvGrassGPUInstance 9-217: flower-only position hash rotation and _Scale.
                    float angle=sin(pivot.x*32.4364319+pivot.z)*30;
                    float s=sin(angle),c=cos(angle);
                    p.xz=lerp(float2(c*p.x+s*p.z,-s*p.x+c*p.z),p.xz,_FlowerNoRot);
                    p*=_Scale;
                }
                o.w=p+pivot;
            }
            if(_Kind==3 || _UseBillboard>.5) o.w=billboard(a.p.xyz,normalize(UNITY_MATRIX_V[2].xyz),false);
            o.w.x+=sin(o.w.x*1.3+o.w.z+_Time.y*1.5)*_Wind*saturate(a.p.y);
            o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);
            o.t=half4(TransformObjectToWorldDir(a.t.xyz),a.t.w*GetOddNegativeScale());
            o.b=cross(o.n,o.t.xyz)*o.t.w;
            o.uv=float4(TRANSFORM_TEX(a.uv,_MainTex),TRANSFORM_TEX(a.uv,_DetailTex));
            float2 waveUv=float2(dot(o.w.xz,float2(.7071068,.7071068)),dot(o.w.xz,float2(-.7071068,.7071068)))*.035+_Time.y*_WaveSpeed;
            o.wave=_TYPE_SLIVER>.5?saturate(a.p.y)*.23*SAMPLE_TEXTURE2D_LOD(_WaveNoise,sampler_WaveNoise,waveUv,0).r:0;
            o.height=a.p.y;o.fog=ComputeFogFactor(o.p.z);return o;
        }
        half4 source(float4 uv) {return _HasDetail>.5?SAMPLE_TEXTURE2D(_DetailTex,sampler_DetailTex,uv.zw):SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv.xy);}
        void alpha(float4 uv) {if(_AlphaTest>.5) clip(source(uv).a-_Cutoff);}
        half3 terrainSample(float2 uv,float2 cell) {
            float2 seed=frac(sin(float2(dot(cell,float2(127.1,311.7)),dot(cell,float2(269.5,183.3))))*43758.5453);
            float angle=floor(seed.x*4)*1.5707963;
            float2x2 rotation=float2x2(cos(angle),-sin(angle),sin(angle),cos(angle));
            return SAMPLE_TEXTURE2D_GRAD(_TerrainDetail,sampler_TerrainDetail,mul(rotation,uv)+seed*13.7,
                mul(rotation,ddx(uv)),mul(rotation,ddy(uv))).rgb;
        }
        half3 terrainDetail(float2 uv) {
            float2 cell=floor(uv*.35),weight=smoothstep(0,1,frac(uv*.35));
            return lerp(lerp(terrainSample(uv,cell),terrainSample(uv,cell+float2(1,0)),weight.x),
                lerp(terrainSample(uv,cell+float2(0,1)),terrainSample(uv,cell+1),weight.x),weight.y);
        }
        ENDHLSL
        Pass
        {
            Name "RecoveredForward" Tags {"LightMode"="UniversalForward"}
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
                alpha(i.uv);half4 diffuse=source(i.uv)*_Color;
                half4 world=SAMPLE_TEXTURE2D_BIAS(_WorldMap,sampler_WorldMap,(i.w.xz-_WorldMapRect.xy)/_WorldMapRect.zw,_WorldMapMip);
                half3 vt=world.rgb*(1-(1-world.a)*_AOIntensity);
                half3 groundDetail=terrainDetail(i.w.xz*_TerrainDetailTiling);
                // Amplify the recovered tile's paint variation when fine VT pages are absent.
                groundDetail=max(half3(.005,.005,.005),half3(.079,.148,.087)+(groundDetail-half3(.079,.148,.087))*_TerrainDetailContrast);
                vt*=lerp(half3(1,1,1),groundDetail/half3(.13,.21,.15),_TerrainDetailStrength);
                half3 albedo=diffuse.rgb;
                if(_Kind==1) albedo=vt;
                if(_Kind==2) albedo=lerp(albedo,vt,diffuse.a);
                if(_Kind==3) albedo*=_Tint.rgb;
                if(_Kind==4) {
                    half g=saturate(i.height-_GradientPos);g=lerp(g,1-g,_RevertGradient);
                    if(_TYPE_SLIVER>.5) {
                        float angle=radians(_NoiseRotation),s=sin(angle),c=cos(angle);
                        float2 noiseUv=float2(dot(float2(c,s),i.w.xz),dot(float2(-s,c),i.w.xz))*_NoiseTex_ST.xy+_NoiseTex_ST.zw;
                        half noise=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,noiseUv).r;
                        half3 reed=lerp(diffuse.rgb*_BaseColor.rgb,_NoiseColor.rgb,noise)+_WaveColor.rgb*i.wave;
                        half reedGradient=saturate(i.height/max(_GradientPos,.0001)-_GradientOffsetSilver);
                        albedo=lerp(_BottomColor.rgb,reed,reedGradient);
                    }
                    else if(_TYPE_FLOWER>.5) albedo=diffuse.rgb*lerp(_BottomColor.rgb,_TopColor.rgb,g);
                    else {
                        half3 base=vt*(1-(1-world.a)*_AOInstensity);
                        albedo=lerp(base*_BottomValue,base+_TopColor.rgb*saturate(base-_TopSatOffset),g);
                    }
                }
                half3 n=normalize(i.n);
                if(_HasNormal>.5) {
                    half2 xy=(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,i.uv.xy).rg*2-1)*_NormalScale;
                    if(_Kind==3) {
                        half3 objectNormal=SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,i.uv.xy).rgb*2-1;
                        objectNormal.xy*=_NormalIntensity;
                        n=normalize(TransformObjectToWorldDir(objectNormal));
                    }
                    else {
                        // EnvWorldMountain interpolates all three vertex basis vectors.
                        half3 basisNormal=lerp(n,i.n,_SourceTangentBasis);
                        half3 basisBitangent=lerp(cross(n,i.t.xyz)*i.t.w,i.b,_SourceTangentBasis);
                        n=normalize(basisNormal+i.t.xyz*xy.x+basisBitangent*xy.y);
                    }
                }
                if(_Kind==4) n=half3(0,1,0);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                Light light=GetMainLight(ComputeScreenPos(TransformWorldToHClip(i.w)));
                #else
                Light light=GetMainLight(TransformWorldToShadowCoord(i.w));
                #endif
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                if(_Kind==3 && _ShadowDepthOffset!=0) {
                    // The source biases the receiver in view space before sampling its cascades.
                    float3 receiver=i.w+mul((float3x3)UNITY_MATRIX_I_V,float3(_ShadowDepthOffset,_ShadowDepthOffset,_ShadowDepthOffset));
                    half cascade=ComputeCascadeIndex(receiver);
                    float4 coord=float4(mul(_MainLightWorldToShadow[cascade],float4(receiver,1)).xyz,0);
                    light.shadowAttenuation=SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture,sampler_LinearClampCompare),coord,GetMainLightShadowSamplingData(),GetMainLightShadowParams(),false);
                }
                #endif
                light.shadowAttenuation*=StudyCloudShadow(i.w);
                half wrapped=dot(n,light.direction)*.5+.5;
                half bright=saturate((wrapped-(_ShadowThreshold-_ShadowFeather))/max(_ShadowFeather,.0001));
                half dark=saturate((wrapped-(_ShadowThreshold*_DarkThreshold-_ShadowFeather))/max(_ShadowFeather,.0001));
                half3 shadowTint=_Kind==4?_ShadowTint.rgb:_DarkTint.rgb;
                half3 shade=_Kind==3 ? albedo*bright : lerp(albedo*shadowTint,albedo,bright);
                shade=lerp(shade,albedo*_MidTint.rgb,bright*(1-bright));
                shade=_Kind==4?shade*dark:lerp(albedo*_DarkTint.rgb,shade,dark);
                half attenuation=min(lerp(1,light.shadowAttenuation,_ShadowIntensity),saturate(bright+_InShadowIntensity*(1-bright)));
                // Recovered shader blends directional and zone ambient terms.
                half3 ambient=lerp(albedo,albedo*_StudyAmbient.rgb,_StudyAmbient.a);
                // EnvWorldMountain 9-100: distance-based terrain fill is added to
                // the directional term before its blend with the zone ambient.
                // Despite its source name, this is not a normal/view-angle rim.
                float viewDistance=unity_OrthoParams.w>.5?1:max(length(_WorldSpaceCameraPos-i.w),.0001);
                float terrainFill=exp2(min(log2(max(_StudyTerrainFresnel.x/viewDistance,.0001))*_StudyTerrainFresnel.y,log2(max(_StudyTerrainFresnel.z,.0001))));
                // Terrain and GPU grass use the same fill without a material toggle.
                half fillWeight=(_Kind==1||_Kind==4)?1:(_Kind==2?_TERRAINFRESNEL:0);
                half3 zoneLight=lerp(light.color,_StudyZoneLightColor.rgb*_StudyZoneLightIntensity,_UseSourceZoneLight);
                half3 fill=saturate(albedo*zoneLight*terrainFill)*fillWeight;
                float night=1-smoothstep(.18,.32,min(_StudyTimeOfDay,1-_StudyTimeOfDay));
                ambient*=lerp(half3(1,1,1),_StudyNightShadowTint.rgb,night);
                half3 rgb=lerp(ambient,shade*zoneLight+fill,attenuation);
                // Cliff diffuse alpha marks the VT-painted cap independently of its normal map.
                half upperSurface=smoothstep(.6,.9,normalize(i.n).y);
                if(_Kind==2) upperSurface=max(upperSurface,diffuse.a);
                half dayTop=_Kind==2?diffuse.a:upperSurface;
                rgb*=lerp(_StudyDayTint.rgb*lerp(half3(1,1,1),_StudyDayTopTint.rgb,dayTop),half3(1,1,1),night);
                half exposure=_StudyNightExposure*lerp(1,_StudyNightTopExposure,upperSurface);
                rgb*=lerp(half3(1,1,1),_StudyNightTint.rgb*exposure,night);
                half luminance=dot(rgb,half3(.2126,.7152,.0722));
                rgb=lerp(rgb,luminance.xxx,night*upperSurface*(1-_StudyNightTopSaturation));
                rgb*=lerp(half3(1,1,1),_StudyNightTopTint.rgb,night*upperSurface);
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData=(InputData)0;
                inputData.positionWS=i.w;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
                uint lightCount=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light local=GetAdditionalLight(lightIndex,i.w,half4(1,1,1,1));
                    // Source foliage/grass adds unwrapped light; rock/ground wraps N dot L.
                    half facing=(_Kind==3||_Kind==4)?1:saturate(dot(n,local.direction)*.5+.5);
                    half fillOcclusion=lerp(1,light.shadowAttenuation,_StudyLocalShadowWeight*night);
                    rgb+=albedo*local.color*local.distanceAttenuation*facing*lerp(1,local.shadowAttenuation,_ShadowIntensity)*_StudyLocalLightWeight*fillOcclusion;
                LIGHT_LOOP_END
                #endif
                return half4(MixFog(saturate(rgb),i.fog),1);
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
                if(_Kind==3 || _UseBillboard>.5) o.w=billboard(a.p.xyz,lightDirection,true);
                o.p=TransformWorldToHClip(ApplyShadowBias(o.w,o.n,lightDirection));
            #if UNITY_REVERSED_Z
                o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE);
            #else
                o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE);
            #endif
                return o;}
            half4 shadowFrag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);alpha(i.uv);return 0;}
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
            half4 depthFrag(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);alpha(i.uv);return i.p.z;}
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
                UNITY_SETUP_INSTANCE_ID(i);alpha(i.uv);
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
