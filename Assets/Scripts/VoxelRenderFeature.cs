using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections.LowLevel.Unsafe;


public class VoxelRenderFeature : ScriptableRendererFeature
{

    public bool m_test = false;
    public uint[] m_usage = new uint[4];
    public int m_predicate = 0;
    public uint[] m_counts = new uint[4];
    public uint[] m_LRU = new uint[4];

    public ComputeShader m_utilsShaders;
    public ComputeShader m_countValidShader;
    public ComputeShader m_compactShader;

    private VoxelScene m_scene;

    public override void Create()
    {
        if(!( m_utilsShaders && m_countValidShader && m_compactShader)) {
            Debug.LogWarning("Voxel Render Features does not have necessary compute shaders");
            return;
        }
        
        GPUCacheUtils.InitializeUtils(m_utilsShaders, m_countValidShader, m_compactShader);
        if(Application.isPlaying) {
            if(!m_scene) {
                m_scene = GameObject.FindObjectOfType<VoxelScene>();
                m_scene?.Create(); 
            }
        } else {
            m_scene = null;
        }

        try {
            if(m_test) {
                m_test = false;
                Test();
            }
        } catch {}
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if(m_scene?.renderPass != null) renderer.EnqueuePass(m_scene.renderPass);
    }

    void Test() {
        CommandBuffer cmd = new CommandBuffer();
        int size = m_usage.Length;
        GraphicsBuffer usageBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, 4);
        usageBuffer.SetData(m_usage);
        GraphicsBuffer LRUBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, 4);
        m_LRU = new uint[size];
        LRUBuffer.SetData(m_LRU);
        GraphicsBuffer countsBuffer = GPUCacheUtils.CreateCountsBuffer(size);

        cmd.CountValid(usageBuffer, countsBuffer, m_predicate, false);
        cmd.PrefixSum(countsBuffer);
        cmd.Compact(usageBuffer, countsBuffer, m_predicate, LRUBuffer, false, false, CompactOutputSource.STREAM);
        
        cmd.CountValid(usageBuffer, countsBuffer, m_predicate, true);
        cmd.PrefixSum(countsBuffer);
        cmd.Compact(usageBuffer, countsBuffer, m_predicate, LRUBuffer, true, true, CompactOutputSource.STREAM);

        Graphics.ExecuteCommandBuffer(cmd);
        m_LRU = new uint[LRUBuffer.count];
        LRUBuffer.GetData(m_LRU);
        m_counts = new uint[countsBuffer.count];
        countsBuffer.GetData(m_counts);

        cmd.Release();
        countsBuffer.Release();
        LRUBuffer.Release();
        usageBuffer.Release();
    }
}
