using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Profiling;

public class StreamCompaction : MonoBehaviour
{
    public bool process;
    public int size = 128;
    public ComputeShader shader;
    public uint[] data;
    public uint[] counts;
    public uint[] output;
    
    void OnStart() {

    }

    // Start is called before the first frame update
    void Update()
    {
        if(!process) return;
        process = false;
        
        int waveSize = 32;

        int countsLog = Mathf.CeilToInt(Mathf.Log((float)size / (float)waveSize, waveSize));
        int countsSize = Mathf.CeilToInt(Mathf.Pow(waveSize, (float)countsLog));
        int[] countsSizes = new int[countsLog];
        for(int i = 0;i < countsSizes.Length;i++) {
            countsSizes[i] = Mathf.CeilToInt(Mathf.Pow(waveSize, (float)i));
        }
        Debug.Log("log: " + countsLog + ", size: " + countsSize);
        ComputeBuffer streamBuff = new ComputeBuffer(size, sizeof(uint));
        ComputeBuffer countsBuff = new ComputeBuffer(countsSize, sizeof(uint));
        ComputeBuffer outputBuff = new ComputeBuffer(size, sizeof(uint));
        
        data = new uint[streamBuff.count];
        for(uint i = 0;i < data.Length;i++) {
            data[i] = i % 2 == 0 ? 0 : i;
        }
        streamBuff.SetData(data);
        output = new uint[outputBuff.count];
        counts = new uint[countsBuff.count];
        countsBuff.SetData(counts);
        outputBuff.SetData(output);

        uint CountValidGS;
        int CountValidK = shader.FindKernel("CountValid");
        shader.SetBuffer(CountValidK, "stream", streamBuff);
        shader.SetBuffer(CountValidK, "counts", countsBuff);
        shader.GetKernelThreadGroupSizes(CountValidK, out CountValidGS, out _, out _);
        int CountValidTG = Mathf.CeilToInt((float)streamBuff.count / (float)CountValidGS);

        uint PrefixSumUpGS;
        int PrefixSumUpK = shader.FindKernel("PrefixSumUp");
        shader.SetBuffer(PrefixSumUpK, "counts", countsBuff);
        shader.GetKernelThreadGroupSizes(PrefixSumUpK, out PrefixSumUpGS, out _, out _);

        int ZeroIndexK = shader.FindKernel("ZeroIndex");
        shader.SetInt("index", countsBuff.count - 1);
        shader.SetBuffer(ZeroIndexK, "counts", countsBuff);
        
        uint PrefixSumDownGS;
        int PrefixSumDownK = shader.FindKernel("PrefixSumDown");
        shader.SetBuffer(PrefixSumDownK, "counts", countsBuff);
        shader.GetKernelThreadGroupSizes(PrefixSumDownK, out PrefixSumDownGS, out _, out _);

        uint CompactGS;
        int CompactK = shader.FindKernel("Compact");
        shader.SetBuffer(CompactK, "stream", streamBuff);
        shader.SetBuffer(CompactK, "counts", countsBuff);
        shader.SetBuffer(CompactK, "output", outputBuff);
        shader.GetKernelThreadGroupSizes(CompactK, out CompactGS, out _, out _);
        int CompactTG = Mathf.CeilToInt((float)streamBuff.count / (float)CompactGS);

        Profiler.BeginSample("StreamReduction");
        shader.Dispatch(CountValidK, CountValidTG, 1, 1);

        int stride = 1;
        int parentStride;
        for(int i = 1;i < countsLog;i++) {
            parentStride = Mathf.RoundToInt(Mathf.Pow(waveSize, i));
            
            shader.SetInt("stride", stride);
            shader.SetInt("parentStride", parentStride);
            shader.Dispatch(PrefixSumUpK, Mathf.CeilToInt((float)countsBuff.count / (float)stride / (float)PrefixSumUpGS), 1, 1);
            
            stride = parentStride;
        }

        shader.Dispatch(ZeroIndexK, 1, 1, 1);

        for(int i = countsLog;i > 0;i--) {
            parentStride = Mathf.RoundToInt(Mathf.Pow(waveSize, i));
            stride = Mathf.RoundToInt(Mathf.Pow(waveSize, i - 1));
            
            shader.SetInt("parentStride", parentStride);
            shader.SetInt("stride", stride);
            shader.Dispatch(PrefixSumDownK, Mathf.CeilToInt((float)countsBuff.count / (float)stride / (float)PrefixSumDownGS), 1, 1);
            
            stride = parentStride;
        }

        shader.Dispatch(CompactK, CompactTG, 1, 1);
        Profiler.EndSample();
        
        countsBuff.GetData(counts);
        streamBuff.GetData(data);
        outputBuff.GetData(output);

        streamBuff.Release();
        countsBuff.Release();
        outputBuff.Release();
        Debug.Break();
    }

}
