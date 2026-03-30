using System.ComponentModel;
using System.Runtime.InteropServices;

using Community.VisualStudio.Toolkit;

namespace PolyglotNotebooks.Options
{
    internal partial class PolyglotNotebooksOptions : BaseOptionModel<PolyglotNotebooksOptions>
    {
        // General
        [Category("General")]
        [DisplayName("Default kernel")]
        [Description("The default language kernel for new code cells")]
        [DefaultValue(DefaultKernel.CSharp)]
        [TypeConverter(typeof(EnumConverter))]
        public DefaultKernel DefaultKernel { get; set; } = DefaultKernel.CSharp;

        [Category("General")]
        [DisplayName("Default file format")]
        [Description("The default file format when creating new notebooks")]
        [DefaultValue(DefaultFileFormat.Dib)]
        [TypeConverter(typeof(EnumConverter))]
        public DefaultFileFormat DefaultFileFormat { get; set; } = DefaultFileFormat.Dib;

        [Category("General")]
        [DisplayName("Clear outputs on kernel restart")]
        [Description("Automatically clear all cell outputs when the kernel is restarted")]
        [DefaultValue(true)]
        public bool ClearOutputsOnRestart { get; set; } = true;

        [Category("General")]
        [DisplayName("Auto-save before execution")]
        [Description("Automatically save the notebook before running cells")]
        [DefaultValue(false)]
        public bool AutoSaveBeforeRun { get; set; } = false;

        // Editor
        [Category("Editor")]
        [DisplayName("Show execution timing")]
        [Description("Display how long each cell took to execute")]
        [DefaultValue(true)]
        public bool ShowExecutionTiming { get; set; } = true;

        [Category("Editor")]
        [DisplayName("Show cell status indicators")]
        [Description("Show kernel name and execution state for each cell")]
        [DefaultValue(true)]
        public bool ShowCellStatusIndicators { get; set; } = true;

        [Category("Editor")]
        [DisplayName("Enable Mermaid diagrams")]
        [Description("Render Mermaid diagram syntax in output cells")]
        [DefaultValue(true)]
        public bool EnableMermaidDiagrams { get; set; } = true;

        // Execution
        [Category("Execution")]
        [DisplayName("Kernel startup timeout (seconds)")]
        [Description("Maximum time to wait for the kernel to start")]
        [DefaultValue(30)]
        public int KernelStartupTimeoutSeconds { get; set; } = 30;

        [Category("Execution")]
        [DisplayName("Maximum output length (characters)")]
        [Description("Truncate cell output after this many characters")]
        [DefaultValue(50000)]
        public int MaxOutputLength { get; set; } = 50000;

        [Category("Execution")]
        [DisplayName("Maximum image width (pixels)")]
        [Description("Scale down images wider than this value")]
        [DefaultValue(800)]
        public int MaxImageWidth { get; set; } = 800;

        [Category("Execution")]
        [DisplayName("Cell execution timeout (seconds)")]
        [Description("Automatically cancel a cell if it runs longer than this many seconds. Set to 0 to disable.")]
        [DefaultValue(0)]
        public int CellExecutionTimeoutSeconds { get; set; } = 0;
    }

    public enum DefaultKernel
    {
        [Description("C#")]
        CSharp,
        [Description("F#")]
        FSharp,
        [Description("JavaScript")]
        JavaScript,
        [Description("SQL")]
        SQL,
        [Description("PowerShell")]
        PowerShell,
        [Description("HTML")]
        HTML
    }

    public enum DefaultFileFormat
    {
        [Description(".dib (Polyglot Notebook)")]
        Dib,
        [Description(".ipynb (Jupyter Notebook)")]
        Ipynb
    }
}

namespace PolyglotNotebooks
{
    /// <summary>
    /// Provides the dialog page instances for the Tools → Options registration.
    /// </summary>
    internal partial class OptionsProvider
    {
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<Options.PolyglotNotebooksOptions> { }
    }
}
