using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using Mono.Cecil;

namespace ThunderstorePurge
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new HttpClient();

            var result = client.GetAsync("https://thunderstore.io/api/v1/package").Result;

            result.EnsureSuccessStatusCode();

            //System.IO.File.WriteAllText("cache.json", result.Content.ReadAsStringAsync().Result);

            List<JObject> allPackages = JArray.Parse(result.Content.ReadAsStringAsync().Result).Select(x => (JObject)x).ToList();

            List<JObject> noModpacks = allPackages.Where(x => !x.GetValue("categories").Value<JArray>().Any(y => y.Value<string>() == "Modpacks")).ToList();

            List<JObject> noDeprecated = noModpacks.Where(x => !x.GetValue("is_deprecated").Value<bool>()).ToList();

            List<JObject> oldPackages = noDeprecated.Where(x => x.GetValue("date_updated").Value<DateTime>().Ticks < new DateTime(2021, 3, 25, 0, 0, 0, 0, DateTimeKind.Utc).Ticks).ToList();

            List<JObject> r2apiPackages = oldPackages
                .Where(x => ((JObject)x.GetValue("versions").Value<JArray>()[0]).GetValue("dependencies").Value<JArray>()
                    .Any(y => y.Value<string>().Contains("tristanmcpherson-R2API")))
                .ToList();

            /*
            var package = r2apiPackages[0];
            TestPackage(package["full_name"].Value<string>(), ((JObject)package["versions"].Value<JArray>()[0])["download_url"].Value<string>());
            */

            try
            {
                Parallel.ForEach(r2apiPackages, (package) => TestPackage(package["full_name"].Value<string>(), ((JObject)package["versions"].Value<JArray>()[0])["download_url"].Value<string>()));
            } catch (Exception e)
            {
                Console.WriteLine(e);
                Console.ReadLine();
                return;
            }
            
            File.WriteAllLines("broken mods.txt", brokenModNames.ToArray());
            //Directory.Delete("TempArchives", true);
        }

        static ConcurrentBag<string> brokenModNames = new ConcurrentBag<string>();

        static string[] BrokenAPIs = new string[]
        {
            "ItemAPI",
            "ItemDropAPI",
            "SkinAPI",
            "SurvivorAPI",
            "LoadoutAPI",
            "SkillAPI",
            "BuffAPI",
            "MonsterItemsAPI",
            "UnlockablesAPI",
            "EliteAPI",
            "AssetAPI"
        };

        public static void TestPackage(string fullName, string downloadUrl)
        {
            try
            {
                Console.WriteLine($"Testing {fullName}");

                var zipDir = Path.Combine("TempArchives", fullName);

                using var client = new HttpClient();
                using var stream = client.GetStreamAsync(downloadUrl).Result;
                using var zip = new ZipArchive(stream);

                zip.ExtractToDirectory(zipDir);

                bool isBroken = false;

                foreach (var dll in Directory.GetFiles(zipDir, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(dll))
                        {
                            foreach (var type in asm.MainModule.Types)
                            {
                                var r2apiAtt = type.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "R2API.Utils.R2APISubmoduleDependency");
                                if (r2apiAtt == default(CustomAttribute))
                                    continue;

                                isBroken |= (r2apiAtt.ConstructorArguments[0].Value as CustomAttributeArgument[]).Select(x => x.Value as string).Intersect(BrokenAPIs).Any();
                            }
                        }
                    }
                    catch (BadImageFormatException) { }
                }

                if (isBroken)
                    brokenModNames.Add(fullName);

                //Directory.Delete(zipDir, true);

                Console.WriteLine($"{fullName} broken? {isBroken}");
            } catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
