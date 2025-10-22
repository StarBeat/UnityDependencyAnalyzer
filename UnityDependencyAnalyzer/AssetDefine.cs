using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetDependencyGraph
{
    [BsonIgnoreExtraElements]
    public class AssetIdentify
    {
        public string Path = null!;
        public string AssetType = null!;
        [AllowNull]
        public string Guid;
        [AllowNull]
        public string Md5;
    }

    public sealed class AssetIdentifyJsonConverter : JsonConverter<AssetIdentify>
    {
        static JsonSerializerOptions serializerOptions = new JsonSerializerOptions() { IncludeFields = true };

        public override AssetIdentify? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<AssetIdentify>(reader.GetString()!, serializerOptions);
        }

        public override void Write(Utf8JsonWriter writer, AssetIdentify value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(JsonSerializer.Serialize(value, serializerOptions));
        }

        public override AssetIdentify ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Read(ref reader, typeToConvert, serializerOptions)!;
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] AssetIdentify value, JsonSerializerOptions options)
        {
            writer.WritePropertyName(JsonSerializer.Serialize(value, serializerOptions));
        }
    }

    [BsonIgnoreExtraElements]
    public class AssetNode
    {
        public AssetIdentify Self=null!;
        public string AssetType=null!;
        [JsonIgnore]
        public ConcurrentBag<AssetIdentify> Dependencies = new();
        [JsonIgnore]
        public ConcurrentBag<AssetIdentify> Dependent = new();

        [AllowNull]
        public HashSet<AssetIdentify> DependencySet;
        [AllowNull]
        public HashSet<AssetIdentify> DependentSet;
    }

    public sealed class AssetNodeJsonConverter : JsonConverter<AssetNode>
    {
        static JsonSerializerOptions serializerOptions = new JsonSerializerOptions()
        {
            IncludeFields = true,
            Converters = { new AssetIdentifyJsonConverter() }
        };

        public override AssetNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<AssetNode>(reader.GetString()!, serializerOptions);
        }

        public override void Write(Utf8JsonWriter writer, AssetNode value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(JsonSerializer.Serialize(value, serializerOptions));
        }
    }

    [BsonIgnoreExtraElements]
    public sealed class FolderNode : AssetNode
    {
    }

    [BsonIgnoreExtraElements]
    public sealed class PackageNode : AssetNode
    {
    }

    public class AssetDependencyGraphDB
    {
        MongoClient client;
        IMongoCollection<FolderNode> FolderNodes;
        IMongoCollection<PackageNode> PackageNodes;
        IMongoCollection<AssetNode> AssetNodes;
        Dictionary<string, AssetNode> findCacheDic = new();

        public AssetDependencyGraphDB(string user, string passwd, string ip)
        {
            MongoClientSettings settings;
            if(string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(ip))
            {
                settings = MongoClientSettings.FromUrl(new MongoUrl($"mongodb://{ip}:27017/"));
            }
            else
            {
                settings = MongoClientSettings.FromUrl(new MongoUrl($"mongodb://{user}:{passwd}@{ip}:27017/"));
            }

            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            settings.MinConnectionPoolSize = 1;
            settings.MaxConnectionPoolSize = 25;
            client = new MongoClient(settings);
            var db = client.GetDatabase("assetgraph");
            FolderNodes = db.GetCollection<FolderNode>("folder_nodes");
            PackageNodes = db.GetCollection<PackageNode>("package_nodes");
            AssetNodes = db.GetCollection<AssetNode>("asset_nodes");
        }

        public void Clean()
        {
            client.DropDatabase("assetgraph");
            var db = client.GetDatabase("assetgraph");
            FolderNodes = db.GetCollection<FolderNode>("folder_nodes");
            PackageNodes = db.GetCollection<PackageNode>("package_nodes");
            AssetNodes = db.GetCollection<AssetNode>("asset_nodes");
        }

        public void Insert<T>(T node) where T : AssetNode
        {
            switch (node)
            {
                case FolderNode folderNode:
                    {
                        FolderNodes.InsertOne(folderNode);
                        break;
                    }
                case PackageNode packageNode:
                    {
                        PackageNodes.InsertOne(packageNode);
                        break;
                    }
                case AssetNode assetNode:
                    {
                        AssetNodes.InsertOne(assetNode);
                        break;
                    }
                default:
                    break;
            }
        }

        public void UpdateOrInsert<T>(T node) where T : AssetNode
        {
            switch (node)
            {
                case FolderNode folderNode:
                    {
                        var filter = Builders<FolderNode>.Filter.And(
                            Builders<FolderNode>.Filter.Eq(fn=>fn.Self.Path,node.Self.Path)
                            );
                        var found = FolderNodes.Find(filter);
                        if (found == null || found.CountDocuments() == 0) 
                        {
                            FolderNodes.InsertOne(folderNode);
                        }
                        else
                        {
                            var result = FolderNodes.UpdateOne(filter, Builders<FolderNode>.Update.Combine(
                               Builders<FolderNode>.Update.Set(fn => fn.Self, folderNode.Self),
                               Builders<FolderNode>.Update.Set(fn => fn.AssetType, folderNode.AssetType),
                               Builders<FolderNode>.Update.Set(fn => fn.Dependencies, folderNode.Dependencies),
                               Builders<FolderNode>.Update.Set(fn => fn.Dependent, folderNode.Dependent)
                               ));
                        }

                        break;
                    }
                case PackageNode packageNode:
                    {
                        var filter = Builders<PackageNode>.Filter.And(
                           Builders<PackageNode>.Filter.Eq(fn => fn.Self.Path, node.Self.Path)
                           );
                        var found = PackageNodes.Find(filter);
                        if (found == null || found.CountDocuments() == 0)
                        {
                            PackageNodes.InsertOne(packageNode);
                        }
                        else
                        {
                            var result = PackageNodes.UpdateOne(filter, Builders<PackageNode>.Update.Combine(
                               Builders<PackageNode>.Update.Set(fn => fn.Self, packageNode.Self),
                               Builders<PackageNode>.Update.Set(fn => fn.AssetType, packageNode.AssetType),
                               Builders<PackageNode>.Update.Set(fn => fn.Dependencies, packageNode.Dependencies),
                               Builders<PackageNode>.Update.Set(fn => fn.Dependent, packageNode.Dependent)
                               ));
                        }
                        break;
                    }
                case AssetNode assetNode:
                    {
                        var filter = Builders<AssetNode>.Filter.And(
                           Builders<AssetNode>.Filter.Eq(fn => fn.Self.Path, node.Self.Path)
                           );
                        var found = AssetNodes.Find(filter);
                        if (found == null || found.CountDocuments() == 0)
                        {
                            AssetNodes.InsertOne(assetNode);
                        }
                        else
                        {
                            var result = AssetNodes.UpdateOne(filter, Builders<AssetNode>.Update.Combine(
                                Builders<AssetNode>.Update.Set(fn => fn.Self, assetNode.Self),
                                Builders<AssetNode>.Update.Set(fn => fn.AssetType, assetNode.AssetType),
                                Builders<AssetNode>.Update.Set(fn => fn.Dependencies, assetNode.Dependencies),
                                Builders<AssetNode>.Update.Set(fn => fn.Dependent, assetNode.Dependent)
                                ));
                        }
                        break;
                    }
                default:
                    break;
            }
        }

        public void Delete<T>(T node) where T : AssetNode
        {
            switch (node)
            {
                case FolderNode folderNode:
                    {
                        var filter = Builders<FolderNode>.Filter.And(
                            Builders<FolderNode>.Filter.Eq(fn => fn.Self.Path, node.Self.Path)
                            );
                        var found = FolderNodes.Find(filter);
                        if (found != null && found.CountDocuments() == 0)
                        {
                            // TODO: del ref dep
                            FolderNodes.DeleteOne(filter);
                        }
                        break;
                    }
                case PackageNode packageNode:
                    {
                        var filter = Builders<PackageNode>.Filter.And(
                          Builders<PackageNode>.Filter.Eq(fn => fn.Self.Path, node.Self.Path)
                          );
                        var found = PackageNodes.Find(filter);
                        if (found != null && found.CountDocuments() == 0)
                        {
                            // TODO: del ref dep
                            PackageNodes.DeleteOne(filter);
                        }
                        break;
                    }
                case AssetNode assetNode:
                    {
                        var filter = Builders<AssetNode>.Filter.And(
                        Builders<AssetNode>.Filter.Eq(fn => fn.Self.Path, node.Self.Path)
                        );
                        var found = AssetNodes.Find(filter);
                        if (found != null && found.CountDocuments() == 0)
                        {
                            // TODO: del ref dep
                            AssetNodes.DeleteOne(filter);
                        }
                        break;
                    }
                default:
                    break;
            }
        }

        public AssetNode Find(string path)
        {
            if(findCacheDic.TryGetValue(path, out var assetNode))
            {
                return assetNode;
            }

            var filter = Builders<AssetNode>.Filter.And(
                           Builders<AssetNode>.Filter.Eq(fn => fn.Self.Path, path)
                           );
            var found = AssetNodes.Find(filter);
            if (found != null && found.CountDocuments() != 0)
            {
                assetNode = found.First();
                findCacheDic[path] = assetNode;
                return assetNode;
            }
            
            var filter1 = Builders<PackageNode>.Filter.And(
                          Builders<PackageNode>.Filter.Eq(fn => fn.Self.Path, path)
                          );
            var found1 = PackageNodes.Find(filter1);
            if (found1 != null && found1.CountDocuments() != 0)
            {
                assetNode = found1.First();
                findCacheDic[path] = assetNode;
                return assetNode;
            }

            var filter2 = Builders<FolderNode>.Filter.And(
                          Builders<FolderNode>.Filter.Eq(fn => fn.Self.Path, path)
                          );
            var found2 = FolderNodes.Find(filter2);
            if (found2 != null && found2.CountDocuments() != 0)
            {
                assetNode = found2.First();
                findCacheDic[path] = assetNode;
                return assetNode;
            }

            return null!;
        }
    }
}
