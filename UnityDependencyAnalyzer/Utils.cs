using System.Text;

namespace AssetDependencyGraph
{
    public static class Utils
    {
        public static string DataPath = null!;
        public static string DataPathLow = null!;

        public static string Md5(string filename)
        {
            try
            {
                FileStream fs = new FileStream(filename, FileMode.Open);
#pragma warning disable SYSLIB0021 // 类型或成员已过时
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
#pragma warning restore SYSLIB0021 // 类型或成员已过时
                byte[] retVal = md5.ComputeHash(fs);
                fs.Close();
                return BitConverter.ToString(retVal).ToLower().Replace("-", "");
            }
            catch
            {
                throw;
            }
        }

        public static void TraverseDirectory(string path, Action<string> action, int depth = 1)
        {
            if(depth == 0)
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(path))
            {
                action.Invoke(file);
            }

            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                action.Invoke(directory);
                TraverseDirectory(directory, action, --depth);
            }
        }

        public static string ToUniversalPath(this string path)
        {
            return path.Replace("\\", "/");
        }

        public static string ToUnityRelatePath(this string path)
        {
            DataPathLow ??= DataPath.ToLowerInvariant();

            if (path.StartsWith(DataPathLow.Replace("assets", "")) && !path.StartsWith(DataPathLow + "/assets"))
            {
                return path.Replace(DataPathLow.Replace("assets", ""), "");
            }
            return path.Replace(DataPathLow, "assets");
        }

        public static string ToUnityFullPath(this string path)
        {
            if(path.StartsWith("packages"))
            {
                var fullPath = (DataPath.Replace("Assets", "") + path);
                if (!File.Exists(fullPath) && Directory.Exists(fullPath))
                {
                    fullPath = (DataPath.Replace("Assets", "Library/PackageCache") + path);
                }

                if (!File.Exists(fullPath) && Directory.Exists(fullPath))
                {
                    Console.WriteLine($"ToUnityFullPath failure:{path}");
                }

                return fullPath;
            }
            
            return Path.Combine(DataPath.Replace("Assets", "") , path);
        }

        public static string ByteString(this byte[] bytes)
        {
            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                stringBuilder.Append(Convert.ToString(bytes[i], 2) );
            }
            return stringBuilder.ToString();
        }
    }
}
