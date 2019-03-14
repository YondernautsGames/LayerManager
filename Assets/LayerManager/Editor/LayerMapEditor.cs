using UnityEngine;
using UnityEditor;

namespace Yondernauts.LayerManager
{
    [CustomEditor(typeof(LayerMap))]
    public class LayerMapEditor : Editor
    {
        private int m_IndexInput;
        private int m_IndexOutput;

        private int m_MaskInput;
        private int m_MaskOutput;

        const float label_width = 20;

        public override void OnInspectorGUI()
        {
            var map = serializedObject.targetObject as LayerMap;
            
            EditorGUILayout.LabelField("Transform Layer Index", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            m_IndexInput = EditorGUILayout.IntField(m_IndexInput);
            m_IndexInput = Mathf.Clamp(m_IndexInput, 0, 31);
            m_IndexOutput = map.TransformLayer(m_IndexInput);
            EditorGUILayout.LabelField("->", GUILayout.Width(label_width));
            EditorGUILayout.IntField(m_IndexOutput);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Transform Layer Mask", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            m_MaskInput = EditorGUILayout.IntField(m_MaskInput);            
            m_MaskOutput = map.TransformMask(m_MaskInput);
            EditorGUILayout.LabelField("->", GUILayout.Width(label_width));
            EditorGUILayout.IntField(m_MaskOutput);
            EditorGUILayout.EndHorizontal();
        }
    }
}