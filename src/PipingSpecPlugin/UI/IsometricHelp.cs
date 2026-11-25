using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PipingSpecPlugin.UI;

internal static class IsometricHelp
{
    sealed record Section(string Title,string Body);
    sealed record Topic(string Name,params Section[] Sections)
    {
        public bool Matches(string word)=>Name.Contains(word,StringComparison.CurrentCultureIgnoreCase)||
            Sections.Any(s=>s.Title.Contains(word,StringComparison.CurrentCultureIgnoreCase)||
                s.Body.Contains(word,StringComparison.CurrentCultureIgnoreCase));
        public override string ToString()=>Name;
    }

    static readonly Topic[] Topics=
    {
        new("Выпуск",
            new("Первый выпуск","Откройте нужный чертёж из проекта Plant 3D. На вкладке «Выпуск» отметьте линии. Нажмите «Выбрать титульный лист» и укажите заполненный DOCX. Проверьте папку выпуска и нажмите «Создать изометрии»."),
            new("Что будет в папке","Единый PDF с титулом, содержанием и листами, отдельные листы DWG и файлы Word для титула и содержания. Номер комплекта берётся из титульного листа."),
            new("Повторный выпуск","«Заменить» обновляет комплект с этим номером. «Добавить индекс» создаёт комплект с новым индексом. «Добавить линии» появляется, если генератор может дополнить существующий комплект."),
            new("Ход выпуска","В окне хода работы видны текущий этап, время и количество готовых листов. Если запустить отмену, генератор завершит текущий шаг. На финальном этапе сохранения отмена недоступна. По завершении можно открыть папку выпуска.")),
        new("Линии",
            new("Выбор и поиск","Введите часть номера линии. «Отметить найденные» добавляет видимые строки к выбору. Уже отмеченные строки остаются в выборе, даже если фильтр их скрыл. «Снять отметки» очищает весь выбор."),
            new("Ракурс","«Автоматически» подбирает ракурс для каждой линии. «Один для всех линий» применяет направление из соседнего списка. «Для каждой линии» позволяет выбрать направление стрелки севера в колонке «Ракурс». Начальное значение берётся из общего ракурса. Настройка запоминается для текущего чертежа при запуске выпуска."),
            new("Состояние линии","«Сохранена» означает, что для линии есть сохранённая компоновка. «Выпущена» означает, что линия уже выпускалась. За выпуск в текущем комплекте отвечает флажок в колонке «Выпуск»."),
            new("Запомнить компоновку","Отметьте «Запомнить», чтобы сохранить компоновку для следующих выпусков этой линии в данном чертеже. Генератор проверяет применимость сохранённой компоновки при обновлении модели и настроек."),
            new("Пересчитать заново","На вкладке «Настройки» включите «Пересчитать выбранные линии», чтобы пересчитать компоновку для отмеченных линий с сохранённой компоновкой и флажком «Запомнить».")),
        new("Документы",
            new("Титульный лист","В «Реквизитах документов» сохраните образец титула, заполните его в Word и выберите готовый DOCX. Проверьте номер документа, объект и установку, которые генератор прочитал из файла."),
            new("Содержание","При пустом поле используется встроенный шаблон. Чтобы использовать своё оформление, сохраните образец содержания, отредактируйте его в Word и выберите DOCX."),
            new("Арматура, опоры и профили","Номера документов по арматуре и опорам используются в оформлении листов. Команды загрузки и сохранения реквизитов переносят номера и файлы шаблонов в профиле .iso-profile.json.")),
        new("Компоновка",
            new("Шаблон листа и масштаб","Используйте локальный шаблон A2 DWG или DWT. Масштаб символов меняет размер условных обозначений. Минимальная длина трубы в расширенных настройках задаёт длину на бумаге. Размерные числа остаются размерами из модели."),
            new("Ракурс и разбиение","Ракурс выбирается на вкладке «Выпуск». Точки разрыва Plant 3D задают границы листов. Флажок «Игнорировать точки разрыва на модели» разрешает рассчитать границы без них."),
            new("Спецификация","Опция исключения фланцевых деталей действует на комплекты при арматуре. Сюда относятся подключённые фланцы, заглушки, прокладки и крепёж. Самостоятельная фланцевая пара на трубе учитывается."),
            new("Расширенные настройки","Выберите параметр, чтобы увидеть его назначение под таблицей. Параметры сгруппированы по геометрии, размерам, выноскам, листу, спецификации, поиску и переходам. «По умолчанию» сбрасывает значения в открытом окне. Чтобы применить их, нажмите «Сохранить»."),
            new("Оформление КИП","Режим «КИП: только выноска с ИД» оставляет выноску прибора на листе. Условное обозначение и позиция в спецификации появятся при отключении режима.")),
        new("Команды и клавиши",
            new("PSISOMETRIC","Открывает окно генератора для активного чертежа Plant 3D. Эту же команду запускает кнопка изометрий на ленте SpecGen."),
            new("ISOGENERATE","Альтернативная команда запуска. Открывает тот же генератор через PSISOMETRIC."),
            new("Управление в окне","F1 открывает справку по текущему разделу. Ctrl+F переносит фокус в поиск линий. Tab переключает элементы окна. В таблице перемещайтесь стрелками и нажимайте пробел в ячейке с флажком. Esc закрывает окно.")),
        new("Если выпуск не получается",
            new("В списке нет нужной линии","Очистите фильтр и убедитесь, что открыт нужный DWG из проекта Plant 3D. Под номером линии должны числиться детали в текущем чертеже."),
            new("Кнопка выпуска недоступна","В нижней части окна показано, что нужно заполнить. Проверьте выбор линий, титульный лист, папку выпуска и путь к шаблону A2."),
            new("Требуется больше места на листе","Проверьте, включён ли автоматический подбор ракурса. Если ошибка повторяется, посмотрите номер линии, листа и объект в сообщении. Для сложных участков можно задать точки разрыва в модели."),
            new("Диагностика","Если в сообщении указан путь к папке задания, сохраните этот путь вместе с текстом ошибки. Там находятся журналы и данные выпуска для проверки причины."))
    };

