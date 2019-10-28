using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;

namespace Yondernauts.LayerManager
{
    public class LayerManager : EditorWindow
    {
        const string instructions = @"Instructions:

- Rename and reorganise layers as desired using the reorderable list below.
- Applying the modifications will update the physics settings; change the layers on all game objects in all scenes and all prefabs; change any LayerMask serialized properties on game objects and scriptable objects.
- Once the manager has finished processing files, you will be able to save out a layer map which you can use to transform layer filters and masks from the old layout to the new layout.
";

        // State
        ManagerState m_State = ManagerState.Complete;
        Vector2 m_EditScroll = Vector2.zero;
        bool m_SkipRepaint = false;

        // Data (serialized object for undo & persistance)
        LayerManagerData m_Data = null;

        // Reorderable list for layer layout
        ReorderableList m_LayerList = null;

        // Processing data
        string[] m_AssetPaths = null;
        int m_CurrentAssetPath = -1;
        int[] m_IndexSwaps = null;
        int[] m_IndexSwapsRedirected = null;
        string[] m_FixedLayers = null;

        // Reporting variables
        int m_SceneCount = 0;
        int m_PrefabCount = 0;
        int m_ObjectCount = 0;
        int m_ComponentCount = 0;
        int m_AssetCount = 0;
        int m_LayerMaskCount = 0;
        bool m_PhysicsMatrixCompleted = false;
        bool m_Physics2DMatrixCompleted = false;
        string m_CompletionReport = string.Empty;

        // Physics layer collisions
        uint[] m_PhysicsMasks = null;
        uint[] m_Physics2DMasks = null;

        // Error reporting
        List<string> m_Errors = new List<string>();

        // Layout helpers
        float lineHeight { get { return EditorGUIUtility.singleLineHeight; } }
        float lineSpacing { get { return EditorGUIUtility.standardVerticalSpacing; } }

        enum ManagerState
        {
            Editing,
            Confirmation,
            Processing,
            Complete
        }

        [MenuItem("Tools/Yondernauts/Layer Manager")]
        static void ShowEdtor()
        {
            // Get existing open window or if none, make a new one:
            LayerManager layerManager = (LayerManager)EditorWindow.GetWindow(typeof(LayerManager));
            layerManager.ResetData();
            layerManager.Show();
        }

        public bool dirty
        {
            get { return m_Data.dirty; }
            private set { m_Data.dirty = value; }
        }

        public bool valid
        {
            get { return m_Data.valid; }
        }

        void Initialise()
        {
            // Create & attach scriptable object (allows undos, etc)
            if (m_Data == null)
            {
                m_Data = CreateInstance<LayerManagerData>();
                m_Data.Initialise();
            }

            // Get fixed layers
            m_FixedLayers = new string[8];
            for (int i = 0; i < 8; ++i)
                m_FixedLayers[i] = LayerMask.LayerToName(i);


            // Create reorderable list
            m_LayerList = new ReorderableList(
                m_Data.serializedObject, m_Data.layerMapProperty,
                true, true, false, false
                );
            m_LayerList.drawHeaderCallback = DrawLayerMapHeader;
            m_LayerList.drawElementCallback = DrawLayerMapElement;
            m_LayerList.elementHeight = lineHeight * 2 + lineSpacing * 3;
            m_LayerList.onReorderCallback = OnLayerMapReorder;

            // Reset state
            m_State = ManagerState.Editing;

            // Reset reporting
            m_SceneCount = 0;
            m_PrefabCount = 0;
            m_ObjectCount = 0;
            m_ComponentCount = 0;
            m_AssetCount = 0;
            m_LayerMaskCount = 0;
            m_PhysicsMatrixCompleted = false;
            m_Physics2DMatrixCompleted = false;
            m_Errors.Clear();
            m_CompletionReport = string.Empty;
        }

        private void OnEnable()
        {
            // Set up window
            titleContent = EditorGUIUtility.IconContent("HorizontalSplit");
            titleContent.text = "Layer Mgr";
            minSize = new Vector2(400, 320);
            Initialise();
            autoRepaintOnSceneChange = true;
            Undo.undoRedoPerformed += OnUndo;
#if UNITY_2018_1_OR_NEWER
            EditorApplication.quitting += OnQuit;
#endif
        }

        private void OnDestroy()
        {
            // Finish processing if closed mid-way through
            while (m_AssetPaths != null)
                IncrementLayerModifications();
            Undo.undoRedoPerformed -= OnUndo;
        }

