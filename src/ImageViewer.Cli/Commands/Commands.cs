using System.Globalization;
using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Files;
using ImageViewer.Core.Imaging;
using ImageViewer.Core.Navigation;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Cli.Commands;

/// <summary>ivc info &lt;файл&gt; — сведения о файле изображения.</summary>
public sealed class InfoCommand : CliCommandBase
{
    public override string Name => "info";
    public override string Summary => "Сведения о файле: размер, тип, формат, разрешение, глубина цвета";
    public override string Usage => "ivc info <файл> [--json]";
    public override IReadOnlySet<string> Flags { get; } = new HashSet<string> { "json" };

    public override int Execute(CliArguments args, CliContext ctx)
    {
        var path = RequireFile(args.RequirePositional(0, "файл"));
        var info = ImageInfoReader.Read(path);

        if (args.Has("json"))
        {
            ctx.WriteJson(new
            {
                info.FullPath,
                info.FileName,
                info.FileSize,
                info.FileSizeText,
                Format = info.Format.ToString(),
                info.FormatName,
                info.MimeType,
                info.Width,
                info.Height,
                info.BitsPerPixel,
                Megapixels = Math.Round(info.Megapixels, 3),
                AspectRatio = info.AspectRatioText,
                info.Modified,
                info.Created
            });
            return 0;
        }

        ctx.Out.WriteLine($"Файл: {info.FileName}");
        WriteTable(ctx.Out, info.ToDisplayPairs().Skip(1));
        return 0;
    }
}

/// <summary>ivc list &lt;папка|файл&gt; — список изображений папки в порядке просмотра.</summary>
public sealed class ListCommand : CliCommandBase
{
    public override string Name => "list";
    public override string Summary => "Список изображений папки в порядке навигации (с номерами)";
    public override string Usage => "ivc list <папка|файл> [--sort name|date|size|type] [--desc] [--json]";
    public override IReadOnlySet<string> Flags { get; } = new HashSet<string> { "json", "desc" };

    public override int Execute(CliArguments args, CliContext ctx)
    {
        var path = RequireFileOrDirectory(args.RequirePositional(0, "папка или файл"));
        var navigator = CreateNavigator(args, ctx);
        navigator.Open(path);

        var items = navigator.Files.Select((f, i) =>
        {
            var info = ImageInfoReader.Read(f);
            return new
            {
                Number = i + 1,
                info.FileName,
                info.FileSize,
                Size = info.FileSizeText,
                Format = ImageFormats.GetDisplayName(info.Format),
                Dimensions = info.DimensionsText,
                IsCurrent = i == navigator.CurrentIndex
            };
        }).ToList();

        if (args.Has("json"))
        {
            ctx.WriteJson(new { Folder = navigator.FolderPath, Count = navigator.Count, Current = navigator.CurrentIndex + 1, Items = items });
            return 0;
        }

        ctx.Out.WriteLine($"Папка: {navigator.FolderPath}");
        ctx.Out.WriteLine($"Изображений: {navigator.Count}   Сортировка: {DisplayNames.Get(navigator.SortMode)}{(navigator.SortDescending ? " (по убыванию)" : "")}");
        ctx.Out.WriteLine();
        int nameWidth = items.Count == 0 ? 10 : Math.Min(40, items.Max(x => x.FileName.Length));
        foreach (var item in items)
        {
            string marker = item.IsCurrent ? "►" : " ";
            ctx.Out.WriteLine($"{marker}{item.Number,4}. {item.FileName.PadRight(nameWidth)}  {item.Dimensions,-13} {item.Size,10}  {item.Format}");
        }
        return 0;
    }

    internal static FolderNavigator CreateNavigator(CliArguments args, CliContext ctx)
    {
        var sort = SortMode.Name;
        if (args.Get("sort") is { } s && !CommandLineParser.TryParseSortMode(s, out sort))
            throw new CliUsageException($"Неизвестная сортировка «{s}». Допустимо: name, date, size, type.");
        return new FolderNavigator(sortMode: sort, sortDescending: args.Has("desc"));
    }
}

/// <summary>ivc nav &lt;файл&gt; --step N — демонстрация навигации по папке.</summary>
public sealed class NavigateCommand : CliCommandBase
{
    public override string Name => "nav";
    public override string Summary => "Навигация: какое изображение откроется после N шагов вперёд/назад";
    public override string Usage => "ivc nav <файл> [--step N] [--no-wrap] [--sort ...] [--desc] [--first|--last]";
    public override IReadOnlySet<string> Flags { get; } = new HashSet<string> { "no-wrap", "desc", "first", "last", "json" };

