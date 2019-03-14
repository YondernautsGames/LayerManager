using System;
using UnityEngine;
using UnityEditor;

namespace Yondernauts.LayerManager
{
    public class LayerManagerData : ScriptableObject
    {
        // Map array hidden from public access
        [SerializeField] private LayerMapEntry[] m_LayerMap;
        [SerializeField] private bool m_Dirty = false;

        private SerializedLayerMapEntry[] m_SerializedEntries;

        [Serializable]
        public class LayerMapEntry
        {
            public string name;
            public string oldName;
            public int oldIndex;
            public int redirect = -1;

            public LayerMapEntry(string n, int i)
            {
                name = n;
                oldName = n;
                oldIndex = i;
            }

            public bool valid
            {
                get { return redirect != -1 || !string.IsNullOrEmpty(name) || string.IsNullOrEmpty(oldName); }
            }
        }

        public class SerializedLayerMapEntry
        {
            private LayerManagerData m_Data;

            public SerializedProperty serializedProperty
            {
                get;
                private set;
            }

            public SerializedLayerMapEntry(LayerManagerData data, int index)
            {
                m_Data = data;
                serializedProperty = data.layerMapProperty.GetArrayElementAtIndex(index);
            }

            public string name
            {
                get { return serializedProperty.FindPropertyRelative("name").stringValue; }
                set
                {
                    Undo.RecordObject(m_Data, "Change Layer Name");
                    serializedProperty.FindPropertyRelative("name").stringValue = value;
                    // if empty, wipe dependent redirects
                    if (string.IsNullOrEmpty (value))
                    {
                        var entries = m_Data.GetAllEntries();
                        foreach (var entry in entries)
                        {
                            if (entry == this)
                                continue;
                            if (entry.redirect == oldIndex)
                                entry.redirect = -1;
                        }
                    }
                    EditorUtility.SetDirty(m_Data);
                }
            }

            public int redirect
            {
                get { return serializedProperty.FindPropertyRelative("redirect").intValue; }
                set
                {
                    Undo.RecordObject(m_Data, "Redirect Layer");
                    int undo = Undo.GetCurrentGroup();
                    serializedProperty.FindPropertyRelative("redirect").intValue = value;
                    // If check dependent redirects and set to new value
                    if (value == -1)
                    {
                        if (string.IsNullOrEmpty (oldName))
                        {
                            var entries = m_Data.GetAllEntries();
                            foreach (var entry in entries)
                            {
                                if (entry == this)
                                    continue;
                                if (entry.redirect == oldIndex)
                                {
                                    Undo.RecordObject(m_Data, "Redirect Layer");
                                    entry.serializedProperty.FindPropertyRelative("redirect").intValue = -1;
                                }
                            }
                        }
                    }
                    else
                    {
                        var entries = m_Data.GetAllEntries();
                        foreach (var entry in entries)
                        {
                            if (entry == this)
                                continue;
                            if (entry.redirect == oldIndex)
                            {
                                Undo.RecordObject(m_Data, "Redirect Layer");
                                entry.serializedProperty.FindPropertyRelative("redirect").intValue = value;
                            }
                        }
                    }
                    Undo.CollapseUndoOperations(undo);
                    EditorUtility.SetDirty(m_Data);
                }
            }

            public string oldName
            {
                get { return serializedProperty.FindPropertyRelative("oldName").stringValue; }
            }

            public int oldIndex
            {
                get { return serializedProperty.FindPropertyRelative("oldIndex").intValue; }
            }

            public bool valid
            {
                get
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        var entries = m_Data.GetAllEntries();
                        foreach (var entry in entries)
                        {
                            if (entry == this)
                                continue;
                            if (entry.name == name)
                                return false;
                        }
                    }
                    return redirect != -1 || !string.IsNullOrEmpty(name) || string.IsNullOrEmpty(oldName);
                }
            }

            public string GetRedirectName()
            {
                foreach (var entry in m_Data.m_LayerMap)
                {
                    if (entry.oldIndex == oldIndex)
                        return entry.name;
                }
                return string.Empty;
            }
        }

        public bool dirty
        {
            get { return m_Dirty; }
            set { serializedObject.FindProperty("m_Dirty").boolValue = value; }
        }

        public bool valid
        {
            get
            {
                foreach (var entry in m_LayerMap)
                {
                    if (!entry.valid)
                        return false;
                }
                return true;
            }
        }

        public SerializedObject serializedObject
        {
            get;
            private set;
        }

        public SerializedProperty layerMapProperty
        {
            get;
            private set;
        }

        public void Initialise()
        {
            // Build layer map from current settings
            m_LayerMap = new LayerMapEntry[24];
            for (int i = 0; i < 24; ++i)
            {
                int oldIndex = i + 8;
                m_LayerMap[i] = new LayerMapEntry(LayerMask.LayerToName(oldIndex), oldIndex);
            }

            // Set as not dirty
            m_Dirty = false;

            // Get serialization objects
            serializedObject = new SerializedObject(this);
            layerMapProperty = serializedObject.FindProperty("m_LayerMap");

            // Build serialized entries
            m_SerializedEntries = new SerializedLayerMapEntry[24];
            RebuildSerializedEntries();

            // Set to not save
            hideFlags = HideFlags.DontSave;
        }

        public void RebuildSerializedEntries()
        {
            for (int i = 0; i < 24; ++i)
                m_SerializedEntries[i] = new SerializedLayerMapEntry(this, i);
        }

        public SerializedLayerMapEntry GetEntryFromIndex(int index)
        {
            return m_SerializedEntries[index];
        }

        public SerializedLayerMapEntry GetEntryFromOldIndex(int oldIndex)
        {
            for (int i = 0; i < m_LayerMap.Length; ++i)
            {
                if (m_LayerMap[i].oldIndex == oldIndex)
                    return m_SerializedEntries[i];
            }
            return null;
        }

        public SerializedLayerMapEntry[] GetAllEntries()
        {
            return m_SerializedEntries;
        }

        public void ApplyModifiedProperties()
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
}