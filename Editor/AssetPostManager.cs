using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// 资产导入的规则：
// 1. Content 下资源一律小写，包括扩展名。原因是很多版本管理软件对大小写支持不好，比如GitHub，P4。但是又不能完全要求脚本也用小写+蛇形命名，故仅对美术资源做限定。

namespace QTC
{
	public enum AssetType
	{
		Prefab,
		Unknown
	}

	// 资产导入的后处理
	public class AssetPostManager : AssetPostprocessor
	{
		static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			for (var i = 0; i < importedAssets.Length; i++)
			{
				OnAssetNewToFolder(importedAssets[i]);
			}

			for (int i = 0; i < movedAssets.Length; i++)
			{
				OnAssetNewToFolder(movedAssets[i]);
			}
		}

		static void OnAssetNewToFolder(string assetPath)
		{
			var fileName = Path.GetFileName(assetPath);
			if (assetPath.Contains("Assets/Content") && !string.Equals(fileName, fileName.ToLower()))
			{
				Debug.LogError($"Content目录（美术向资源）都要小写：{assetPath}");

				var fileDir = Path.GetDirectoryName(assetPath);
				File.Move(assetPath, fileDir + $"/{fileName.ToLower()}");
				AssetDatabase.Refresh();
			}
		}

		static AssetType TryGetTypeFromAssetPath(string assetPath)
		{
			try
			{
				var ext = Path.GetExtension(assetPath);
				return ext switch
				{
					".prefab" => AssetType.Prefab,
					_ => AssetType.Unknown
				};
			}
			catch (Exception)
			{
				return AssetType.Unknown;
			}
		}
	}
}