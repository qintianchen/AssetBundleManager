using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

// 在这个管理器中，我们有以下命名约定：
// fileFullName =         "Assets/Content/Environment/Prefabs/prefab_cube.prefab"       是指带后缀的文件全路径
// fileName =             "prefab_cube.prefab"                                          是指带后缀的文件名
// filePath =             "Assets/Content/Environment/Prefabs"                          是指文件所在的目录路径
// assetName =            "prefab_cube"                                                 是指资源名，也同时是不带后缀的文件名
// assetFullName =	      "Assets/Content/Environment/Prefabs/prefab_cube.prefab"       与fileFullName，带后缀的文件全路径
// assetBundleName =	  "content_environment_prefabs"                                 AssetBundle名称，是经过转换的filePath
// assetBundleFullName =  "Assets/StreamingAssets/content_environment_prefabs"          AssetBundle全路径名称

// 打包策略里面，我们规定一个目录会被打成一个assetBundle，故目录下不能存在同名的资源（即使不同扩展名）
// assetBundle的名字由目录的路径唯一确定
// 上层只需要传文件名，即可加载出资源，由上层业务决定资源具体是什么类型。比如 icon.png 可以是 Sprite，也可以是 Texture2D

namespace QTC
{
	// 基于AssetBundle的资产管理
	public class AssetManager : SingleTon<AssetManager>
	{
		#region 属性

		public enum AssetMode
		{
			AssetBundle,
			AssetDataBase
		}

		public List<string> assetDirs = new List<string> // 定义哪些目录的资产将会被打成AssetBundle作为合法的资产加载目录
		{
			"Assets/Content/Environment",
			"Assets/Content/Scenes",
			"Assets/Content/Shaders",
		};

		public List<string> residentAssetDirs = new List<string> // 定义哪些目录下的资产将会常驻内存，不会被卸载掉
		{
		
		};

		private string m_ASSETBUNDLE_DIR;
		public string ASSETBUNDLE_DIR // AssetBundle的生成目录
		{
			get
			{
				if (string.IsNullOrEmpty(m_ASSETBUNDLE_DIR))
					m_ASSETBUNDLE_DIR = Path.Combine(Application.streamingAssetsPath, "AssetBundles").Replace("\\", "/");
				return m_ASSETBUNDLE_DIR;
			}
		}

		private AssetMode assetModeInEditor = AssetMode.AssetBundle; // 指示编辑器使用AssetBundleMode，否则默认走AssetDataBase机制
		public Dictionary<string, string> assetName_assetFullName;
		public Dictionary<string, string> assetName_assetBundleName;
		public Dictionary<string, string> assetBundleName_assetBundleFullName;
		public Dictionary<string, bool> assetBundleName_resident;

		private Queue<AssetBundleWrap> assetBundlesToLoad; // 准备被加载的AssetBundle队列
		private Dictionary<string, AssetBundleWrap> assetBundleName_loadingAssetBundle; // 正在加载的AssetBundle
		private Dictionary<string, AssetBundleWrap> assetBundleName_loadedAssetBundle; // 加载完成的AssetBundle
		private Dictionary<string, AssetBundleWrap> assetBundleName_assetBundleToRemove; // 准备卸载的AssetBundle
		private Dictionary<string, AssetWrap> assetName_loadingAsset; // 正在加载的Asset

		private AssetBundleManifest assetBundleManifest; // 资源的依赖关系

		public int MAX_ASSETBUNDLE_LOAD_PER_FRAME = 32;

		public bool isInited = false; // 指示资源管理器是否初始化过
		public Action onInit;

		#endregion

