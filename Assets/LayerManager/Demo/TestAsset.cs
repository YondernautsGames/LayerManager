using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TestAsset", menuName = "Layer Manager/TestAsset")]
public class TestAsset : ScriptableObject
{
    [SerializeField] private LayerMask m_Mask1;

    public LayerMask mask2;
}
