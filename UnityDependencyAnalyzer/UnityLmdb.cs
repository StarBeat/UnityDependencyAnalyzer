using LightningDB;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AssetDependencyGraph
{
    public sealed class UnityLmdb
    {
        private Dictionary<string, string> guid2Path = new();
        private Dictionary<string, string> path2Guid = new();
        public static string ProjPath = null!;

        private string dbFilePath = null!;

        public static byte[] Guid2LmdbKey(string guid)
        {
            var inputByteArray = new byte[guid.Length / 2];
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < guid.Length; i += 2)
            {
                sb.Append(guid[i + 1]);
                sb.Append(guid[i]);
            }
            guid = sb.ToString();
            for (var x = 0; x < inputByteArray.Length; x++)
            {
                inputByteArray[x] = (byte)Convert.ToInt32(guid.Substring(x * 2, 2), 16);
            }

            return inputByteArray;
        }

        public static string LmdbKey2Guid(byte[] bytes)
        {
            StringBuilder ret = new StringBuilder();
            for (var i = 0; i < bytes.Length; i++)
            {
                ret.AppendFormat("{0:x2}", bytes[i]);
                if (ret.Length == 32)
                {
                    break;
                }
            }
            for (int i = 0; i < ret.Length; i += 2)
            {
                var c = ret[i];
                ret[i] = ret[i + 1];
                ret[i + 1] = c;
            }
            var hex = ret.ToString();
            return hex;
        }

        public void ResolveGuidPath()
        {
            var sourceDbPath = Path.Combine(ProjPath, "Library", "SourceAssetDB");
            var dbPath = dbFilePath = Path.Combine(ProjPath, "Library", "SourceAssetDB1");
            File.Copy(sourceDbPath, dbPath, true);
            using var env = new LightningEnvironment(dbPath, configuration: new()
            {
                MaxDatabases = 64,
                MaxReaders = 64,
            });
            env.Open(EnvironmentOpenFlags.NoSubDir | EnvironmentOpenFlags.ReadOnly);
            using var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
            using (var db = tx.OpenDatabase("GuidToPath", closeOnDispose: true))
            using (var cursor = tx.CreateCursor(db))
            {
                foreach (var item in cursor.AsEnumerable())
                {
                    guid2Path[LmdbKey2Guid(item.Item1.AsSpan().ToArray())] = Encoding.UTF8.GetString(item.Item2.AsSpan()).ToLowerInvariant().Trim('\0');
                }
            }

            using (var db = tx.OpenDatabase("PathToGuid", closeOnDispose: true))
            using (var cursor = tx.CreateCursor(db))
            {
                foreach (var item in cursor.AsEnumerable())
                {
                    path2Guid[Encoding.UTF8.GetString(item.Item1.AsSpan()).ToLowerInvariant().Trim('\0')] = LmdbKey2Guid(item.Item2.AsSpan().ToArray());
                }
            }
        }


        public void ResolveGuidPathByDBPath(string dbPath)
        {
            dbFilePath = dbPath;
            using var env = new LightningEnvironment(dbPath, configuration: new()
            {
                MaxDatabases = 64,
                MaxReaders = 64,
            });
            env.Open(EnvironmentOpenFlags.NoSubDir | EnvironmentOpenFlags.ReadOnly);
            using var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
            using (var db = tx.OpenDatabase("GuidToPath", closeOnDispose: true))
            using (var cursor = tx.CreateCursor(db))
            {
                foreach (var item in cursor.AsEnumerable())
                {
                    guid2Path[LmdbKey2Guid(item.Item1.AsSpan().ToArray())] = Encoding.UTF8.GetString(item.Item2.AsSpan()).ToLowerInvariant().Trim('\0');
                }
            }

            using (var db = tx.OpenDatabase("PathToGuid", closeOnDispose: true))
            using (var cursor = tx.CreateCursor(db))
            {
                foreach (var item in cursor.AsEnumerable())
                {
                    path2Guid[Encoding.UTF8.GetString(item.Item1.AsSpan()).ToLowerInvariant().Trim('\0')] = LmdbKey2Guid(item.Item2.AsSpan().ToArray());
                }
            }
        }

        public ConcurrentBag<string> VerifyGUID()
        {
            ConcurrentBag<string> result = new ();
            Parallel.ForEach(path2Guid, (item) =>
            {
                var f = item.Key + ".meta";
                if (File.Exists(f))
                {
                    var ftext = File.ReadAllText(f);
                    if (!ftext.Contains(item.Value))
                    {
                        result.Add(item.Key);
                    }
                }
            });
          
            return result;
        }

        public string ResultToJson()
        {
            return JsonSerializer.Serialize(path2Guid, new JsonSerializerOptions { IncludeFields = true });
        }

        public string GetGuidByPath(string path)
        {
            if (path2Guid.ContainsKey(path))
            {
                return path2Guid[path];
            }
            else
            {
                return null!;
            }
        }

        public string GetPathByGuid(string guid)
        {
            if (guid2Path.ContainsKey(guid))
            {
                return guid2Path[guid];
            }
            else
            {
                return null!;
            }
        }
    }
}
