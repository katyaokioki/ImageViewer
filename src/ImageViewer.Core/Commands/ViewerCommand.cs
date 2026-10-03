namespace ImageViewer.Core.Commands;

/// <summary>Все действия просмотрщика. UI связывает с ними клавиши, кнопки и пункты меню.</summary>
public enum ViewerCommand
{
    None,

    // Файлы и навигация
    OpenFile,
    NextImage,
    PreviousImage,
    FirstImage,
    LastImage,
    SkipForward,
    SkipBackward,
    ReloadFolder,

    // Масштаб
    ZoomIn,
    ZoomOut,
    ZoomActualSize,
    ZoomFitToScreen,
    ZoomShrinkToFit,
    ZoomFitWidth,
    ZoomFitHeight,
    ZoomFill,

    // Преобразования и перемещение
    RotateClockwise,
    RotateCounterClockwise,
    PanLeft,
    PanRight,
    PanUp,
    PanDown,

    // Вид
    ToggleFullscreen,
    ToggleInfoPanel,
    ToggleHistogram,
    ToggleStatusBar,
    ToggleTheme,
    CycleBackground,
    ToggleScalingQuality,
    ToggleSlideshow,

    // Окна
    ShowSettings,
    ShowHelp,
    Escape,
    Exit
}

/// <summary>Описание команды для справки и меню.</summary>
public sealed record CommandInfo(ViewerCommand Command, string Category, string Description, bool Repeatable = false);

/// <summary>Справочник описаний команд.</summary>
public static class CommandCatalog
{
    private const string Files = "Файлы и навигация";
    private const string Zoom = "Масштаб";
    private const string Move = "Поворот и перемещение";
    private const string View = "Вид";
    private const string App = "Приложение";

    public static IReadOnlyList<CommandInfo> All { get; } =
    [
        new(ViewerCommand.OpenFile, Files, "Открыть другой файл"),
        new(ViewerCommand.NextImage, Files, "Следующее изображение", true),
        new(ViewerCommand.PreviousImage, Files, "Предыдущее изображение", true),
        new(ViewerCommand.FirstImage, Files, "Первое изображение в папке"),
        new(ViewerCommand.LastImage, Files, "Последнее изображение в папке"),
        new(ViewerCommand.SkipForward, Files, "Вперёд на 10 изображений", true),
        new(ViewerCommand.SkipBackward, Files, "Назад на 10 изображений", true),
        new(ViewerCommand.ReloadFolder, Files, "Перечитать папку"),

        new(ViewerCommand.ZoomIn, Zoom, "Увеличить", true),
        new(ViewerCommand.ZoomOut, Zoom, "Уменьшить", true),
        new(ViewerCommand.ZoomActualSize, Zoom, "Масштаб 100%"),
        new(ViewerCommand.ZoomFitToScreen, Zoom, "Вписать в экран"),
        new(ViewerCommand.ZoomShrinkToFit, Zoom, "Уменьшить до экрана (маленькие — 100%)"),
        new(ViewerCommand.ZoomFitWidth, Zoom, "По ширине экрана"),
        new(ViewerCommand.ZoomFitHeight, Zoom, "По высоте экрана"),
        new(ViewerCommand.ZoomFill, Zoom, "Заполнить экран"),

        new(ViewerCommand.RotateClockwise, Move, "Повернуть по часовой стрелке"),
        new(ViewerCommand.RotateCounterClockwise, Move, "Повернуть против часовой стрелки"),
        new(ViewerCommand.PanLeft, Move, "Сдвинуть влево", true),
        new(ViewerCommand.PanRight, Move, "Сдвинуть вправо", true),
        new(ViewerCommand.PanUp, Move, "Сдвинуть вверх", true),
        new(ViewerCommand.PanDown, Move, "Сдвинуть вниз", true),

        new(ViewerCommand.ToggleFullscreen, View, "Полноэкранный / оконный режим"),
        new(ViewerCommand.ToggleInfoPanel, View, "Панель информации о файле"),
        new(ViewerCommand.ToggleHistogram, View, "Гистограмма"),
        new(ViewerCommand.ToggleStatusBar, View, "Строка состояния"),
        new(ViewerCommand.ToggleTheme, View, "Сменить тему (тёмная / светлая)"),
        new(ViewerCommand.CycleBackground, View, "Сменить фон (тема / цвет / шахматка)"),
        new(ViewerCommand.ToggleScalingQuality, View, "Сглаживание / пиксели"),
        new(ViewerCommand.ToggleSlideshow, View, "Слайд-шоу"),

        new(ViewerCommand.ShowSettings, App, "Настройки"),
        new(ViewerCommand.ShowHelp, App, "Справка"),
        new(ViewerCommand.Escape, App, "Выйти из полноэкранного режима / закрыть"),
        new(ViewerCommand.Exit, App, "Выход"),
    ];

    private static readonly Dictionary<ViewerCommand, CommandInfo> ByCommand = All.ToDictionary(c => c.Command);

    public static CommandInfo? Get(ViewerCommand command) => ByCommand.GetValueOrDefault(command);

    public static string Describe(ViewerCommand command) => Get(command)?.Description ?? command.ToString();

    public static bool IsRepeatable(ViewerCommand command) => Get(command)?.Repeatable ?? false;
}