        void OnUndo()
        {
            Repaint();
        }

        void OnQuit()
        {
            Close();
        }

        void OnGUI()
        {
            if (m_SkipRepaint && Event.current.type == EventType.Repaint)
            {
                m_SkipRepaint = false;
                return;
            }

            switch (m_State)
            {
                case ManagerState.Editing:
                    OnEditingGUI();
                    break;
                case ManagerState.Confirmation:
                    OnConfirmationGUI();
                    break;
                case ManagerState.Processing:
                    OnProcessingGUI();
                    break;
                case ManagerState.Complete:
                    OnCompleteGUI();
                    break;
            }
        }

        void OnEditingGUI ()
        {
            if (m_LayerList == null || m_LayerList.serializedProperty == null)
            {
                ResetData();
                if (Event.current.type == EventType.Layout)
                    m_SkipRepaint = true;
                return;
            }
            
            // Use a scroll view to fit it all in
            m_EditScroll = EditorGUILayout.BeginScrollView(m_EditScroll);

            // Draw instructions
            EditorGUILayout.HelpBox(instructions, MessageType.Info);
            float helpHeight = GUILayoutUtility.GetLastRect().height + 8;

            // Show reorganise list
            float h = m_LayerList.elementHeight * 25;
            var r = EditorGUILayout.GetControlRect(false, h + helpHeight + 52);
            r.x = 8;
            r.y = helpHeight;
            r.height = h;
            r.width -= 6;
            m_LayerList.DoList(r);

            // Show controls
            r.y += r.height;
            r.height = lineHeight + 8;

            // Apply modifications
            GUI.enabled = dirty && valid;
            if (GUI.Button(r, "Apply Layer Modifications"))
                m_State = ManagerState.Confirmation;
            GUI.enabled = true;

            r.y += lineHeight + 12;

            // Reset modifications
            if (GUI.Button(r, "Reset Layer Moditications"))
                ResetData();            

            // End the scroll view
            EditorGUILayout.EndScrollView();

            // Apply changes to data
            m_Data.ApplyModifiedProperties();
        }

        void OnConfirmationGUI ()
        {
            // Show warning
            EditorGUILayout.HelpBox("Warning: This process is not reversible and modifies a lot of files.\n\nMake sure all scenes are saved (including the open scene) and you have an up to date backup in case anything gose wrong.", MessageType.Warning);

            // OK
            if (GUILayout.Button("Yes, I have a backup"))
                ApplyLayerModifications();

            // Cancel
            if (GUILayout.Button("No, I'm not ready yet"))
                m_State = ManagerState.Editing;
        }

        void OnProcessingGUI ()
        {
            // Show info
            EditorGUILayout.HelpBox("Processing layer modifications. Do not close this window until completed.", MessageType.Info);

            // Show progress bar
            float helpHeight = GUILayoutUtility.GetLastRect().height + 8;
            Rect r = position;
            r.y = helpHeight;
            r.height = EditorGUIUtility.singleLineHeight;
            r.x = 4;
            r.width -= 8;
            EditorGUI.ProgressBar(r, (float)m_CurrentAssetPath / (float)m_AssetPaths.Length, "Progress");

            // Process
            if (Event.current.type == EventType.Repaint)
            {
                IncrementLayerModifications();
                Repaint();
            }
        }
        
        void OnCompleteGUI ()
        {
            // Show completion report
            EditorGUILayout.HelpBox(m_CompletionReport, MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("Close the Layer Manager"))
                Close();

            if (GUILayout.Button("Keep making changes"))
                ResetData();

            if (GUILayout.Button("Save Layer Map"))
                CreateMap();

            // Handle errors selection
            if (m_Errors.Count == 0)
                GUI.enabled = false;
            if (GUILayout.Button("Handle Errors"))
            {
                GenericMenu menu = new GenericMenu();

                // Use Debug.LogError
                menu.AddItem(new GUIContent("Output To Console"), false, () =>
                {
                    Debug.LogError(BuildErrorReport(false));
                });

                // Use mailto with support email
                menu.AddItem(new GUIContent("Email Support"), false, () =>
                {
                    Application.OpenURL("mailto:support@yondernauts.games?subject=Layer%20Manager&body=" + BuildErrorReport(true));
                });

                menu.ShowAsContext();
            }
            if (m_Errors.Count == 0)
                GUI.enabled = true;
        }

