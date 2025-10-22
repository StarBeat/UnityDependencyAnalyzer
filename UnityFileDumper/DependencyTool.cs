using System;
using System.Collections.Generic;
namespace UnityFileApi
{
    public static class DependencyTool
    {
        static DependencyTool()
        {
            UnityFileSystem.Init();
        }

        public static List<string> GetDependencies(string path)
        {
            List<string> dependencies = new List<string>();
            try
            {
                using var archive = UnityFileSystem.MountArchive(path, "/");
                foreach (var node in archive.Nodes)
                {
                    Console.WriteLine($"Processing {node.Path} {node.Size} {node.Flags}");

                    if (node.Flags.HasFlag(ArchiveNodeFlags.SerializedFile))
                    {
                        using (var serializedFile = UnityFileSystem.OpenSerializedFile(path))
                        {
                            foreach (var extRef in serializedFile.ExternalReferences)
                            {
                                dependencies.Add(extRef.Guid);
                            }
                        }
                    }
                }
                return dependencies;
            }
            catch (NotSupportedException)
            {
                // Try as SerializedFile
                using (var serializedFile = UnityFileSystem.OpenSerializedFile(path))
                {
                    foreach (var extRef in serializedFile.ExternalReferences)
                    {
                        dependencies.Add(extRef.Guid);
                    }
                }
                return dependencies;
            }
        }
    }
}