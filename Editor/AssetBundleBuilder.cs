using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace QTC.Editor
{
    public class AssetBundleBuilder : UnityEditor.Editor
    {
        [MenuItem("Build/Build AssetBundles")]
        private static void BuildAssetBundles()
        {
            // 构造 <目录，目录下所有的文件> 映射，<文件名, 目录名> 映射。前者用于打包，后者用于运行时查询。
            Dictionary<string, List<string>> filePath_fileFullName = new Dictionary<string, List<string>>();
            Dictionary<string, string> fileName_filePath = new Dictionary<string, string>();
            foreach (var assetDir in AssetManager.Instance.assetDirs)
            {
                AddFiles(assetDir, ref filePath_fileFullName, ref fileName_filePath);
            }

            Debug.Log(
                $"filePath_fileFullName = {JsonConvert.SerializeObject(filePath_fileFullName, Formatting.Indented)}");

            // 通过上面映射构造 AssetBundleBuild 列表，其中每个目录对应一个AB，AB 里面包含目录下的所有文件
            List<AssetBundleBuild> builds = new List<AssetBundleBuild>();
            foreach (var keyValuePair in filePath_fileFullName)
            {
                if (keyValuePair.Value.Count == 0)
                    continue;

                AssetBundleBuild build = new AssetBundleBuild();
                var assetBundleNames = new List<string>();
                foreach (var s in keyValuePair.Value)
                {
                    assetBundleNames.Add(s);
                }

                build.assetNames = assetBundleNames.ToArray();
                string assetBundleName = AssetHelper.DirectoryPathToAssetBundleName(keyValuePair.Key);
                build.assetBundleName = assetBundleName;

                builds.Add(build);
            }

            // 开始构建 AB
            if (Directory.Exists(AssetManager.Instance.ASSETBUNDLE_DIR))
            {
                Directory.Delete(AssetManager.Instance.ASSETBUNDLE_DIR, true);
            }

            Directory.CreateDirectory(AssetManager.Instance.ASSETBUNDLE_DIR);

            BuildPipeline.BuildAssetBundles(AssetManager.Instance.ASSETBUNDLE_DIR, builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);

            StringBuilder sb = new StringBuilder();
            foreach (var keyValuePair in fileName_filePath)
            {
                sb.Append(keyValuePair.Key).Append(",").Append(keyValuePair.Value).Append("\n");
            }

            File.WriteAllText(AssetManager.Instance.ASSETBUNDLE_DIR + "/fileName_dirName.csv", sb.ToString());

            AssetDatabase.Refresh();
        }

        private static void AddFiles(string dir, ref Dictionary<string, List<string>> filePath_fileFullName,
            ref Dictionary<string, string> fileName_filePath)
        {
            var files = Directory.GetFiles(dir);
            var validFiles = new List<string>();
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                var fileName = Path.GetFileName(file);
                if (ext.Equals(".meta"))
                {
                    continue;
                }

                validFiles.Add(file.Replace("\\", "/"));
                fileName_filePath[fileName] = Path.GetDirectoryName(file).Replace("\\", "/").TrimStart('/');
            }

            if (validFiles.Count > 0)
                filePath_fileFullName[dir] = validFiles;

            var dirs = Directory.GetDirectories(dir);
            foreach (var s in dirs)
            {
                AddFiles(s, ref filePath_fileFullName, ref fileName_filePath);
            }
        }
    }
}