#ifndef INSECTSPACE_AFK_ATMOSPHERE
#define INSECTSPACE_AFK_ATMOSPHERE
TEXTURE2D(_StudyCloudTexture); SAMPLER(sampler_StudyCloudTexture);
float4 _StudyCloudSettings; // scale, speed x/z, strength
float _StudyCloudFalloff;
float2 _StudyCloudOffset;
half StudyCloudShadow(float3 world)
{
    float2 uv=world.xz*_StudyCloudSettings.x+_Time.y*_StudyCloudSettings.yz+_StudyCloudOffset;
    half cloud=SAMPLE_TEXTURE2D_LOD(_StudyCloudTexture,sampler_StudyCloudTexture,uv,0).r;
    return 1-smoothstep(_StudyCloudFalloff,1,cloud)*_StudyCloudSettings.w;
}
#endif
