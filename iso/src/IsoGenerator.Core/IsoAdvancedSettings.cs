using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace IsoGenerator.Core;

public sealed class IsoAdvancedSettings
{
    const string EnvironmentKey="ISO_ADVANCED_SETTINGS";
    static readonly AsyncLocal<IsoAdvancedSettings?> active=new();
    static readonly IsoAdvancedSettings defaults=new();
    internal static IsoAdvancedSettings Current=>active.Value??defaults;

    // Rendering and planning can run concurrently with different saved profiles.
    public static IDisposable Use(IsoAdvancedSettings? settings)
    {
        if(settings is not null)settings.Validate();
        var scope=new SettingsScope(active.Value);
        active.Value=settings??defaults;
        return scope;
    }
    public static IsoAdvancedSettings WorkerSettings=>active.Value??new();
    internal static string? Signature
    {
        get
        {
            var json=JsonSerializer.Serialize(Current);
            return json==DefaultSignature?null:json;
        }
    }
    static readonly string DefaultSignature=JsonSerializer.Serialize(defaults);
    public static IDisposable UseWorkerProfile()
    {
        var json=Environment.GetEnvironmentVariable(EnvironmentKey);
        return Use(string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<IsoAdvancedSettings>(json));
    }
    public void SetWorkerProfile(IDictionary<string,string?> environment)=>
        environment[EnvironmentKey]=JsonSerializer.Serialize(this);

    public void Validate()
    {
        Validator.ValidateObject(this,new ValidationContext(this),validateAllProperties:true);
        if(PaperRight-PaperLeft<40||PaperTop-PaperBottom<40)
            throw new ValidationException("Рабочее поле должно быть не меньше 40 × 40 мм.");
        if(LeaderTarget>LeaderSoftLimit||LeaderSoftLimit>LeaderMaxReach)
            throw new ValidationException("Для выносок задайте предпочтительную длину ≤ порога штрафа ≤ верхней границы.");
    }

    sealed class SettingsScope(IsoAdvancedSettings? previous):IDisposable
    {
        public void Dispose()=>active.Value=previous;
    }

    [Description("Задаёт кратность графических длин при растяжении труб на листе. Размерные числа берутся из модели.")]
    [Category("Трубы и символы"),DisplayName("Шаг длин труб на листе, мм"),Range(1,50)]
    public double PipeGridStep { get; set; } = 10;

    [Description("Задаёт разрыв дальней трубы в месте пересечения в проекции. Соединения модели сохраняются.")]
    [Category("Трубы и символы"),DisplayName("Разрыв трубы при перекрытии, мм"),Range(0.1,25)]
    public double PipeGap { get; set; } = 6;

    [Description("Расстояние от оси трубы до каждой из двух линий условного изображения изоляции.")]
    [Category("Трубы и символы"),DisplayName("Отступ линии изоляции, мм"),Range(0.1,20)]
    public double InsulationOffset { get; set; } = 3;

    [Description("Множитель размера условных обозначений опор относительно библиотечных блоков.")]
    [Category("Трубы и символы"),DisplayName("Масштаб опор"),Range(0.1,100)]
    public double SupportSymbolScale { get; set; } = 10;

    [Description("Защищает зазор от размерного ряда до трубы и символов. При большем зазоре чертежу требуется больше места.")]
    [Category("Размеры"),DisplayName("Отступ размерной линии от трубы и арматуры, мм"),Range(1,50)]
    public double DimensionClearance { get; set; } = 10;

    [Description("Зазор между соседними параллельными рядами размеров на одной стороне трассы.")]
    [Category("Размеры"),DisplayName("Шаг размерных рядов, мм"),Range(2,30)]
    public double DimensionRowStep { get; set; } = 7;

    [Description("Сколько дополнительных позиций доступно размерным рядам с каждой стороны трубы. Большее значение расширяет поиск.")]
    [Category("Размеры"),DisplayName("Дополнительные ряды на каждой стороне"),Range(1,18)]
    public int DimensionLaneCount { get; set; } = 8;

    [Description("Смещение размерного числа от размерной линии при подборе положения текста.")]
    [Category("Размеры"),DisplayName("Подъём размерного числа над линией, мм"),Range(1,10)]
    public double TextLift { get; set; } = 2.8;

