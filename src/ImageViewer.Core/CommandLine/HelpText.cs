using System.Text;
using ImageViewer.Core.Commands;

namespace ImageViewer.Core.CommandLine;

/// <summary>Тексты справки: параметры командной строки и горячие клавиши (общие для GUI и консоли).</summary>
public static class HelpText
{
    public static string CommandLineUsage(string exeName = "ImageViewer")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Использование: {exeName} [путь] [параметры]");
        sb.AppendLine();
        sb.AppendLine("  путь                       Файл изображения или папка с изображениями");
        sb.AppendLine();
        sb.AppendLine("Параметры:");
        sb.AppendLine("  -z, --zoom <режим>         100 | fit | shrink | width | height | fill");
        sb.AppendLine("  -f, --fullscreen           Полноэкранный режим");
        sb.AppendLine("  -m, --maximized            Развёрнутое окно");
        sb.AppendLine("  -w, --windowed             Обычное окно");
        sb.AppendLine("  -t, --theme <dark|light>   Тема оформления");
        sb.AppendLine("  -b, --background <фон>     theme | checker | #RRGGBB");
        sb.AppendLine("  -s, --slideshow [сек]      Запустить слайд-шоу (интервал в секундах)");
        sb.AppendLine("      --sort <порядок>       name | date | size | type");
        sb.AppendLine("      --desc, --asc          Порядок сортировки по убыванию / возрастанию");
        sb.AppendLine("      --wrap, --no-wrap      Переход по кругу в папке включён / выключен");
        sb.AppendLine("  -i, --info                 Показать панель информации о файле");
        sb.AppendLine("      --histogram            Показать гистограмму (--no-histogram — скрыть)");
        sb.AppendLine("      --pixelated, --smooth  Масштабирование без сглаживания / со сглаживанием");
        sb.AppendLine("  -n, --index <N>            Открыть N-е изображение папки (с 1)");
        sb.AppendLine("      --settings <файл>      Использовать другой файл настроек");
        sb.AppendLine("      --reset-settings       Сбросить настройки по умолчанию");
        sb.AppendLine("  -h, -?, --help             Эта справка");
        sb.AppendLine();
        sb.AppendLine("Примеры:");
        sb.AppendLine($"  {exeName} photo.jpg");
        sb.AppendLine($"  {exeName} photo.jpg --zoom 100 --info");
        sb.AppendLine($"  {exeName} C:\\Photos --sort date --desc --slideshow 5");
        sb.AppendLine($"  {exeName} sprite.png -z fill --pixelated -b checker --windowed");
        return sb.ToString();
    }

    /// <summary>Полная справка: горячие клавиши + командная строка.</summary>
    public static string Full(HotkeyMap hotkeys, string exeName = "ImageViewer")
    {
        var sb = new StringBuilder();
        sb.AppendLine("ГОРЯЧИЕ КЛАВИШИ");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine(hotkeys.BuildHelpText());
        sb.AppendLine();
        sb.AppendLine("КОМАНДНАЯ СТРОКА");
        sb.AppendLine(new string('═', 60));
        sb.Append(CommandLineUsage(exeName));
        return sb.ToString();
    }
}
