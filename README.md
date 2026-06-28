# Switcher

Windows tray utility for automatic RU/EN keyboard layout correction.

Switcher watches the last typed word, detects common wrong-layout input such as
`ghbdtn -> привет`, switches the active keyboard layout, replaces the typed text,
and plays a system sound. It also supports manual conversion and undo hotkeys.

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

## GitHub Releases

The repository includes GitHub Actions:

- `.github/workflows/build.yml` builds and uploads `Switcher.exe` as an artifact on pushes and pull requests.
- `.github/workflows/release.yml` builds a single-file Windows x64 exe and attaches it to a GitHub Release when a tag like `v0.1.0` is pushed.

Release locally:

```powershell
git tag v0.1.0
git push origin main --tags
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
