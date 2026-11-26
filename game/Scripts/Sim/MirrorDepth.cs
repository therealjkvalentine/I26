using System;
using Godot;

namespace I76.Game.Sim;

/// <summary>
/// Mirror Rate's Smooth values with the move as well as the turn (MirrorWarp, I76_MIRROR_WARP=depth): a compositor effect on a mirror
/// camera that copies its depth, as metres ahead of the camera, into a float texture of its own after the scene is drawn, which the
/// glass's shader reads (<see cref="Texture"/>) to re-project each point for where the camera has moved since. One per camera; a
/// kernel over the render's pixels (the mirrors' 1024x256 and 512x256: well under 0.05 ms). The sky reads a million metres.
/// </summary>
public sealed partial class MirrorDepth : CompositorEffect
{
    public MirrorDepth()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedDepth = true;
    }

    /// <summary>The depth in metres, for a material (its RID set once the kernel has made it, on the main thread by <see cref="Sync"/>).</summary>
    public Texture2Drd Texture { get; } = new();

    private bool _failed;
    private Rid _out, _set, _setDepth;
    private Vector2I _size;
    private static Rid _shader, _pipeline, _nearest;
    private static bool _setupFailed;

    /// <summary>The texture's RID handed to the material's texture when the kernel made a new one (main thread, each frame).</summary>
    public void Sync()
    {
        if (_out.IsValid && Texture.TextureRdRid != _out) Texture.TextureRdRid = _out;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (_failed || _setupFailed || effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent) return;
        try { Render(renderData); }
        catch (Exception e) { _failed = true; GD.PrintErr($"mirror depth: {e}"); }
    }

    private void Render(RenderData renderData)
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers || renderData.GetRenderSceneData() is not RenderSceneDataRD scene) return;
        if (buffers.GetViewCount() != 1) return;
        var size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0) return;
        if (!_pipeline.IsValid && !Setup(rd)) return;
        var depth = buffers.GetDepthLayer(0, false);
        if (!depth.IsValid) return;
        if (!_out.IsValid || _size != size)
        {
            if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
            _set = default;
            var old = _out;
            _out = rd.TextureCreate(new RDTextureFormat
            {
                Width = (uint)size.X, Height = (uint)size.Y, Format = RenderingDevice.DataFormat.R32Sfloat,
                UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
            }, new RDTextureView());
            _size = size;
            // the old texture is the material's until Sync hands it the new one: freed with this effect, not here
            if (old.IsValid) _stale = old;
        }
        if (!_set.IsValid || !rd.UniformSetIsValid(_set) || depth != _setDepth)
        {
            if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
            _setDepth = depth;
            var dep = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 }; dep.AddId(_nearest); dep.AddId(depth);
            var img = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 }; img.AddId(_out);
            _set = rd.UniformSetCreate([dep, img], _shader, 0);
            if (!_set.IsValid) { _failed = true; GD.PrintErr("mirror depth: no uniform set; off"); return; }
        }
        var inv = scene.GetViewProjection(0).Inverse();
        Span<float> pc = stackalloc float[20];
        pc[0] = inv.X.X; pc[1] = inv.X.Y; pc[2] = inv.X.Z; pc[3] = inv.X.W;
        pc[4] = inv.Y.X; pc[5] = inv.Y.Y; pc[6] = inv.Y.Z; pc[7] = inv.Y.W;
        pc[8] = inv.Z.X; pc[9] = inv.Z.Y; pc[10] = inv.Z.Z; pc[11] = inv.Z.W;
        pc[12] = inv.W.X; pc[13] = inv.W.Y; pc[14] = inv.W.Z; pc[15] = inv.W.W;
        pc[16] = size.X; pc[17] = size.Y; pc[18] = SmokeDepth.NearerIsLarger(scene.GetViewProjection(0)) ? 1f : 0f; pc[19] = 0;
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(pc);
        long cl = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(cl, _pipeline);
        rd.ComputeListBindUniformSet(cl, _set, 0);
        rd.ComputeListSetPushConstant(cl, bytes, (uint)bytes.Length);
        rd.ComputeListDispatch(cl, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        rd.ComputeListEnd();
    }

    private Rid _stale;

    /// <summary>Frees the textures (the mirror going away).</summary>
    public void Free()
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return;
        Texture.TextureRdRid = default;
        if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
        foreach (var r in new[] { _out, _stale }) if (r.IsValid) rd.FreeRid(r);
        _set = _out = _stale = default;
    }

    private static bool Setup(RenderingDevice rd)
    {
        var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = Kernel });
        if (spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } err) { _setupFailed = true; GD.PrintErr($"mirror depth: the kernel did not compile: {err}"); return false; }
        _shader = rd.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) { _setupFailed = true; return false; }
        _pipeline = rd.ComputePipelineCreate(_shader);
        _nearest = rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Nearest, MagFilter = RenderingDevice.SamplerFilter.Nearest, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        return _pipeline.IsValid;
    }

    private const string Kernel = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(set = 0, binding = 0) uniform sampler2D depth_tex;
        layout(r32f, set = 0, binding = 1) uniform restrict writeonly image2D out_img;
        layout(push_constant, std430) uniform Params { mat4 inv_proj; vec2 size; float reversed; float pad; } p;
        void main() {
            ivec2 g = ivec2(gl_GlobalInvocationID.xy);
            if (g.x >= int(p.size.x) || g.y >= int(p.size.y)) return;
            vec2 uv = (vec2(g) + 0.5) / p.size;
            float d = texelFetch(depth_tex, g, 0).r;
            bool sky = p.reversed > 0.5 ? d <= 0.0 : d >= 1.0;
            vec4 v = p.inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
            float z = -v.z / v.w;
            imageStore(out_img, g, vec4(sky ? 1.0e6 : z, 0.0, 0.0, 0.0));
        }
        """;
}