    public static void Show(IWin32Window owner,string topic)
    {
        using var window=new Form{Text="Справка · Генератор изометрий",StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(825,545),MinimumSize=new Size(660,420),AutoScaleMode=AutoScaleMode.Dpi,
            Font=new Font("Segoe UI",10),ShowInTaskbar=false,MinimizeBox=false,KeyPreview=true};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=2,RowCount=4};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var searchLabel=new Label{Text="Поиск в справке",AutoSize=true,Margin=new Padding(0,0,0,5)};
        var search=new TextBox{AccessibleName=searchLabel.Text,Dock=DockStyle.Top,Margin=new Padding(0,0,0,10)};
        var topics=new ListBox{Dock=DockStyle.Fill,AccessibleName="Темы справки",IntegralHeight=false,
            Margin=new Padding(0,0,10,0)};
        var article=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,DetectUrls=false,AccessibleName="Текст справки",
            BackColor=SystemColors.Window,ForeColor=SystemColors.WindowText,Margin=new Padding(0),BorderStyle=BorderStyle.None};
        var articlePanel=new Panel{Dock=DockStyle.Fill,BackColor=SystemColors.Window,Padding=new Padding(12),
            Margin=new Padding(0),BorderStyle=BorderStyle.FixedSingle};
        articlePanel.Controls.Add(article);
        var close=new Button{Text="Закрыть",DialogResult=DialogResult.Cancel,AutoSize=true,Anchor=AnchorStyles.Right,
            Padding=new Padding(10,2,10,2),Margin=new Padding(0,12,0,0)};
        window.CancelButton=close;
        layout.Controls.Add(searchLabel,0,0);layout.SetColumnSpan(searchLabel,2);
        layout.Controls.Add(search,0,1);layout.SetColumnSpan(search,2);
        layout.Controls.Add(topics,0,2);layout.Controls.Add(articlePanel,1,2);layout.Controls.Add(close,1,3);
        window.Controls.Add(layout);

        using var heading=new Font(window.Font.FontFamily,17,FontStyle.Bold);
        using var sectionHeading=new Font(window.Font,FontStyle.Bold);
        void Render()
        {
            article.Clear();
            if(topics.SelectedItem is not Topic selected){article.Text="По запросу ничего не найдено. Попробуйте другое слово.";return;}
            article.SelectionFont=heading;
            article.AppendText(selected.Name+"\n\n");
            foreach(var section in selected.Sections)
            {
                article.SelectionFont=sectionHeading;article.AppendText(section.Title+"\n");
                article.SelectionFont=window.Font;article.AppendText(section.Body+"\n\n");
            }
            article.SelectionStart=0;article.ScrollToCaret();
        }
        void Filter(string preferred)
        {
            var words=search.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            var matching=Topics.Where(t=>words.All(t.Matches)).ToArray();
            topics.BeginUpdate();
            try
            {
                topics.Items.Clear();topics.Items.AddRange(matching);
                if(matching.Length>0)topics.SelectedIndex=Math.Max(0,Array.FindIndex(matching,t=>t.Name==preferred));
            }
            finally{topics.EndUpdate();}
            Render();
        }
        topics.SelectedIndexChanged+=(_,_)=>Render();
        search.TextChanged+=(_,_)=>Filter((topics.SelectedItem as Topic)?.Name);
        window.KeyDown+=(_,args)=>
        {
            if(!args.Control||args.KeyCode!=Keys.F)return;
            search.Focus();search.SelectAll();args.SuppressKeyPress=true;
        };
        window.Shown+=(_,_)=>search.Focus();
        Filter(topic);
        window.ShowDialog(owner);
    }
}
