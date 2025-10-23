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