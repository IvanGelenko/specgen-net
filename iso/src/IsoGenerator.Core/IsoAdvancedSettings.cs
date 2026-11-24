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

    [Category("Трубы и символы"),DisplayName("Шаг длин труб на листе, мм"),Range(1,50)]
    public double PipeGridStep { get; set; } = 10;

    [Category("Трубы и символы"),DisplayName("Разрыв трубы при перекрытии, мм"),Range(0.1,25)]
    public double PipeGap { get; set; } = 6;

    [Category("Трубы и символы"),DisplayName("Отступ линии изоляции, мм"),Range(0.1,20)]
    public double InsulationOffset { get; set; } = 3;

    [Category("Трубы и символы"),DisplayName("Масштаб опор"),Range(0.1,100)]
    public double SupportSymbolScale { get; set; } = 10;

    [Category("Размеры"),DisplayName("Отступ размерной линии от трубы и арматуры, мм"),Range(1,50)]
    public double DimensionClearance { get; set; } = 10;

    [Category("Размеры"),DisplayName("Шаг размерных рядов, мм"),Range(2,30)]
    public double DimensionRowStep { get; set; } = 7;

    [Category("Размеры"),DisplayName("Дополнительные ряды на каждой стороне"),Range(1,18)]
    public int DimensionLaneCount { get; set; } = 8;

    [Category("Размеры"),DisplayName("Подъём размерного числа над линией, мм"),Range(1,10)]
    public double TextLift { get; set; } = 2.8;

    [Category("Размеры"),DisplayName("Высота размерного числа в DWG, мм"),Range(1,10)]
    public double DimensionTextHeight { get; set; } = 3.5;

    [Category("Размеры"),DisplayName("Длина размерной стрелки и стрелки выноски, мм"),Range(0.5,10)]
    public double ArrowSize { get; set; } = 2.5;

    [Category("Размеры"),DisplayName("Зазор от начала выносной линии до детали, мм"),Range(0,10)]
    public double ExtensionOffset { get; set; } = 1;

    [Category("Размеры"),DisplayName("Выход выносной линии за размер, мм"),Range(0,10)]
    public double ExtensionOvershoot { get; set; } = 1;

    [Category("Размеры"),DisplayName("Порог длинной выносной линии размера, мм"),Range(5,100)]
    public double LongExtensionThreshold { get; set; } = 35;

    [Category("Выноски и подписи"),DisplayName("Показывать выноски с ИД арматуры")]
    public bool ShowValveIdentifierLeaders { get; set; } = true;

    [Category("Выноски и подписи"),DisplayName("Предпочтительная длина выноски, мм"),Range(3,60)]
    public double LeaderTarget { get; set; } = 15;

    [Category("Выноски и подписи"),DisplayName("Длина, после которой выноска получает штраф, мм"),Range(5,100)]
    public double LeaderSoftLimit { get; set; } = 20;

    [Category("Выноски и подписи"),DisplayName("Верхняя граница поиска обычной выноски, мм"),Range(10,150)]
    public double LeaderMaxReach { get; set; } = 96;

    [Category("Выноски и подписи"),DisplayName("Высота текста выносок и позиций в DWG, мм"),Range(1,10)]
    public double LeaderTextHeight { get; set; } = 3.5;

    [Category("Выноски и подписи"),DisplayName("Вес пересечения линии выноски"),Range(0,10)]
    public double LeaderCrossingWeight { get; set; } = 0.6;

    [Category("Выноски и подписи"),DisplayName("Вес пересечения выноски арматуры"),Range(0,10)]
    public double ValveLeaderCrossingWeight { get; set; } = 0.35;

    [Category("Выноски и подписи"),DisplayName("Длина колена полки в DWG, мм"),Range(0,10)]
    public double LeaderLanding { get; set; } = 1;

    [Category("Лист"),DisplayName("Граница рабочего поля слева, мм"),Range(0,590)]
    public double PaperLeft { get; set; } = 25;

    [Category("Лист"),DisplayName("Граница рабочего поля справа, мм"),Range(1,594)]
    public double PaperRight { get; set; } = 393;

    [Category("Лист"),DisplayName("Граница рабочего поля снизу, мм"),Range(0,419)]
    public double PaperBottom { get; set; } = 25;

    [Category("Лист"),DisplayName("Граница рабочего поля сверху, мм"),Range(1,420)]
    public double PaperTop { get; set; } = 370;

    [Category("Спецификация"),DisplayName("Высота строки спецификации, мм"),Range(2,30)]
    public double BomRowHeight { get; set; } = 14;

    [Category("Спецификация"),DisplayName("Высота заголовка раздела, мм"),Range(2,20)]
    public double BomHeadingHeight { get; set; } = 7;

    [Category("Спецификация"),DisplayName("Высота текста спецификации, мм"),Range(1,10)]
    public double BomTextHeight { get; set; } = 3;

    [Category("Спецификация"),DisplayName("Допустимая высота тела спецификации, мм"),Range(20,380)]
    public double BomBodyLimit { get; set; } = 330;

    [Category("Поиск"),DisplayName("Лимит одного ракурса, с"),Range(5,600)]
    public int ViewTimeSeconds { get; set; } = 120;

    [Category("Поиск"),DisplayName("Общий лимит автоподбора, с"),Range(5,1200)]
    public int TotalViewTimeSeconds { get; set; } = 180;

    [Category("Поиск"),DisplayName("Лимит выпуска одного листа, с"),Range(10,1800)]
    public int RenderTimeoutSeconds { get; set; } = 300;

    [Category("Поиск"),DisplayName("Лимит вариантов компоновки"),Range(32,4096)]
    public int PlanningVariants { get; set; } = 512;

    [Category("Поиск"),DisplayName("Дополнительные варианты границ листов"),Range(0,24)]
    public int PartitionTrials { get; set; } = 4;

    [Category("Поиск"),DisplayName("Ширина поиска маршрута размеров"),Range(4,72)]
    public int DimensionBeamWidth { get; set; } = 24;

    [Category("Поиск"),DisplayName("Лимит перебора при пересечениях размеров"),Range(20,3000)]
    public int DimensionBacktracking { get; set; } = 500;

    [Category("Поиск"),DisplayName("Шаг локальной поправки трубы, мм"),Range(1,50)]
    public double PipeAdjustment { get; set; } = 10;

    [Category("Переходы"),DisplayName("Длина пунктирного продолжения, мм"),Range(2,45)]
    public double ContinuationPipeLength { get; set; } = 12;

    [Category("Переходы"),DisplayName("Масштаб штрихов линии продолжения"),Range(0.5,5)]
    public double ContinuationDashScale { get; set; } = 2.5;

    [Category("Переходы"),DisplayName("Длина штриха пунктира, мм"),Range(0.2,12)]
    public double DashLength { get; set; } = 2;

    [Category("Переходы"),DisplayName("Просвет между штрихами, мм"),Range(0.1,12)]
    public double DashGap { get; set; } = 1;
}
