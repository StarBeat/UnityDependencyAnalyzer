using MemoryPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AssetDependencyGraph
{
    public class AssetGraphView : GraphView
    {
        public AssetGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new FreehandSelector());

            VisualElement background = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(0.17f, 0.17f, 0.17f, 1f)
                }
            };
            Insert(0, background);

            background.StretchToParentSize();
        }

    }

    public class AssetGroup
    {
        public AssetNode AssetNode;
        public Group GroupNode = new Group();
        public Node MainGraphNode = new Node();
        public Rect MainGraphNodeLastPosition = new Rect();
        public List<GraphElement> AssetGraphNodes = new List<GraphElement>();
        public List<GraphElement> AssetGraphConnections = new List<GraphElement>();
        public List<Node> DependenciesForPlacement = new List<Node>();
    }


    public class AssetDependencyGraph : EditorWindow
    {
        private const float NodeWidth = 300.0f;
        private static System.Text.RegularExpressions.Regex isGuid = new("^[\\da-f]{32}$");

        private Dictionary<string, Toggle> type2Toogle = new();
        (string assetType, bool show)[] assetTypeHidenTogleItems = new[] {
            ("Executable" , true), ("UnityAssembly", true), ("SourceFile", true),
            ("MakeFile", true),  ("DatFile", true), ("AudioClip", true),
            ("VideoClip", true),  ("Texture", false), ("Shader", false),
            ("ComputeShader", false), ("ShaderHeader", false), ("Binary", true),
            ("TextFile", true),  ("Excel", true), ("UnknowFileType", false),
        };

        private Toggle AlignmentToggle;

        private GraphView graphView;

        private readonly List<Object> selectedObjects = new List<Object>();
        private readonly List<AssetGroup> assetGroups = new List<AssetGroup>();

        private readonly Dictionary<string, Node> fullPathNodeLookup = new Dictionary<string, Node>();

        private static Dictionary<AssetIdentify, AssetNode> assetId2NodeDic;
        private static Dictionary<string, AssetIdentify> path2NodeIdDic;
        private static Dictionary<string, string> guid2PathDic = new();
        private static Dictionary<string, string> path2GuidDic = new();

        static AssetNode FindAssetNode(string path)
        {
            if (path2NodeIdDic.TryGetValue(path, out var id))
            {
                return assetId2NodeDic[id];
            }

            return null;
        }

        [MenuItem("Window/资源引用分析")]
        public static void CreateTestGraphViewWindow()
        {
            var window = GetWindow<AssetDependencyGraph>(true);
            window.titleContent = new GUIContent("Asset Dependency Graph");
        }

        public void OnEnable()
        {
            CreateGraph();
        }

        public void OnDisable()
        {
            rootVisualElement.Remove(graphView);
        }

        public static bool IsGuid(string str)
        {
            if (str.Length == 32)
            {
                return isGuid.IsMatch(str);
            }
            return false;
        }

        private static string GUIDToPath(string guid)
        {
            if(guid2PathDic != null)
            {
                if (guid2PathDic.ContainsKey(guid))
                {
                    return guid2PathDic[guid];
                }
            }
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        private static string PathToGUID(string path)
        {
            if(path2NodeIdDic != null)
            {
                if(path2NodeIdDic.ContainsKey(path))
                {
                    return path2GuidDic[path];
                }
            }

            return AssetDatabase.AssetPathToGUID(path);
        }

        private static int GetRefCountByGUID(string guid)
        {
            var path = GUIDToPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return 0;
            }

            if(path2NodeIdDic == null || assetId2NodeDic == null)
            {
                return 0;
            }

            if(path2NodeIdDic.TryGetValue(path, out var id) && assetId2NodeDic.TryGetValue(id, out var assetNode))
            {
                return assetNode.DependentSet.Count(n => n.AssetType != "Folder");
            }

            return 0;
        }

        void ResolveDependency()
        {
            System.Diagnostics.Stopwatch stopwatch = new();

            var projectPath = Application.dataPath.Replace("Assets", "");
            var libraryPath = Path.Combine(projectPath, "Library");
            var dependencyGraph = Path.Combine(libraryPath, "dependencyGraph.bin");
            if (!File.Exists(dependencyGraph))
            {
                EditorUtility.DisplayDialog("提示", "需要解析引用关系", "确认");
                return;
            }

            var bytes = File.ReadAllBytes(dependencyGraph);
            stopwatch.Restart();

            assetId2NodeDic = MemoryPackSerializer.Deserialize<Dictionary<AssetIdentify, AssetNode>>(bytes.AsSpan());

            var guid2pathBinPath = Path.Combine(libraryPath, "guid2path.bin");
            if (File.Exists(guid2pathBinPath))
            {
                guid2PathDic =  MemoryPackSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(guid2pathBinPath));
            }

            var path2guidBinPath = Path.Combine(libraryPath, "path2guid.bin");
            if (File.Exists(path2guidBinPath))
            {
                path2GuidDic = MemoryPackSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path2guidBinPath));
            }

            stopwatch.Stop();
            Debug.Log($"Deserialize {stopwatch.ElapsedMilliseconds}");
            stopwatch.Restart();
            path2NodeIdDic = new();
            foreach (var node in assetId2NodeDic)
            {
                path2NodeIdDic[node.Key.Path] = node.Key;
            }
            stopwatch.Stop();
            Debug.Log($"ResolveDependency {stopwatch.ElapsedMilliseconds}");
        }

        void CreateGraph()
        {  
            graphView = new AssetGraphView
            {
                name = "Asset Dependency Graph",
            };

            VisualElement toolbar = CreateToolbar();
            VisualElement toolbar2 = CreateFilterbar();

            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(toolbar2);
            rootVisualElement.Add(graphView);
            graphView.StretchToParentSize();
            toolbar.BringToFront();
            toolbar2.BringToFront();
        }

        VisualElement CreateToolbar()
        {
            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 0,
                    backgroundColor = new Color(0.25f, 0.25f, 0.25f, 0.75f)
                }
            };

            var options = new VisualElement
            {
                style = { alignContent = Align.Center }
            };

            toolbar.Add(options);
            toolbar.Add(new Button(ExploreAsset)
            {
                text = "展示选择对象引用关系",
            });
            toolbar.Add(new Button(ClearGraph)
            {
                text = "Clear"
            });
            toolbar.Add(new Button(ResetGroups)
            {
                text = "Reset Groups"
            });
            toolbar.Add(new Button(ResetAllNodes)
            {
                text = "Reset Nodes"
            });
            toolbar.Add(new Button(() =>
            {
                System.Diagnostics.Stopwatch stopwatch = new();
                stopwatch.Restart();
                System.Diagnostics.Process.Start(startInfo: new()
                {
                    FileName = $"{Application.dataPath}/../Tools/UnityDependencyAnalyzer.exe",
                    Arguments = $"-reference {Application.dataPath.Replace("Assets", "")} \" \" \" \" localhost ",
                    UseShellExecute = true,
                })
                .WaitForExit();
                stopwatch.Stop();
                Debug.Log($"resolve {stopwatch.ElapsedMilliseconds}");
                ResolveDependency();
            })
            {
                text = "分析或更新引用"
            });
            toolbar.Add(new Button(() =>
            {
                AssetRefCountView.HookAssetViewer(GetRefCountByGUID);
            })
            { 
                text  = "在 Project 面板显示被引用",
            });
           var ts = new ToolbarSearchField();
            ts.RegisterValueChangedCallback(x =>
            {
                if (string.IsNullOrEmpty(x.newValue))
                {
                    graphView.FrameAll();
                    return;
                }

                graphView.ClearSelection();
                graphView.graphElements.ToList().ForEach(y =>
                {
                    if (y is Node node && y.title.IndexOf(x.newValue, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        graphView.AddToSelection(node);
                    }
                });

                graphView.FrameSelection();
            });
            toolbar.Add(ts);

            AlignmentToggle = new Toggle();
            AlignmentToggle.text = "Horizontal Layout";
            AlignmentToggle.value = true;
            AlignmentToggle.RegisterValueChangedCallback(x =>
            {
                ResetAllNodes();
            });
            toolbar.Add(AlignmentToggle);

            return toolbar;
        }

        VisualElement CreateFilterbar()
        {
            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 0,
                    backgroundColor = new Color(0.25f, 0.25f, 0.25f, 0.75f)
                }
            };

            var options = new VisualElement
            {
                style = { alignContent = Align.Center }
            };

            toolbar.Add(options);

            toolbar.Add(new Label("Filters: "));
            foreach (var pair in assetTypeHidenTogleItems)
            {
                var assetTypeTogle = new Toggle();
                assetTypeTogle.text = "Hide " + pair.assetType;
                assetTypeTogle.value = pair.show;
                assetTypeTogle.RegisterValueChangedCallback(x =>
                {
                    FilterAssetGroups();
                });
                toolbar.Add(assetTypeTogle);
                type2Toogle[pair.assetType] = assetTypeTogle;
            }

            return toolbar;
        }

        private static string GetAnalyzedAssetPath(UnityEngine.Object obj)
        {
            return AssetDatabase.GetAssetPath(obj).Replace('\\', '/').ToLowerInvariant();
        }

        private static void OnDeleteAsset(UnityEngine.Object asset)
        {
            var path = GetAnalyzedAssetPath(asset);
            var delAssetNode = FindAssetNode(path);
            foreach (var dependency in delAssetNode.DependencySet) 
            {
                assetId2NodeDic[dependency].DependentSet.Remove(delAssetNode.Self);
            }

            assetId2NodeDic.Remove(delAssetNode.Self);
            path2NodeIdDic.Remove(path);
        }

        private void ExploreAsset()
        {
            Object[] objs = Selection.objects;
            if (path2NodeIdDic == null || path2NodeIdDic.Count == 0)
            {
                ResolveDependency();
            }
        
            foreach (var obj in objs)
            {
                //Prevent readding same object
                if (selectedObjects.Contains(obj))
                {
                    Debug.Log("Object already loaded");
                    return;
                }
                selectedObjects.Add(obj);

                AssetGroup AssetGroup = new AssetGroup();
                AssetGroup.AssetNode = FindAssetNode(GetAnalyzedAssetPath(obj));
                assetGroups.Add(AssetGroup);

                // assetPath will be empty if obj is null or isn't an asset (a scene object)
                if (obj == null)
                    return;

                AssetGroup.GroupNode = new Group { title = obj.name };

                PopulateGroup(AssetGroup, obj, new Rect(10, graphView.contentRect.height / 2, 0, 0));
            }

        }

        void PopulateGroup(AssetGroup AssetGroup, Object obj, Rect position)
        {
            if (obj == null)
            {
                obj = AssetDatabase.LoadMainAssetAtPath(AssetGroup.AssetNode.Self.Path);

                if (obj == null)
                {
                    Debug.Log("Object doesn't exist anymore");
                    return;
                }
            }

            AssetGroup.MainGraphNode = CreateNode(AssetGroup, AssetGroup.AssetNode, obj, true);
            AssetGroup.MainGraphNode.userData = 0;
            AssetGroup.MainGraphNode.SetPosition(position);

            if (!graphView.Contains(AssetGroup.GroupNode))
            {
                graphView.AddElement(AssetGroup.GroupNode);
            }

            graphView.AddElement(AssetGroup.MainGraphNode);

            AssetGroup.GroupNode.AddElement(AssetGroup.MainGraphNode);

            CreateDependencyNodes(AssetGroup, AssetGroup.AssetNode, AssetGroup.MainGraphNode, AssetGroup.GroupNode, 1);
            CreateDependentNodes(AssetGroup, AssetGroup.AssetNode, AssetGroup.MainGraphNode, AssetGroup.GroupNode, -1);

            AssetGroup.AssetGraphNodes.Add(AssetGroup.MainGraphNode);
            AssetGroup.GroupNode.capabilities &= ~Capabilities.Deletable;

            AssetGroup.GroupNode.Focus();

            AssetGroup.MainGraphNode.RegisterCallback<GeometryChangedEvent, AssetGroup>(
                UpdateGroupDependencyNodePlacement, AssetGroup
            );
        }

        //Recreate the groups but use the already created groups instead of new ones
        void FilterAssetGroups()
        {

            //first collect the main node's position and then clear the graph
            foreach (var AssetGroup in assetGroups)
            {
                AssetGroup.MainGraphNodeLastPosition = AssetGroup.MainGraphNode.GetPosition();
            }

            fullPathNodeLookup.Clear();

            foreach (var AssetGroup in assetGroups)
            {
                //clear the nodes and dependencies after getting the position of the main node 
                CleanGroup(AssetGroup);

                PopulateGroup(AssetGroup, null, AssetGroup.MainGraphNodeLastPosition);
            }
        }

        void CleanGroup(AssetGroup assetGroup)
        {
            if (assetGroup.AssetGraphConnections.Count > 0)
            {
                foreach (var edge in assetGroup.AssetGraphConnections)
                {
                    graphView.RemoveElement(edge);
                }
            }
            assetGroup.AssetGraphConnections.Clear();

            foreach (var node in assetGroup.AssetGraphNodes)
            {
                graphView.RemoveElement(node);
            }
            assetGroup.AssetGraphNodes.Clear();

            assetGroup.DependenciesForPlacement.Clear();
        }

        private void CreateDependencyNodes(AssetGroup assetGroup, AssetNode asssetNode, Node selfGraphNode, Group groupGraphNode, int depth)
        {
            foreach (var dependAssetId in asssetNode.DependencySet)
            {
                AssetNode dependAssetNode = FindAssetNode(dependAssetId.Path);
                if(dependAssetNode == null)
                {
                    continue;
                }    
                var typeName = dependAssetNode.AssetType;
                //filter out selected asset types
                if (FilterType(typeName))
                {
                    continue;
                }

                var fullPath = dependAssetId.Path;
                var pathIsGuid = IsGuid(fullPath);
                if (pathIsGuid)
                {
                    fullPath = GUIDToPath(fullPath);
                }
                var obj = AssetDatabase.LoadMainAssetAtPath(fullPath);
                if(obj == null)
                {
                    if (!pathIsGuid)
                    {
                        Debug.Log($"{dependAssetId.Path} 可能已经删除");
                    }
                    continue;
                }
                Node dependGraphNode = CreateNode(assetGroup, dependAssetNode, obj, false);

                if (!assetGroup.AssetGraphNodes.Contains(dependGraphNode))
                {
                   
                    dependGraphNode.userData = depth;
                }

                //CreateDependencyNodes(assetGroup, dependAssetNode, dependGraphNode, groupGraphNode, depth + 1);

                //if the node doesnt exists yet, put it in the group
                if (!graphView.Contains(dependGraphNode))
                {
                    graphView.AddElement(dependGraphNode);

                    assetGroup.DependenciesForPlacement.Add(dependGraphNode);
                    groupGraphNode.AddElement(dependGraphNode);
                }
                else
                {
                    //TODO: if it already exists, put it in a separate group for shared assets
                    //Check if the dependencyNode is in the same group or not
                    //if it's a different group move it to a new shared group
                    /*
                    if (SharedToggle.value) {
                        if (!assetGroup.m_AssetNodes.Contains(dependencyNode)) {
                            if (assetGroup.SharedGroup == null) {
                                assetGroup.SharedGroup = new AssetGroup();

                                AssetGroups.Add(assetGroup.SharedGroup);
                                assetGroup.SharedGroup.assetPath = assetGroup.assetPath;

                                assetGroup.SharedGroup.groupNode = new Group { title = "Shared Group" };

                                assetGroup.SharedGroup.mainNode = dependencyNode;
                                assetGroup.SharedGroup.mainNode.userData = 0;
                            }

                            if (!m_GraphView.Contains(assetGroup.SharedGroup.groupNode)) {
                                m_GraphView.AddElement(assetGroup.SharedGroup.groupNode);
                            }

                            //add the node to the group and remove it from the previous group
                            assetGroup.m_AssetNodes.Remove(dependencyNode);
                            //assetGroup.groupNode.RemoveElement(dependencyNode);
                            assetGroup.m_DependenciesForPlacement.Remove(dependencyNode);

                            assetGroup.SharedGroup.m_DependenciesForPlacement.Add(dependencyNode);

                            if (!assetGroup.SharedGroup.groupNode.ContainsElement(dependencyNode)) {
                                assetGroup.SharedGroup.groupNode.AddElement(dependencyNode);
                            }

                            assetGroup.SharedGroup.m_AssetNodes.Add(dependencyNode);
                        }
                    }*/
                }

                Edge edge = CreateEdge(dependGraphNode, selfGraphNode);

                assetGroup.AssetGraphConnections.Add(edge);
                assetGroup.AssetGraphNodes.Add(dependGraphNode);
            }

        }

        private void CreateDependentNodes(AssetGroup assetGroup, AssetNode asssetNode, Node selfGraphNode, Group groupGraphNode, int depth)
        {
            foreach (var dependAssetId in asssetNode.DependentSet)
            {
                AssetNode dependAssetNode = FindAssetNode(dependAssetId.Path);
                if (dependAssetNode == null)
                {
                    continue;
                }

                var typeName = dependAssetNode.AssetType;
                //filter out selected asset types
                if (FilterType(typeName))
                {
                    continue;
                }
                var fullPath = dependAssetId.Path;
                var pathIsGuid = IsGuid(fullPath);
                if (pathIsGuid)
                {
                    fullPath = GUIDToPath(fullPath);
                }
                var obj = AssetDatabase.LoadMainAssetAtPath(fullPath);
                if (obj == null)
                {
                    if (!pathIsGuid)
                    {
                        Debug.Log($"{dependAssetId.Path} 可能已经删除");
                    }
                    continue;
                }
                Node dependentGraphNode = CreateNode(assetGroup, dependAssetNode, obj, false);

                if (!assetGroup.AssetGraphNodes.Contains(dependentGraphNode))
                {
                    dependentGraphNode.userData = depth;
                }

                //CreateDependencyNodes(assetGroup, dependAssetNode, dependGraphNode, groupGraphNode, depth - 1);

                //if the node doesnt exists yet, put it in the group
                if (!graphView.Contains(dependentGraphNode))
                {
                    graphView.AddElement(dependentGraphNode);

                    assetGroup.DependenciesForPlacement.Add(dependentGraphNode);
                    groupGraphNode.AddElement(dependentGraphNode);
                }
                else
                {
                    //TODO: if it already exists, put it in a separate group for shared assets
                    //Check if the dependencyNode is in the same group or not
                    //if it's a different group move it to a new shared group
                    /*
                    if (SharedToggle.value) {
                        if (!assetGroup.m_AssetNodes.Contains(dependencyNode)) {
                            if (assetGroup.SharedGroup == null) {
                                assetGroup.SharedGroup = new AssetGroup();

                                AssetGroups.Add(assetGroup.SharedGroup);
                                assetGroup.SharedGroup.assetPath = assetGroup.assetPath;

                                assetGroup.SharedGroup.groupNode = new Group { title = "Shared Group" };

                                assetGroup.SharedGroup.mainNode = dependencyNode;
                                assetGroup.SharedGroup.mainNode.userData = 0;
                            }

                            if (!m_GraphView.Contains(assetGroup.SharedGroup.groupNode)) {
                                m_GraphView.AddElement(assetGroup.SharedGroup.groupNode);
                            }

                            //add the node to the group and remove it from the previous group
                            assetGroup.m_AssetNodes.Remove(dependencyNode);
                            //assetGroup.groupNode.RemoveElement(dependencyNode);
                            assetGroup.m_DependenciesForPlacement.Remove(dependencyNode);

                            assetGroup.SharedGroup.m_DependenciesForPlacement.Add(dependencyNode);

                            if (!assetGroup.SharedGroup.groupNode.ContainsElement(dependencyNode)) {
                                assetGroup.SharedGroup.groupNode.AddElement(dependencyNode);
                            }

                            assetGroup.SharedGroup.m_AssetNodes.Add(dependencyNode);
                        }
                    }*/
                }

                Edge edge = CreateEdge(selfGraphNode, dependentGraphNode);

                assetGroup.AssetGraphConnections.Add(edge);
                assetGroup.AssetGraphNodes.Add(dependentGraphNode);
            }
        }

        Edge CreateEdge(Node dependencyNode, Node parentNode)
        {
            Edge edge = new Edge
            {
                input = dependencyNode.inputContainer[0] as Port,
                output = parentNode.outputContainer[0] as Port,
            };
            edge.input?.Connect(edge);
            edge.output?.Connect(edge);

            dependencyNode.RefreshPorts();

            graphView.AddElement(edge);

            edge.capabilities &= ~Capabilities.Deletable;

            return edge;
        }

        private Node CreateNode(AssetGroup assetGroup, AssetNode assetNode, Object obj, bool isMainNode)
        {
            Node resultNode;
            string fullPath = assetNode.Self.Path;
       
            if (fullPathNodeLookup.TryGetValue(fullPath, out resultNode))
            {
                //----not sure what this is, the more dependencies the further removed on the chart?
                //int currentDepth = (int)resultNode.userData;
                //resultNode.userData = currentDepth + 1;
                return resultNode;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var assetGuid, out long _))
            {
                var objNode = new Node
                {
                    title = obj.name,
                    style =
                    {
                        width = NodeWidth
                    }
                };

                objNode.extensionContainer.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f, 0.8f);
                
                #region Select button
                objNode.titleContainer.Add(new Button(() =>
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                })
                {
                    style =
                    {
                        height = 16.0f,
                        alignSelf = Align.Center,
                        alignItems = Align.Center
                    },
                    text = "Select"
                });
                objNode.titleContainer.Add(new Button(() =>
                {
                    if(assetNode.AssetType == "Folder")
                    {
                    }
                    else
                    {
                        bool hasRef = false;
                        foreach (var item in assetNode.DependentSet)
                        {
                            if (item.AssetType != "Folder")
                            {
                                hasRef = true; 
                                break;
                            }
                        }
                        if(!hasRef)
                        {
                            if (EditorUtility.DisplayDialog("提示", "当前 asset 没有引用,是否直接删除", "确认删除", "取消"))
                            {
                                OnDeleteAsset(obj);
                                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(obj));
                            }
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("提示", "当前 asset 有引用,请先处理引用关系", "确认");
                        }
                    }
                })
                {
                    style =
                    {
                        height = 16.0f,
                        alignSelf = Align.Center,
                        alignItems = Align.Center
                    },
                    text = "Delete"
                });
                #endregion

                #region Padding
                var infoContainer = new VisualElement
                {
                    style =
                    {
                    paddingBottom = 4.0f,
                    paddingTop = 4.0f,
                    paddingLeft = 4.0f,
                    paddingRight = 4.0f
                }
                };
                #endregion

                #region Asset Path, removed to improve visibility with large amount of assets
                //            infoContainer.Add(new Label {
                //                text = assetPath,
                //#if UNITY_2019_1_OR_NEWER
                //                style = { whiteSpace = WhiteSpace.Normal }
                //#else
                //                style = { wordWrap = true }
                //#endif
                //            });
                #endregion

                #region Asset type
                var typeName = assetNode.AssetType;

                var typeLabel = new Label
                {
                    text = $"Type: {typeName}",
                };
                infoContainer.Add(typeLabel);

                objNode.extensionContainer.Add(infoContainer);
                #endregion

                var typeContainer = new VisualElement
                {
                    style =
                    {
                        paddingBottom = 4.0f,
                        paddingTop = 4.0f,
                        paddingLeft = 4.0f,
                        paddingRight = 4.0f,
                        backgroundColor = GetColorByAssetType(typeName)
                    }
                };

                objNode.extensionContainer.Add(typeContainer);
                objNode.RegisterCallback<FocusInEvent>(e =>
                {
                    Debug.Log("FocusInEvent");
                });

                objNode.RegisterCallback<FocusOutEvent>(e =>
                {
                    Debug.Log("FocusOutEvent");
                });

                #region Node Icon, replaced with color 
                //Texture assetTexture = AssetPreview.GetAssetPreview(obj);
                //if (!assetTexture)
                //    assetTexture = AssetPreview.GetMiniThumbnail(obj);

                //if (assetTexture)
                //{
                //    AddDivider(objNode);

                //    objNode.extensionContainer.Add(new Image
                //    {
                //        image = assetTexture,
                //        scaleMode = ScaleMode.ScaleToFit,
                //        style =
                //        {
                //            paddingBottom = 4.0f,
                //            paddingTop = 4.0f,
                //            paddingLeft = 4.0f,
                //            paddingRight = 4.0f
                //        }
                //    });
                //} 
                #endregion

                // Ports
                var dependentAmount = 0;
                foreach (var item in assetNode.DependentSet)
                {
                    if (item.AssetType != "Folder")
                    {
                        ++dependentAmount;
                    }
                }
                if (assetNode.DependentSet.Count > 0)
                {
                    Port port = objNode.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(Object));
                    port.Add(new Button(() =>
                        {
                            CreateDependentNodes(assetGroup, assetNode, fullPathNodeLookup[fullPath], assetGroup.GroupNode, (int)fullPathNodeLookup[fullPath].userData - 1);
                            EditorApplication.delayCall += () => ResetAllNodes();
                        })
                        {
                            style =
                        {
                            height = 16.0f,
                            alignSelf = Align.Center,
                            alignItems = Align.Center
                        },
                            text = "展开"
                        });
                    port.portName = dependentAmount + "个引用";
                    objNode.inputContainer.Add(port);
                }

                var dependencyAmount = assetNode.DependencySet.Count;
                if (dependencyAmount > 0)
                {
                    Port port = objNode.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(Object));
                    port.Add(new Button(() =>
                        {
                            CreateDependencyNodes(assetGroup, assetNode, fullPathNodeLookup[fullPath], assetGroup.GroupNode, (int)fullPathNodeLookup[fullPath].userData + 1);
                            EditorApplication.delayCall += () => ResetAllNodes();
                        })
                        {
                            style =
                            {
                                height = 16.0f,
                                alignSelf = Align.Center,
                                alignItems = Align.FlexEnd
                            },
                            text = "展开"
                        });
                    port.portName = dependencyAmount + "个依赖";
                    objNode.outputContainer.Add(port);
                    objNode.RefreshPorts();
                }

                resultNode = objNode;

                resultNode.RefreshExpandedState();
                resultNode.RefreshPorts();
                resultNode.capabilities &= ~Capabilities.Deletable;
                resultNode.capabilities |= Capabilities.Collapsible;
            }
            fullPathNodeLookup[fullPath] = resultNode;
            return resultNode;
        }

        bool FilterType(string type)
        {
            if (type2Toogle.TryGetValue(type, out var result))
            {
                return result.value;
            }

            return false;
        }


        StyleColor GetColorByAssetType(string typeName)
        {
            switch (typeName)
            {
                case "MonoScript":
                    return Color.black;
                case "Material":
                    return new Color(0.1f, 0.5f, 0.1f);   //green
                case "Texture2D":
                    return new Color(0.5f, 0.1f, 0.1f); //red
                case "RenderTexture":
                    return new Color(0.8f, 0.1f, 0.1f); //red
                case "Shader":
                    return new Color(0.1f, 0.1f, 0.5f); //dark blue
                case "ComputeShader":
                    return new Color(0.1f, 0.1f, 0.5f); //dark blue
                case "GameObject":
                    return new Color(0f, 0.8f, 0.7f); //light blue
                case "AnimationClip":
                    return new Color(1, 0.7f, 1); //pink
                case "AnimatorController":
                    return new Color(1, 0.7f, 0.8f); //pink
                case "AudioClip":
                    return new Color(1, 0.8f, 0); //orange
                case "AudioMixerController":
                    return new Color(1, 0.8f, 0); //orange
                case "Font":
                    return new Color(0.9f, 1, 0.9f); //light green
                case "TMP_FontAsset":
                    return new Color(0.9f, 1, 0.9f); //light green
                case "Mesh":
                    return new Color(0.5f, 0, 0.5f); //purple
                case "TerrainLayer":
                    return new Color(0.5f, 0.8f, 0f);   //green
                case "Folder":
                    return Color.yellow;
                default:
                    break;
            }

            return CustomColor(typeName);
        }

        //Add custom assets here 
        StyleColor CustomColor(string assetType)
        {
            switch (assetType)
            {
                case "GearObject":
                    return new Color(0.9f, 0, 0.9f); //pink
                case "TalentObject":
                    return new Color(0.9f, 0, 0.9f); //
                case "AbilityInfo":
                    return new Color(0.9f, 0, 0.9f); //
                case "HealthSO":
                    return new Color(0.9f, 0, 0.9f); //
                default:
                    break;
            }

            //standard color
            return new Color(0.24f, 0.24f, 0.24f, 0.8f);
        }

        private static void AddDivider(Node objNode)
        {
            var divider = new VisualElement { name = "divider" };
            divider.AddToClassList("horizontal");
            objNode.extensionContainer.Add(divider);
        }

        private void ClearGraph()
        {
            selectedObjects.Clear();

            foreach (var assetGroup in assetGroups)
            {
                EmptyGroup(assetGroup);
            }

            fullPathNodeLookup.Clear();

            assetGroups.Clear();
        }

        void EmptyGroup(AssetGroup assetGroup)
        {
            if (assetGroup.AssetGraphConnections.Count > 0)
            {
                foreach (var edge in assetGroup.AssetGraphConnections)
                {
                    graphView.RemoveElement(edge);
                }
            }
            assetGroup.AssetGraphConnections.Clear();

            foreach (var node in assetGroup.AssetGraphNodes)
            {
                graphView.RemoveElement(node);
            }
            assetGroup.AssetGraphNodes.Clear();

            assetGroup.DependenciesForPlacement.Clear();

            graphView.RemoveElement(assetGroup.GroupNode);

            assetGroup.GroupNode = null;
        }

        private void UpdateGroupDependencyNodePlacement(GeometryChangedEvent e, AssetGroup assetGroup)
        {
            assetGroup.MainGraphNode.UnregisterCallback<GeometryChangedEvent, AssetGroup>(
                UpdateGroupDependencyNodePlacement
            );

            ResetNodes(assetGroup);
        }

        void ResetAllNodes()
        {
            foreach (var assetGroup in assetGroups)
            {
                ResetNodes(assetGroup);
            }
        }

        //Reset the node positions of the given group
        void ResetNodes(AssetGroup assetGroup)
        {
            // The current y offset in per depth
            var depthOffset = new Dictionary<int, float>();

            foreach (var node in assetGroup.DependenciesForPlacement)
            {
                int depth = (int)node.userData;

                if (!depthOffset.ContainsKey(depth))
                    depthOffset.Add(depth, 0.0f);

                if (AlignmentToggle.value)
                {
                    depthOffset[depth] += node.layout.height;
                }
                else
                {
                    depthOffset[depth] += node.layout.width;
                }
            }

            // Move half of the node into negative y space so they're on either size of the main node in y axis
            var depths = new List<int>(depthOffset.Keys);
            foreach (int depth in depths)
            {
                if (depth == 0)
                    continue;

                float offset = depthOffset[depth];
                depthOffset[depth] = (0f - offset / 2.0f);
            }

            Rect mainNodeRect = assetGroup.MainGraphNode.GetPosition();

            foreach (var node in assetGroup.DependenciesForPlacement)
            {
                int depth = (int)node.userData;
                if (AlignmentToggle.value)
                {
                    node.SetPosition(new Rect(mainNodeRect.x + node.layout.width * 1.5f * depth, mainNodeRect.y + depthOffset[depth], 0, 0));
                }
                else
                {
                    node.SetPosition(new Rect(mainNodeRect.x + depthOffset[depth], mainNodeRect.y + node.layout.height * 1.5f * depth, 0, 0));
                }

                if (AlignmentToggle.value)
                {
                    depthOffset[depth] += node.layout.height;
                }
                else
                {
                    depthOffset[depth] += node.layout.width;
                }
            }
        }

        //fix the position of the groups so they dont overlap
        void ResetGroups()
        {
            float y = 0;
            float x = 0;

            foreach (var assetGroup in assetGroups)
            {
                if (AlignmentToggle.value)
                {
                    Rect pos = assetGroup.GroupNode.GetPosition();
                    pos.x = x;
                    assetGroup.GroupNode.SetPosition(pos);
                    x += assetGroup.GroupNode.GetPosition().width;
                }
                else
                {
                    Rect pos = assetGroup.GroupNode.GetPosition();
                    pos.y = y;
                    assetGroup.GroupNode.SetPosition(pos);
                    y += assetGroup.GroupNode.GetPosition().height;
                }
            }
        }
    }
}
