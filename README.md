# Switcher

Tray-приложение для Windows, которое автоматически исправляет слова, набранные в неверной RU/EN раскладке, переключает раскладку и проигрывает системный звук.

## Управление

- `Ctrl+Alt+Space` - конвертировать текущее слово или последнее слово перед курсором.
- `Ctrl+Alt+Backspace` - откатить последнее автоматическое исправление.
- Двойной клик по иконке в трее - открыть настройки.
- В меню трея можно включить или выключить автопереключение, звук и автозагрузку.

## Сборка

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 -o .\publish\win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

Готовый файл после публикации:

```text
publish\win-x64\Switcher.exe
```

## Проверка

```powershell
Start-Process .\publish\win-x64\Switcher.exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
```

Self-test проверяет базовые пары вроде `ghbdtn -> привет`, `руддщ -> hello`, `ntcn -> тест`.

## Идеи следующего этапа

- Черный список приложений и режим паузы для password manager, терминалов и игр.
- Редактируемые пользовательские словари.
- История последних исправлений в окне настроек.
- Отдельные звуки для направления `EN -> RU` и `RU -> EN`.
- Конвертация выделенного текста через безопасный clipboard-режим с восстановлением буфера.
