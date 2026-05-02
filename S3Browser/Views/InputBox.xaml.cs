using System.Windows;

namespace S3Browser.Views;

public partial class InputBox : Window
{
    public string Value => ValueBox.Text;

    public InputBox(string prompt, string title, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string? Show(string prompt, string title, string initial)
    {
        var dlg = new InputBox(prompt, title, initial)
        {
            Owner = Application.Current.MainWindow
        };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
