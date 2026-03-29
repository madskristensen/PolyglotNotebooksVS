namespace PolyglotNotebooks.Models
{
    public enum NotebookFormat { Dib, Ipynb }

    public enum CellKind { Code, Markdown }

    public enum CellOutputKind { ReturnValue, StandardOutput, StandardError, Display, Error }

    public enum CellExecutionStatus { Idle, Running, Succeeded, Failed, Queued }
}
