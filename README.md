# Debug Console for Godot C#

## 0. Install

- nuget package `Microsoft.CodeAnalysis` is required for C# runtime execution.
- Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in your csproj file, just inside `<PropertyGroup>` part. 
- Add your custom code in `DebugConsoleController._ToggleTerminal`, for example: disable capture mouse when console is open.

## 1. Usage

- Press ``` ` ``` key (the key left to `1`), console UI will be visible.
- Press again for close console.
- Press `Up` and `Down` key for history commands selection.
- Quick guides
  - `#NodePath` for quick get node in scene.
    - eg: `#Player/HUD/HealthBar`
  - Tab completion for node's properties, fields and member functions
    - eg: input `#Player.Get`, then press `TAB`
  - Support temporary variables, with `%` prefix.
    - eg: `%a = #Player.MoveSpeed`
    - eg: `%b = %a * 2`
  - Global variable `D` is pre-defined. `D.SCENE` for current scene node.
    - eg: `%n = new Node()`
    - eg: `D.SCENE.AddChild(%n)`

## 2. Todo

- Support `Tab` completion for non-node cases.

## 3. License

- The Hack font license is at `fonts/LICENSE_HACK.md`. Check https://github.com/source-foundry/Hack/ for more detail.
- This plugin itself is in MIT license.
