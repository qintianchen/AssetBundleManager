# AssetBundleManager

Manager asset loading and unloading in AssetDataBase/AssetBundle mode.

There are some hidden rules in this manager:
1. contents' names must be lowercase. The reason for that is that some version controlling softwares are not case-sensitive.
2. Only contents lied in specified directories will be packed as assetbundle and be loaded as well.
3. One directory path coresponding to one assetbundle name and one assetbundle, so that we don't have to take too much energy to think about how to bundle the contents —— folder path tells you all. 
4. Please make sure that each file has a unique name in global so that the manager dont have to consider the extension name of the asset.

## Usage:

1. Define your asset directories in `AssetManager.assetDirs`. The assets not included in these directories will neither be packed as asset bundle nor be loaded.
2. Also define `AssetManager.residentAssetDirs` to make some assetbundles resident in memory.
3. Build AssetBundle from ***Build/Build AssetBundles***。

example:
```c#
using System.Collections;
using System.Collections.Generic;
using QTC;
using UnityEngine;

public class GameMain_AssetManager : MonoBehaviour
{
    IEnumerator Start()
    {
        Application.targetFrameRate = 30;
        yield return AssetManager.Instance.Init();

        // Load a GameObject called prefab_cube, without extensionName
        AssetManager.Instance.LoadAssetAsync<GameObject>("prefab_cube", this, go =>
        {
            var newGo = Instantiate(go);
            newGo.name = go.name;
        });
        
        // Unload all unused assetBundle
        AssetManager.Instance.UnloadAllUnusedAssetBundle();
        
        // print all loaded assetBundle information
        Debug.Log($"Loaded AssetBundle: {AssetManager.Instance.GetLoadedAssetBundlesInfo()}");
    }
}
```