using System.Windows;
using System.Windows.Interop;
using WinQuickSwitch.Features.Legal;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch;

public partial class LegalDocumentWindow : Window
{
    private readonly bool _useDarkTheme;

    internal LegalDocumentWindow(
        Window owner,
        LegalDocument document,
        bool useDarkTheme)
    {
        Owner = owner;
        _useDarkTheme = useDarkTheme;
        InitializeComponent();
        Title = $"WinQuickSwitch — {document.Title}";
        DocumentTitleText.Text = document.Title;
        DocumentTextBox.Text = document.DisplayText;
        DocumentTextBox.CaretIndex = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WidgetTheme.Apply(
            _useDarkTheme,
            new WindowInteropHelper(this).Handle);
        DocumentTextBox.ScrollToHome();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
