Shader "InsectSpace/Local Homestead Water"
{
    Properties
    {
        _NoiseTex("Original half float flow noise",2D)="gray"{}
        _TilingWater("Original ripple noise",2D)="gray"{}
        _LitSpotTex("Original light spots",2D)="black"{}
        _RefCube("Original skybox_02 reflection",Cube)="black"{}
        _RecoveredReflection("Recovered reflection path",Float)=0
        _NormalTilling("Source reflection noise tiling",Float)=.6
        _NormalIntensity("Source reflection distortion",Float)=.03
        _NoiseSpd("Source reflection flow speed",Float)=.15
        _RefRate("Source reflection rate",Float)=.8
        _RefRateMultiplier("Source reflection flow attenuation",Float)=1.5
        _Rotation("Source reflection rotation",Float)=190
        _PosBasedRotationMult("Source position rotation",Float)=.1
        _TimeBasedRotationMult("Source time rotation",Float)=.001
        _ReflectVectorOffset("Source reflection vector offset",Vector)=(0,0,0,0)
        _SunPosOffset("Source sun offset",Vector)=(0,0,0,0)
        _SunReflectDistort("Source sun distortion",Float)=.44
        _SunReflectRange("Source sun threshold",Float)=5.8
        _SunReflectAmount("Source sun amount",Float)=.22
        _CenterColor("Source center",Color)=(.085,.396,.547,1)
        _BedColor("Source bed",Color)=(.24,.839,1,1)
        _CoastColor("Source coast",Color)=(.114,.83,.442,1)
        _IntersectColor("Source shore",Color)=(.679,.667,.656,.412)
        _OutlineWidth("Source outline exponent",Float)=2
        _OutlineNoiseIntensity("Source outline noise",Float)=.434
        _FirstLayerWidth("Source first layer",Float)=.204
        _FlowFrequency("Source flow frequency",Float)=.091
        _WaveFeq("Source wave frequency",Float)=7.21
        _NoiseSizeSpeed("Source noise scale",Float)=.09
        _NoiseFrequency("Source noise frequency",Float)=1
        _ShadowIntensity("Source shadow strength",Float)=.5
        _LightAlphaThreshold("Source bed threshold",Float)=.618
        _DarkAlphaThreshold("Source coast threshold",Float)=.2
        _ApproximateMaskRange("Local VT substitute: min, max, noise amount",Vector)=(.74,.81,0,0)
        _StudyNightExposure("Calibrated night exposure",Float)=1
        _StudyShoreStrength("Local shore composition strength",Range(0,1))=1
        _StudyDayTint("Calibrated day tint",Color)=(1,1,1,1)
        _StudyNightTint("Calibrated night tint",Color)=(1,1,1,1)
        _StudyAbsorption("Local water absorption RGB",Vector)=(1.8,.65,.35,0)
        _StudyRefraction("Local refraction pixels",Float)=4
        _StudyDepthColor("Local depth composition",Range(0,1))=.65
        _StudySoftShore("Local shoreline distance stream",Range(0,1))=0
        _StudyRippleStrength("Local fine ripple strength",Range(0,1))=.28
        _StudyShoreWaveSpeed("Shore wave speed (m/s)",Float)=.4
        _StudyShoreWaveSpacing("Shore wave spacing (m)",Float)=.8
        _StudyShoreWaveWidth("Shore wave width (m)",Float)=.045
        _StudyShoreWaveRange("Shore wave fade distance (m)",Float)=1.8
        _StudyShoreWaveStrength("Shore wave strength",Range(0,1))=.6
        _StudyShoreSwayHeight("Near shore height amplitude (m)",Range(0,.02))=.009
        _StudyShoreSwayWidth("Near shore wash amplitude (m)",Range(0,.08))=.035
        [HideInInspector] _StudyShoreWaveTime("Shore wave capture time (-1 realtime)",Float)=-1
        [HideInInspector] _StudyDebugView("Water diagnostics",Float)=0
        _LitSpotsMaskScale("Source spots mask scale",Float)=.09
        _LitSpotsMaskOffset("Source spots mask offset",Float)=-.5
        _LitSpotsTexScale("Source spots texture scale",Float)=1
        _LitSpotsMask("Source spots mask",Float)=.48
        _LitSpotsBrightness("Source spots brightness",Float)=100
        _FadeDistance("Source spots fade distance",Float)=.08
        _FadeGradient("Source spots fade gradient",Float)=.84
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20"}
        Cull Off
        ZWrite On
        Blend One Zero
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "AfkAtmosphere.hlsl"
            TEXTURE2D(_NoiseTex);SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_TilingWater);SAMPLER(sampler_TilingWater);
            TEXTURE2D(_LitSpotTex);SAMPLER(sampler_LitSpotTex);
            TEXTURECUBE(_RefCube);SAMPLER(sampler_RefCube);
            TEXTURE2D(_StudyPlanarReflection);SAMPLER(sampler_StudyPlanarReflection);
            float4x4 _StudyPlanarVP;
            float _StudyPlanarEnabled;
            CBUFFER_START(UnityPerMaterial)
            half4 _CenterColor,_BedColor,_CoastColor,_IntersectColor;
            float _OutlineWidth,_OutlineNoiseIntensity,_FirstLayerWidth,_FlowFrequency,_WaveFeq,_NoiseSizeSpeed,_NoiseFrequency,_ShadowIntensity;
            float _RecoveredReflection,_NormalTilling,_NormalIntensity,_NoiseSpd,_RefRate,_RefRateMultiplier,_Rotation,_PosBasedRotationMult,_TimeBasedRotationMult;
            float4 _ReflectVectorOffset,_SunPosOffset;
            float _SunReflectDistort,_SunReflectRange,_SunReflectAmount;
            float _LightAlphaThreshold,_DarkAlphaThreshold;
            float4 _ApproximateMaskRange;
            float _StudyNightExposure,_StudyShoreStrength;
            half4 _StudyDayTint,_StudyNightTint;
            float4 _StudyAbsorption;
            float _StudyRefraction,_StudyDepthColor,_StudyDebugView;
            float _StudySoftShore,_StudyRippleStrength;
            float _StudyShoreWaveSpeed,_StudyShoreWaveSpacing,_StudyShoreWaveWidth,_StudyShoreWaveRange,_StudyShoreWaveStrength,_StudyShoreWaveTime;
            float _StudyShoreSwayHeight,_StudyShoreSwayWidth;
            float _LitSpotsMaskScale,_LitSpotsMaskOffset,_LitSpotsTexScale,_LitSpotsMask,_LitSpotsBrightness,_FadeDistance,_FadeGradient;
            CBUFFER_END
            half4 _StudyAmbient;
            half4 _StudyWaterLight,_StudyWaterDark,_StudyWaterCloud,_StudySunReflection;
            half4 _StudyWaterZoneLight,_StudyWaterAmbient,_StudyWaterCenter,_StudyWaterBed,_StudyWaterCoast,_StudyWaterIntersect;
            float4 _StudyWaterGradient;
            float _StudyWaterSkyWeight;
            float _StudyTimeOfDay;
            struct A {float4 p:POSITION;half4 color:COLOR;};
            struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half depth:TEXCOORD1;float shore:TEXCOORD2;};
            float ShoreTime(){return _StudyShoreWaveTime>=0?_StudyShoreWaveTime:_Time.y;}
            float2 ShoreSway(float3 world,float distance)
            {
                float phase=(distance+ShoreTime()*max(_StudyShoreWaveSpeed,0))/max(_StudyShoreWaveSpacing,.1)*TWO_PI+world.z*.7+world.x*.31;
                float motion=(sin(phase)+.25*sin(phase*2+world.z*.83))/1.25;
                float envelope=1-smoothstep(.12,1.2,distance);
                return motion*envelope*float2(_StudyShoreSwayHeight,_StudyShoreSwayWidth);
            }
            V vert(A a)
            {
                V o;o.w=TransformObjectToWorld(a.p.xyz);o.depth=a.color.r;o.shore=a.color.g;
                o.w.y+=ShoreSway(o.w,max(o.shore,0)).x*_StudySoftShore;
                o.p=TransformWorldToHClip(o.w);return o;
            }
            half4 frag(V i):SV_Target
            {
                float phase=frac(_Time.x*_NoiseFrequency);
                float2 uv=i.w.xz*_NoiseSizeSpeed;
                half2 a=SAMPLE_TEXTURE2D(_TilingWater,sampler_TilingWater,uv-float2(phase,0)).rg;
                half2 b=SAMPLE_TEXTURE2D(_TilingWater,sampler_TilingWater,uv-float2(frac(phase+.5),0)).rg;
                half2 noise=lerp(a,b,abs(phase-.5)*2);
                // Two nonparallel wavelengths add small moving ripples without a tiled grid.
                float phaseA=dot(i.w.xz,float2(.72,.29))*7+_Time.y*.85+noise.x*2;
                float phaseB=dot(i.w.xz,float2(-.36,.83))*11-_Time.y*1.1+noise.y*3;
                float2 ripple=(cos(phaseA)*float2(.72,.29)*.55+cos(phaseB)*float2(-.36,.83)*.3)*_StudyRippleStrength;
                float coast=saturate(1-i.depth+(noise.x-.5)*.2*_OutlineNoiseIntensity);
                // Preserve the source sawtooth; filter its subpixel threshold bands.
                float coastAA=max(fwidth(coast)*.5,.0001);
                float first=smoothstep(1-_FirstLayerWidth-coastAA,1-_FirstLayerWidth+coastAA,coast);
                float wave=frac((coast-_Time.y*_FlowFrequency)*_WaveFeq)*pow(coast,_OutlineWidth);
                float waveMask=wave*saturate(noise.y*.75+first);
                float waveAA=max(fwidth(waveMask)*.5,.0001);
                half edge=saturate(first+smoothstep(.1-waveAA,.1+waveAA,waveMask));
                edge*=1-_StudySoftShore;
                half3 color=lerp(_BedColor.rgb,_CenterColor.rgb,saturate(i.depth*1.5));
                float night=1-smoothstep(.18,.32,min(_StudyTimeOfDay,1-_StudyTimeOfDay));
                half3 waterTint=lerp(_StudyDayTint.rgb,_StudyNightTint.rgb*_StudyNightExposure,night);
                half shoreExposure=lerp(1,_StudyNightExposure,night);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                Light light=GetMainLight(ComputeScreenPos(TransformWorldToHClip(i.w)));
                #else
                Light light=GetMainLight(TransformWorldToShadowCoord(i.w));
                #endif
                light.shadowAttenuation*=StudyCloudShadow(i.w);
                if(_RecoveredReflection>.5) {
                    // Painted VT mask is absent. The depth-based mask is a local approximation.
                    half mask=lerp(_ApproximateMaskRange.x,_ApproximateMaskRange.y,saturate(i.depth));
                    mask=saturate(mask+_ApproximateMaskRange.z*(noise.x-.5));
                    mask=mask*mask*mask;
                    half2 weights=saturate((mask-half2(_LightAlphaThreshold,_DarkAlphaThreshold))/max(1-half2(_LightAlphaThreshold,_DarkAlphaThreshold),.0001));
                    color=lerp(_StudyWaterCoast.rgb,lerp(_StudyWaterCenter.rgb,_StudyWaterBed.rgb,weights.x),weights.y);
                    half3 direct=color*_StudyWaterZoneLight.rgb;
                    float3 view=GetWorldSpaceNormalizeViewDir(i.w);
                    float gradient=saturate((length(_WorldSpaceCameraPos.xz-i.w.xz)+_StudyWaterGradient.x)/max(_StudyWaterGradient.y*100,.0001));
                    gradient=lerp(gradient,.3,saturate((_WorldSpaceCameraPos.y-i.w.y-_StudyWaterGradient.z)*.008));
                    half3 sky=lerp(_StudyWaterDark.rgb,_StudyWaterLight.rgb,gradient);
                    direct=lerp(direct*sky,sky,saturate(_StudyWaterSkyWeight));
                    // The study has no painted VT flow map; use its minimum flow of one.
                    float2 flow=float2(1,0);
                    float phase0=frac(_Time.y*.25),phase1=frac(_Time.y*.25+.5);
                    float2 normalUV=i.w.xz*_NormalTilling*.2;
                    float flowSpeed=saturate(_NoiseSpd);
                    float2 normal0=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,frac(normalUV-2*phase0*flow*.5*flowSpeed)).xy;
                    float2 normal1=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,frac(normalUV-2*phase1*flow*.5*flowSpeed)).xy;
                    float2 distortion=lerp(normal0,normal1,abs(phase0-.5)*2);
                    float3 reflected=float3(-view.x,view.y,-view.z);
                    float2 positionRotation=frac(_WorldSpaceCameraPos.xz*_PosBasedRotationMult*float2(.01,.005));
                    float angle=radians(_Rotation+(positionRotation.y-positionRotation.x-frac(_Time.y*_TimeBasedRotationMult*.01))*360);
                    float s=sin(angle),c=cos(angle);
                    reflected.xz=float2(c*reflected.x-s*reflected.z,s*reflected.x+c*reflected.z);
                    reflected+=_ReflectVectorOffset.xyz+distortion.xyx*_NormalIntensity;
                    half3 cloud=SAMPLE_TEXTURECUBE(_RefCube,sampler_RefCube,reflected).rgb;
                    float rate=max(pow(.5,_RefRateMultiplier),_RefRate)*_RefRate;
                    direct+=cloud*rate*_StudyWaterZoneLight.rgb*_StudyWaterCloud.rgb;
                    float3 sunView=normalize(float3(view.x+distortion.x*_SunReflectDistort,view.y+1,view.z+distortion.y*_SunReflectDistort)+_SunPosOffset.xyz*.01);
                    float glint=pow(saturate(dot(sunView,light.direction)),2500);
                    glint=glint*glint*(3-2*glint);
                    direct+=saturate(glint*10-_SunReflectRange)*_StudySunReflection.rgb*_StudySunReflection.a*_SunReflectAmount;
                    // Water-body calibration must not recolor the source neutral shore bands.
                    direct=lerp(direct*waterTint,_IntersectColor.rgb*saturate(_StudyWaterZoneLight.rgb)*shoreExposure,edge*_StudyShoreStrength);
                    float2 spotsUV=i.w.xz*.1*_LitSpotsTexScale;
                    half spotsMask=SAMPLE_TEXTURE2D(_LitSpotTex,sampler_LitSpotTex,i.w.xz*.1*_LitSpotsMaskScale+_LitSpotsMaskOffset).g;
                    half2 spots0=SAMPLE_TEXTURE2D(_LitSpotTex,sampler_LitSpotTex,spotsUV-flow*(2*phase0)*.05).rb;
                    half2 spots1=SAMPLE_TEXTURE2D(_LitSpotTex,sampler_LitSpotTex,spotsUV-flow*(2*phase1)*.05).rb;
                    half2 spots=lerp(spots0,spots1,abs(phase0-.5)*2);
                    float fade=saturate(length(_WorldSpaceCameraPos-i.w)/max(_FadeGradient*50,.0001)-_FadeDistance);
                    half threshold=lerp(.38,_LitSpotsMask,fade);
                    half spotWeight=saturate((spotsMask-threshold)/max(spotsMask+1-threshold,.0001));
                    spotWeight=spotWeight*spotWeight*(3-2*spotWeight);
                    direct+=max((spots.x-.03)*spots.y*spotWeight*_LitSpotsBrightness*.2,0)*_StudyWaterIntersect.rgb*waterTint;
                    direct=min(direct,1.5);
                    half attenuation=saturate(light.shadowAttenuation+1-_ShadowIntensity);
                    color=lerp(lerp(direct,direct*_StudyWaterAmbient.rgb,_StudyWaterAmbient.a),direct,attenuation);
                } else {
                    color=lerp(color*waterTint,_IntersectColor.rgb*shoreExposure,edge*_IntersectColor.a*_StudyShoreStrength);
                    color*=lerp(_StudyAmbient.rgb,light.color,lerp(1,light.shadowAttenuation,_ShadowIntensity));
                }
                float2 screen=GetNormalizedScreenSpaceUV(i.p);
                float2 refractUV=saturate(screen+(noise-.5+ripple)*_StudyRefraction/_ScaledScreenParams.xy);
                float raw=SampleSceneDepth(refractUV);
                float3 bed=ComputeWorldSpacePosition(refractUV,raw,UNITY_MATRIX_I_VP);
                // Reject offsets that land on foreground geometry above the lake.
                if(bed.y>i.w.y+.02) {refractUV=screen;raw=SampleSceneDepth(screen);bed=ComputeWorldSpacePosition(screen,raw,UNITY_MATRIX_I_VP);}
                float depth=max(i.w.y-bed.y,0);
                half3 transmission=exp(-_StudyAbsorption.rgb*depth*2.2);
                half3 bedColor=SampleSceneColor(refractUV);
                if(_StudyDebugView>2.5&&_StudyDebugView<3.5) return half4(raw,bed.y,depth,1);
                if(_StudyDebugView>.5&&_StudyDebugView<2.5) return _StudyDebugView<1.5?half4(saturate(depth),0,0,1):half4(bedColor,1);
                float causticField=sin(phaseA*.62+sin(phaseB*.47))+sin(phaseB*.58+sin(phaseA*.39));
                half caustics=pow(saturate(1-abs(causticField)*2.8),5)*.055*light.shadowAttenuation*exp(-depth*1.5)*smoothstep(.03,.15,depth);
                half3 refracted=bedColor*transmission+color*(1-transmission)+caustics*light.color*transmission;
                color=lerp(color,refracted,_StudyDepthColor*(1-edge*_StudyShoreStrength));
                float3 surfaceNormal=normalize(cross(ddy(i.w),ddx(i.w)));
                surfaceNormal*=surfaceNormal.y<0?-1:1;
                float3 waterNormal=normalize(surfaceNormal-float3(ripple.x*.24,0,ripple.y*.24));
                float3 halfVector=SafeNormalize(GetWorldSpaceNormalizeViewDir(i.w)+light.direction);
                half softGlint=pow(saturate(dot(waterNormal,halfVector)),90)*.12*_StudyRippleStrength;
                color+=softGlint*light.color*light.shadowAttenuation;
                color*=1+(sin(phaseA)*sin(phaseB)*.065)*_StudyRippleStrength;
                if(_StudyPlanarEnabled>0) {
                    float4 projected=mul(_StudyPlanarVP,float4(i.w,1));
                    float2 reflectionUV=projected.xy/projected.w*.5+.5;
                    #if UNITY_UV_STARTS_AT_TOP
                    reflectionUV.y=1-reflectionUV.y;
                    #endif
                    reflectionUV+=(noise-.5+ripple)*.009+surfaceNormal.xz*.012;
                    half3 reflection=SAMPLE_TEXTURE2D(_StudyPlanarReflection,sampler_StudyPlanarReflection,saturate(reflectionUV)).rgb;
                    half fresnel=.15+.85*pow(1-saturate(GetWorldSpaceNormalizeViewDir(i.w).y),5);
                    color=lerp(color,reflection*_StudyWaterCloud.rgb,min(_StudyPlanarEnabled*(.5+fresnel),.65)*lerp(1,smoothstep(.025,.3,depth),_StudySoftShore));
                }
                half contact=(1-smoothstep(.015,.18,depth))*saturate(noise.y+.25)*.35*(1-_StudySoftShore);
                color=lerp(color,_IntersectColor.rgb*shoreExposure,max(contact,edge*_StudyShoreStrength*.25));
                if(_StudySoftShore>.5) {
                    float distance=max(i.shore,0);
                    float aa=max(fwidth(distance),.012);
                    float breakup=smoothstep(.18,.72,noise.x+.24*sin(i.w.x*2.1+i.w.z*2.7));
                    float wash=ShoreSway(i.w,distance).y;
                    float waterline=max(distance+wash,0);
                    float contactFoam=(1-smoothstep(.035-aa,.075+aa,waterline))*breakup;
                    float waveTime=ShoreTime();
                    float spacing=max(_StudyShoreWaveSpacing,.1);
                    float warp=(noise.x-.5)*.045+sin(i.w.z*2+i.w.x)*.025;
                    // Increasing time moves each constant-phase crest to smaller shore distance.
                    float travel=distance+warp+waveTime*max(_StudyShoreWaveSpeed,0);
                    float crestDistance=abs(frac(travel/spacing+.5)-.5)*spacing;
                    float width=max(_StudyShoreWaveWidth,.015);
                    float crest=1-smoothstep(max(0,width*.35-aa),width+aa,crestDistance);
                    float reach=max(_StudyShoreWaveRange,spacing);
                    float fade=smoothstep(.025,.1,distance)*(1-smoothstep(reach*.35,reach,distance));
                    float lace=crest*fade*lerp(.6,1,breakup)*_StudyShoreWaveStrength;
                    if(_StudyDebugView>4.5) return float4(i.w.y,distance,waterline,1);
                    if(_StudyDebugView>3.5) return half4(lace,distance,crest,1);
                    half3 foam=_IntersectColor.rgb*shoreExposure*lerp(half3(.7,.85,.92),half3(1,1,1),light.shadowAttenuation);
                    color=lerp(color,foam,saturate((contactFoam*.4+lace)*_StudyShoreStrength));
                    float coverage=smoothstep(0,max(.12,aa*2),waterline)*smoothstep(.005,.055,depth);
                    color=lerp(SampleSceneColor(screen),color,coverage);
                }
                return half4(max(color,0),1);
            }
            ENDHLSL
        }
    }
}
