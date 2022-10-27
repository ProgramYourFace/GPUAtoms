using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unity.Mathematics;

[System.Serializable]
public struct GPUCacheManagerSettings {    
    public int m_nodeTileCount;
    public int m_nodeTileWidth;
    public int m_brickPoolWidth;
    public int m_brickWidth;
    public int m_maxRequests;
    public ComputeShader m_allocationUtils;
    public ComputeShader m_model;
}

public class GPUCacheManager {
    public GPUCacheManager(GPUCacheManagerSettings settings) {
        m_settings = settings;
        int brickPoolWidth = settings.m_brickPoolWidth;
        int brickWidth = settings.m_brickWidth;
        int nodeTileCount = settings.m_nodeTileCount;
        int nodeTileWidth = settings.m_nodeTileWidth;
        int maxRequests = settings.m_maxRequests;
        
        m_nodePointerPool = GPUCacheUtils.CreateNodePoolBuffer<uint>(nodeTileWidth, nodeTileCount);
        m_nodeContentPool = GPUCacheUtils.CreateNodePoolBuffer<uint>(nodeTileWidth, nodeTileCount);
        m_nodeParentPool = GPUCacheUtils.CreatePtrBuffer(nodeTileCount);
        m_nodeUsageResources = new GPUCacheUsageResources(nodeTileCount);
        
        uint brickCount = (uint)(brickPoolWidth * brickPoolWidth * brickPoolWidth);
        m_brickColorPool = GPUCacheUtils.CreateBrickPoolLayer(brickPoolWidth, brickWidth, GraphicsFormat.R8G8B8A8_UNorm, FilterMode.Bilinear);
        m_brickParentPool = GPUCacheUtils.CreatePtrBuffer((int)brickCount);
        m_brickUsageResources = new GPUCacheUsageResources((int)brickCount);
        
        m_requestResources = new GPUCacheRequestResources(nodeTileWidth, nodeTileCount, maxRequests);
        m_framePredicate = GPUCacheUtils.CreateSingleIntBuffer(0);
        m_frame = new uint[1];
    }

    private GPUCacheManagerSettings m_settings;
    //Cache Resources
    public RenderTexture m_brickColorPool;
    public GraphicsBuffer m_nodePointerPool;
    public GraphicsBuffer m_nodeContentPool;

    public GraphicsBuffer m_nodeParentPool;
    public GraphicsBuffer m_brickParentPool;
    
    public GPUCacheUsageResources m_brickUsageResources;
    public GPUCacheUsageResources m_nodeUsageResources;
    public GPUCacheRequestResources m_requestResources;
    
    private GraphicsBuffer m_framePredicate;
    private uint[] m_frame = new uint[1];
    public uint framePredicate { get => m_frame[0]; }

    public void Release() {
        m_brickColorPool.Release();
        m_nodePointerPool.Release();
        m_nodeContentPool.Release();

        m_nodeParentPool.Release();
        m_brickParentPool.Release();

        m_brickUsageResources.Release();
        m_nodeUsageResources.Release();
        m_framePredicate.Release();

        m_requestResources.Release();
    }

    public void WriteCommands(CommandBuffer cmd) {
        m_nodeUsageResources.LRUSort(cmd, m_framePredicate);
        m_brickUsageResources.LRUSort(cmd, m_framePredicate);

        int allocatorK;
        m_requestResources.RequestsToList(cmd, 1, false, 1);
        ComputeShader allocUtils = m_settings.m_allocationUtils;
        allocatorK = allocUtils.FindKernel("AllocateNodes");
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "requestPtrs", m_requestResources.list);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "LRU", m_nodeUsageResources.LRU);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "nodePtrs", m_nodePointerPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "brickPtrs", m_nodeContentPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "localization", m_requestResources.localization);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "parents", m_nodeParentPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "requestFlags", m_requestResources.requests);

        cmd.DispatchCompute(allocUtils, allocatorK, m_requestResources.args, 0);

        m_requestResources.RequestsToList(cmd, 2, false, 1);
        allocatorK = allocUtils.FindKernel("AllocateBricks");
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "requestPtrs", m_requestResources.list);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "LRU", m_brickUsageResources.LRU);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "brickPtrs", m_nodeContentPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "parents", m_brickParentPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "requestFlags", m_requestResources.requests);
        
        cmd.DispatchCompute(allocUtils, allocatorK, m_requestResources.args, 0);

        ComputeShader model = m_settings.m_model;
        int modelK = model.FindKernel("Model");
        cmd.SetComputeIntParam(model, "_BrickPoolWidth", m_settings.m_brickPoolWidth);
        cmd.SetComputeBufferParam(model, modelK, "requestPtrs", m_requestResources.list);
        cmd.SetComputeBufferParam(model, modelK, "brickPtrs", m_nodeContentPool);
        cmd.SetComputeBufferParam(model, modelK, "localization", m_requestResources.localization);
        cmd.SetComputeTextureParam(model, modelK, "bricks", m_brickColorPool);
        cmd.DispatchCompute(model, modelK, m_requestResources.args, 0);

        allocatorK = allocUtils.FindKernel("SimplifyBricks");
        cmd.SetComputeIntParam(allocUtils, "_BrickPoolWidth", m_settings.m_brickPoolWidth);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "requestPtrs", m_requestResources.list);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "nodePtrs", m_nodePointerPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "brickPtrs", m_nodeContentPool);
        cmd.SetComputeBufferParam(allocUtils, allocatorK, "parents", m_brickParentPool);
        cmd.SetComputeTextureParam(allocUtils, allocatorK, "bricks", m_brickColorPool);
        
        cmd.DispatchCompute(allocUtils, allocatorK, m_requestResources.args, 0);
    }

    public uint UpdateFramePredicate() {
        uint newFrame = m_frame[0] + 1;
        m_frame[0] = newFrame;
        m_framePredicate.SetData(m_frame);
        return m_frame[0];
    }
}