    [Description("Высота текста размерных чисел в чертеже DWG и на PDF-листе.")]
    [Category("Размеры"),DisplayName("Высота размерного числа в DWG, мм"),Range(1,10)]
    public double DimensionTextHeight { get; set; } = 3.5;

    [Description("Длина стрелок на размерах и выносках. Для коротких размеров может потребоваться больше места.")]
    [Category("Размеры"),DisplayName("Длина размерной стрелки и стрелки выноски, мм"),Range(0.5,10)]
    public double ArrowSize { get; set; } = 2.5;

    [Description("Зазор между точкой привязки размера и началом выносной линии.")]
    [Category("Размеры"),DisplayName("Зазор от начала выносной линии до детали, мм"),Range(0,10)]
    public double ExtensionOffset { get; set; } = 1;

    [Description("Насколько выносная линия проходит за размерную линию.")]
    [Category("Размеры"),DisplayName("Выход выносной линии за размер, мм"),Range(0,10)]
    public double ExtensionOvershoot { get; set; } = 1;

    [Description("После этой длины выносная линия считается длинной при сравнении вариантов компоновки.")]
    [Category("Размеры"),DisplayName("Порог длинной выносной линии размера, мм"),Range(5,100)]
    public double LongExtensionThreshold { get; set; } = 35;

    [Description("Управляет выносками с идентификаторами арматуры на листе.")]
    [Category("Выноски и подписи"),DisplayName("Показывать выноски с ИД арматуры")]
    public bool ShowValveIdentifierLeaders { get; set; } = true;

    [Description("Предпочтительная длина выноски при выборе свободного места для подписи.")]
    [Category("Выноски и подписи"),DisplayName("Предпочтительная длина выноски, мм"),Range(3,60)]
    public double LeaderTarget { get; set; } = 15;

    [Description("После этой длины генератор снижает оценку варианта с выноской. Должна быть между предпочтительной длиной и верхней границей.")]
    [Category("Выноски и подписи"),DisplayName("Длина, после которой выноска получает штраф, мм"),Range(5,100)]
    public double LeaderSoftLimit { get; set; } = 20;

    [Description("Максимальная длина обычной выноски, которую генератор проверяет при поиске позиции.")]
    [Category("Выноски и подписи"),DisplayName("Верхняя граница поиска обычной выноски, мм"),Range(10,150)]
    public double LeaderMaxReach { get; set; } = 96;

    [Description("Высота текста выносок и номеров позиций на листе.")]
    [Category("Выноски и подписи"),DisplayName("Высота текста выносок и позиций в DWG, мм"),Range(1,10)]
    public double LeaderTextHeight { get; set; } = 3.5;

    [Description("Вес пересечения для оценки варианта размещения выноски. Большее значение сильнее снижает оценку варианта с пересечением.")]
    [Category("Выноски и подписи"),DisplayName("Вес пересечения линии выноски"),Range(0,10)]
    public double LeaderCrossingWeight { get; set; } = 0.6;

    [Description("Вес пересечения выноски с обозначением арматуры при выборе её положения.")]
    [Category("Выноски и подписи"),DisplayName("Вес пересечения выноски арматуры"),Range(0,10)]
    public double ValveLeaderCrossingWeight { get; set; } = 0.35;

    [Description("Длина горизонтального участка выноски перед текстом.")]
    [Category("Выноски и подписи"),DisplayName("Длина колена полки в DWG, мм"),Range(0,10)]
    public double LeaderLanding { get; set; } = 1;

    [Description("Левая координата рабочего поля листа A2, измеряется от левого края листа.")]
    [Category("Лист"),DisplayName("Граница рабочего поля слева, мм"),Range(0,590)]
    public double PaperLeft { get; set; } = 25;

    [Description("Правая координата рабочего поля A2. Оставьте место для таблицы спецификации и оформления.")]
    [Category("Лист"),DisplayName("Граница рабочего поля справа, мм"),Range(1,594)]
    public double PaperRight { get; set; } = 393;

    [Description("Нижняя координата рабочего поля A2, измеряется от нижнего края листа.")]
    [Category("Лист"),DisplayName("Граница рабочего поля снизу, мм"),Range(0,419)]
    public double PaperBottom { get; set; } = 25;

