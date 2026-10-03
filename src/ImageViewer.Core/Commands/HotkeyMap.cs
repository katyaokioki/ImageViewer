using System.Text;

namespace ImageViewer.Core.Commands;

/// <summary>
/// Таблица горячих клавиш «жест → команда». Жест — строка вида "Ctrl+Shift+O".
/// Имена клавиш не зависят от UI-фреймворка: каждое приложение переводит свои коды клавиш в эти строки.
/// Специальные имена: Plus, Minus, 0–9, Left/Right/Up/Down, PageUp/PageDown, Enter, Escape, Space, Backspace, Tab, F1–F12.
/// </summary>
public sealed class HotkeyMap
{
    private static readonly string[] ModifierOrder = ["Ctrl", "Shift", "Alt"];

    private readonly Dictionary<string, ViewerCommand> _bindings = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ViewerCommand> Bindings => _bindings;

    /// <summary>Раскладка по умолчанию.</summary>
    public static HotkeyMap CreateDefault()
    {
        var map = new HotkeyMap();

        map.Bind(ViewerCommand.OpenFile, "Ctrl+O");
        map.Bind(ViewerCommand.NextImage, "Right", "PageDown", "Space");
        map.Bind(ViewerCommand.PreviousImage, "Left", "PageUp", "Backspace");
        map.Bind(ViewerCommand.FirstImage, "Home");
        map.Bind(ViewerCommand.LastImage, "End");
        map.Bind(ViewerCommand.SkipForward, "Shift+Right", "Shift+PageDown");
        map.Bind(ViewerCommand.SkipBackward, "Shift+Left", "Shift+PageUp");
        map.Bind(ViewerCommand.ReloadFolder, "F5");

        map.Bind(ViewerCommand.ZoomIn, "Plus", "Ctrl+Plus");
        map.Bind(ViewerCommand.ZoomOut, "Minus", "Ctrl+Minus");
        map.Bind(ViewerCommand.ZoomActualSize, "1", "Ctrl+0");
        map.Bind(ViewerCommand.ZoomFitToScreen, "2", "0");
        map.Bind(ViewerCommand.ZoomShrinkToFit, "3");
        map.Bind(ViewerCommand.ZoomFitWidth, "4");
        map.Bind(ViewerCommand.ZoomFitHeight, "5");
        map.Bind(ViewerCommand.ZoomFill, "6");

        map.Bind(ViewerCommand.RotateClockwise, "R");
        map.Bind(ViewerCommand.RotateCounterClockwise, "Shift+R", "L");
        map.Bind(ViewerCommand.PanLeft, "Ctrl+Left", "A");
        map.Bind(ViewerCommand.PanRight, "Ctrl+Right", "D");
        map.Bind(ViewerCommand.PanUp, "Up", "W");
        map.Bind(ViewerCommand.PanDown, "Down", "S");

        map.Bind(ViewerCommand.ToggleFullscreen, "F11", "F", "Enter");
        map.Bind(ViewerCommand.ToggleInfoPanel, "I");
        map.Bind(ViewerCommand.ToggleHistogram, "H");
        map.Bind(ViewerCommand.ToggleStatusBar, "Tab");
        map.Bind(ViewerCommand.ToggleTheme, "T");
        map.Bind(ViewerCommand.CycleBackground, "B");
        map.Bind(ViewerCommand.ToggleScalingQuality, "P");
        map.Bind(ViewerCommand.ToggleSlideshow, "Ctrl+S", "F6");

        map.Bind(ViewerCommand.ShowSettings, "F9", "Ctrl+P");
        map.Bind(ViewerCommand.ShowHelp, "F1");
        map.Bind(ViewerCommand.Escape, "Escape");
        map.Bind(ViewerCommand.Exit, "Ctrl+Q", "Ctrl+W");

        return map;
    }

    /// <summary>Назначить одной команде один или несколько жестов.</summary>
    public void Bind(ViewerCommand command, params string[] gestures)
    {
        foreach (var g in gestures)
            _bindings[Normalize(g)] = command;
    }

    public bool Unbind(string gesture) => _bindings.Remove(Normalize(gesture));

    /// <summary>Какая команда соответствует жесту (None — никакая).</summary>
    public ViewerCommand Resolve(string gesture) =>
        _bindings.TryGetValue(Normalize(gesture), out var command) ? command : ViewerCommand.None;

    public IReadOnlyList<string> GetGestures(ViewerCommand command) =>
        _bindings.Where(b => b.Value == command).Select(b => b.Key).ToList();

    /// <summary>
    /// Приводит жест к каноническому виду: модификаторы в порядке Ctrl, Shift, Alt; «Control» → «Ctrl».
    /// </summary>
    public static string Normalize(string gesture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gesture);
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries);

        // «Ctrl++» после Split даёт пустые части — это клавиша Plus
        string key = parts[^1].Length == 0 ? "Plus" : parts[^1];
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in parts[..^1])
        {
            if (raw.Length == 0) continue;
            modifiers.Add(raw.ToLowerInvariant() switch
            {
                "control" or "ctl" or "ctrl" => "Ctrl",
                "shift" => "Shift",
                "alt" or "menu" => "Alt",
                _ => throw new FormatException($"Неизвестный модификатор «{raw}» в «{gesture}».")
            });
        }

        key = key.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            "back" => "Backspace",
            "add" or "+" => "Plus",
            "subtract" or "-" => "Minus",
            "pgup" or "prior" => "PageUp",
            "pgdn" or "next" => "PageDown",
            _ => key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..]
        };

        var sb = new StringBuilder();
        foreach (var m in ModifierOrder.Where(modifiers.Contains))
            sb.Append(m).Append('+');
        return sb.Append(key).ToString();
    }

    /// <summary>Текст справки по горячим клавишам, сгруппированный по категориям.</summary>
    public string BuildHelpText()
    {
        var sb = new StringBuilder();
        foreach (var group in CommandCatalog.All.GroupBy(c => c.Category))
        {
            sb.AppendLine(group.Key);
            foreach (var info in group)
            {
                var gestures = GetGestures(info.Command);
                if (gestures.Count == 0) continue;
                sb.Append("  ").Append(string.Join(", ", gestures).PadRight(28)).AppendLine(info.Description);
            }
            sb.AppendLine();
        }
        sb.AppendLine("Мышь");
        sb.AppendLine("  Колесо                      Масштаб относительно курсора");
        sb.AppendLine("  Левая кнопка + перемещение  Перемещение изображения");
        sb.AppendLine("  Двойной щелчок              Полноэкранный режим");
        sb.AppendLine("  Кнопки «Назад/Вперёд»       Предыдущее / следующее изображение");
        sb.AppendLine("  Правая кнопка               Контекстное меню");
        sb.AppendLine("  Перетаскивание файла        Открыть файл");
        return sb.ToString();
    }
}
