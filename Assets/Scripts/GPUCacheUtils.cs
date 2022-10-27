using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using UnityEngine.Rendering;

public struct GPUCacheUsageResources {
    public GraphicsBuffer usage;
    public GraphicsBuffer mask;
    public GraphicsBuffer counts;
    public GraphicsBuffer LRUOutput;
    public GraphicsBuffer LRU;

    public GPUCacheUsageResources(int size) {
        
        usage = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, 4);
        mask = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, 4);
        LRU = new GraphicsBuffer(GraphicsBuffer.Target.CopyDestination | GraphicsBuffer.Target.Structured, size, 4);
        LRUOutput = new GraphicsBuffer(GraphicsBuffer.Target.CopySource | GraphicsBuffer.Target.Structured, size, 4);

        NativeArray<uint> dataInit = new NativeArray<uint>(size, Allocator.Temp, NativeArrayOptions.ClearMemory);
        usage.SetData(dataInit);
        mask.SetData(dataInit);
        for(uint i = 0;i < size;i++) {
            dataInit[(int)i] = i;
        }
        LRU.SetData(dataInit);
        LRUOutput.SetData(dataInit);
        dataInit.Dispose();

        counts = GPUCacheUtils.CreateCountsBuffer(size);
    }

    public void LRUSort(CommandBuffer cmd, GraphicsBuffer framePredicate) {
        ComputeShader utils = GPUCacheUtils.UtilsShader;
        int maskK = utils.FindKernel("MaskLRU");
        cmd.SetComputeBufferParam(utils, maskK, "framePredicate", framePredicate);
        cmd.SetComputeBufferParam(utils, maskK, "usage", usage);
        cmd.SetComputeBufferParam(utils, maskK, "LRU", LRU);
        cmd.SetComputeBufferParam(utils, maskK, "LRUMask", mask);
        cmd.DispatchCompute(utils, maskK, usage.count);

        //Write 0 to beginning
        cmd.CountValid(mask, counts, 0, false);
        cmd.PrefixSum(counts);
        cmd.Compact(mask, counts, 0, LRUOutput, false, false, 
        CompactOutputSource.OTHER, LRU);
        //Write other to end
        cmd.CountValid(mask, counts, 0, true);
        cmd.PrefixSum(counts);
        cmd.Compact(mask, counts, 0, LRUOutput, true, true, 
        CompactOutputSource.OTHER, LRU);

        cmd.CopyBuffer(LRUOutput, LRU);
    }

    public void Release() {
        usage.Release();
        mask.Release();
        counts.Release();
        LRUOutput.Release();
        LRU.Release();
    }
}
public struct GPUCacheRequestResources {
    public GraphicsBuffer requests;
    public GraphicsBuffer counts;
    public GraphicsBuffer list;
    // public GraphicsBuffer subCounts;
    // public GraphicsBuffer subList;
    public GraphicsBuffer localization;
    public GraphicsBuffer args;

    public GPUCacheRequestResources(int nodeTileWidth, int nodeTileCount, int maxRequests) {
        int nodeCount = nodeTileWidth * nodeTileWidth * nodeTileWidth * nodeTileCount;
        requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nodeCount, 4);
        NativeArray<uint> dataInit = new NativeArray<uint>(nodeCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
        requests.SetData(dataInit);
        dataInit.Dispose();

        list = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxRequests, 4);
        // resources.subList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxRequests, 4);
        dataInit = new NativeArray<uint>(maxRequests, Allocator.Temp, NativeArrayOptions.ClearMemory);
        list.SetData(dataInit);
        // resources.subList.SetData(dataInit);
        dataInit.Dispose();

        localization = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nodeTileCount, 4 * 4);
        NativeArray<float4> longData = new NativeArray<float4>(nodeTileCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
        longData[0] = new float4(0f,0f,0f,32f);
        localization.SetData(longData);
        longData.Dispose();

        counts = GPUCacheUtils.CreateCountsBuffer(nodeCount);
        // resources.subCounts = CreateCountsBuffer(maxRequests);
        args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, 4);
    }

    public void Release() {
        requests.Release();
        counts.Release();
        list.Release();
        localization.Release();
        // subList.Release();
        // subCounts.Release();
        args.Release();
    }

    public void RequestsToList(CommandBuffer cmd, int predicate, bool notEqual, int threadGroupsX) {
        cmd.StreamCompaction(requests, counts, predicate, list, notEqual, false, CompactOutputSource.INDEX);
        cmd.WriteArgsFromCounts(counts, args, 0, threadGroupsX, requests.count);
    }
    
    // public void ListToSubList(CommandBuffer cmd, GraphicsBuffer predicate, bool notEqual, int threadGroupsX) {
    //     cmd.StreamCompaction(list, subCounts, predicate, subList, notEqual, false, CompactOutputSource.STREAM);
    //     cmd.WriteArgsFromCounts(subCounts, args, 0, threadGroupsX, list.count);
    // }
}

