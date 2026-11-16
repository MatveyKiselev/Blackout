# Blackout.Windows

WinForms-шелл и все Win32-реализации. Точка входа — `Program.cs`, режим приложения —
`ApplicationContext` без формы (форма создаётся по требованию).

```
Program.cs                  STAThread, single instance, логгер, ApplicationConfiguration
TrayApplicationContext.cs   ручная сборка зависимостей и запуск
SettingsForm.cs             окно настроек на Win32-контролах
SettingsWindowService.cs    ленивое окно, скрытие вместо закрытия, trim working set
WinFormsTrayService.cs      NotifyIcon + ContextMenuStrip
WinFormsUiDispatcher.cs     маршалинг в UI-поток через Control.BeginInvoke
SingleInstanceGuard.cs      мьютекс + окно-приёмник IPC
Win32HotkeyService.cs       RegisterHotKey + pump-поток
Win32OverlayService.cs      чёрные окна по мониторам, перехват Esc
Win32IdleMonitor.cs         GetLastInputInfo с ленивым таймером
Win32MonitorService.cs      EnumDisplayMonitors + стабильные id
RegistryAutostartService.cs HKCU\...\Run
Interop/                    P/Invoke и MessagePumpThread
```

## Запуск

```powershell
dotnet run --project Blackout.Windows                  # с окном настроек
dotnet run --project Blackout.Windows -- --background  # только трей
```

## Ручная проверка перед коммитом

1. `--background`: нет окна и кнопки в taskbar, иконка в трее есть, процесс жив.
2. Клик по иконке — открывается окно настроек; крестик скрывает, процесс остаётся.
3. Хоткей (`Ctrl+Alt+B`) при скрытом окне — чёрный экран на выбранных мониторах.
4. `Esc` — оверлей исчезает, курсор возвращается.
5. Второй запуск — открывает настройки первого, второй процесс завершается.
6. «Выход» в трее — иконка исчезает, процесс завершается.
7. Автозапуск: галочка пишет `HKCU\...\Run\Blackout` с `--background`.
8. Простой: включить таймер на 1 минуту и не трогать ввод.

Диагностика — `%APPDATA%\Blackout\blackout.log`.
