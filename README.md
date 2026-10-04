# LogiToolboxPlugin

**LogiToolbox** is a plugin for [Logi Plugin Service](https://logitech.github.io/actions-sdk-docs/) (the successor to Loupedeck software). It adds a small set of general-purpose actions for Loupedeck / Logitech console devices: keyboard automation, a network ping, and a dial counter.

It is a *universal* plugin: it isn't tied to any application and works with whatever is in the foreground.

## Actions

| Action | Type | Group | What it does |
| --- | --- | --- | --- |
| **Type Text** | Button command | Keyboard | Types the configured text into the foreground application, one character at a time. Configurable delay between keystrokes (default 50 ms, clamped to 0–1000 ms). Ignores presses while a previous run is still typing. |
| **Repeat Key Combination** | Button command | Keyboard | Press once to start sending a key combination to the foreground application on a timer; press again to stop. Configurable interval (default 1000 ms, minimum 20 ms). The button icon shows a circular arrow that is dim when idle, blue while repeating, and flashes on every key press. Several buttons can repeat different keys independently. |
| **Ping** | Button command | Network | Pings a per-button host name or IP address (2 s timeout) and shows the round-trip time, or the failure status, on the button. |
| **Counter** | Dial adjustment | Counters | Counts dial rotation ticks and short presses, shown as `ticks (pressesx)`. A long press resets both counts. |

Button commands use the Action Editor, so each button you assign has its own settings (host, text, key, interval) configured in the Logi software.

## Requirements

- Windows 11 (macOS paths are present in the project but `pluginFolderMac` is disabled in the package manifest)
- [Logi Plugin Service](https://www.logitech.com/) installed (provides `PluginApi.dll`)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A supported device: the `LoupedeckCtFamily` (Loupedeck CT, Live, Live S, Razer Stream Controller / X). Minimum Loupedeck version 6.0.
- `logiplugintool` (ships with the Logi Plugin SDK) for packaging and installing

## Building and running

The project references `PluginApi.dll` from `C:\Program Files\Logi\LogiPluginService\`.

```powershell
dotnet build -c Debug
```

A build will:

1. Compile to `bin\<Configuration>\bin\`.
2. Copy `src\package\**` (the `metadata` folder with `LoupedeckPackage.yaml` and the icon) next to the output.
3. Write a `.link` file into `%LocalAppData%\Logi\LogiPluginService\Plugins\` so the service loads the plugin straight from the build folder.
4. Send a `loupedeck:plugin/LogiToolbox/reload` command so the running service hot-reloads it.

`dotnet clean` removes the link file and output directories.

VS Code tasks are provided in `.vscode/tasks.json`: *Build (Debug)*, *Build (Release)*, *Package Plugin*, *Install Plugin*.

### Packaging

```powershell
dotnet build -c Release
logiplugintool pack ./bin/Release ./LogiToolbox.lplug4
logiplugintool install ./LogiToolbox.lplug4
```

## Project layout

```
LogiToolboxPlugin.sln
src/
├── LogiToolboxPlugin.csproj      net10.0 project, build/link/reload targets
├── LogiToolboxPlugin.cs          Plugin entry point (universal, API-only)
├── LogiToolboxApplication.cs     ClientApplication stub required by the SDK
├── Actions/
│   ├── CounterAdjustment.cs      Dial counter
│   ├── PingCommand.cs            Ping button
│   ├── RepeatKeyCommand.cs       Key repeater with custom-drawn icon
│   └── TypeTextCommand.cs        Text typer
├── Helpers/
│   ├── PluginLog.cs              Logging wrapper
│   └── PluginResources.cs        Embedded-resource helpers
└── package/metadata/             LoupedeckPackage.yaml and plugin icon
```

## Adding an action

Add a class in `src/Actions/` deriving from `ActionEditorCommand` (buttons) or `PluginDynamicAdjustment` (dials). The service discovers it automatically; no registration is needed. Because one command instance is shared by every button it's assigned to, keep per-button state keyed by the button's configuration, as `PingCommand` and `RepeatKeyCommand` do.

## License

[MIT](LICENSE.txt), matching `license: MIT` in `LoupedeckPackage.yaml`. Note that the package description in that file is still the template placeholder.
