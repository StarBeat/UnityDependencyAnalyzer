using MonoHook;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace AssetDependencyGraph
{
    internal class AssetRefCountView
    {
        private static MethodHook hook;
        private static FieldInfo assetNameFiled;
        private static Func<string, int> getRefCountByGuid; 

        internal static void HookAssetViewer(Func<string, int> getRefCountByGuid)
        {
            AssetRefCountView.getRefCountByGuid = getRefCountByGuid;
            var editorCore = System.AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.Equals("UnityEditor.CoreModule")).FirstOrDefault();
            var targetMethod = editorCore.GetType("UnityEditor.FilteredHierarchy").GetMethod("CopyPropertyData", BindingFlags.Instance | BindingFlags.NonPublic);

            assetNameFiled = editorCore.GetType("UnityEditor.FilteredHierarchy+FilterResult").GetField("name", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            hook = new MethodHook(targetMethod, typeof(AssetRefCountView).GetMethod("FuncReplace", BindingFlags.Instance | BindingFlags.NonPublic), typeof(AssetRefCountView).GetMethod("FuncProxy", BindingFlags.Instance | BindingFlags.NonPublic));
            hook.Install();
        }

        internal static void UnHookAssetViewer()
        {
            if (hook != null)
            {
                hook.Uninstall();
            }

            hook = null;
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private void FuncReplace(ref object result, HierarchyProperty property)
        {
            FuncProxy(ref result, property);
            if (!property.isFolder)
            {
                var refCnt = getRefCountByGuid.Invoke(property.guid);
                if(refCnt != 0)
                {
                    assetNameFiled.SetValue(result, $"{assetNameFiled.GetValue(result)}  [被引用次数:{refCnt}]");
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private void FuncProxy(ref object result, HierarchyProperty property)
        {
            Debug.Log("Not need code");
        }

    }
}
