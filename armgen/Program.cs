using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace armgen
{
    internal static class Program
    {
        public class Plane
        {
            public string Code { get; set; }
            public string DisplayName { get; set; }
            public bool HasInstances { get; set; }
            public bool HasRegions { get; set; }
        }

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
            var outputDir = args.FirstOrDefault();
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
            CreateGroupOutput(outputDir, groups);
        }

        public static void WriteGroups(IDictionary<string, IDictionary<string, string>> groups)
        {
            var json = JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            Console.WriteLine();
        }

        public static void CreateGroupOutput(string outputDir, IDictionary<string, IDictionary<string, string>> groups)
        {
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);

                foreach (var groupItem in groups)
                {
                    var group = groupItem.Value;
                    var dir = Path.Combine(outputDir, group["outputPath"]);
                    var nameFile = Path.Combine(outputDir, group["nameFile"]);
                    Directory.CreateDirectory(dir);
                    var json = JsonSerializer.Serialize(group, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(nameFile, json);
                }
            }
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
}
