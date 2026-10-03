using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ImageViewer.Ui.Views;

/// <summary>Окно справки (F1): горячие клавиши и параметры командной строки.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow() : this(string.Empty)
    {
    }

    public HelpWindow(string text)
    {
        InitializeComponent();
        HelpTextBlock.Text = text;

        CloseButton.Click += (_, _) => Close();
        CopyButton.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F1)
            {
                Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }
}
