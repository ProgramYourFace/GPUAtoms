Shader "Voxels/Octree"
{
    Properties
    {
        _Box ("Box", Vector) = (0, 0, 0, 16)
        _MaxDepth ("Max Depth", Integer) = 2
        _LOD("LOD", Float) = 4
        _SDFSteps ("SDF Steps", Int) = 19
        _OctreeSteps ("Octree Steps", Int) = 20
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        CULL FRONT

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD1;
            };

            // float _Threshold;
            uint _OctreeSteps;
            float _LOD;
            uint _SDFSteps;
            float4 _Box;

            uint _BrickWidth;
            uint _BrickPoolWidth;
            float _BrickTexelSize;
            uint _Frame;

            uint _MaxDepth;

            //Content
            // Texture3D _BrickColor;
            sampler3D _Bricks;
            StructuredBuffer<uint> _NodePtrs;
            StructuredBuffer<uint> _NodeContent;
            //Write
            uniform RWStructuredBuffer<uint> _NodeUsage : register(u1);
            uniform RWStructuredBuffer<uint> _BrickUsage : register(u2);
            uniform RWStructuredBuffer<uint> _Requests  : register(u3);
            
            #define RAY_EP 0.001
            #include "../VoxelCommon.cginc"

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;//TRANSFORM_TEX(v.uv, _MainTex);
                o.viewDir = -WorldSpaceViewDir(v.vertex);
                return o;
            }

            float2 intersectAABB(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax) {
                float3 tMin = (boxMin - rayOrigin) / rayDir;
                float3 tMax = (boxMax - rayOrigin) / rayDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);
                return float2(tNear, tFar);
            }

            // float marchBrickCells(float3 ro, float3 rd, int3 offset, float4 boundBox, out float4 color) {
            //     int3 cell = floor(ro);
            //     int3 rs = sign(rd);
            //     float3 ri = 1.0 / rd;
            //     float3 dis = (cell-ro + 0.5 + rs*0.5) * ri;

            //     float3 boundStart = boundBox.xyz - 0.5;
            //     float3 boundEnd = boundStart + boundBox.w;

            //     float res = -1.0;
            //     float3 mm = 0.0;
            //     [loop]
            //     for(uint i = 0;i < _BrickSteps;i++) {
            //         if(any(cell < boundStart || cell > boundEnd)) {break;}
            //         color = _BrickColor.Load(int4(cell + offset, 0));
            //         if(color.w <= _Threshold) { res = 1.0; break; }
            //         mm = step(dis.xyz, dis.yzx) * step(dis.xyz, dis.zxy);
            //         dis += mm * rs * ri;
            //         cell += mm * rs;
            //     }

            //     float3 mini = (cell-ro + 0.5 - rs*0.5) * ri;
            //     float t = max(max(mini.x, max(mini.y, mini.z)), 0);

            //     return t * res;
            // }
            
            // float marchBrickISOCells(float3 ro, float3 rd, int3 offset, float4 boundBox, float maxT, out float4 c0) {
            //     int3 cell = clamp(floor(ro), 0, 7);
            //     int3 rs = sign(rd);
            //     float3 ri = 1.0 / rd;
            //     float3 dis = (cell-ro + 0.5 + rs*0.5) * ri;

            //     float3 boundStart = boundBox.xyz - 0.5;
            //     float3 boundEnd = boundStart + boundBox.w;

            //     float4 off4 = float4(offset + 0.5 + ro, 0.0);
            //     float4 rd4 = float4(rd, 0.0);
                
            //     float t0 = 0.0;
            //     c0 = tex3Dlod(_Bricks, (off4 + rd4 * t0) * _BrickTexelSize);

            //     if(c0.w <= 0.5) { return 0.0; }

            //     float t1;
            //     float4 c1;

            //     float t;
            //     float3 mm = 0.0;
            //     [loop]
            //     for(uint i = 0;i < _SDFSteps;i++) {
            //         if(any(cell < boundStart || cell > boundEnd)) { return -maxT; }

            //         t1 = min(min(dis.x, dis.y), dis.z);
            //         c1 = tex3Dlod(_Bricks, (off4 + rd4 * t1) * _BrickTexelSize);

            //         if(c1.w <= 0.5) { 
            //             t = t0 + (t1 - t0) * (0.5 - c0.w) / (c1.w - c0.w);
            //             c0 = tex3Dlod(_Bricks, (off4 + rd4 * t) * _BrickTexelSize);
            //             return t;
            //         }

            //         mm = step(dis.xyz, dis.yzx) * step(dis.xyz, dis.zxy);
            //         dis += mm * rs * ri;
            //         cell += mm * rs;
            //         t0 = t1;
            //         c0 = c1;
            //     }

            //     return -maxT;
            // }

            float marchBrick(float3 ro, float3 rd, int3 offset, float4 boundBox, float maxT, out float4 c0) {
                int3 rs = sign(rd);
                float3 ri = 1.0 / rd;

                float t = 0.0;
                float3 cell;
                float3 nearOffset = 0.5 - ro - rs*0.5;
                float3 farOffset = 0.5 - ro + rs*0.5;
                
                float t0; float t1;
                float4 c1;
                float3 p;
                float3 far; float3 near;
                
                //For sampling bricks
                float4 off4 = float4(offset + 0.5 + ro, 0.0);
                float4 rd4 = float4(rd, 0.0);

                float3 boundStart = boundBox.xyz - 0.5;
                float3 boundEnd = boundStart + boundBox.w;
                float res = -1.0;
                [loop]
                for(uint i = 0;i < _SDFSteps;i++) {
                    // if(t >= maxT - RAY_EP) { t=maxT; break; }
                    p = ro + rd * t;
                    cell = floor(p);
                    if(any(cell < boundStart || cell > boundEnd)) {break;}

                    //In
                    near = (cell + nearOffset) * ri;
                    t0 = max(max(near.x, max(near.y, near.z)), 0.0);
                    c0 = tex3Dlod(_Bricks, (off4 + rd4 * t0) * _BrickTexelSize);
                    if(c0.w <= THRESHOLD) { t = t0; res = 1.0; break; }

                    //out
                    far = (cell + farOffset) * ri;
                    t1 = min(far.x, min(far.y, far.z));
                    c1 = tex3Dlod(_Bricks, (off4 + rd4 * t1) * _BrickTexelSize);
                    if(c1.w <= THRESHOLD) {
                        t = t0 + (t1 - t0) * (THRESHOLD - c0.w) / (c1.w - c0.w);
                        c0 = tex3Dlod(_Bricks, (off4 + rd4 * t) * _BrickTexelSize);
                        res = 1.0;
                        break;
                    } else {
                        float step = (c1.w * 2.0 - 1.0) * SDF_RADIUS * 0.9;
                        t = t1 + max(step - 1.0, 0.001);
                    }
                }
                
                return t * res;
            }

            
            // float marchBrickSDF(float3 ro, float3 rd, int3 offset, float4 boundBox, float maxT, out float4 color) {
            //     float t = 0.0;
            //     float subT;
            //     float lastSubT;
            //     float3 bMin = boundBox.xyz;
            //     float3 bMax = bMin + boundBox.w;
            //     float3 p;
            //     float res = -1.0;
            //     float absMax = 1.0;
            //     [loop]
            //     for(uint i = 0;i < _SDFSteps;i++) {
            //         if(t > maxT) {break;}
            //         p = ro + rd * clamp(t, 0, maxT);
            //         color = tex3D(_Bricks, (offset + 0.5 + p) * _BrickTexelSize);
            //         lastSubT = subT;
            //         subT = (color.w * 2.0 - 1.0) * SDF_RADIUS;

            //         if(subT < 0) {subT = -min(absMax, -subT); absMax *= 0.75;}
            //         t += subT;
            //         if(abs(subT) <= _SDFThreshold || t < 0.0) { res = 1.0; t = clamp(t, 0, maxT); break; }
            //     }
                
            //     return t * res;
            // }

            uint sampleOctree(float3 p, int maxDepth, out float4 viewBox, out float4 contentBox, out bool shouldHaveBrick) {
                uint cursor = 0;
                _NodeUsage[cursor] = _Frame;
                
                viewBox = _Box;
                contentBox = _Box;
                uint content = NULL_PTR;

                uint next;
                uint thisContent;
                bool decend;
                bool divide;
                uint3 childBlock;
                uint3 childIdx;
                uint4 brickOffsetAndIdx;

                [loop]
                for(int d = 0;d <= maxDepth;d++) {
                    next = _NodePtrs[cursor];
                    thisContent = _NodeContent[cursor];
                    shouldHaveBrick = (next & DIV_MASK) != 0;
                    next &= PTR_MASK;
                    divide = d < maxDepth && shouldHaveBrick;
                    decend = divide && next != PTR_MASK;

                    if(!shouldHaveBrick || thisContent != NULL_PTR) {
                        content = thisContent;
                        contentBox = viewBox;
                    }

                    if(decend) {
                        _NodeUsage[next] = _Frame;//Mark node as used for this frame

                        //Update view and content boxes to child
                        viewBox.w *= 0.5;
                        childBlock = min(max(floor((p - viewBox.xyz) / viewBox.w), 0), 1); 
                        childIdx = next * TILE_NODE_COUNT + CubeOffsetToIdx(childBlock, TILE_WIDTH);
                        viewBox += float4(childBlock * viewBox.w, 0);

                        //Move cursor to child
                        cursor = childIdx;
                    } else { break; }
                }

                if(shouldHaveBrick) {
                    if(thisContent == NULL_PTR) {
                        _Requests[cursor] = 2;//Request brick
                    } else if (divide) {
                        _Requests[cursor] = 1;//Request node
                    }
                    
                    if(content != NULL_PTR)
                        _BrickUsage[content] = _Frame;//Mark brick as used for this frame
                    else
                        shouldHaveBrick = false;
                }

                return content;
            }

            float marchOctree(float3 ro, float3 rd, float start, float end, out float4 color, out uint stepCount) {
                float t = start + RAY_EP;

                float3 p;
                float2 I;
                uint content;
                float4 viewBox;
                float4 contentBox;
                bool contentIsBrick;
                float contentToBrick;
                stepCount = 0;

                uint depth;

                float res = -1.0;
                [loop]
                while(t < end && stepCount < _OctreeSteps) {
                    p = ro + rd * t;
                    stepCount++;
                    depth = clamp(floor(_LOD / t), 0, _MaxDepth);
                    content = sampleOctree(p, depth, viewBox, contentBox, contentIsBrick);
                    I = max(intersectAABB(ro, rd, viewBox.xyz, viewBox.xyz + viewBox.w), 0.0);
                    if(contentIsBrick) {
                        contentToBrick = (_BrickWidth - 1.0) / contentBox.w;
                        t = marchBrick(contentToBrick * (ro + rd * (I.x + RAY_EP) - contentBox.xyz), rd, 
                        CubeIdxToOffset(content, _BrickPoolWidth) * _BrickWidth, 
                        float4((viewBox.xyz - contentBox.xyz), viewBox.w) * contentToBrick, (I.y - I.x) * contentToBrick, color) / contentToBrick;
                        t += (t >= 0) ? I.x : -I.x;
                        // color = tex3D(_Bricks)
                    } else {
                        color = ConstBrickToColor(content);
                        t = color.w <= THRESHOLD ? I.x : -1.0;
                    }
                    if(t >= 0.0) {res = 1.0; break;}
                    t = I.y + RAY_EP;
                }

                return t * res;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos.xyz;
                float3 rd = normalize(i.viewDir);
                float2 I = max(intersectAABB(ro, rd, _Box.xyz, _Box.xyz + _Box.www), 0);
                // sample the texture
                bool hit;
                float4 color;
                uint stepCount;
                float t;
                // if(_Frame < 200) {
                    t = marchOctree(ro, rd, I.x, I.y, color, stepCount);
                // } else {
                //     float contentToBrick = (_BrickWidth - 1.0) / _Box.w;
                //     t = marchBrick(contentToBrick * (ro + rd * I.x), rd, 0, 0, (I.y - I.x) * contentToBrick, color) / contentToBrick;
                // }
                clip(t);
                // return t / 16.0;
                // if(t >= 0.0)
                return color; 
                // return lerp(t >= 0.0 ? color : 0.0, float4(1.0, 0.0, 0.0, 1.0), stepCount / 16.0);
                // else return float4(stepCount / 10.0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
