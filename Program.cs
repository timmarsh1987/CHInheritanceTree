using System.Text;
using Newtonsoft.Json;
using Stylelabs.M.Sdk.WebClient;
using Stylelabs.M.Sdk.WebClient.Authentication;
using Stylelabs.M.Sdk.WebClient.Http;
using Stylelabs.M.Base.Web.Api.Models;
using CHInheritanceTree;

namespace Stylelabs.M.WebSdk.Examples
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            Console.WriteLine("Content Hub - Schema Visualiser - Timothy Marsh");

            var startup = new Startup();

            // Your Sitecore Content Hub endpoint to connect to
            Uri url = new Uri(startup.ApiSettings.Url);
            OAuthPasswordGrant oauth = new OAuthPasswordGrant
            {
                ClientId = startup.ApiSettings.ClientId,
                ClientSecret = startup.ApiSettings.ClientSecret,
                UserName = startup.ApiSettings.UserName,
                Password = startup.ApiSettings.Password
            };

            Console.WriteLine($"Connecting to Content Hub - {url}");

            IWebMClient? client = null;

            try
            {
                client = MClientFactory.CreateMClient(url, oauth);
                Console.WriteLine($"Successfully connected to Content Hub - {url}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed");
                Console.WriteLine(ex);
                Console.WriteLine(ex.Message);
                throw;
            }

            var endPointLink = new Link(url + "api/entitydefinitions?includeConditionalMembers=True", "", true);
            var bindings = new Dictionary<string, string>();
            var endpoint = endPointLink.Bind(bindings);
            var keepGoing = true;
            var result = new List<EntityDefinition>();

            Console.WriteLine($"Getting all entity definitions");

            while (keepGoing)
            {
                var response = await client.Raw.GetAsync(endpoint).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var businessAuditQuery = await response.Content.ReadAsJsonAsync<EntityDefinitionQuery>().ConfigureAwait(false);

                result.AddRange(businessAuditQuery.Items);
                endpoint = businessAuditQuery?.Next?.Uri;
                keepGoing = businessAuditQuery?.Items?.Any() == true && businessAuditQuery.Next != null;
            }

            Console.WriteLine($"Found {result.Count} entity definitions");

            StringBuilder sb = new StringBuilder();
            StringBuilder relations = new StringBuilder();

            Dictionary<string, List<Tuple<string, string>>> tableMapping = new Dictionary<string, List<Tuple<string, string>>>();

            foreach (var entity in result.OrderBy(e => e.Name))
            {
                if (!tableMapping.ContainsKey(entity.Name.Replace(".", "")))
                {
                    Console.WriteLine($" > Processing '{entity.Name}'");
                    tableMapping.Add(entity.Name.Replace(".", ""), new List<Tuple<string, string>> { });
                }

                foreach (var member in entity.MemberGroups)
                {
                    foreach (var relation in member.Relations.Where(x => !x.IsSystemOwned).Distinct().GroupBy(p => p.Name).Select(g => g.First()))
                    {
                        if (!tableMapping[entity.Name.Replace(".", "")].Any(x => x.Item1 == relation.Name.Replace(".", "")))
                        {
                            tableMapping[entity.Name.Replace(".", "")].Add(new Tuple<string, string>(relation.Name.Replace(".", ""), relation?.Type == "Relation" ? relation.Definition.href.Split('/').Last().Replace(".", "") : relation.Type));
                        }

                        if (relation?.Type == "Relation")
                        {
                            var relationTable = relation.Definition.href.Split('/').Last().Replace(".", "");

                            if (!tableMapping.ContainsKey(relationTable))
                            {
                                tableMapping.Add(relationTable, new List<Tuple<string, string>> { });
                                tableMapping[relationTable].Add(new Tuple<string, string>(relation.Name.Replace(".", ""), entity.Name.Replace(".", "")));
                            }
                        }
                    }
                }
            }

            foreach (var entity in tableMapping.OrderBy(x => x.Key))
            {
                sb.AppendLine(entity.Key);

                foreach (var item in entity.Value.OrderBy(y => y))
                {
                    switch (item.Item2)
                    {
                        case "String":
                        case "Boolean":
                        case "Json":
                        case "Integer":
                        case "Long":
                        case "DateTime":
                        case "Decimal":
                        case "DateTimeOffset":
                            sb.AppendLine("  " + item.Item1 + " " + item.Item2);
                            break;

                        default:
                            sb.AppendLine("  " + item.Item1 + " relation fk " + item.Item2 + "." + item.Item1);
                            break;
                    }
                }

                if (entity.Key == "MAction")
                {
                    sb.AppendLine("  ActionToStateMachine relation fk MAutomationStateMachine.ActionToStateMachine");
                    sb.AppendLine("  ActionToScript relation fk MScript.ActionToScript");
                    sb.AppendLine("  DetailsPageToAction relation fk PortalPage.DetailsPageToAction");
                    sb.AppendLine("  BlockToDeliverablesLifecycleStatus relation fk MProjectDeliverablesLifecycleStatus.BlockToDeliverablesLifecycleStatus");
                    sb.AppendLine("  TaskToDeliverablesLifecycleStatus relation fk MProjectDeliverablesLifecycleStatus.TaskToDeliverablesLifecycleStatus");
                }

                if (entity.Key == "MProjectBlock")
                {
                    sb.AppendLine("  BlockToDeliverablesLifecycleStatus relation fk MProjectDeliverablesLifecycleStatus.BlockToDeliverablesLifecycleStatus");
                }

                if (entity.Key == "MProjectTask")
                {
                    sb.AppendLine("  TaskToDeliverablesLifecycleStatus relation fk MProjectDeliverablesLifecycleStatus.TaskToDeliverablesLifecycleStatus");
                }

                sb.AppendLine();
            }

            Console.WriteLine("----------Output----------");
            Console.WriteLine(sb.ToString() + relations.ToString());
            Console.WriteLine("--------------------------");

            // Write the changes to file
            FileStream fileStream;
            StreamWriter writer;
            try
            {
                fileStream = new FileStream("./CHInheritanceTreeOutput.txt", FileMode.OpenOrCreate, FileAccess.Write);
                writer = new StreamWriter(fileStream);
            }
            catch (Exception e)
            {
                Console.WriteLine("Cannot open CHInheritanceTreeOutput.txt for writing");
                Console.WriteLine(e.Message);
                return;
            }

            writer.Write(sb.ToString());
            writer.Close();

            fileStream.Close();

            Console.WriteLine("File saved here - " + fileStream.Name);
            Console.WriteLine("Upload to new document using - https://azimutt.app/");
        }

        public class EntityDefinitionQuery
        {
            [JsonProperty(PropertyName = "items")]
            public List<EntityDefinition> Items { get; set; }

            [JsonProperty(PropertyName = "total_items")]
            public int TotalItems { get; set; }

            [JsonProperty(PropertyName = "returned_items")]
            public int ReturnedItems { get; set; }

            [JsonProperty(PropertyName = "next")]
            public Link Next { get; set; }
        }

        public class EntityDefinition
        {
            [JsonProperty(PropertyName = "id")]
            public string Id { get; set; }

            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "member_groups")]
            public List<MemberGroups> MemberGroups { get; set; }
        }

        public class MemberGroups
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "members")]
            public List<PropertyRelations> Relations { get; set; }
        }

        public class PropertyRelations
        {
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }

            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "role")]
            public string Role { get; set; }

            [JsonProperty(PropertyName = "cardinality")]
            public string Cardinality { get; set; }

            [JsonProperty(PropertyName = "associated_entitydefinition")]
            public AssociatedDefinition Definition { get; set; }

            [JsonProperty(PropertyName = "allow_navigation")]
            public bool AllowNAvigation { get; set; }

            [JsonProperty(PropertyName = "is_taxonomy_relation")]
            public bool IsTaxonomyRelation { get; set; }

            [JsonProperty(PropertyName = "content_is_copied")]
            public bool ContentIsCopied { get; set; }

            [JsonProperty(PropertyName = "is_system_owned")]
            public bool IsSystemOwned { get; set; }

            [JsonProperty(PropertyName = "is_nested")]
            public bool IsNested { get; set; }
        }

        public class AssociatedDefinition
        {
            [JsonProperty(PropertyName = "href")]
            public string href { get; set; }
        }
    }
}