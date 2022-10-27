using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoxelTest : MonoBehaviour
{
    public Texture3D m_brick;
    // Start is called before the first frame update
    void Start()
    {
        int size = 16;
        m_brick = new Texture3D(size, size, size, TextureFormat.RGBA32, 1);
        Color[] colors = new Color[size * size * size];
        Vector3 center = new Vector3(size / 2, size / 2, size / 2);
        for(int x = 0; x < size;x++) {
            for(int y = 0; y < size;y++) {
                for(int z = 0; z < size;z++) {
                    Color color = new Color(x / (float)size, y / (float)size, z / (float)size, 
                    Vector3.Distance(new Vector3(x, y, z), center) / center.x);

                    colors[x + y * size + z * size * size] = color;
                }
            }
        }
        m_brick.filterMode = FilterMode.Point;
        m_brick.SetPixels(colors);
        m_brick.Apply();


        GetComponent<MeshRenderer>().material.SetTexture("_Brick", m_brick);
    }

    void OnDestroy() {
        Destroy(m_brick);
    }
}
