using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Interfaces;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using System.Diagnostics;

// TODO refactor code to make SOLID

namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationImporter(ITemplateManagementService templateManagementService, IApplicationsClient applicationsClient) : IApplicationImporter
    {
        private SharedStringTable? sst;

        public async Task<ApplicationImportResult> ImportSpreadsheet(Guid templateId, Stream stream)
        {
            Debug.WriteLine($"Importing spreadsheet for templateId: {templateId}, stream length: {stream.Length}.");

            IDictionary<string, string>? fields = GetSpreadsheetFields(stream, out string? error);
            if (fields == null)
            {
                return new ApplicationImportResult { Errors = [ $"Failed to get spreadsheet fields: {error}" ] };
            }

            FormTemplate formTemplate = await templateManagementService.LoadTemplateAsync(templateId.ToString());
            if (formTemplate == null)
            {
                return new ApplicationImportResult { Errors = [$"Template not found ({templateId})"] };
            }

            IEnumerable<KeyValuePair<string, string>> matchedFields = fields!.Where(f => formTemplate.TaskGroups.Any(tg => tg.Tasks.Any(t => t.Pages!.Any(page => page.Fields.Any(field => field.FieldId == f.Key)))));
            if (!matchedFields.Any())
            {
                return new ApplicationImportResult { Errors = ["No matching fields found in the template."] };
            }

            List<string> matchingErrors = [];
            foreach (var field in fields)
            {
                KeyValuePair<string, string> matchingField = matchedFields.FirstOrDefault(f => f.Key == field.Key);
                if (matchingField.Equals(default(KeyValuePair<string, string>)))
                {
                    matchingErrors.Add($"No matching field found in the template for '{field.Key}'");
                }
            }
            if (matchingErrors.Count > 0) 
            { 
                return new ApplicationImportResult { Errors = matchingErrors };
            }

            // TODO use a transaction to create & submit application?

            // TODO: Construct the response body JSON based on the matched fields and their values
            string responseBody = string.Empty;
            CreateApplicationRequest request = new()
            {
                TemplateId = templateId,
                InitialResponseBody = responseBody
            };
            ApplicationDto createResponse = await applicationsClient.CreateApplicationAsync(request);
            if (createResponse == null || createResponse.Status != ApplicationStatus.Created)
            {
                return new ApplicationImportResult { Errors = ["Failed to create application"] };
            }

            // TODO IApplicationsClient.AddApplicationResponseAsync?

            ApplicationDto submitResponse = await applicationsClient.SubmitApplicationAsync(createResponse.ApplicationId);
            if (submitResponse == null || submitResponse.Status != ApplicationStatus.Submitted)
            {
                return new ApplicationImportResult { Errors = ["Failed to submit application"] };
            }

            return new ApplicationImportResult { Success = true, FieldCount = fields.Count() };
        }

        public async Task<ApplicationImportResult> ImportSpreadsheet2(Guid templateId, FileStream stream)
        {
            Debug.WriteLine($"Importing spreadsheet for templateId: {templateId}, stream length: {stream.Length}.");

            FormTemplate template = await templateManagementService.LoadTemplateAsync(templateId.ToString());
            if (template == null)
            {
                return new ApplicationImportResult { Errors = [$"Template not found ({templateId})"] };
            }

            // TODO get spreadsheet data using mapping
            Dictionary<string, string> mapping = new()
            {
                { "B1", "start-year" },
                { "B2", "end-year" },
                { "B3", "local-authority" }
            };
            IDictionary<string, string?>? fields = GetSpreadsheetFields2(stream, "Sheet1", mapping, out IList<string> errors);
            if (fields == null || fields.Count == 0)
            {
                return new ApplicationImportResult { Errors = [ $"Failed to get spreadsheet fields: {string.Join(", ", errors)}" ] };
            }

            ApplicationImport applicationImport = BuildApplicationImport(fields, template);
            if (applicationImport.Errors != null && applicationImport.Errors.Any())
            {
                return new ApplicationImportResult { Errors = applicationImport.Errors };
            }

            return new ApplicationImportResult { Success = true, FieldCount = fields.Count };
        }

        private static Dictionary<string, string?>? GetSpreadsheetFields2(FileStream stream, string sheet, Dictionary<string, string> mapping, out IList<string> errors)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false);

            WorkbookPart? wbPart = document.WorkbookPart;

            errors = [];

            Sheet? theSheet = wbPart?.Workbook?.Descendants<Sheet>().Where(s => s.Name == sheet).FirstOrDefault();
            if (theSheet is null || theSheet.Id is null)
            {
                errors.Add($"Sheet '{sheet}' not found in the spreadsheet.");
                return default;
            }

            WorksheetPart wsPart = (WorksheetPart)wbPart!.GetPartById(theSheet.Id!);

            Dictionary<string, string?> fields = [];

            foreach (var kvp in mapping)
            {
                Cell? theCell = wsPart.Worksheet?.Descendants<Cell>()?.Where(c => c.CellReference == kvp.Key).FirstOrDefault();
                if (theCell is null)
                {
                    errors.Add($"Cell '{kvp.Key}' not found in the worksheet.");
                    continue;
                }
                string? cellValue;
                if (theCell is null || theCell.InnerText.Length < 0)
                {
                    fields.Add(kvp.Key, null);
                    continue;
                }
                cellValue = theCell.InnerText;
                if (theCell.DataType is not null)
                {
                    if (theCell.DataType.Value == CellValues.SharedString)
                    {
                        var stringTable = wbPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
                        if (stringTable is not null)
                        {
                            cellValue = stringTable.SharedStringTable!.ElementAt(int.Parse(cellValue)).InnerText;
                        }
                    }
                    else if (theCell.DataType.Value == CellValues.Boolean)
                    {
                        cellValue = cellValue switch
                        {
                            "0" => "FALSE",
                            _ => "TRUE",
                        };
                    }
                }

                Debug.WriteLine($"Cell {kvp.Key}: {cellValue}");
                fields.Add(kvp.Key, cellValue);
            }

            return fields;
        }

        private static ApplicationImport BuildApplicationImport(IDictionary<string, string?> fields, FormTemplate template)
        {
            Dictionary<string, string> fieldMapping = []; // TODO get from template or external source

            List<string> warnings = [];
            List<string> errors = [];
            List<dynamic> responseFields = [];
            foreach (var field in fields)
            {
                var matchedField = template.TaskGroups
                    .SelectMany(tg => tg.Tasks)
                    .SelectMany(t => t.Pages!)
                    .SelectMany(p => p.Fields)
                    .FirstOrDefault(f => f.FieldId == field.Key);

                if (matchedField == null)
                {
                    errors.Add($"No single matching field found in the template for field '{field.Key}'"); 
                    continue;
                }

                // TODO construct the response field JSON based on the matched field and its value
                var responseField = new
                {
                    FieldId = field.Key,
                    Value = field.Value
                };
                responseFields.Add(responseField);
            }

            // TODO construct the response body JSON based on the matched fields and their values

            return new ApplicationImport()
            {
                Warnings = warnings,
                Errors = errors,
                ResponseBody = null
            };
        }

        private Dictionary<string, string>? GetSpreadsheetFields(Stream stream, out string? error)
        {
            using SpreadsheetDocument doc = SpreadsheetDocument.Open(stream, false);

            Debug.WriteLine($"Spreadsheet opened. Document type: {doc.DocumentType}, WorkbookPart: {doc.WorkbookPart != null}.");
            WorkbookPart? workbookPart = doc.WorkbookPart;
            if (workbookPart == null)
            {
                error = "WorkbookPart is null.";
                return null;
            }

            SharedStringTablePart sstpart = workbookPart.GetPartsOfType<SharedStringTablePart>().First();
            sst = sstpart.SharedStringTable;

            Debug.WriteLine($"WorkbookPart retrieved. WorksheetParts count: {workbookPart.WorksheetParts.Count()}.");
            WorksheetPart worksheetPart = workbookPart.WorksheetParts.First();
            if (worksheetPart == null)
            {
                error = "WorksheetPart is null.";
                return null;
            }

            Worksheet? sheet = worksheetPart.Worksheet;
            if (sheet == null)
            {
                error = "Worksheet is null.";
                return null;
            }

            var rows = sheet.Descendants<Row>();
            Debug.WriteLine($"Found {rows.Count()} rows in the worksheet.");
            if (rows.Count() != 2)
            {
                error = "The worksheet does not contain exactly 2 rows (header & data).";
                return null;
            }

            Row headerRow = rows.First();
            Row dataRow = rows.Last();

            IEnumerable<string> headerCellValues = GetCellValues(headerRow);
            IEnumerable<string> dataCellValues = GetCellValues(dataRow);
            if (headerCellValues.Count() != dataCellValues.Count())
            {
                error = "Header and data row cell counts do not match.";
                return null;
            }

            error = null;
            return headerCellValues.Zip(dataCellValues, (key, value) => new KeyValuePair<string, string>(key, value)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private IEnumerable<string> GetCellValues(Row dataRow)
        {
            List<string> dataCellValues = [];
            foreach (Cell c in dataRow.Elements<Cell>())
            {
                if (c.CellValue == null)
                {
                    Debug.WriteLine($"Cell is empty.");
                    continue;
                }
                string cellValue;
                if ((c.DataType != null) && (c.DataType == CellValues.SharedString))
                {
                    int ssid = int.Parse(c.CellValue.Text);
                    string str = sst!.ChildElements[ssid].InnerText;
                    cellValue = str;
                }
                else
                {
                    cellValue = c.CellValue.Text;
                }
                Debug.WriteLine($"Processing cell: {c.CellReference}, DataType: {c.DataType}, CellValue: {cellValue}");
                dataCellValues.Add(cellValue);
            }
            return [.. dataCellValues];
        }
    }

    internal class ApplicationImport
    {
        public string? ResponseBody { get; set; }
        public IEnumerable<string>? Warnings { get; set; }
        public IEnumerable<string>? Errors { get; set; }
    }
}
