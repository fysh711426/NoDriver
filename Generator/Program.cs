using Generator.Models;
using System.Text.Json;

namespace Generator
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var rootPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var outputDir = Path.Combine(rootPath, "NoDriver", "Cdp");

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            Console.WriteLine("Fetching Protocol Specs...");

            var domains = new List<Domain>();

            var files = new List<string>()
            {
                "browser_protocol.json",
                "js_protocol.json"
            };

            using (var httpClient = new HttpClient())
            {
                foreach (var file in files)
                {
                    var url = $"https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/json/{file}";

                    var json = await httpClient.GetStringAsync(url);

                    var jsonOptions = new JsonSerializerOptions()
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    var domain = JsonSerializer.Deserialize<Definition>(json, jsonOptions)?.Domains ?? new();

                    domains.AddRange(domain);
                }
            }

            Console.WriteLine("Generating C# Code...");

            domains = domains.OrderBy(it => it.Name).ToList();

            foreach (var domain in domains)
            {
                var code = CdpGenerator.GenerateCode(domain);

                var filePath = Path.Combine(outputDir, $"{domain.Name}.cs");

                File.WriteAllText(filePath, code);

                Console.WriteLine($"Generated {domain.Name}.cs");
            }

            Console.WriteLine($"Done! Code generated in {outputDir}");
        }
    }
}