		public IEnumerator Init()
		{
			// 初始化一系列名字的映射关系，这个映射关系体现了当前工程的打包策略
			yield return InitAssetNameMap();
			assetBundlesToLoad = new Queue<AssetBundleWrap>();
			assetBundleName_loadingAssetBundle = new Dictionary<string, AssetBundleWrap>();
			assetBundleName_loadedAssetBundle = new Dictionary<string, AssetBundleWrap>();
			assetBundleName_assetBundleToRemove = new Dictionary<string, AssetBundleWrap>();
			assetName_loadingAsset = new Dictionary<string, AssetWrap>();
			AssetTicker.Instance.onUpdate += Update;

			var assetBundle = AssetBundle.LoadFromFile(assetBundleName_assetBundleFullName[assetName_assetBundleName["AssetBundleManifest"]]);
			assetBundleManifest = assetBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");

			isInited = true;
			onInit?.Invoke();
		}

		#region private

		private IEnumerator InitAssetNameMap()
		{
			assetName_assetFullName = new Dictionary<string, string>();
			assetName_assetBundleName = new Dictionary<string, string>();
			assetBundleName_assetBundleFullName = new Dictionary<string, string>();
			assetBundleName_resident = new Dictionary<string, bool>();
#if UNITY_EDITOR
			if (assetModeInEditor == AssetMode.AssetBundle)
			{
				yield return InitAssetNameMapInAssetBundleMode();
			}
			else
			{
				InitAssetNameMapInAssetDataBaseMode();
			}
#else
		yield return InitAssetNameMapInAssetBundleMode();
#endif

			Debug.Log($"assetName_assetFullName = {JsonConvert.SerializeObject(assetName_assetFullName, Formatting.Indented)}");
			Debug.Log($"assetName_assetBundleFullName = {JsonConvert.SerializeObject(assetName_assetBundleName, Formatting.Indented)}");
		}

		private IEnumerator InitAssetNameMapInAssetBundleMode()
		{
			using var request = UnityWebRequest.Get(Path.Combine(ASSETBUNDLE_DIR, "fileName_dirName.csv"));
			yield return request.SendWebRequest();
			if (request.result == UnityWebRequest.Result.Success)
			{
				var text = request.downloadHandler.text;
				var lines = text.Split('\n');
				foreach (var line in lines)
				{
					var newLine = line.Trim();
					var cells = newLine.Split(',');
					if (cells.Length == 2 && !string.IsNullOrEmpty(cells[0]) && !string.IsNullOrEmpty(cells[1]))
					{
						var assetName = Path.GetFileNameWithoutExtension(cells[0]);
						var assetBundleName = AssetHelper.DirectoryPathToAssetBundleName(cells[1]);
						assetName_assetFullName[assetName] = Path.Combine(cells[1], cells[0]).Replace("\\", "/");
						assetName_assetBundleName[assetName] = assetBundleName;
						assetBundleName_assetBundleFullName[assetBundleName] = Path.Combine(ASSETBUNDLE_DIR, assetBundleName).Replace("\\", "/");
					}
				}
			}

			var dirs = ASSETBUNDLE_DIR.Split('/');
			var manifestAssetBundleName = dirs[dirs.Length - 1];
			assetName_assetFullName["AssetBundleManifest"] = "AssetBundleManifest";
			assetName_assetBundleName["AssetBundleManifest"] = manifestAssetBundleName;
			assetBundleName_assetBundleFullName[manifestAssetBundleName] = Path.Combine(ASSETBUNDLE_DIR, manifestAssetBundleName).Replace("\\", "/");
		}

		private void InitAssetNameMapInAssetDataBaseMode()
		{
			foreach (var assetDir in assetDirs)
			{
				var files = Directory.GetFiles(assetDir);
				foreach (var file in files)
				{
					var ext = Path.GetExtension(file);
					var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
					if (ext.Equals(".meta"))
					{
						continue;
					}

					assetName_assetFullName[fileNameWithoutExtension] = file.Replace("\\", "/");
				}
			}

			foreach (var assetDir in residentAssetDirs)
			{
				var assetBundleName = AssetHelper.DirectoryPathToAssetBundleName(assetDir);
				assetBundleName_resident[assetBundleName] = true;
			}
		}