    public override int Execute(CliArguments args, CliContext ctx)
    {
        var path = RequireFileOrDirectory(args.RequirePositional(0, "файл"));
        int step = args.GetInt("step", 1);

        var navigator = ListCommand.CreateNavigator(args, ctx);
        navigator.WrapAround = !args.Has("no-wrap");
        navigator.Open(path);
        if (navigator.Count == 0)
        {
            ctx.Out.WriteLine("В папке нет изображений.");
            return 1;
        }

        string Describe() => $"{navigator.CurrentIndex + 1}/{navigator.Count}  {Path.GetFileName(navigator.CurrentPath)}";
        string from = Describe();

        bool moved;
        string action;
        if (args.Has("first")) { moved = navigator.MoveFirst(); action = "к первому"; }
        else if (args.Has("last")) { moved = navigator.MoveLast(); action = "к последнему"; }
        else { moved = navigator.MoveBy(step); action = step >= 0 ? $"вперёд на {step}" : $"назад на {-step}"; }

        if (args.Has("json"))
        {
            ctx.WriteJson(new { From = from, Action = action, Moved = moved, To = Describe(), navigator.WrapAround });
            return 0;
        }

        ctx.Out.WriteLine($"Текущее:   {from}");
        ctx.Out.WriteLine($"Переход:   {action} (по кругу: {(navigator.WrapAround ? "да" : "нет")})");
        ctx.Out.WriteLine(moved ? $"Результат: {Describe()}" : "Результат: переход невозможен — достигнута граница папки");
        return 0;
    }
}

/// <summary>ivc histogram &lt;файл&gt; — гистограмма в виде псевдографики или JSON.</summary>
public sealed class HistogramCommand : CliCommandBase
{
    public override string Name => "histogram";
    public override string Summary => "Гистограмма яркости и каналов R/G/B (псевдографика или JSON)";
    public override string Usage => "ivc histogram <файл> [--channel lum|r|g|b|all] [--width 64] [--height 12] [--json]";
    public override IReadOnlySet<string> Flags { get; } = new HashSet<string> { "json" };

    public override int Execute(CliArguments args, CliContext ctx)
    {
        var path = RequireFile(args.RequirePositional(0, "файл"));
        if (!ctx.Decoder.CanDecode(path))
            throw new CliUsageException($"Декодер «{ctx.Decoder.Name}» не поддерживает этот формат на данной платформе.");

        var image = ctx.Decoder.Decode(path);
        var histogram = Histogram.Compute(image);

        if (args.Has("json"))
        {
            ctx.WriteJson(new
            {
                File = Path.GetFileName(path),
                image.Width,
                image.Height,
                histogram.SampleCount,
                histogram.SampleStep,
                MeanLuminance = Math.Round(histogram.MeanLuminance, 2),
                histogram.Red,
                histogram.Green,
                histogram.Blue,
                histogram.Luminance
            });
            return 0;
        }

        int width = args.GetInt("width", 64, 8, 256);
        int height = args.GetInt("height", 12, 3, 50);
        var channels = (args.Get("channel") ?? "lum").ToLowerInvariant() switch
        {
            "lum" or "l" or "luminance" => new[] { HistogramChannel.Luminance },
            "r" or "red" => [HistogramChannel.Red],
            "g" or "green" => [HistogramChannel.Green],
            "b" or "blue" => [HistogramChannel.Blue],
            "all" => [HistogramChannel.Luminance, HistogramChannel.Red, HistogramChannel.Green, HistogramChannel.Blue],
            var other => throw new CliUsageException($"Неизвестный канал «{other}».")
        };

        ctx.Out.WriteLine($"{Path.GetFileName(path)}: {image.Width}×{image.Height}, выборка {histogram.SampleCount:N0} пикс.");
        ctx.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Средняя яркость: {histogram.MeanLuminance:0.0}   тени (5%): {histogram.Percentile(HistogramChannel.Luminance, 0.05)}   света (95%): {histogram.Percentile(HistogramChannel.Luminance, 0.95)}"));
        foreach (var channel in channels)
        {
            ctx.Out.WriteLine();
            ctx.Out.WriteLine($"Канал: {ChannelName(channel)}");
            ctx.Out.WriteLine(histogram.ToAsciiChart(channel, width, height));
        }
        return 0;
    }

    private static string ChannelName(HistogramChannel c) => c switch
    {
        HistogramChannel.Red => "красный",
        HistogramChannel.Green => "зелёный",
        HistogramChannel.Blue => "синий",
        _ => "яркость"
    };
}

/// <summary>ivc fit — расчёт масштаба и положения для режимов «100%», «вписать», «по ширине» и т.д.</summary>
public sealed class FitCommand : CliCommandBase
{
    public override string Name => "fit";
    public override string Summary => "Расчёт масштаба для режимов 100% / вписать / по ширине / по высоте / заполнить";
    public override string Usage => "ivc fit <файл|ШxВ> [--viewport 1920x1080] [--zoom 100|fit|shrink|width|height|fill] [--rotate 0|90|180|270] [--json]";
    public override IReadOnlySet<string> Flags { get; } = new HashSet<string> { "json" };

