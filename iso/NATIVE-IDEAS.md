# Рабочие идеи нативной генерации Plant 3D

Изучены справка Autodesk и проектные IsoConfig.xml / IsoSkeyAcadBlockMap.xml в C:\project for test\Isometric. Штатный генератор не используется как обязательный движок нашего модуля; закрытые внутренние алгоритмы Autodesk не воспроизводились и не извлекались.

## Применено

1. Независимая немасштабная схема: сохранение истинных координат отдельно от минимальных длин на листе. В комментариях IsoConfig View/Geometry определены MinLength, DrawingArea/NoDraw, MaxDistortionRatio.
2. Разделение компонентного символа, конечного условия и оператора. SkeyMap, EndTypeMap и ValveToSpindleSkeyMap применяются отдельно.
3. Два прохода оптимизации. В LayoutOptimization вашего IsoConfig прямо разделены DimensionOptimization (расположение рядов) и AnnotationOptimization (расположение текста). У нас это DimensionChainPlanner → DimensionTextPlacement → AnnotationPlacement.
4. Overall и String с самостоятельными опорными точками. В нашем текущем выпуске габариты обязательны по требованию пользователя; автоматическое скрытие последнего звена цепочки не включено.
5. Прокладка включается в соседний размер без самостоятельного звена. Графический зазор по уточнению пользователя — 2 мм; реальная толщина из данных сохраняется.
6. Стиль текста, символы, отступы и геометрия стрелок задаются явно. Случайное наследование параметров размерного стиля шаблона ограничено.
7. Нехватка места диагностируется; текст и стрелки не уменьшаются. Автоматическое разбиение по насыщенности пока является следующим этапом.

## Первоисточники

- Autodesk: настройки Overall/String/Locating, отступы и варианты учёта прокладок: https://help.autodesk.com/cloudhelp/2026/ENU/Plant3D-UserGuide/files/GUID-CD09ED9E-4CF0-4A42-BADB-420A15C90938.htm
- Autodesk: опорные точки на начале/конце/центре компонента: https://help.autodesk.com/cloudhelp/2021/ENU/Plant3D-UserGuide/files/GUID-CECA5583-328B-4036-BFFA-725BE8099C9B.htm
- Autodesk: соответствие SKEY и проектной библиотеки: https://help.autodesk.com/cloudhelp/2024/ENU/Plant3D-UserGuide/files/GUID-6B69F31B-0D4B-45E7-A9FB-3894225464F4.htm
- Autodesk: создание символов, Ports и условный масштаб: https://help.autodesk.com/cloudhelp/2017/ENU/Plant3D-UserGuide/files/GUID-FC8897C5-2553-4022-A966-BE6F32906866.htm
- Autodesk: разбиение по насыщенности: https://help.autodesk.com/cloudhelp/2026/ENU/Plant3D-UserGuide/files/GUID-2D75898E-99B3-4660-818C-73582EBE7B84.htm
- Autodesk: пользовательские стрелки: https://help.autodesk.com/cloudhelp/2020/ENG/AutoCAD-Core/files/GUID-5D1F8D41-86EC-481F-ACA0-B169F0B91D00.htm
- Предоставленный пользователем ГОСТ 2.307-2011, раздел 5: https://www.spds.ru/info/standarts/2.307-2011/part5.html

Из этих материалов взяты принципы и открытые настройки. Соответствие нашему PDF-образцу проверяется отдельно; наличие настроек нативного генератора само по себе не означает совпадения оформления.
