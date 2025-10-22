using AssetDependencyGraph;
using System.Text.Json;

switch (Environment.GetCommandLineArgs()[1])
{
    case "-dump":
        {
            UnityLmdb unityLmdb = new();
            var projDir = Environment.GetCommandLineArgs()[2].TrimEnd('/').TrimEnd('\\');
            unityLmdb.ResolveGuidPathByDBPath($"{projDir}/Library/SourceAssetDB");
            var js = unityLmdb.ResultToJson();
            File.WriteAllText($"{projDir}/Library/SourceAssetDB.json", js);
            break;
        }
    case "-verify":
        {
            Console.WriteLine("Start");
            UnityLmdb unityLmdb = new();
            var projDir = Environment.GetCommandLineArgs()[2].TrimEnd('/').TrimEnd('\\');
            Directory.SetCurrentDirectory(projDir);
            unityLmdb.ResolveGuidPathByDBPath($"{projDir}/Library/SourceAssetDB");
            var res = unityLmdb.VerifyGUID();
            if (res.Count > 0)
            {
                var js = JsonSerializer.Serialize(res, new JsonSerializerOptions { IncludeFields = true });
                Console.WriteLine("Has Error.");

                File.WriteAllText($"{projDir}/verify-result.json", js);
            }
            Console.WriteLine("End");
            break;
        }
    case "-reference":
        {
            UnityLmdb.ProjPath = Environment.GetCommandLineArgs()[2];
            Utils.DataPath = Path.Combine(UnityLmdb.ProjPath, "Assets").ToUniversalPath();
            Console.WriteLine(string.Join(' ', Environment.GetCommandLineArgs()));
            if (Environment.GetCommandLineArgs().Length > 3 && Environment.GetCommandLineArgs()[3].Equals("SubProcess"))
            {
                //System.Diagnostics.Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)0x00FF;
                await new DependencyAnalyzer().AnalyzeSubProcess(Environment.GetCommandLineArgs()[4], Environment.GetCommandLineArgs()[5]);
            }
            else
            {
                await new DependencyAnalyzer().AnalyzeMainProcess(UnityLmdb.ProjPath, Utils.DataPath, 10);
            }
            break;
        }
    default:
        break;
}

