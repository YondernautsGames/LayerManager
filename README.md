# LayerManager

A simple tool for reordering, merging and modifying unity layers. Updates game objects, components and assets to match.

## The Editor

To open the layer manager, select **"Tools/Yondernauts/Layer Manager"**. This will open the editor in a new window that looks like the image below:

![Editor](/Images/editor.jpg?raw=true)

The layer manager supports three operations from the main window:
+ Rename
+ Reorder
+ Redirect

Once a change has been made, the **Apply Layer Modifications** button will be enabled. Pressing this will apply the changes to the project settings and then scan through all scenes and assets. The manager will change the layer dropdown on all scene and prefab objects to match the modifications, as well as modifying all **LayerMask** inspector properties on all game object components and scriptable object assets.

The **Reset Layer Modifications** button is always enabled. If you modify the layers outside of the layer manager then pressing this button will update the manager to reflect any changes.

### Renaming Layers

To rename a layer, simply select the text field at for the relevant layer and type a new name. The original name will be displayed below this so that you don't lose track of the layer edits.

If the new name is not valid, then the layer name text field will turn red. This can happen for one of 2 reasons:
1. The name clashes with another layer. In this case both layers will turn red. This can be fixed by renaming either of the layers.
2. The layer had a name before but the name is now blank. This is a problem as any objects or properties that were set to the old layer will now be pointing to nothing. This can be fixed by adding a new name to the layer **or** by redirecting the layer.

### Reordering Layers

You can reorder layers by dragging them to their new order in the list. The list entry will display both the new index and the original index for reference.

### Redirecting Layers

Redirecting a layer modify any objects or properties that reference this layer to point at the redirect layer instead. This essentially merges the layers together. If the redirect layer is also redirected later then this will ripple through to correct all layers.

For example, if A redirects to B and B is redirected to point at C, then both A and B will now be set to redirect to C.

## Physics Layer Collision Matrix

The physics and physics 2D layer collision matrices define whether objects on different layers can collide with each other. These can be accessed from **"Edit/Project Settings/Physics"** and **"Edit/Project Settings/Physics 2D"** and look like this:

![Layer Collision Matrix](/images/collision-matrix.jpg)

The layer manager will update both of these collision matrices to reflect renamed and reordered layers, but **not** redirected layers. This is because it is impossible to know the desired result in all but the simplest of cases. If you redirect layers and make use of the physics layer collision matrix then you will need to tweak this manually after applying your layer modifications.

## Saving The Layer Map

Once the modifications have been completed you will have the option of saving a layer map asset. In the inspector this asset has 2 sections:

![Layer Map](/images/layer-map.jpg)

The **Transform Layer Index** section will give you the new layer index when an old layer index is entered in the left hand text field.

The **Transform Layer Mask** section can be used to manually convert layer mask values from the old layout to the new layout.

## Handling Errors

If the layer manager encounters any errors when processing changes, the count will be given in the completion report. You can then choose to output the errors to the console, or email a copy to support@yondernauts.games