public enum CompactOutputSource {
    OTHER = 0,
    STREAM = 1,
    INDEX = 2
}

public static class GPUCacheUtils {
    private static ComputeShader SIMD_UTILS;
    private static ComputeShader SIMD_COUNT_VALID;
    private static ComputeShader SIMD_COMPACT;
    private static int GPU_WAVE_WIDTH = 32;

    public static ComputeShader UtilsShader { get => SIMD_UTILS; }
    public static int GPUWaveWidth { get => GPU_WAVE_WIDTH; }

    public const uint NULL_PTR = uint.MaxValue;
    public const uint MASK_TEN = 0x3FF;

    public static void InitializeUtils(ComputeShader SIMDUtils, ComputeShader SIMDCountValid, ComputeShader SIMDCompact) {
        SIMD_UTILS = SIMDUtils;
        SIMD_COUNT_VALID = SIMDCountValid;
        SIMD_COMPACT = SIMDCompact;

        int GetSimdWidthK = SIMDUtils.FindKernel("GetSimdWidth");
        GraphicsBuffer simdWidthBuff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
        SIMDUtils.SetBuffer(GetSimdWidthK, "simdWidth", simdWidthBuff);
        SIMDUtils.Dispatch(GetSimdWidthK, 1, 1, 1);
        int[] simdWidth = new int[1];
        simdWidthBuff.GetData(simdWidth);
        GPU_WAVE_WIDTH = simdWidth[0];
        simdWidthBuff.Release();
    }

    public static GraphicsBuffer CreatePtrBuffer(int count) {
        NativeArray<uint> data = new NativeArray<uint>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        for(int i = 0;i < count;i++) data[i] = NULL_PTR;
        GraphicsBuffer buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
        buffer.SetData(data);
        data.Dispose();
        return buffer;
    }

    public static GraphicsBuffer CreateNodePoolBuffer<T>(int nodeTileWidth, int nodeTileCount) where T : struct {
        int count =  nodeTileWidth * nodeTileWidth * nodeTileWidth * nodeTileCount;
        return CreatePtrBuffer(count);
    }

    public static RenderTexture CreateBrickPoolLayer(int brickPoolWidth, int brickWidth, 
        GraphicsFormat format = GraphicsFormat.B8G8R8A8_UNorm, FilterMode filter = FilterMode.Point) {
        int width = brickPoolWidth * brickWidth;
        RenderTexture layer = new RenderTexture(width, width, 0, format);
        layer.dimension = TextureDimension.Tex3D;
        layer.useMipMap = false;
        layer.volumeDepth = width;
        layer.enableRandomWrite = true;
        layer.filterMode = filter;
        layer.Create();

        return layer;
    }
    
