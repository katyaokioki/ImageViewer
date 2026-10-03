using System.Windows;
using System.Windows.Input;

namespace ImageViewer.Wpf.Views;

/// <summary>Окно справки (F1): горячие клавиши и параметры командной строки.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow(string text)
    {
        InitializeComponent();
        HelpTextBlock.Text = text;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.F1 or Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(HelpTextBlock.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Буфер обмена занят другим приложением — просто игнорируем
        }
    }
}
