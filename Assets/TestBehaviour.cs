using System;
using UnityEngine;

public class TestBehaviour : MonoBehaviour
{
    [SerializeField] private LayerMask m_Mask1;

    public ChildProperty childProperty;

    public LayerMask mask2;

    [Serializable]
    public class ChildProperty
    {
        public LayerMask childMask1;
        public LayerMask childMask2;
    }
}
