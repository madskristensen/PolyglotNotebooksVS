using Microsoft.VisualStudio.Shell;

using PolyglotNotebooks;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: ProvideBindingRedirection(
    AssemblyName = "Microsoft.Web.WebView2.Core",
    NewVersion = "1.0.3856.49",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "1.0.3856.49")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "Microsoft.Web.WebView2.Wpf",
    NewVersion = "1.0.3856.49",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "1.0.3856.49")]

[assembly: AssemblyTitle(Vsix.Name)]
[assembly: AssemblyDescription(Vsix.Description)]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany(Vsix.Author)]
[assembly: AssemblyProduct(Vsix.Name)]
[assembly: AssemblyCopyright(Vsix.Author)]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion(Vsix.Version)]
[assembly: AssemblyFileVersion(Vsix.Version)]
[assembly: InternalsVisibleTo("PolyglotNotebooks.Test")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}