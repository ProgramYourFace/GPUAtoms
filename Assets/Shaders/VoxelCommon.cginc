#define NULL_PTR 0xFFFFFFFF
#define DIV_MASK 0x80000000
#define PTR_MASK 0x7FFFFFFF
#define BYTE_MASK 0xFF
#define BRICK_WIDTH 8
#define BRICK_LIDX 7 //BRICK_WIDTH - 1
#define BRICK_CELL_COUNT 512//BRICK_WIDTH * BRICK_WIDTH * BRICK_WIDTH
#define TILE_WIDTH 2
#define TILE_NODE_COUNT 8//TILE_WIDTH * TILE_WIDTH * TILE_WIDTH
#define THRESHOLD 0.5
#define SDF_RADIUS 4
#define SDF_ITERATIONS 7 // ceil(SDF_RADIUS / sqrt(3)) * 3 - 2

uint CubeOffsetToIdx(uint3 offset, uint cubeWidth) {
    return offset.x + (offset.y + offset.z * cubeWidth) * cubeWidth;
}

uint3 CubeIdxToOffset(uint idx, uint cubeWidth) {
    uint once = idx / cubeWidth;
    return uint3(idx % cubeWidth,
    once % cubeWidth,
    once / cubeWidth);
}

float4 ConstBrickToColor(uint C) {
    return float4(C >> 24, (C >> 16) & BYTE_MASK, (C >> 8) & BYTE_MASK, C & BYTE_MASK) / 255.0;
}