		public AssetWrap LoadAssetAsync<T>(string assetName, Object objRef, Action<T> onLoaded) where T: Object
		{
			if (!assetName_assetBundleName.TryGetValue(assetName, out var assetBundleName) || !assetName_assetFullName.TryGetValue(assetName, out var assetFullName))
			{
				Debug.LogError($"Failed to find asset, assetName = {assetName}");
				return null;
			}
			
			// 资源已经在加载中啦，别催啦
			if (assetName_loadingAsset.TryGetValue(assetName, out var assetWrap))
			{
				assetWrap.onLoaded += obj =>
				{
					var asset = obj as T;
					if (asset == null)
					{
						Debug.LogError($"Fail to convert asset({asset.name}) to type({typeof(T).Name})");
						return;
					}

					onLoaded(asset);
				};
				return assetWrap;
			}
			
			// 资源没有在加载中，但是资源所在AssetBundle加载出来了
			if (assetBundleName_loadedAssetBundle.TryGetValue(assetBundleName, out var assetBundleWrap))
			{
				assetWrap = assetBundleWrap.LoadAssetAsync<T>(assetName, assetFullName, objRef, onLoaded);
				assetName_loadingAsset[assetName] = assetWrap;
				return assetWrap;
			}
		
			// 资源没有在加载中，资源所在的AssetBundle也在加载中，别催啦
			if (assetBundleName_loadingAssetBundle.TryGetValue(assetBundleName, out var assetBundleWrap2))
			{
				assetBundleWrap2.onLoaded += assetBundle =>
				{
					assetWrap = assetBundleWrap2.LoadAssetAsync<T>(assetName, assetFullName, objRef, onLoaded);
					assetName_loadingAsset[assetName] = assetWrap;
				};
				return null;
			}

			// 资源没在加载，资源所在的AssetBundle也没在加载
			LoadAssetBundleAsync(assetBundleName, assetBundleWrap =>
			{
				assetWrap = assetBundleWrap.LoadAssetAsync(assetName, assetFullName, objRef, onLoaded);
				assetName_loadingAsset[assetName] = assetWrap;
			});

			return null;
		}
		
		private AssetBundleWrap LoadAssetBundleAsync(string assetBundleName, Action<AssetBundleWrap> onLoaded)
		{
			if (!assetBundleName_assetBundleFullName.TryGetValue(assetBundleName, out var assetBundleFullName))
			{
				Debug.LogError($"Fail to find assetBundle, assetBundleName ={assetBundleName}");
				return null;
			}

			if (assetBundleName_loadedAssetBundle.TryGetValue(assetBundleName, out var assetBundleWrap))
			{
				return assetBundleWrap;
			}
			
			if (assetBundleName_loadingAssetBundle.TryGetValue(assetBundleName, out var assetBundleWrap2))
			{
				return assetBundleWrap2;
			}
			
			// 生成AssetBundleWrap
			var wrap = new AssetBundleWrap(assetBundleName, assetBundleFullName, onLoaded);
			
			// 先添加依赖到队列
			string[] assetBundleNames = assetBundleManifest.GetDirectDependencies(wrap.assetBundleName);
			foreach (var assetBundleName2 in assetBundleNames)
			{
				var wrap2 = LoadAssetBundleAsync(assetBundleName2, null);
				wrap.deps.Add(wrap2);
				wrap2.abRefs.Add(wrap);
			}
			
			// 再把自己添加进队列
			assetBundlesToLoad.Enqueue(wrap);
			return wrap;
		}

