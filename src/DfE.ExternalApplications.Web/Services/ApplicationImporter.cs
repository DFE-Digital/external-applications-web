using DfE.ExternalApplications.Web.Interfaces;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Diagnostics;

namespace DfE.ExternalApplications.Web.Services
{
    public class ApplicationImporter : IApplicationImporter
    {
        private SharedStringTable? sst;

        public ApplicationImportResult ImportSpreadsheet(Guid templateId, Stream stream)
        {
            // TODO : Implement the logic to import the spreadsheet based on the templateId and stream
            Debug.WriteLine($"Importing spreadsheet for templateId: {templateId}, stream length: {stream.Length}.");

            var fieldCount = 0;

            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(stream, false))
            {
                Debug.WriteLine($"Spreadsheet opened. Document type: {doc.DocumentType}, WorkbookPart: {doc.WorkbookPart != null}.");
                WorkbookPart? workbookPart = doc.WorkbookPart;
                if (workbookPart == null)
                {
                    Debug.WriteLine("WorkbookPart is null.");
                    return new ApplicationImportResult { Success = false, Errors = ["WorkbookPart is null."] };
                }

                SharedStringTablePart sstpart = workbookPart.GetPartsOfType<SharedStringTablePart>().First();
                sst = sstpart.SharedStringTable;

                Debug.WriteLine($"WorkbookPart retrieved. WorksheetParts count: {workbookPart.WorksheetParts.Count()}.");
                WorksheetPart worksheetPart = workbookPart.WorksheetParts.First();
                if (worksheetPart == null)
                {
                    Debug.WriteLine("WorksheetPart is null.");
                    return new ApplicationImportResult { Success = false, Errors = ["WorksheetPart is null."] };
                }

                Worksheet? sheet = worksheetPart.Worksheet;
                if (sheet == null)
                {
                    Debug.WriteLine("Worksheet is null.");
                    return new ApplicationImportResult { Success = false, Errors = ["Worksheet is null."] };
                }

                var rows = sheet.Descendants<Row>();
                Debug.WriteLine($"Found {rows.Count()} rows in the worksheet.");
                if (rows.Count() != 2)
                {
                    return new ApplicationImportResult { Success = false, Errors = ["The worksheet does not contain exactly 2 rows (header & data)."] };
                }

                Row headerRow = rows.First();
                Row dataRow = rows.Last();

                IEnumerable<string> headerCellValues = GetCellValues(headerRow);
                IEnumerable<string> dataCellValues = GetCellValues(dataRow);
                if (headerCellValues.Count() != dataCellValues.Count())
                {
                    return new ApplicationImportResult { Success = false, Errors = ["Header and data row cell counts do not match."] };
                }

                fieldCount = headerCellValues.Count();

                IEnumerable<KeyValuePair<string, string>> keyValuePairs = headerCellValues.Zip(dataCellValues, (key, value) => new KeyValuePair<string, string>(key, value));
                foreach (var kvp in keyValuePairs)
                {
                    Debug.WriteLine($"Field: {kvp.Key}, Value: {kvp.Value}");
                }

                // TODO create application from key value pairs
            }

            return new ApplicationImportResult { Success = true, FieldCount = fieldCount };
        }

        private IEnumerable<string> GetCellValues(Row dataRow)
        {
            var dataCellValues = new List<string>();
            foreach (var c in dataRow.Elements<Cell>())
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
                dataCellValues.Add(cellValue);
            }
            return [.. dataCellValues];
        }
    }
}
