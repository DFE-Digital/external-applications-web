using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Linq;
using System.Text;
using DfE.ExternalApplications.Domain.Models;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Services

{
    [ExcludeFromCodeCoverage]
    public class FieldViewModel
    {
        public Field Field { get; }
        public string Prefix { get; }
        public string CurrentValue { get; }

        public string ErrorMessage { get; }

        public FieldViewModel(Field field, string prefix, string currentValue, string errorMessage)
        {
            Field = field;
            Prefix = prefix;
            CurrentValue = currentValue;
            ErrorMessage = errorMessage;
        }

        public string Name => $"{Prefix}[{Field.FieldId}]";
        public string Id => $"{Prefix}_{Field.FieldId}";

        // Builds data-val attributes for client-side validation, including conditional rules
        public string ValidationAttributes
        {
            get
            {
                if (Field.Validations == null || !Field.Validations.Any())
                    return string.Empty;

                var sb = new StringBuilder();
                sb.Append(" data-val=\"true\"");

                foreach (var v in Field.Validations)
                {
                    // Emit conditional metadata if needed
                    if (v.Condition != null)
                    {
                        sb.Append($" data-val-cond-field=\"{v.Condition.TriggerField}\"");
                        sb.Append($" data-val-cond-operator=\"{v.Condition.Operator}\"");
                        sb.Append($" data-val-cond-value=\"{v.Condition.Value}\"");
                    }

                    switch (v.Type)
                    {
                        case "required":
                            sb.Append($" data-val-required=\"{v.Message}\"");
                            break;
                        case "regex":
                            sb.Append($" data-val-regex=\"{v.Message}\" data-val-regex-pattern=\"{v.Rule}\"");
                            break;
                        case "maxLength":
                            sb.Append($" data-val-maxlength=\"{v.Message}\" data-val-maxlength-max=\"{v.Rule}\"");
                            break;
                    }
                }

                return sb.ToString();
            }
        }
    }
}