        string BuildErrorReport (bool url)
        {
            StringBuilder result = new StringBuilder("Layer Manager failed with the following errors:");
            foreach (var err in m_Errors)
                result.Append(" - ").AppendLine(err);
            if (url)
                return Uri.EscapeDataString(result.ToString());
            return result.ToString();
        }

        private void DrawLayerMapHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "Modified Layer Map");
        }

        private void DrawLayerMapElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var entry = m_Data.GetEntryFromIndex(index);
            
            rect.height = lineHeight;
            rect.y += lineSpacing;
            
            // Draw top label (modified & new index)
            EditorGUI.LabelField(rect, new GUIContent(string.Format("Modified [{0:D2}]", index + 8)), EditorStyles.boldLabel);

            // Draw the name entry field
            Color bg = GUI.backgroundColor;
            if (!entry.valid)
                GUI.backgroundColor = Color.red;
            Rect r1 = rect;
            r1.x += 120;
            r1.width -= 204;
            string nameInput = EditorGUI.TextField(r1, entry.name);
            if (entry.name != nameInput)
            {
                // Dirty on name change
                entry.name = nameInput;
                dirty = true;
            }
            GUI.backgroundColor = bg;

            // Draw the redirect control
            r1.x += r1.width + 4;
            r1.width = 80;
            GUI.enabled = !string.IsNullOrEmpty(entry.oldName);
            if (EditorGUI.DropdownButton(r1, new GUIContent("Redirect"), FocusType.Passive))
            {
                GenericMenu menu = new GenericMenu();

                // Add None option
                menu.AddItem(new GUIContent("None"), false, () =>
                {
                    entry.redirect = -1;
                    dirty = true;
                });
                menu.AddSeparator("");

                // Add options for fixed layers
                for (int i = 0; i < 8; ++i)
                {
                    if (!string.IsNullOrEmpty(m_FixedLayers[i]))
                    {
                        // Do stuff
                        int id = i;
                        menu.AddItem(new GUIContent(m_FixedLayers[i]), false, () =>
                        {
                            entry.redirect = id;
                            dirty = true;
                        });
                    }
                }
                menu.AddSeparator("");

                // Add options for valid layers
                var allEntries = m_Data.GetAllEntries();
                for (int i = 0; i < allEntries.Length; ++i)
                {
                    if (i == index)
                        continue;

                    var targetEntry = allEntries[i];
                    if (targetEntry.redirect != -1)
                        continue;
                    if (string.IsNullOrEmpty(targetEntry.name))
                        continue;

                    menu.AddItem(new GUIContent(targetEntry.name), false, () =>
                    {
                        entry.redirect = targetEntry.oldIndex;
                        dirty = true;
                    });
                }

                menu.ShowAsContext();
            }
            GUI.enabled = true;

            // Draw bottom label (original & old index)
            rect.y += lineHeight + lineSpacing;
            EditorGUI.LabelField(rect, new GUIContent(string.Format("Original [{0:D2}]", entry.oldIndex)));

            // Draw old name
            Rect r2 = rect;
            r2.x += 120;
            r2.width -= 204;
            EditorGUI.LabelField(r2, entry.oldName);

            // Draw redirect
            r2.x += r2.width + 4;
            r2.width = 80;
            int redirect = entry.redirect;
            if (redirect == -1)
                EditorGUI.LabelField(r2, "No Redirect");
            else
            {
                if (redirect < 8)
                    EditorGUI.LabelField(r2, m_FixedLayers[redirect]);
                else
                    EditorGUI.LabelField(r2, m_Data.GetEntryFromOldIndex(redirect).name);
            }
        }

        void OnLayerMapReorder (ReorderableList list)
        {
            dirty = true;
            m_Data.RebuildSerializedEntries();
        }

        void ResetData ()
        {
            if (m_Data != null)
            {
                DestroyImmediate(m_Data);
                m_Data = null;
            }
            Initialise();
        }

        void ApplyLayerModifications()
        {
            // Get the layer collision matrix before altering the layers
            GetLayerCollisionMatrix();
            Get2DLayerCollisionMatrix();

            // Get Tags and Layers settings
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            if (tagManager == null)
            {
                // Complete with error
                m_State = ManagerState.Complete;
                m_CompletionReport = "Failed to process layer modifcations. Asset not found: ProjectSettings/TagManager.asset";
                return;
            }

            // Get layer properties
            var layerProps = tagManager.FindProperty("layers");
            if (layerProps == null)
            {
                // Complete with error
                m_State = ManagerState.Complete;
                m_CompletionReport = "Failed to process layer modifcations. No layers property found in tag manager asset.";
                return;
            }

            // Modify layer settings
            var allEntries = m_Data.GetAllEntries();
            try
            {
                for (int i = 0; i < 24; ++i)
                    layerProps.GetArrayElementAtIndex(i + 8).stringValue = allEntries[i].name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (Exception e)
            {
                // Complete with error
                m_State = ManagerState.Complete;
                m_CompletionReport = "Failed to process layer modifcations. Exception when updating layer settings: " + e.Message;
                return;
            }

            // Build reverse array of index swaps (old to new)
            m_IndexSwaps = new int[32];
            m_IndexSwapsRedirected = new int[32];
            for (int i = 0; i < 8; ++i)
            {
                m_IndexSwaps[i] = i;
                m_IndexSwapsRedirected[i] = i;
            }
            for (int i = 0; i < 24; ++i)
                m_IndexSwaps[allEntries[i].oldIndex] = i + 8;
            for (int i = 0; i < 24; ++i)
            {
                if (allEntries[i].redirect == -1)
                    m_IndexSwapsRedirected[allEntries[i].oldIndex] = i + 8;
                else
                    m_IndexSwapsRedirected[allEntries[i].oldIndex] = TransformLayer(allEntries[i].redirect, false);
            }

            // Apply new layers to collision matrix
            ProcessLayerCollisionMatrix();
            Process2DLayerCollisionMatrix();

            // Set up for incremental processing.
            m_AssetPaths = AssetDatabase.GetAllAssetPaths();
            m_CurrentAssetPath = 0;
            m_State = ManagerState.Processing;
        }

        void IncrementLayerModifications ()
        {
            if (m_AssetPaths == null)
                return;
            if (m_CurrentAssetPath >= m_AssetPaths.Length)
            {
                m_AssetPaths = null;
                m_CurrentAssetPath = 0;
                return;
            }
            
            string path = m_AssetPaths[m_CurrentAssetPath];
            try
            {
                if (path.StartsWith("Assets/"))
                {
                    // Process prefab
                    if (path.EndsWith(".prefab"))
                    {
                        // Record object & component counts to check if modified
                        int objCount = m_ObjectCount;
                        int compCount = m_ComponentCount;

                        // Load the prefab asset and modify
                        var obj = (GameObject)AssetDatabase.LoadMainAssetAtPath(path);
                        ProcessGameObject(obj, false);

                        // Prefab was modified
                        if (m_ObjectCount > objCount || m_ComponentCount > compCount)
                            ++m_PrefabCount;
                    } 
                    else
                    {
                        // Process ScriptableObject asset
                        if (path.EndsWith(".asset"))
                        {
                            // Load the scriptable object (and children) and modify
                            var objects = AssetDatabase.LoadAllAssetsAtPath(path);
                            foreach (var obj in objects)
                            {
                                SerializedObject so = new SerializedObject(obj);
                                if (ProcessSerializedObject(so))
                                    ++m_AssetCount;
                            }
                        }
                        else
                        {
                            // Process scene
                            if (path.EndsWith(".unity"))
                            {
                                // Record object & component counts to check if modified
                                int objCount = m_ObjectCount;
                                int compCount = m_ComponentCount;

                                // Load the scene
                                var scene = EditorSceneManager.OpenScene(path);

                                // Iterate through objects and modify
                                GameObject[] objects = scene.GetRootGameObjects();
                                foreach (var obj in objects)
                                    ProcessGameObject(obj, true);

                                // Scene was modified
                                if (m_ObjectCount > objCount || m_ComponentCount > compCount)
                                {
                                    ++m_SceneCount;
                                    //EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                                    if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ""))
                                        Debug.LogWarning("Failed to save scene: " + path);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                m_Errors.Add(string.Format("Encountered error processing asset: \"{0}\", message: {1}", path, e.Message));
            }

            // Increment or complete
            ++m_CurrentAssetPath;
            if (m_CurrentAssetPath >= m_AssetPaths.Length)
            {
                m_AssetPaths = null;
                m_CurrentAssetPath = 0;
                AssetDatabase.SaveAssets();
                m_State = ManagerState.Complete;
                BuildReport();
            }
        }

#if UNITY_2018_3_OR_NEWER

        void ProcessGameObject(GameObject go, bool inScene)
        {
            try
            {
                if (inScene)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsPrefabAssetMissing(go))
                    {
                        // Checking prefab in scene - only process unapplied modifications
                        // The rest will be done through the prefabs in the project hierarchy pass
                        ProcessPrefabModifications(go);
                    }
                    else
                    {
                        // Process children
                        Transform t = go.transform;
                        int childCount = t.childCount;
                        for (int i = 0; i < childCount; ++i)
                            ProcessGameObject(t.GetChild(i).gameObject, inScene);

                        // Swap layer
                        SerializedObject so = new SerializedObject(go);
                        var layerProp = so.FindProperty("m_Layer");
                        int oldLayer = layerProp.intValue;
                        int transformedLayer = TransformLayer(oldLayer, true);
                        if (transformedLayer != oldLayer)
                        {
                            layerProp.intValue = transformedLayer;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            ++m_ObjectCount;
                        }

                        // Process Components
                        Component[] components = go.GetComponents<Component>();
                        for (int i = 0; i < components.Length; ++i)
                        {
                            if (ProcessSerializedObject(new SerializedObject(components[i])))
                                ++m_ComponentCount;
                        }
                    }
                }
                else
                {
                    if (PrefabUtility.IsPartOfVariantPrefab(go))
                        ProcessVariantPrefab(go, go);
                    else
                        ProcessProjectPrefab(go);
                }
            }
            catch (Exception e)
            {
                m_Errors.Add(string.Format("Encountered error processing GameObject: \"{0}\", message: {1}", go.name, e.Message));
            }
        }

        void ProcessProjectPrefab(GameObject go)
        {
            ProcessPrefabGameObject(go);

            // Process children
            Transform t = go.transform;
            int childCount = t.childCount;
            for (int i = 0; i < childCount; ++i)
            {
                var child = t.GetChild(i).gameObject;
                var childRoot = PrefabUtility.GetNearestPrefabInstanceRoot(child);
                if (childRoot != null)
                    ProcessPrefabModifications(child);
                else
                    ProcessProjectPrefab(child);
            }
        }

        void ProcessVariantPrefab(GameObject go, GameObject root)
        {
            if (go == root)
                ProcessPrefabModifications(go);

            // PROCESSING CHILDREN NOT REQUIRED
            /*
            // Process children
            Transform t = go.transform;
            int childCount = t.childCount;
            for (int i = 0; i < childCount; ++i)
            {
                var child = t.GetChild(i).gameObject;
                var childRoot = PrefabUtility.GetNearestPrefabInstanceRoot(child);
                if (childRoot != root)
                    ProcessVariantPrefab(child, childRoot);
                else
                    ProcessVariantPrefab(child, root);
            }
            */
        }

        void ProcessPrefabGameObject(GameObject go)
        {
            // Swap layer
            SerializedObject so = new SerializedObject(go);
            var layerProp = so.FindProperty("m_Layer");
            int oldLayer = layerProp.intValue;
            int transformedLayer = TransformLayer(oldLayer, true);
            if (transformedLayer != oldLayer)
            {
                layerProp.intValue = transformedLayer;
                so.ApplyModifiedPropertiesWithoutUndo();
                ++m_ObjectCount;
            }

            // Process Components
            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; ++i)
            {
                if (ProcessSerializedObject(new SerializedObject(components[i])))
                    ++m_ComponentCount;
            }
        }

        void ProcessPrefabModifications(GameObject go)
        {
            var mods = PrefabUtility.GetPropertyModifications(go);
            if (mods == null)
                return;

            bool found = false;
            foreach (var mod in mods)
            {
                SerializedObject so = new SerializedObject(mod.target);
                var itr = so.GetIterator();
                string prev = string.Empty;
                while (itr.Next(true))
                {
                    if (itr.propertyPath == mod.propertyPath)
                    {
                        if (so.targetObject.GetType() == typeof(GameObject))
                        {
                            if (itr.name == "m_Layer")
                            {
                                Debug.Log("Found modified object layer on object: " + so.targetObject.name + ", type: " + itr.type);
                                int oldLayer = itr.intValue;
                                int transformedLayer = TransformLayer(oldLayer, true);
                                if (transformedLayer != oldLayer)
                                {
                                    found = true;
                                    mod.value = transformedLayer.ToString();
                                }
                            }
                            else
                            {
                                if (prev == "LayerMask")
                                {
                                    found = true;
                                    Debug.Log("Found modified LayerMask property: " + itr.propertyPath);
                                    int oldMask = itr.intValue;
                                    int transformedMask = TransformLayer(oldMask, true);
                                    if (transformedMask != oldMask)
                                    {
                                        found = true;
                                        mod.value = transformedMask.ToString();
                                    }
                                }
                            }
                            so.ApplyModifiedProperties();
                            break;
                        }
                        prev = itr.type;
                    }
                }

                if (found)
                    PrefabUtility.SetPropertyModifications(go, mods);
            }
        }

#else
        void ProcessGameObject (GameObject go, bool inScene)
        {
            try
            {
                if (inScene && PrefabUtility.GetPrefabObject(go) != null)
                    return;

                // Process children
                Transform t = go.transform;
                int childCount = t.childCount;
                for (int i = 0; i < childCount; ++i)
                    ProcessGameObject(t.GetChild(i).gameObject, inScene);

                // Swap layer
                SerializedObject so = new SerializedObject(go);
                var layerProp = so.FindProperty("m_Layer");
                int oldLayer = layerProp.intValue;
                int transformedLayer = TransformLayer(oldLayer, true);
                if (transformedLayer != oldLayer)
                {
                    layerProp.intValue = transformedLayer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    ++m_ObjectCount;
                }

                // Process Components
                Component[] components = go.GetComponents<Component>();
                for (int i = 0; i < components.Length; ++i)
                {
                    if (ProcessSerializedObject(new SerializedObject(components[i])))
                        ++m_ComponentCount;
                }
            }
            catch (Exception e)
            {
                m_Errors.Add(string.Format("Encountered error processing GameObject: \"{0}\", message: {1}", go.name, e.Message));
            }
        }
#endif
            
        bool ProcessSerializedObject (SerializedObject so)
        {
            try
            {
                int oldMaskCount = m_LayerMaskCount;

                // Get property iterator
                SerializedProperty itr = so.GetIterator();
                if (itr != null)
                {
                    // Iterate through properties
                    bool complete = false;
                    while (!complete)
                    {
                        // Only process LayerMask properties
                        if (itr.type == "LayerMask")
                        {
                            int old = itr.intValue;
                            int transformed = TransformMask(old);

                            // Record modifications
                            if (old != transformed)
                            {
                                itr.intValue = transformed;
                                ++m_LayerMaskCount;
                            }
                        }

                        complete = !itr.Next(true);
                    }
                }

                // Apply changes if there are any
                if (m_LayerMaskCount > oldMaskCount)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return true;
                }
            }
            catch (Exception e)
            {
                m_Errors.Add(string.Format("Encountered error processing SerializedObject asset: \"{0}\", message: {1}", so.targetObject.name, e.Message));
            }

            return false;
        }

        int TransformLayer (int old, bool redirected)
        {
            if (redirected)
                return m_IndexSwapsRedirected[old];
            else
                return m_IndexSwaps[old];
        }

        int TransformMask (int old)
        {
            int result = 0;

            // Iterate through each old layer
            for (int i = 0; i < 32; ++i)
            {
                // Get old flag for layer
                int flag = (old >> i) & 1;
                // Assign flag to new layer
                result |= flag << TransformLayer(i, true);
            }

            return result;
        }

        uint TransformMatrix (uint old)
        {
            uint result = 0;

            // Iterate through each old layer
            for (int i = 0; i < 32; ++i)
            {
                // Get old flag for layer
                uint flag = (old >> i) & 1;
                // Assign flag to new layer
                result |= flag << TransformLayer(i, false);
            }

            return result;
        }

        void CreateMap ()
        {
            // Create map scriptable object
            var map = CreateInstance<LayerMap>();

            // Get the map serialied property
            var mapSO = new SerializedObject(map);
            var mapSP = mapSO.FindProperty("m_Map");

            // Build the layer map
            mapSP.arraySize = 32;
            for (int i = 0; i < 32; ++i)
            mapSP.GetArrayElementAtIndex(i).intValue = m_IndexSwapsRedirected[i];

            // Save the map asset
            mapSO.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(map, AssetDatabase.GenerateUniqueAssetPath("Assets/LayerMap.asset"));
            AssetDatabase.SaveAssets();
        }


        void GetLayerCollisionMatrix ()
        {
            m_PhysicsMasks = null;

            try
            {
                // Get dynamics manager asset
                var dynamicsManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
                if (dynamicsManager == null)
                    return;

                // Get collision matrix property
                var matrixProp = dynamicsManager.FindProperty("m_LayerCollisionMatrix");
                if (matrixProp == null)
                    return;

                // Get layer masks
                m_PhysicsMasks = new uint[32];
                for (int i = 0; i < 32; ++i)
                    m_PhysicsMasks[i] = (uint)matrixProp.GetArrayElementAtIndex(i).longValue;

                // Fix layer masks by setting empty layers to everything and cross-referencing
                for (int i = 0; i < 32; ++i)
                {
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                        m_PhysicsMasks[i] = uint.MaxValue;
                }
                for (int i = 0; i < 32; ++i)
                    for (int j = 0; j < 32; ++j)
                    {
                        if (i == j)
                            continue;

                        // Cross reference here
                        uint referenced = (m_PhysicsMasks[j] >> i) & 1;
                        m_PhysicsMasks[i] |= referenced << j;
                    }

                // Print out binary for checking against
                //string total = "Old Layer Collision Matrix:\n";
                //for (int i = 0; i < 32; ++i)
                //    total += string.Format("{0}{1:D2}: {2} \n", LayerMask.LayerToName(i).PadRight(16, ' '), i, Convert.ToString(m_PhysicsMasks[i], 2).PadLeft(32, '0'));
                //Debug.Log(total);
            }
            catch (Exception e)
            {
                m_Errors.Add("Failed to read physics layer collision matrix. Exception when updating settings: " + e.Message);
                m_PhysicsMasks = null;
            }
        }

        void ProcessLayerCollisionMatrix ()
        {
            if (m_PhysicsMasks == null)
                return;
            
            try
            {
                // Get dynamics manager asset
                var dynamicsManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
                if (dynamicsManager == null)
                {
                    m_Errors.Add("Failed to process physics layer collision matrix. Asset not found: ProjectSettings/DynamicsManager.asset");
                    return;
                }

                // Get collision matrix property
                var matrixProp = dynamicsManager.FindProperty("m_LayerCollisionMatrix");
                if (matrixProp == null)
                {
                    m_Errors.Add("Failed to process physics layer collision matrix. Matrix property not found in dynamics manager asset.");
                    return;
                }

                // Process layer masks
                for (int i = 0; i < 32; ++i)
                {
                    uint oldLayerMask = m_PhysicsMasks[i];
                    uint newLayerMask = TransformMatrix(oldLayerMask);

                    // Apply and record onlly if changed
                    matrixProp.GetArrayElementAtIndex(TransformLayer(i, false)).longValue = newLayerMask;
                }

                // Print out binary for checking against
                //string total = "New Layer Collision Matrix:\n";
                //for (int i = 0; i < 32; ++i)
                //    total += string.Format("{0}{1:D2}: {2} \n", LayerMask.LayerToName(i).PadRight(16, ' '), i, Convert.ToString(matrixProp.GetArrayElementAtIndex(i).intValue, 2).PadLeft(32, '0'));
                //Debug.Log(total);

                // Apply modifications
                dynamicsManager.ApplyModifiedPropertiesWithoutUndo();

                m_PhysicsMatrixCompleted = true;
            }
            catch (Exception e)
            {
                m_Errors.Add("Failed to process physics layer collision matrix. Exception when updating settings: " + e.Message);
            }
        }

        void Get2DLayerCollisionMatrix()
        {
            m_Physics2DMasks = null;

            try
            {
                // Get dynamics manager asset
                var dynamicsManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset")[0]);
                if (dynamicsManager == null)
                    return;

                // Get collision matrix property
                var matrixProp = dynamicsManager.FindProperty("m_LayerCollisionMatrix");
                if (matrixProp == null)
                    return;

                // Get layer masks
                m_Physics2DMasks = new uint[32];
                for (int i = 0; i < 32; ++i)
                    m_Physics2DMasks[i] = (uint)matrixProp.GetArrayElementAtIndex(i).longValue;
                
                // Fix layer masks by setting empty layers to everything and cross-referencing
                for (int i = 0; i < 32; ++i)
                {
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                        m_Physics2DMasks[i] = uint.MaxValue;
                }
                for (int i = 0; i < 32; ++i)
                    for (int j = 0; j < 32; ++j)
                    {
                        if (i == j)
                            continue;

                        // Cross reference here
                        uint referenced = (m_Physics2DMasks[j] >> i) & 1;
                        m_Physics2DMasks[i] |= referenced << j;
                    }
            }
            catch (Exception e)
            {
                m_Errors.Add("Failed to read physics 2D layer collision matrix. Exception when updating settings: " + e.Message);
                m_Physics2DMasks = null;
            }
        }

        void Process2DLayerCollisionMatrix()
        {
            if (m_Physics2DMasks == null)
                return;

            try
            {
                // Get dynamics manager asset
                var dynamicsManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset")[0]);
                if (dynamicsManager == null)
                {
                    m_Errors.Add("Failed to process physics 2D layer collision matrix. Asset not found: ProjectSettings/Physics2DSettings.asset");
                    return;
                }

                // Get collision matrix property
                var matrixProp = dynamicsManager.FindProperty("m_LayerCollisionMatrix");
                if (matrixProp == null)
                {
                    m_Errors.Add("Failed to process physics 2D layer collision matrix. Matrix property not found in dynamics manager asset.");
                    return;
                }

                // Process layer masks
                for (int i = 0; i < 32; ++i)
                {
                    uint oldLayerMask = m_Physics2DMasks[i];
                    uint newLayerMask = TransformMatrix(oldLayerMask);

                    // Apply and record onlly if changed
                    matrixProp.GetArrayElementAtIndex(TransformLayer(i, false)).longValue = newLayerMask;
                }

                // Apply modifications
                dynamicsManager.ApplyModifiedPropertiesWithoutUndo();

                m_Physics2DMatrixCompleted = true;
            }
            catch (Exception e)
            {
                m_Errors.Add("Failed to process physics 2D layer collision matrix. Exception when updating settings: " + e.Message);
            }
        }

        void BuildReport ()
        {
            m_CompletionReport = string.Format(
                "Layer Modification Completed\n\n- Modified tags and layers settings.\n- {0}\n- {1}\n- {2}\n- {3}\n- Errors encountered: {4}.",
                GetCollisionMatrixReport(),
                Get2DCollisionMatrixReport(),
                GetObjectsReport(),
                GetMasksReport(),
                m_Errors.Count
                );
        }

        string GetCollisionMatrixReport ()
        {
            if (m_PhysicsMatrixCompleted)
                return "Physics layer collision matrix modifications succeeded.";
            else
                return "Physics layer collision matrix modifications failed with errors.";
        }

        string Get2DCollisionMatrixReport()
        {
            if (m_Physics2DMatrixCompleted)
                return "Physics 2D layer collision matrix modifications succeeded.";
            else
                return "Physics 2D layer collision matrix modifications failed with errors.";
        }

        string GetObjectsReport ()
        {
            // Get string dependent on numbers of prefabs and scene objects
            if (m_SceneCount > 0 && m_PrefabCount > 0)
                return string.Format("Modified layer property for {0} GameObjects across {1} scenes and {2} prefabs.", m_ObjectCount, m_SceneCount, m_PrefabCount);

            if (m_SceneCount > 0)
                return string.Format("Modified layer property for {0} GameObjects across {1} scenes.", m_ObjectCount, m_SceneCount);

            if (m_PrefabCount > 0)
                return string.Format("Modified layer property for {0} GameObjects across {1} prefabs.", m_ObjectCount, m_PrefabCount);

            // Objects that didn't belong to a scene or prefab (this should never be reached)
            if (m_ObjectCount > 0)
                return string.Format("Modified layer property for {0} GameObjects.", m_ObjectCount);

            // No changes to game objects
            return "No GameObject layers affected by changes.";
        }

        string GetMasksReport ()
        {
            // Get string dependent on numbers of game object components and scriptable object assets
            if (m_ComponentCount > 0 && m_AssetCount > 0)
                return string.Format("Modified {0} LayerMask properties on {1} components and {2} scriptable object assets.", m_LayerMaskCount, m_ComponentCount, m_AssetCount);

            if (m_ComponentCount > 0)
                return string.Format("Modified {0} LayerMask properties on {1} components.", m_LayerMaskCount, m_ComponentCount);

            if (m_AssetCount > 0)
                return string.Format("Modified {0} LayerMask properties on {1} scriptable object assets.", m_LayerMaskCount, m_AssetCount);

            // Properties that did not belong to a scriptable object or component (this should never be reached)
            if (m_LayerMaskCount > 0)
                return string.Format("Modified {0} LayerMask properties.", m_LayerMaskCount);

            // No layermask properties found
            return "No LayerMask properties found on components or scriptable object assets.";
        }
    }
}