    public override int Execute(CliArguments args, CliContext ctx)
    {
        string source = args.RequirePositional(0, "файл или размер изображения");
        (int w, int h) = File.Exists(source) ? ReadSize(source, ctx) : ParseSize(source, "Размер изображения");
        var (vw, vh) = ParseSize(args.Get("viewport") ?? "1920x1080", "--viewport");

        int rotate = args.GetInt("rotate", 0);
        if (rotate is not (0 or 90 or 180 or 270)) throw new CliUsageException("--rotate: допустимо 0, 90, 180, 270.");

        IEnumerable<ZoomMode> modes;
        if (args.Get("zoom") is { } z)
        {
            if (!CommandLineParser.TryParseZoomMode(z, out var single))
                throw new CliUsageException($"Неизвестный режим «{z}».");
            modes = [single];
        }
        else
        {
            modes = ZoomStrategy.All.Select(s => s.Mode);
        }

        var rows = modes.Select(mode =>
        {
            // Тот же ZoomController, что и в WPF-приложении
            var zoom = new ZoomController();
            zoom.SetViewport(new SizeD(vw, vh));
            zoom.SetImage(new SizeD(w, h), mode);
            for (int r = 0; r < rotate; r += 90) zoom.RotateClockwise();
            var d = zoom.DisplaySize;
            return new
            {
                Mode = mode.ToString(),
                Name = ZoomStrategy.GetDisplayName(mode),
                Percent = Math.Round(zoom.Scale * 100, 2),
                DisplayWidth = Math.Round(d.Width),
                DisplayHeight = Math.Round(d.Height),
                OffsetX = Math.Round(zoom.OffsetX),
                OffsetY = Math.Round(zoom.OffsetY),
                zoom.CanPan
            };
        }).ToList();

        if (args.Has("json"))
        {
            ctx.WriteJson(new { Image = new { Width = w, Height = h }, Viewport = new { Width = vw, Height = vh }, Rotation = rotate, Modes = rows });
            return 0;
        }

        ctx.Out.WriteLine($"Изображение {w}×{h}, экран {vw}×{vh}, поворот {rotate}°");
        ctx.Out.WriteLine();
        ctx.Out.WriteLine($"  {"Режим",-22} {"Масштаб",9} {"На экране",13} {"Смещение",14}  Прокрутка");
        foreach (var r in rows)
        {
            string percent = r.Percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            ctx.Out.WriteLine($"  {r.Name,-22} {percent,9} {$"{r.DisplayWidth}×{r.DisplayHeight}",13} {$"({r.OffsetX}; {r.OffsetY})",14}  {(r.CanPan ? "да" : "нет")}");
        }
        return 0;
    }

    private static (int, int) ReadSize(string path, CliContext ctx)
    {
        var header = ImageHeaderReader.Read(path);
        if (header.HasSize) return (header.Width, header.Height);
        if (ctx.Decoder.CanDecode(path))
        {
            var img = ctx.Decoder.Decode(path);
            return (img.Width, img.Height);
        }
        throw new CliUsageException("Не удалось определить размер изображения.");
    }
}

/// <summary>ivc generate &lt;папка&gt; — создать набор тестовых изображений для демонстрации.</summary>
public sealed class GenerateCommand : CliCommandBase
{
    public override string Name => "generate";
    public override string Summary => "Создать папку с тестовыми изображениями (градиенты, шахматка, широкие и высокие)";
    public override string Usage => "ivc generate <папка> [--count 8] [--size 800x600]";

    public override int Execute(CliArguments args, CliContext ctx)
    {
        string folder = Path.GetFullPath(args.RequirePositional(0, "папка"));
        int count = args.GetInt("count", 8, 1, 200);
        var (w, h) = ParseSize(args.Get("size") ?? "800x600", "--size");
        Directory.CreateDirectory(folder);

        for (int i = 0; i < count; i++)
        {
            // Каждое пятое изображение — «панорама», каждое седьмое — «длинный скриншот»
            (int iw, int ih) = (i % 5 == 4) ? (w * 3, h / 2) : (i % 7 == 6) ? (w / 2, h * 3) : (w, h);
            var image = CreatePattern(i, Math.Max(16, iw), Math.Max(16, ih));
            string name = Path.Combine(folder, $"demo_{i + 1}.bmp"); // demo_1, demo_2 … demo_10 — проверка естественной сортировки
            BmpEncoder.Save(image, name);
            ctx.Out.WriteLine($"  создан {Path.GetFileName(name)}  {image.Width}×{image.Height}");
        }
        ctx.Out.WriteLine($"Готово: {count} файлов в {folder}");
        return 0;
    }

