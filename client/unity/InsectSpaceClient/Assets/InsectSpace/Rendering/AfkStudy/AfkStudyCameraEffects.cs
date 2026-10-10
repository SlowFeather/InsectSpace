#if UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InsectSpace.Rendering
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class AfkStudyCameraEffects : MonoBehaviour
    {
        public Renderer Water;
        public Material FogMaterial;
        public bool PlanarReflection = true;
        public bool VolumetricFog = true;
        [Range(128, 1024)] public int ReflectionSize = 512;
        [Range(0, 1)] public float ReflectionStrength = .38f;
        public float WaterHeight = .005f;
        Camera view, reflectionCamera;
        RenderTexture reflectionTexture;
        MaterialPropertyBlock waterProperties;
        DepthRequest depthRequest;
        FogPass fogPass;
        bool renderingReflection;
        public RenderTexture ReflectionTexture => reflectionTexture;
        public int ReflectionFrames { get; private set; }
        public int FogFrames { get; private set; }

        void OnEnable()
        {
            view = GetComponent<Camera>();
            var data = view.GetUniversalAdditionalCameraData();
            data.requiresDepthTexture = true;
            data.requiresColorTexture = true;
            depthRequest = new DepthRequest();
            fogPass = new FogPass();
            fogPass.Owner = this;
            waterProperties = new MaterialPropertyBlock();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || renderingReflection) return;
            if (Water) {
                if (PlanarReflection) RenderReflection(camera);
                Water.GetPropertyBlock(waterProperties);
                waterProperties.SetFloat("_StudyPlanarEnabled", PlanarReflection && reflectionTexture ? ReflectionStrength : 0);
                if (reflectionTexture) waterProperties.SetTexture("_StudyPlanarReflection", reflectionTexture);
                Water.SetPropertyBlock(waterProperties);
            }
            // This early depth input forces a prepass for opaque character depth rims.
            var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
            renderer.EnqueuePass(depthRequest);
            if (VolumetricFog && FogMaterial) {
                fogPass.Material = FogMaterial;
                renderer.EnqueuePass(fogPass);
            }
        }

        void RenderReflection(Camera source)
        {
            if (!reflectionCamera) {
                var go = new GameObject("LOCAL lake planar reflection") { hideFlags = HideFlags.HideAndDontSave };
                reflectionCamera = go.AddComponent<Camera>();
                reflectionCamera.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.requiresColorTexture = false;
                data.requiresDepthTexture = false;
            }
            int width = Mathf.Max(64, Mathf.RoundToInt(ReflectionSize * source.aspect));
            if (!reflectionTexture || reflectionTexture.width != width || reflectionTexture.height != ReflectionSize) {
                ReleaseTexture();
                reflectionTexture = new RenderTexture(width, ReflectionSize, 24, RenderTextureFormat.ARGBHalf) {
                    name = "LOCAL realtime lake reflection", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                reflectionTexture.Create();
            }
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled = false;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.cameraType = CameraType.Reflection;
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var reflection = Matrix4x4.identity;
            reflection.m11 = -1;
            reflection.m13 = 2 * WaterHeight;
            var position = reflection.MultiplyPoint(source.transform.position);
            var forward = reflection.MultiplyVector(source.transform.forward);
            var up = reflection.MultiplyVector(source.transform.up);
            reflectionCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * reflection;
            var normal = reflectionCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
            var clipPoint = reflectionCamera.worldToCameraMatrix.MultiplyPoint(new Vector3(0, WaterHeight + .02f, 0));
            reflectionCamera.projectionMatrix = source.projectionMatrix;
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(clipPoint, normal)));
            var projection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true);
            Water.GetPropertyBlock(waterProperties);
            waterProperties.SetMatrix("_StudyPlanarVP", projection * reflectionCamera.worldToCameraMatrix);
            Water.SetPropertyBlock(waterProperties);
            bool visible = Water.enabled, inverted = GL.invertCulling;
            try {
                renderingReflection = true;
                Water.enabled = false;
                GL.invertCulling = !inverted;
                RenderPipeline.SubmitRenderRequest(reflectionCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = reflectionTexture });
                ReflectionFrames++;
            } finally {
                GL.invertCulling = inverted;
                Water.enabled = visible;
                renderingReflection = false;
            }
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            if (Water && waterProperties != null) {
                Water.GetPropertyBlock(waterProperties);
                waterProperties.SetFloat("_StudyPlanarEnabled", 0);
                Water.SetPropertyBlock(waterProperties);
            }
            ReleaseTexture();
            if (reflectionCamera) CoreUtils.Destroy(reflectionCamera.gameObject);
        }

        void ReleaseTexture()
        {
            if (!reflectionTexture) return;
            reflectionTexture.Release();
            CoreUtils.Destroy(reflectionTexture);
            reflectionTexture = null;
        }

        sealed class DepthRequest : ScriptableRenderPass
        {
            public DepthRequest()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) { }
        }

        sealed class FogPass : ScriptableRenderPass
        {
            public Material Material;
            public AfkStudyCameraEffects Owner;
            sealed class PassData { public Material Material; }
            public FogPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                Owner.FogFrames++;
                var resources = frameData.Get<UniversalResourceData>();
                using var builder = graph.AddRasterRenderPass<PassData>("LOCAL depth bounded volumetric fog", out var pass);
                pass.Material = Material;
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3));
            }
        }
    }
}
#endif
