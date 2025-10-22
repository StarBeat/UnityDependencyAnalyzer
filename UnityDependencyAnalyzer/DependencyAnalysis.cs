using MemoryPack;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssetDependencyGraph
{
    public static class FileExtensionHelper
    {
        public static string GetTypeByExtension(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".a":
                case ".dll":
                case ".so":
                case ".exe":
                case ".dynlib":
                    return "Executable";
                case ".asmdef":
                case ".asmref":
                    return "UnityAssembly";
                case ".cs":
                case ".lua":
                case ".js":
                case ".ts":
                case ".java":
                case ".h":
                case ".cpp":
                case ".cxx":
                case ".mm":
                case ".py":
                case ".bat":
                case ".jar":
                case ".arr":
                case ".jslib":
                    return "SourceFile";
                case ".gradle":
                    return "MakeFile";
                case ".dat":
                case ".data":
                    return "DatFile";
                case ".mp3":
                case ".ogg":
                case ".wav":
                    return "AudioClip";
                case ".mp4":
                case ".webm":
                    return "VideoClip";
                case ".mat":
                    return "Material";
                case ".rendertexture":
                case ".dds":
                case ".exr":
                case ".hdr":
                case ".png":
                case ".jpg":
                case ".gif":
                case ".psd":
                case ".bmp":
                case ".tiff":
                case ".tga":
                case ".gradient":
                case ".spriteatlas":
                    return "Texture";
                case ".obj":
                case ".fbx":
                case ".mesh":
                    return "Mesh";
                case ".shader":
                case ".surfshader":
                case ".shadergraph":
                    return "Shader";
                case ".compute":
                    return "ComputeShader";
                case ".hlsl":
                case ".cginc":
                case ".shadersubgraph":
                    return "ShaderHeader";
                case ".otf":
                case ".ttf":
                    return "Font";
                case ".byte":
                case ".bytes":
                case ".bin":
                    return "Binary";
                case ".txt":
                case ".md":
                case ".chm":
                case ".yml":
                case ".url":
                case ".json":
                case ".json5":
                case ".xml":
                case ".uxml":
                case ".nson":
                case ".config":
                case ".pdf":
                    return "TextFile";
                case ".xlsx":
                case ".xls":
                    return "Excel";
                case ".unity":
                case ".scene":
                    return "Scene";
                case ".prefab":
                    return "Prefab";
                default:
                    return "UnknowFileType";
            }
        }


        public static bool IsPackage(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".prefab":
                case ".unity":
                    return true;
                default:
                    return false;
            }
        }

        public static bool NeedAnalyzeDepend(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".prefab":
                case ".unity":
                case ".asset":
                case ".mat":
                    return true;
                default:
                    return false;
            }
        }
        public static bool Exclude(string path) => path.EndsWith(".meta")
            || path.EndsWith(".unitypackage")
            || path.EndsWith(".preset")
            || path.EndsWith(".backup")
            || path.EndsWith(".tmp")
            || path.EndsWith(".editor")
            || path.EndsWith(".zip")
            || path.EndsWith(".scenetemplate");
    }

    public interface IDependencyAnalysis
    {
        void Analyze(string path, Dictionary<string, HashSet<string>> result);
    }

    public class FolderDependencyAnalysis : IDependencyAnalysis
    {
        public void Analyze(string path, Dictionary<string, HashSet<string>> result)
        {
            if (!result.TryGetValue(path, out var list))
            {
                result[path] = list = new();
            }

            foreach (string file in Directory.EnumerateFiles(path))
            {
                if (FileExtensionHelper.Exclude(file))
                {
                    continue;
                }

                var p = file.ToUniversalPath().ToUnityRelatePath();
                list.Add(p);
            }

            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                var p = directory.ToUniversalPath().ToUnityRelatePath();
                list.Add(p);
            }
        }
    }

    public class UnityDependencyAnalysis2 : IDependencyAnalysis
    {
        Regex guidRegex = new Regex("guid:\\s?([\\da-f]+)");
        List<string> GetDepGuidByFile(string path)
        {
            List<string> result;
            try
            {
                result = UnityFileApi.DependencyTool.GetDependencies(path);
            }
            catch (NotSupportedException)
            {
                var str = File.ReadAllText(path);
                result = new();
                var matches = guidRegex.Matches(str);
                for (int i = 0; i < matches.Count; i++)
                {
                    var guid = matches[i].Groups[1].Value;
                    if (!result.Contains(guid))
                    {
                        result.Add(guid);
                    }
                }
            }

            return result;
        }

        public void Analyze(string path, Dictionary<string, HashSet<string>> result)
        {
            if (!result.TryGetValue(path, out var list))
            {
                result[path] = list = new();
            }

            var ext = Path.GetExtension(path);
            if (FileExtensionHelper.NeedAnalyzeDepend(ext))
            {
                var dependencies = GetDepGuidByFile(path);
                for (int i = 0; i < dependencies.Count; i++)
                {
                    var dep = dependencies[i];
                    list.Add(dep);
                }
            }
        }
    }

    internal class DependencyAnalyzer
    {
        private Dictionary<Predicate<(string path, bool isDir)>, IDependencyAnalysis> dependencyAnalysisDic = new();
        private Dictionary<string, HashSet<string>> path2Dependences = new();

        private JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
        [BsonDictionaryOptions(Representation = DictionaryRepresentation.ArrayOfArrays)]
        private ConcurrentDictionary<AssetIdentify, AssetNode> assetIdentify2AssetNodeDic = new();
        private ConcurrentDictionary<string, AssetIdentify> path2Id = new();
        private ConcurrentBag<(string path, bool isDir)> allPath = new();
        private UnityLmdb unityLmdb;
        private static Regex isGuid = new Regex("^[\\da-f]{32}$");

        public DependencyAnalyzer()
        {
            unityLmdb = new UnityLmdb();
            dependencyAnalysisDic.Add(new Predicate<(string path, bool isDir)>(pi => !pi.isDir), new UnityDependencyAnalysis2());
            dependencyAnalysisDic.Add(new Predicate<(string path, bool isDir)>(pi => pi.isDir), new FolderDependencyAnalysis());
        }

        private void Visivt(string path)
        {
            path = path.ToUniversalPath();
            if (FileExtensionHelper.Exclude(path))
            {
                return;
            }
            allPath.Add((path, Directory.Exists(path)));
        }

        public static bool IsGuid(string str)
        {
            if (str.Length == 32)
            {
                return isGuid.IsMatch(str);
            }
            return false;
        }

        public (AssetIdentify id, AssetNode node) GetOrCreateFolderNode(string path)
        {
            if (!path2Id.TryGetValue(path, out var k))
            {
                if (k == null)
                {
                    k = new AssetIdentify()
                    {
                        Path = path,
                        AssetType = "Folder",
                        Guid = null,
                        Md5 = null
                    };
                    assetIdentify2AssetNodeDic[k] = new FolderNode()
                    {
                        Self = k,
                        AssetType = "Folder",
                    };
                }
                path2Id[path] = k;
            }
            return (k, assetIdentify2AssetNodeDic[k]);
        }

        public (AssetIdentify id, AssetNode node) GetOrCreateAssetNode(string path)
        {
            if (!path2Id.TryGetValue(path, out var k))
            {
                if (k == null)
                {
                    var ext = Path.GetExtension(path);
                    k = new AssetIdentify()
                    {
                        Path = path,
                        Guid = null,
                        AssetType = FileExtensionHelper.GetTypeByExtension(ext)
                        //Md5 = Utils.Md5(path)
                    };
                    if (FileExtensionHelper.IsPackage(ext))
                    {
                        assetIdentify2AssetNodeDic[k] = new PackageNode()
                        {
                            Self = k,
                            AssetType = k.AssetType,
                        };
                    }
                    else
                    {
                        assetIdentify2AssetNodeDic[k] = new AssetNode()
                        {
                            Self = k,
                            AssetType = k.AssetType,
                        };
                    }
                    path2Id[path] = k;
                }
            }

            return (k, assetIdentify2AssetNodeDic[k]);
        }

        public void ResolveGuidDatabase()
        {
            unityLmdb.ResolveGuidPath();
        }

        public async ValueTask AnalyzeMainProcess(string projectPath, string rootFolder, int processCnt = 8)
        {
            Stopwatch sw = Stopwatch.StartNew();
            sw.Start();
            Utils.TraverseDirectoryParallel(rootFolder, Visivt);
            sw.Stop();
            Console.WriteLine($"遍历目录耗时:{sw.ElapsedMilliseconds / 1000f}s");

            sw.Restart();
            var itemCnt = allPath.Count / processCnt;
            List<string> subProcessArgs = new();
            List<string> resultPaths = new();
            var allPathArray = allPath.ToArray();
            for (int i = 0; i < processCnt; i++)
            {
                int r = (itemCnt * (i + 1));
                if (r >= allPath.Count)
                {
                    r = allPath.Count;
                }
                
                var s = JsonSerializer.Serialize(allPathArray[(i * itemCnt)..r], options);
                var jsonPath = Path.Combine(Path.GetTempPath(), $"path{i}.json");
                var resulPath = Path.Combine(Path.GetTempPath(), $"result{i}.bin");
                resultPaths.Add(resulPath);
                subProcessArgs.Add($"-reference {projectPath} SubProcess {jsonPath} {resulPath}");
                File.WriteAllText(jsonPath, s);
            }

            Task[] subProcessTask = new Task[subProcessArgs.Count];
            var exe = Environment.GetCommandLineArgs()[0];
            if (exe.EndsWith(".dll"))
            {
                exe = exe.Replace(".dll", ".exe");
            }

            for (int i = 0; i < subProcessArgs.Count; i++)
            {
                int index = i;
                subProcessTask[i] = Task.Factory.StartNew(() =>
                {
                    Process p = new Process();
                    p.StartInfo = new ProcessStartInfo()
                    {
                        FileName = exe,
                        Arguments = subProcessArgs[index],
                        UseShellExecute = true,
                    };
                    p.Start();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        Console.WriteLine("Sub Process Error.");
                    }
                });
            }

            Stopwatch sw1 = Stopwatch.StartNew();
            sw1.Start();
            ResolveGuidDatabase();
            sw1.Stop();
            Console.WriteLine($"加载数据库耗时:{sw1.ElapsedMilliseconds / 1000f}s");

            Task.WaitAll(subProcessTask);
            List<Dictionary<string, HashSet<string>>> subProcessResults = new();
            foreach (var item in resultPaths)
            {
                var s = File.ReadAllBytes(item);
                subProcessResults.Add(MemoryPackSerializer.Deserialize<Dictionary<string, HashSet<string>>>(s.AsSpan())!);
            }
            sw.Stop();
            Console.WriteLine($"分析引用耗时:{sw.ElapsedMilliseconds / 1000f}s");
            sw.Restart();
            Parallel.ForEach(subProcessResults, arg => ResolveSubProcessResult(arg));
            sw.Stop();
            Console.WriteLine($"合并数据耗时:{sw.ElapsedMilliseconds / 1000f}s");
            sw.Restart();
            foreach (var item in assetIdentify2AssetNodeDic)
            {
                item.Value.DependencySet = item.Value.Dependencies.ToHashSet();
                item.Value.DependentSet = item.Value.Dependent.ToHashSet();
            }

            using var wr = File.OpenWrite(Path.Combine(UnityLmdb.ProjPath, "Library", "dependencyGraph.bin"));
            await MemoryPackSerializer.SerializeAsync(wr, assetIdentify2AssetNodeDic);
            sw.Stop();
            Console.WriteLine($"写入文件耗时:{sw.ElapsedMilliseconds / 1000f}s");

            //AssetDependencyGraphDB db = new AssetDependencyGraphDB(Environment.GetCommandLineArgs()[2], Environment.GetCommandLineArgs()[3], Environment.GetCommandLineArgs()[4]);
            //sw.Restart();
            //db.Clean();
            //Parallel.ForEach(assetIdentify2AssetNodeDic, item =>
            //{
            //    db.Insert(item.Value);
            //});
            //sw.Stop(); 
            //Console.WriteLine($"更新数据库:{sw.ElapsedMilliseconds / 1000f}s");
        }

        private void ResolveSubProcessResult(Dictionary<string, HashSet<string>> subProcessResult)
        {
            Parallel.ForEach(subProcessResult, item =>
            {
                var relPath = item.Key.ToLowerInvariant().ToUnityRelatePath();
                var fullPath = relPath.ToUnityFullPath();
                if (File.Exists(fullPath))
                {
                    var selfNode = GetOrCreateAssetNode(relPath);
                    selfNode.id.Guid = unityLmdb.GetGuidByPath(relPath);
                    foreach (var dep in item.Value)
                    {
                        var depPath = dep;
                        if (IsGuid(dep))
                        {
                            depPath = unityLmdb.GetPathByGuid(dep.ToLowerInvariant());
                            if (string.IsNullOrEmpty(depPath))
                            {
                                depPath = dep;
                            }
                        }
                        depPath = depPath.ToLowerInvariant();
                        var depNode = GetOrCreateAssetNode(depPath);
                        depNode.node.Dependent.Add(selfNode.id);
                        selfNode.node.Dependencies.Add(depNode.id);
                    }
                }
                else
                {
                    var selfNode = GetOrCreateFolderNode(relPath);
                    selfNode.id.Guid = unityLmdb.GetGuidByPath(relPath);
                    foreach (var dep in item.Value)
                    {
                        var depPath = dep.ToLowerInvariant().ToUnityRelatePath();
                        fullPath = depPath.ToUnityFullPath();
                        (AssetIdentify id, AssetNode node) depNode;

                        if (File.Exists(fullPath))
                        {
                            depNode = GetOrCreateAssetNode(depPath);
                        }
                        else 
                        {
                            depNode = GetOrCreateFolderNode(depPath);
                        }

                        depNode.node.Dependent.Add(selfNode.id);
                        selfNode.node.Dependencies.Add(depNode.id);
                    }
                }
            });
        }

        public async ValueTask AnalyzeSubProcess(string pathFile, string resultFilePath)
        {
            var s = File.ReadAllText(pathFile);
            var allPath = JsonSerializer.Deserialize<List<(string path, bool isDir)>>(s, options)!;
            if (allPath != null)
            {
                for (int i = 0; i < allPath.Count; i++)
                {
                    var path = allPath[i];
                    foreach (var item1 in dependencyAnalysisDic)
                    {
                        if (item1.Key(path))
                        {
                            item1.Value.Analyze(path.path, path2Dependences);
                        }
                    }
                }

                using var wr = File.OpenWrite(resultFilePath);
                await MemoryPackSerializer.SerializeAsync(wr, path2Dependences);
            }
        }


        public void Analyze(string rootFolder)
        {
            Stopwatch sw = Stopwatch.StartNew();
            sw.Start();
            Utils.TraverseDirectory(rootFolder, Visivt, -1);
            foreach (var item in allPath)
            {
                foreach (var item1 in dependencyAnalysisDic)
                {
                    if (item1.Key(item))
                    {
                        item1.Value.Analyze(item.path, path2Dependences);
                    }
                }
            }

            //Parallel.ForEach(allPath, (pi) =>
            //{
            //    foreach (var item in dependencyAnalysisDic)
            //    {
            //        if (item.Key(pi))
            //        {
            //            item.Value.Analyze(pi.path, path2Dependences);
            //        }
            //    }
            //});

            sw.Stop();
            Console.WriteLine($"分析引用耗时:{sw.ElapsedMilliseconds / 1000f}s");
            //AssetDependencyGraphDB db = new AssetDependencyGraphDB("", "", "localhost");
            //sw.Restart();
            //db.Clean();
            //Parallel.ForEach(assetIdentify2AssetNodeDic, item =>
            //{
            //    db.UpdateOrInsert(item.Value);
            //});
            //sw.Stop();
            //Console.WriteLine($"更新数据库:{sw.ElapsedMilliseconds / 1000f}s");
        }
    }
}
