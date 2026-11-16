# installer

Установщик на Inno Setup 6.

`Blackout.iss` — per-user установка в `%LOCALAPPDATA%\Programs\Blackout` без UAC:

- стабильный `AppId`, поэтому новая версия обновляет предыдущую на месте;
- `AppMutex=Blackout.SingleInstance` и `taskkill` перед установкой: окно приложения
  скрывается вместо закрытия, файлы иначе не заменить;
- при удалении снимается автозапуск и предлагается удалить `%APPDATA%\Blackout`;
- русский и английский языки мастера.

Собирается скриптом `build/package.ps1`, версия передаётся как `/DMyAppVersion=`.

```powershell
.\build\package.ps1 -Version 0.1.0
```