		private void Update()
		{
			// 从预加载队列中取出指定数量的AssetBundle进行加载
			for (int i = 0; i < MAX_ASSETBUNDLE_LOAD_PER_FRAME; i++)
			{
				if (assetBundlesToLoad.Count == 0)
					break;
				
				AssetBundleWrap wrap = assetBundlesToLoad.Dequeue();
				StartLoadAssetBundleWrap(wrap);
			}
			
			// 遍历正在加载的列表
			List<string> keys = new List<string>();
			foreach (var keyValuePair in assetBundleName_loadingAssetBundle)
			{
				var wrap = keyValuePair.Value;
				if (wrap.isDone)
				{
					keys.Add(keyValuePair.Key);
				}
			}
			foreach (var key in keys)
			{
				var wrap = assetBundleName_loadingAssetBundle[key];
				assetBundleName_loadedAssetBundle[wrap.assetBundleName] = wrap;
				wrap.onLoaded?.Invoke(wrap);
				assetBundleName_loadingAssetBundle.Remove(key);
			}
			keys.Clear();
			
			foreach (var keyValuePair in assetName_loadingAsset)
			{
				var assetWrap = keyValuePair.Value;
				if (assetWrap.isDone)
				{
					keys.Add(assetWrap.assetName);
				}
			}
			foreach (var key in keys)
			{
				var assetWrap = assetName_loadingAsset[key];
				assetWrap.onLoaded?.Invoke(assetWrap.request.asset);
				assetName_loadingAsset.Remove(key);
			}
			keys.Clear();
		}

		private void StartLoadAssetBundleWrap(AssetBundleWrap wrap)
		{
			if (wrap == null)
			{
				return;
			}
			
			if (wrap.isDone)
			{
				Debug.Log($"完成加载AB:{wrap.request.assetBundle.name}");
				wrap.onLoaded?.Invoke(wrap);
				assetBundleName_loadedAssetBundle[wrap.assetBundleName] = wrap;
			}
			else
			{
				wrap.Load();
				if (wrap.isDone)
				{
					Debug.Log($"完成加载AB:{wrap.request.assetBundle.name}");
					wrap.onLoaded?.Invoke(wrap);
					assetBundleName_loadedAssetBundle[wrap.assetBundleName] = wrap;
				}
				else
				{
					assetBundleName_loadingAssetBundle[wrap.assetBundleName] = wrap;
				}
			}
		}

		/// 卸载所有未被引用的AssetBundle，会绕过 Resident 列表
		public void UnloadAllUnusedAssetBundle()
		{
			List<string> keys = new List<string>();
			foreach (var keyValuePair in assetBundleName_loadedAssetBundle)
			{
				var wrap = keyValuePair.Value;
				if (!assetBundleName_resident.ContainsKey(wrap.assetBundleName) && wrap.refCount == 0)
				{
					keys.Add(wrap.assetBundleName);
					assetBundleName_assetBundleToRemove[wrap.assetBundleName] = wrap;
				}
			}
			
			foreach (var key in keys)
			{
				assetBundleName_loadedAssetBundle.Remove(key);
			}
			
			// 总感觉以后要对ToRemoe做什么事情，现在就直接立即清空好了
			foreach (var keyValuePair in assetBundleName_assetBundleToRemove)
			{
				keyValuePair.Value.UnLoad();
			}
			
			assetBundleName_assetBundleToRemove.Clear();
		}

		// 调试用，输出已经加载了AssetBundle信息，Json形式
		public string GetLoadedAssetBundlesInfo()
		{
			List<AssetBundleWrapInfo> infos = new List<AssetBundleWrapInfo>();
			foreach (var keyValuePair in assetBundleName_loadedAssetBundle)
			{
				var wrap = keyValuePair.Value;
				var info = new AssetBundleWrapInfo();
				info.assetBundleName = wrap.assetBundleName;
				info.deps = new List<string>();
				info.abRefs = new List<string>();
				info.objRefs = new List<string>();
				
				foreach (var dep in wrap.deps)
				{
					info.deps.Add(dep.assetBundleName);
				}
				
				foreach (var abRef in wrap.abRefs)
				{
					info.abRefs.Add(abRef.assetBundleName);
				}
				
				foreach (var objRef in wrap.objRefs)
				{
					info.objRefs.Add(objRef.name);
				}
				
				infos.Add(info);
			}

			return JsonConvert.SerializeObject(infos, Formatting.Indented);
		}
		
		#endregion
	}
}