    public static GraphicsBuffer CreateCountsBuffer(int forStreamSize) {
        int countsLog = Mathf.Max(Mathf.CeilToInt(Mathf.Log((float)forStreamSize / (float)GPU_WAVE_WIDTH, GPU_WAVE_WIDTH)), 1);
        int countsSize = Mathf.CeilToInt(Mathf.Pow(GPU_WAVE_WIDTH, (float)countsLog));
        GraphicsBuffer counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, countsSize + 1, 4);
        NativeArray<uint> dataInit = new NativeArray<uint>(counts.count, Allocator.Temp, NativeArrayOptions.ClearMemory);
        counts.SetData(dataInit);
        dataInit.Dispose();
        return counts;
    }
    
    public static void SelectKeyword(ComputeShader shader, int value, params string[] keywords) {
        for(int i = 0;i < keywords.Length;i++) {
            shader.SetKeyword(new LocalKeyword(shader, keywords[i]), value == i);
        }
    }
    public static void SelectKeyword(this CommandBuffer cmd, ComputeShader shader, int value, params string[] keywords) {
        SelectKeyword(shader, value, keywords);
        for(int i = 0;i < keywords.Length;i++) {
            cmd.SetKeyword(shader, new LocalKeyword(shader, keywords[i]), value == i);
        }
    }

    public static void ToggleKeyword(ComputeShader shader, bool value, string off, string on) {
        SelectKeyword(shader, value ? 1 : 0, off, on);
    }
    public static void ToggleKeyword(this CommandBuffer cmd, ComputeShader shader, bool value, string off, string on) {
        ToggleKeyword(shader, value, off, on);
        cmd.SelectKeyword(shader, value ? 1 : 0, off, on);
    }

    public static void CountValid(this CommandBuffer cmd, GraphicsBuffer stream, GraphicsBuffer counts, int predicate, bool notEqual) {
        cmd.ToggleKeyword(SIMD_COUNT_VALID, notEqual, "PRED_EQUAL", "PRED_NOT_EQUAL");
        int countValidK = SIMD_COUNT_VALID.FindKernel("CountValid");
        cmd.SetComputeIntParam(SIMD_COUNT_VALID, "predicate", predicate);
        cmd.SetComputeBufferParam(SIMD_COUNT_VALID, countValidK, "stream", stream);
        cmd.SetComputeBufferParam(SIMD_COUNT_VALID, countValidK, "counts", counts);
        cmd.SetComputeIntParam(SIMD_COUNT_VALID, "streamSize", stream.count);
        cmd.DispatchCompute(SIMD_COUNT_VALID, countValidK, stream.count);
    }

    public static void DispatchCompute(this CommandBuffer cmd, ComputeShader shader, int kernel, int minX) {
        shader.GetKernelThreadGroupSizes(kernel, out uint size, out _, out _);
        cmd.DispatchCompute(shader, kernel, Mathf.CeilToInt(minX / (float)size), 1, 1);
    }

    public static void PrefixSum(this CommandBuffer cmd, GraphicsBuffer counts) {
        int countsSize = counts.count - 1;
        int countsLog = Mathf.CeilToInt(Mathf.Log(countsSize, 32f));
        int stride = 1;
        int parentStride;
        int prefixSumUpK = SIMD_UTILS.FindKernel("PrefixSumUp");
        SIMD_UTILS.GetKernelThreadGroupSizes(prefixSumUpK, out uint prefixSumUpGS, out _, out _);
        cmd.SetComputeBufferParam(SIMD_UTILS, prefixSumUpK, "counts", counts);
            
        for(int i = 1;i <= countsLog;i++) {
            parentStride = Mathf.RoundToInt(Mathf.Pow(GPU_WAVE_WIDTH, i));
            
            cmd.SetComputeIntParam(SIMD_UTILS, "stride", stride);
            cmd.SetComputeIntParam(SIMD_UTILS, "parentStride", parentStride);
            cmd.DispatchCompute(SIMD_UTILS, prefixSumUpK, Mathf.CeilToInt((float)countsSize / (float)stride / (float)prefixSumUpGS), 1, 1);
            
            stride = parentStride;
        }

        int zeroIndexK = SIMD_UTILS.FindKernel("ZeroIndex");
        cmd.SetComputeIntParam(SIMD_UTILS, "index", countsSize - 1);
        cmd.SetComputeBufferParam(SIMD_UTILS, zeroIndexK, "counts", counts);
        cmd.DispatchCompute(SIMD_UTILS, zeroIndexK, 1, 1, 1);

        int prefixSumDownK = SIMD_UTILS.FindKernel("PrefixSumDown");
        SIMD_UTILS.GetKernelThreadGroupSizes(prefixSumDownK, out uint prefixSumDownGS, out _, out _);
        cmd.SetComputeBufferParam(SIMD_UTILS, prefixSumDownK, "counts", counts);
        
        for(int i = countsLog;i > 0;i--) {
            parentStride = Mathf.RoundToInt(Mathf.Pow(GPU_WAVE_WIDTH, i));
            stride = Mathf.RoundToInt(Mathf.Pow(GPU_WAVE_WIDTH, i - 1));
            
            cmd.SetComputeIntParam(SIMD_UTILS, "stride", stride);
            cmd.SetComputeIntParam(SIMD_UTILS, "parentStride", parentStride);
            cmd.DispatchCompute(SIMD_UTILS, prefixSumDownK, Mathf.CeilToInt((float)countsSize / (float)stride / (float)prefixSumDownGS), 1, 1);
            stride = parentStride;
        }
    }

    public static void Compact(this CommandBuffer cmd, GraphicsBuffer stream, GraphicsBuffer counts, int predicate, GraphicsBuffer output, bool notEqual, bool writeEnd, CompactOutputSource outputSource, GraphicsBuffer otherSource = null) {
        cmd.ToggleKeyword(SIMD_COMPACT, notEqual, "PRED_EQUAL", "PRED_NOT_EQUAL");
        cmd.ToggleKeyword(SIMD_COMPACT, writeEnd, "WRITE_START", "WRITE_END");
        cmd.SelectKeyword(SIMD_COMPACT, (int)outputSource, "OUTPUT_OTHER", "OUTPUT_STREAM", "OUTPUT_INDEX");

        int compactK = SIMD_COMPACT.FindKernel("Compact");
        cmd.SetComputeIntParam(SIMD_COMPACT, "predicate", predicate);
        cmd.SetComputeBufferParam(SIMD_COMPACT, compactK, "stream", stream);
        cmd.SetComputeBufferParam(SIMD_COMPACT, compactK, "counts", counts);
        cmd.SetComputeBufferParam(SIMD_COMPACT, compactK, "output", output);
        if(outputSource == CompactOutputSource.OTHER) 
            cmd.SetComputeBufferParam(SIMD_COMPACT, compactK, "other", otherSource);

        cmd.SetComputeIntParam(SIMD_COMPACT, "streamSize", stream.count);
        cmd.SetComputeIntParam(SIMD_COMPACT, "outputSize", output.count);
        if(writeEnd) {
            int validCountIdx = Mathf.Max(Mathf.FloorToInt((stream.count - 1) / GPU_WAVE_WIDTH) + 1, 1);
            cmd.SetComputeIntParam(SIMD_COMPACT, "validCountIdx", validCountIdx);
        }

        SIMD_COMPACT.GetKernelThreadGroupSizes(compactK, out uint compactGS, out _, out _);
        cmd.DispatchCompute(SIMD_COMPACT, compactK, Mathf.CeilToInt(stream.count / (float)compactGS), 1, 1);
    }
    

    public static void WriteArgsFromCounts(this CommandBuffer cmd, GraphicsBuffer counts, GraphicsBuffer args, int argsOffset, int threadGroupsX, int streamSize, int countCap = 0) {
        int kernel = SIMD_UTILS.FindKernel("WriteArgsFromCounts");
        cmd.SetComputeIntParam(SIMD_UTILS, "argsOffset", argsOffset);
        cmd.SetComputeIntParam(SIMD_UTILS, "threadGroupsX", threadGroupsX);
        int validCountIdx = Mathf.Max(Mathf.FloorToInt((streamSize - 1) / GPU_WAVE_WIDTH) + 1, 1);
        cmd.SetComputeIntParam(SIMD_UTILS, "validCountIdx", validCountIdx);
        cmd.SetComputeIntParam(SIMD_UTILS, "countCap", countCap > 0 ? countCap : streamSize);
        cmd.SetComputeBufferParam(SIMD_UTILS, kernel, "counts", counts);
        cmd.SetComputeBufferParam(SIMD_UTILS, kernel, "indirectArgs", args);
        cmd.DispatchCompute(SIMD_UTILS, kernel, 1, 1, 1);
    }

    public static void StreamCompaction(this CommandBuffer cmd, GraphicsBuffer stream, GraphicsBuffer counts, 
    int predicate, GraphicsBuffer output, bool notEqual, bool writeEnd, CompactOutputSource outputSource, GraphicsBuffer otherSource = null) {
        cmd.CountValid(stream, counts, predicate, notEqual);
        cmd.PrefixSum(counts);
        cmd.Compact(stream, counts, predicate, output, notEqual, writeEnd, outputSource, otherSource);
    }

    public static GraphicsBuffer CreateSingleIntBuffer(uint predicate) {
        GraphicsBuffer buff = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
        buff.SetData(new uint[1]{predicate});
        return buff;
    }
    

}
