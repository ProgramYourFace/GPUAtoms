using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

[System.Serializable]
public class VoxelRenderPass : ScriptableRenderPass {
    public bool render;
        private CommandBuffer m_commandBuffer;
        public CommandBuffer commandBuffer { get => m_commandBuffer; }

        public VoxelRenderPass(CommandBuffer cmd) {
            m_commandBuffer = cmd;
            // cache.WriteCommands(m_commandBuffer);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            if(render && m_commandBuffer != null)context.ExecuteCommandBuffer(m_commandBuffer); 
        }

        public void Release() {
            // m_commandBuffer.Release();
        }
    }

public class VoxelScene : MonoBehaviour
{
    public bool m_step = false;
    public GPUCacheManagerSettings m_cacheSettings = new GPUCacheManagerSettings(){
        m_nodeTileCount = 262144,
        m_nodeTileWidth = 2,
        m_brickPoolWidth = 48 * 48 * 48,
        m_brickWidth = 8,
        m_maxRequests = 128,
    };

    public Mesh m_hull;
    public Material m_material;
    public RenderTexture rt;

    //public bool poll;
    //[Header("Requests")]
    //public uint[] requests;
    //public uint[] counts;
    //public uint[] list;
    //public uint[] args;
    //[Header("Nodes")]
    //public uint[] nodePtrs;
    //public uint[] nodeContent;
    //public uint[] nodeLRU;
    //[Header("Bricks")]
    //public float4[] boxes;
    //public uint[] brick;
    // [Header("Usage")]
    // public uint 

    public VoxelRenderPass m_renderPass;
    private GPUCacheManager m_cache;
    public VoxelRenderPass renderPass {get => m_renderPass;}

    private CommandBuffer m_renderCmd;
    private CommandBuffer m_cacheCmd;

    public void Create() {
        m_cache = new GPUCacheManager(m_cacheSettings);
        m_cacheCmd = new CommandBuffer();
        m_cache.WriteCommands(m_cacheCmd);

        rt = m_cache.m_brickColorPool;
        
        m_renderCmd = new CommandBuffer();
        m_renderCmd.name = "RenderVoxelObject";
        m_renderCmd.ClearRandomWriteTargets();
        m_renderCmd.SetRandomWriteTarget(1, m_cache.m_nodeUsageResources.usage);
        m_renderCmd.SetRandomWriteTarget(2, m_cache.m_brickUsageResources.usage);
        m_renderCmd.SetRandomWriteTarget(3, m_cache.m_requestResources.requests);
        m_renderCmd.DrawMesh(m_hull, transform.localToWorldMatrix, m_material);
        m_renderCmd.ClearRandomWriteTargets();
        // m_cache.WriteCommands(m_renderCmd);
        m_renderPass = new VoxelRenderPass(m_renderCmd);
        m_renderPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        
        m_material.SetBuffer("_NodePtrs", m_cache.m_nodePointerPool);
        m_material.SetBuffer("_NodeContent", m_cache.m_nodeContentPool);
        m_material.SetBuffer("_NodeUsage", m_cache.m_nodeUsageResources.usage);
        m_material.SetTexture("_Bricks", m_cache.m_brickColorPool);
        m_material.SetBuffer("_BrickUsage", m_cache.m_brickUsageResources.usage);
        m_material.SetBuffer("_Requests", m_cache.m_requestResources.requests);
        m_material.SetInteger("_Frame", (int)m_cache.framePredicate);
        m_material.SetInteger("_BrickWidth", m_cacheSettings.m_brickWidth);
        m_material.SetInteger("_BrickPoolWidth", m_cacheSettings.m_brickPoolWidth);
        // m_material.SetInteger("_BrickSteps", m_cacheSettings.m_brickWidth + (m_cacheSettings.m_brickWidth - 1) * 2);
        m_material.SetFloat("_BrickTexelSize", 1.0f / (m_cacheSettings.m_brickPoolWidth * m_cacheSettings.m_brickWidth));

        // Camera.main.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, m_renderCmd);
    }

    void LateUpdate() {
        if(m_cache != null && m_step) {
            // m_step = false;

            // requests = new uint[m_cache.m_requestResources.requests.count];
            // m_cache.m_requestResources.requests.GetData(requests);
            // Graphics.ExecuteCommandBuffer(m_cacheCmd);
            // counts = new uint[m_cache.m_requestResources.counts.count];
            // list = new uint[m_cache.m_requestResources.list.count];
            // args = new uint[m_cache.m_requestResources.args.count];
            // m_cache.m_requestResources.counts.GetData(counts);
            // m_cache.m_requestResources.list.GetData(list);
            // m_cache.m_requestResources.args.GetData(args);

            // nodePtrs = new uint[m_cache.m_nodePointerPool.count];
            // nodeContent = new uint[m_cache.m_nodeContentPool.count];
            // nodeLRU = new uint[m_cache.m_nodeUsageResources.LRU.count];
            // m_cache.m_nodePointerPool.GetData(nodePtrs);
            // m_cache.m_nodeContentPool.GetData(nodeContent);
            // m_cache.m_nodeUsageResources.LRU.GetData(nodeLRU);

            // boxes = new float4[m_cache.m_requestResources.localization.count];
            // m_cache.m_requestResources.localization.GetData(boxes);
            Graphics.ExecuteCommandBuffer(m_cacheCmd);
            uint frame = m_cache.UpdateFramePredicate();
            m_material.SetInteger("_Frame", (int)frame);
        }

        //if(poll)
        //{
        //    poll = false;
        //
        //    requests = new uint[m_cache.m_requestResources.requests.count];
        //    counts = new uint[m_cache.m_requestResources.counts.count];
        //    list = new uint[m_cache.m_requestResources.list.count];
        //    args = new uint[m_cache.m_requestResources.args.count];
        //    m_cache.m_requestResources.requests.GetData(requests);
        //    m_cache.m_requestResources.counts.GetData(counts);
        //    m_cache.m_requestResources.list.GetData(list);
        //    m_cache.m_requestResources.args.GetData(args);
        //
        //    nodePtrs = new uint[m_cache.m_nodePointerPool.count];
        //    nodeContent = new uint[m_cache.m_nodeContentPool.count];
        //    nodeLRU = new uint[m_cache.m_nodeUsageResources.LRU.count];
        //    m_cache.m_nodePointerPool.GetData(nodePtrs);
        //    m_cache.m_nodeContentPool.GetData(nodeContent);
        //    m_cache.m_nodeUsageResources.LRU.GetData(nodeLRU);
        //}
    }

    void OnDestroy() {
        m_cacheCmd?.Release();
        m_renderCmd?.Release();
        m_renderPass?.Release();
        m_cache?.Release();
    }
}
