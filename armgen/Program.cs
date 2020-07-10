using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace armgen
{
    internal static class Program
    {
        private static readonly string Prefix = "vscs";
        private static readonly string[] Components = new[] { "core", "codesp", "collab", "comm" };
        private static readonly string[] Environments = new[] { "dev", "ppe", "prod" };
        private static readonly IDictionary<string, Plane> Planes = new Dictionary<string, Plane> {
            {  "ops", new Plane{ Code = "ops", DisplayName = "Ops", HasInstances = false, HasRegions = false } },
            {  "ctl", new Plane{ Code = "ctl", DisplayName = "Control", HasInstances = true, HasRegions = true } },
            {  "data", new Plane{ Code = "data", DisplayName = "Data", HasInstances = false, HasRegions = true} },
        };
        private static readonly string[] Instances = new string[] { "ci", "rel" };
        private static readonly string[] Regions = new string[] { "ap-se", "us-w2" };

        static void Main(string[] args)
        {
            var templateDir = args.Length == 1 ? args[0] : null;
            var outputDir = args.Length == 2 ? args[1] : null;

            if (outputDir == null)
            {
                outputDir = Path.Combine(Environment.CurrentDirectory, "out");
            }

            var groups = new SortedDictionary<string, IDictionary<string, string>>();

            void AddGroup(IDictionary<string, string> group)
            {
                var id = group["id"];
                groups[id] = group;
            }

            foreach (var component in Components)
            {
                AddGroup(BuildGroup(component));

                foreach (var env in Environments)
                {
                    AddGroup(BuildGroup(component, env));

                    foreach (var planeItem in Planes)
                    {
                        var plane = planeItem.Value;
                        AddGroup(BuildGroup(component, env, plane));

                        if (plane.HasInstances)
                        {
                            foreach (var instance in Instances)
                            {
                                AddGroup(BuildGroup(component, env, plane, instance));

                                foreach (var region in Regions)
                                {
                                    AddGroup(BuildGroup(component, env, plane, instance, region));
                                }
                            }
                        }
                        else if (plane.HasRegions)
                        {
                            foreach (var region in Regions)
                            {
                                AddGroup(BuildGroup(component, env, plane, null, region));
                            }
                        }
                    }
                }
            }

            WriteGroups(groups);
            CreateGroupOutput(templateDir, outputDir, groups);
        }

        public static void WriteGroups(IDictionary<string, IDictionary<string, string>> groups)
        {
            var json = JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            Console.WriteLine();
        }

        public static void CreateGroupOutput(string templateDir, string outputDir, IDictionary<string, IDictionary<string, string>> groups)
        {
            var templateFiles = Enumerable.Empty<string>();
            if (!string.IsNullOrEmpty(templateDir))
            {
                templateFiles = Directory.EnumerateFiles(templateDir);
            }
            var templateFileDimensions = templateFiles.ToDictionary(item => item, item => GetTemplateFileDimensions(item));

            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);

                foreach (var groupItem in groups)
                {
                    var group = groupItem.Value;
                    var groupDimensions = GetGroupDimensions(group);
                    var dir = Path.Combine(outputDir, group["outputPath"]);
                    var nameFile = Path.Combine(outputDir, group["nameFile"]);
                    Directory.CreateDirectory(dir);
                    var json = JsonSerializer.Serialize(group, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(nameFile, json);

                    foreach (var templateFile in templateFileDimensions
                        .Where(item => item.Value.Equals(groupDimensions))
                        .Select(item => item.Key))
                    {
                        var contents = File.ReadAllText(templateFile);
                        contents = ReplaceContents(contents, group);

                        var fileName = Path.GetFileName(templateFile);
                        fileName = ReplaceFilenameDimensions(fileName, group);

                        var outputFile = Path.Combine(dir, fileName);
                        File.WriteAllText(outputFile, contents);
                    }
                }
            }
        }

        public static string ReplaceContents(string contents, IDictionary<string, string> group)
        {
            foreach (var item in group)
            {
                var replaceKey = $"__{item.Key}__";
                var replaceValue = item.Value;
                contents = contents.Replace(replaceKey, replaceValue);
            }

            return contents;
        }

        public static string ReplaceFilenameDimensions(string filename, IDictionary<string, string> group)
        {
            foreach (var key in new[] { "env", "plane", "instance", "region" })
            {
                if (group.TryGetValue(key, out string dimension))
                {
                    filename = filename.Replace($"{{{key}}}", dimension);
                }
            }

            return filename;
        }

        public static Dimensions GetGroupDimensions(IDictionary<string, string> group)
        {
            var env = group.ContainsKey("env");
            var plane = group.ContainsKey("plane");
            var instance = group.ContainsKey("instance");
            var region = group.ContainsKey("region");
            return new Dimensions
            {
                Env = env,
                Plane = plane,
                Instance = instance,
                Region = region
            };
        }

        public static Dimensions GetTemplateFileDimensions(string filename)
        {
            var env = filename.Contains("{env}", StringComparison.OrdinalIgnoreCase);
            var plane = filename.Contains("{plane}", StringComparison.OrdinalIgnoreCase);
            var instance = filename.Contains("{instance}", StringComparison.OrdinalIgnoreCase);
            var region = filename.Contains("{region}", StringComparison.OrdinalIgnoreCase);
            return new Dimensions
            {
                Env = env,
                Plane = plane,
                Instance = instance,
                Region = region
            };
        }

        public static IDictionary<string, string> BuildGroup(
            string component,
            string env = null,
            Plane plane = null,
            string instance = null,
            string region = null)
        {
            var prefix = Prefix;
            var names = new SortedDictionary<string, string>();

            if (!string.IsNullOrEmpty(component))
            {
                names["component"] = component;
                names["baseName"] = component;
                names["id"] = names["baseName"];
                names["outputPath"] = component;

                if (!string.IsNullOrEmpty(env))
                {
                    names["env"] = env;
                    names["outputPath"] = Path.Combine(names["outputPath"], env);
                    names["baseEnvName"] = $"{prefix}-{component}-{env}";
                    names["id"] = names["baseEnvName"];

                    if (plane != null)
                    {
                        var planeCode = plane.Code;
                        names["plane"] = planeCode;
                        names["outputPath"] = Path.Combine(names["outputPath"], planeCode);
                        names["basePlaneName"] = $"{names["baseEnvName"]}-{planeCode}";
                        names["id"] = names["basePlaneName"];

                        if (!string.IsNullOrEmpty(instance))
                        {
                            names["instance"] = instance;
                            names["outputPath"] = Path.Combine(names["outputPath"], instance);
                            names["baseInstanceName"] = $"{names["basePlaneName"]}-{instance}";
                            names["id"] = names["baseInstanceName"];

                            if (!string.IsNullOrEmpty(region))
                            {
                                names["region"] = region;
                                names["outputPath"] = Path.Combine(names["outputPath"], region);
                                names["baseRegionName"] = $"{names["baseInstanceName"]}-{region}";
                                names["id"] = names["baseRegionName"];
                            }
                        }
                        else if (!string.IsNullOrEmpty(region))
                        {
                            names["region"] = region;
                            names["outputPath"] = Path.Combine(names["outputPath"], region);
                            names["baseRegionName"] = $"{names["basePlaneName"]}-{region}";
                            names["id"] = names["baseRegionName"];
                        }
                    }
                }

                names["nameFile"] = Path.Combine(names["outputPath"], "names.json");

                return names;
            }


            return names;
        }
    }

    public class Plane
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public bool HasInstances { get; set; }
        public bool HasRegions { get; set; }
    }

    public class Dimensions : IEquatable<Dimensions>
    {
        public bool Env { get; set; }
        public bool Plane { get; set; }
        public bool Instance { get; set; }
        public bool Region { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is Dimensions other)
            {
                return Equals(other);
            }

            return false;
        }

        public bool Equals([AllowNull] Dimensions other)
        {
            if (other is null) return false;

            return Env == other.Env &&
                Plane == other.Plane &&
                Instance == other.Instance &&
                Region == other.Region;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Env, Plane, Instance, Region);
        }
    }
}