    [Description("Верхняя координата рабочего поля A2. Поле должно оставаться не меньше 40 × 40 мм.")]
    [Category("Лист"),DisplayName("Граница рабочего поля сверху, мм"),Range(1,420)]
    public double PaperTop { get; set; } = 370;

    [Description("Высота строки детали в таблице спецификации. Более высокие строки оставляют больше места для переноса текста.")]
    [Category("Спецификация"),DisplayName("Высота строки спецификации, мм"),Range(2,30)]
    public double BomRowHeight { get; set; } = 14;

    [Description("Высота строки с названием раздела спецификации, например «Трубы».")]
    [Category("Спецификация"),DisplayName("Высота заголовка раздела, мм"),Range(2,20)]
    public double BomHeadingHeight { get; set; } = 7;

    [Description("Высота текста в таблице спецификации на листе.")]
    [Category("Спецификация"),DisplayName("Высота текста спецификации, мм"),Range(1,10)]
    public double BomTextHeight { get; set; } = 3;

    [Description("После достижения этой высоты тела таблицы позиции переносятся на листы продолжения.")]
    [Category("Спецификация"),DisplayName("Допустимая высота тела спецификации, мм"),Range(20,380)]
    public double BomBodyLimit { get; set; } = 330;

    [Description("Время на одну попытку расчёта для выбранного ракурса.")]
    [Category("Поиск"),DisplayName("Лимит одного ракурса, с"),Range(5,600)]
    public int ViewTimeSeconds { get; set; } = 120;

    [Description("Общий бюджет времени на автоматический подбор ракурсов для одной линии.")]
    [Category("Поиск"),DisplayName("Общий лимит автоподбора, с"),Range(5,1200)]
    public int TotalViewTimeSeconds { get; set; } = 180;

    [Description("Максимальное время на создание одного листа DWG и его PDF после расчёта компоновки.")]
    [Category("Поиск"),DisplayName("Лимит выпуска одного листа, с"),Range(10,1800)]
    public int RenderTimeoutSeconds { get; set; } = 300;

    [Description("Максимальное число вариантов, которые перебирает планировщик при расчёте компоновки.")]
    [Category("Поиск"),DisplayName("Лимит вариантов компоновки"),Range(32,4096)]
    public int PlanningVariants { get; set; } = 512;

    [Description("Бюджет дополнительных попыток изменить границы и объединить соседние листы.")]
    [Category("Поиск"),DisplayName("Дополнительные варианты границ листов"),Range(0,24)]
    public int PartitionTrials { get; set; } = 4;

    [Description("Число перспективных вариантов, которые хранятся при поиске расположения размерных рядов.")]
    [Category("Поиск"),DisplayName("Ширина поиска маршрута размеров"),Range(4,72)]
    public int DimensionBeamWidth { get; set; } = 24;

    [Description("Число попыток вернуться к предыдущим решениям при поиске компоновки размеров.")]
    [Category("Поиск"),DisplayName("Лимит перебора при пересечениях размеров"),Range(20,3000)]
    public int DimensionBacktracking { get; set; } = 500;

    [Description("Шаг локальной подстройки графической длины, когда рядом с трубой надо освободить место.")]
    [Category("Поиск"),DisplayName("Шаг локальной поправки трубы, мм"),Range(1,50)]
    public double PipeAdjustment { get; set; } = 10;

    [Description("Длина контекстного пунктирного участка за границей листа.")]
    [Category("Переходы"),DisplayName("Длина пунктирного продолжения, мм"),Range(2,45)]
    public double ContinuationPipeLength { get; set; } = 12;

    [Description("Множитель масштаба штрихов на контекстном продолжении трубы в DWG.")]
    [Category("Переходы"),DisplayName("Масштаб штрихов линии продолжения"),Range(0.5,5)]
    public double ContinuationDashScale { get; set; } = 2.5;

    [Description("Длина видимого штриха базовой пунктирной линии.")]
    [Category("Переходы"),DisplayName("Длина штриха пунктира, мм"),Range(0.2,12)]
    public double DashLength { get; set; } = 2;

    [Description("Длина пробела между видимыми штрихами базовой пунктирной линии.")]
    [Category("Переходы"),DisplayName("Просвет между штрихами, мм"),Range(0.1,12)]
    public double DashGap { get; set; } = 1;
}