    /// <summary>Узоры: градиенты разных цветов, шахматка, «закат» — чтобы гистограммы заметно различались.</summary>
    internal static RawImage CreatePattern(int seed, int width, int height)
    {
        var image = new RawImage(width, height);
        var px = image.Pixels;
        int kind = seed % 4;
        double hue = seed * 47 % 360;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = (double)x / (width - 1), v = (double)y / (height - 1);
                (double r, double g, double b) = kind switch
                {
                    0 => HsvToRgb(hue + u * 120, 0.8, 0.3 + 0.7 * v),               // цветной градиент
                    1 => ((x / 32 + y / 32) % 2 == 0 ? (0.9, 0.9, 0.9) : (0.15, 0.15, 0.2)), // шахматка
                    2 => (u, u, u),                                                  // серая шкала
                    _ => HsvToRgb(hue + 200 * v, 0.6 + 0.4 * u, 1.0 - 0.6 * v)       // «закат»
                };
                int i = (y * width + x) * 4;
                px[i] = (byte)(Math.Clamp(b, 0, 1) * 255);
                px[i + 1] = (byte)(Math.Clamp(g, 0, 1) * 255);
                px[i + 2] = (byte)(Math.Clamp(r, 0, 1) * 255);
                px[i + 3] = 255;
            }
        }
        return image;
    }

    private static (double, double, double) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360 / 60;
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return (r + m, g + m, b + m);
    }
}

/// <summary>ivc check-args -- [параметры GUI] — как графическое приложение поймёт командную строку.</summary>
public sealed class CheckArgsCommand : CliCommandBase
{
    public override string Name => "check-args";
    public override string Summary => "Проверить параметры запуска графического просмотрщика (без запуска окна)";
    public override string Usage => "ivc check-args -- [путь] [--zoom fit] [--fullscreen] ...";

    public override int Execute(CliArguments args, CliContext ctx)
    {
        var raw = args.Rest.Count > 0 ? args.Rest : args.Positional;
        var result = CommandLineParser.Parse(raw);
        var o = result.Options;

        var effective = new ViewerSettings();
        o.ApplyTo(effective);

        ctx.Out.WriteLine($"Аргументы: {string.Join(" ", raw)}");
        ctx.Out.WriteLine(result.IsValid ? "Ошибок нет." : "Ошибки:");
        foreach (var e in result.Errors) ctx.Out.WriteLine($"  ✗ {e}");
        ctx.Out.WriteLine();
        WriteTable(ctx.Out,
        [
            new("Путь", o.Path ?? "—"),
            new("Справка", o.ShowHelp ? "да" : "нет"),
            new("Масштаб", DisplayNames.Get(effective.DefaultZoomMode)),
            new("Окно", DisplayNames.Get(effective.WindowStartMode)),
            new("Тема", DisplayNames.Get(effective.Theme)),
            new("Фон", effective.BackgroundMode == BackgroundMode.SolidColor
                ? effective.BackgroundColor
                : DisplayNames.Get(effective.BackgroundMode)),
            new("Сортировка", DisplayNames.Get(effective.SortMode) + (effective.SortDescending ? ", по убыванию" : "")),
            new("По кругу", effective.WrapAround ? "да" : "нет"),
            new("Панель информации", effective.ShowInfoPanel ? "да" : "нет"),
            new("Слайд-шоу", o.StartSlideshow ? $"да, {effective.SlideshowIntervalSeconds} с" : "нет"),
            new("Номер изображения", o.Index?.ToString() ?? "—"),
        ]);
        return result.IsValid ? 0 : 2;
    }
}

/// <summary>ivc keys — горячие клавиши графического приложения.</summary>
public sealed class KeysCommand : CliCommandBase
{
    public override string Name => "keys";
    public override string Summary => "Горячие клавиши графического просмотрщика";
    public override string Usage => "ivc keys";

    public override int Execute(CliArguments args, CliContext ctx)
    {
        ctx.Out.WriteLine(ctx.Hotkeys.BuildHelpText());
        return 0;
    }
}

/// <summary>ivc settings — показать файл и содержимое пользовательских настроек.</summary>
public sealed class SettingsCommand : CliCommandBase
{
    public override string Name => "settings";
    public override string Summary => "Показать пользовательские настройки (общие с графическим приложением)";
    public override string Usage => "ivc settings [--path файл]";

    public override int Execute(CliArguments args, CliContext ctx)
    {
        var store = new JsonSettingsStore(args.Get("path"));
        ctx.Out.WriteLine($"Файл: {store.Location} {(File.Exists(store.Location) ? "" : "(ещё не создан — значения по умолчанию)")}");
        ctx.Out.WriteLine(JsonSettingsStore.Serialize(store.Load()));
        return 0;
    }
}
