using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoxelRenderer : MonoBehaviour
{
    private VoxelScene m_scene;
    void Update()
    {
        if(!m_scene) {
            m_scene = GameObject.FindObjectOfType<VoxelScene>();
            Initialize();
        }
    }

    void Initialize() {
        
    }
}
