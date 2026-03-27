using System.Collections.Generic;

namespace PolyglotNotebooks.Models
{
    public class FormattedOutput
    {
        public string MimeType { get; }
        public string Value { get; }
        public bool SuppressDisplay { get; }

        public FormattedOutput(string mimeType, string value, bool suppressDisplay = false)
        {
            MimeType = mimeType;
            Value = value;
            SuppressDisplay = suppressDisplay;
        }
    }

    public class CellOutput
    {
        public CellOutputKind Kind { get; }
        public List<FormattedOutput> FormattedValues { get; }
        public string? ValueId { get; }

        public CellOutput(CellOutputKind kind, List<FormattedOutput> formattedValues, string? valueId = null)
        {
            Kind = kind;
            FormattedValues = formattedValues ?? new List<FormattedOutput>();
            ValueId = valueId;
        }
    }
}
