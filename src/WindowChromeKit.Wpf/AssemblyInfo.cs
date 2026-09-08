using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

[assembly: XmlnsDefinition(
    "https://windowchromekit.dev/wpf",
    "WindowChromeKit.Wpf")]

[assembly: XmlnsPrefix(
    "https://windowchromekit.dev/wpf",
    "chrome")]

[assembly: InternalsVisibleTo("WindowChromeKit.Wpf.Tests")]
