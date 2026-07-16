using DfE.ExternalApplications.Web.Interfaces;
using HtmlAgilityPack;
using System.Text.Json;

namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationCsvGenerator : IApplicationCsvGenerator
    {
        private readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

        public Stream? Generate(string html)
        {
            HtmlDocument doc = new();
            doc.LoadHtml(html);

            IEnumerable<HtmlNode> tasks = doc.DocumentNode.Descendants().Where(x => x.GetDataAttribute("group") != null);

            if (!tasks.Any())
            {
                return null;
            }

            return new MemoryStream();
        }

        public string? GenerateJson(string html)
        {
            HtmlDocument doc = new();
            doc.LoadHtml(html);

            IEnumerable<HtmlNode> groupNodes = doc.DocumentNode.Descendants().Where(x => x.GetDataAttribute("group") != null);

            if (!groupNodes.Any())
            {
                return null;
            }

            /* Data hierarchy:
            template
            |
            * group
              |
              * task
                |
                * page
                  |
                  * field
            */

            FormTemplateData templateData = new();
            List<FormTemplateData.Group> groups = [];
            foreach (var groupNode in groupNodes) 
            {
                FormTemplateData.Group group = new()
                {
                    Name = groupNode.GetDataAttribute("group").Value,
                    Tasks = groupNode.Descendants().Where(x => x.GetDataAttribute("task") != null).Select(taskNode => new FormTemplateData.Task
                    {
                        Name = taskNode.GetDataAttribute("task").Value,
                        Pages = taskNode.Descendants().Where(x => x.GetDataAttribute("page") != null).Select(pageNode => new FormTemplateData.Page
                        {
                            Name = pageNode.GetDataAttribute("page").Value,
                            Fields = pageNode.Descendants().Where(x => x.GetDataAttribute("field") != null).Select(fieldNode => new FormTemplateData.Field
                            {
                                Name = fieldNode.GetDataAttribute("field").Value,
                                Value = fieldNode.Descendants("dd").SingleOrDefault()?.InnerText.Trim()
                            })
                        })
                    })
                };
                groups.Add(group);
            }
            templateData.Groups = groups;

            return JsonSerializer.Serialize(templateData, serializerOptions);
        }
    }

    public class FormTemplateData
    {
        public IEnumerable<Group>? Groups { get; set; }

        public class Group
        {
            public string? Name { get; set; }
            public IEnumerable<Task>? Tasks { get; set; }
        }

        public class Task
        {
            public string? Name { get; set; }
            public IEnumerable<Page>? Pages { get; set; }
        }

        public class Page
        {
            public string? Name { get; set; }
            public IEnumerable<Field>? Fields { get; set; }
        }

        public class Field
        {
            public string? Name { get; set; }
            public string? Value { get; set; }
        }
    }
}
