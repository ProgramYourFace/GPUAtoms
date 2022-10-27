Shader "Voxels/Brick"
{
    Properties
    {
        _Brick ("Texture", 3D) = "white" {}
        _BrickLocation ("Brick Location", Vector) = (0, 0, 0, 16)
        _StepCount ("Step Count", Int) = 16
        _Threshold ("Threshold", Float) = 0.5
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
            // make fog work
            #pragma multi_compile_fog

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

            float _Threshold;
            int _StepCount;
            float4 _BrickLocation;
            Texture3D _Brick;

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

            

            float marchBrick(float3 ro, float3 rd, int3 offset, int3 size, out float4 color) {
                int3 cell = floor(ro);
                int3 rs = sign(rd);
                float3 ri = 1.0 / rd; 
                float3 dis = (cell-ro + 0.5 + rs*0.5) * ri;

                float res = -1.0;
                float3 mm = 0.0;
                [loop]
                for(int i = 0;i < _StepCount;i++) {
                    if(any(cell < 0 || cell >= size)) {break;}
                    color = _Brick.Load(int4(cell + offset, 0));
                    if(color.w <= _Threshold) { res = 1.0; break; }
                    mm = step(dis.xyz, dis.yzx) * step(dis.xyz, dis.zxy);
                    dis += mm * rs * ri;
                    cell += mm * rs;
                }

                float3 mini = (cell-ro + 0.5 - rs*0.5) * ri;
                float t = max(max(mini.x, max(mini.y, mini.z)), 0);

                return t * res;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos.xyz;
                float3 rd = normalize(i.viewDir);
                float2 I = max(intersectAABB(ro, rd, _BrickLocation.xyz, _BrickLocation.xyz + _BrickLocation.www), 0);
                // sample the texture
                bool hit;
                float4 color;
                float t = marchBrick(ro + rd * (I.x + 0.01), rd, 0, 16, color);
                clip(t);
                return color;
            }
            ENDCG
        }
    }
}
