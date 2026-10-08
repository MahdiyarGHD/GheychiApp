# MAUI -> Avalonia 12 porting guide (temporary; deleted with `_legacy/` when the port is done)

Why: performance. Avalonia 12's Android backend (Skia, own compositor/render thread) removes the native-view-per-control
cost of MAUI. Every port decision below favours a flat visual tree, compiled bindings, virtualised lists and animations that
run on the render thread. Behaviour and look must stay the same as the MAUI app.

## Workflow
- Untouched MAUI reference copy of the whole app: `C:\Users\m.ghanad\source\repos\GheychiApp.maui-ref` (read-only, never edit).
- Not-yet-ported MAUI sources sit in `src/Gheychi.App/_legacy/<same relative path>`. `_legacy` is excluded from the build.
  Port a file by `git mv _legacy/X <final location>` then rewriting it. The final location mirrors the legacy path
  (`_legacy/Controls/ChatView.xaml` -> `Controls/ChatView.axaml`, `.xaml.cs` -> `.axaml.cs`, `_legacy/ViewModels/*.cs` -> `ViewModels/*.cs`).
- Keep the public surface (class names, public methods/events/properties) of each view the same as the legacy one unless this
  guide says otherwise, so the other ports that call it keep compiling.
- No comments that restate the code. Comment only a non-obvious why. Keep existing meaningful comments when they still apply.
- Edit with the Edit/Write tools. NEVER `sed -i` on tracked files (strips CRLF and makes whole-file diffs). New files: LF is fine.
- No Python on this machine. Use bash/PowerShell/dotnet.
- Do not commit, push, or run `git checkout/reset/stash`. Do not modify files outside your assignment (tell the lead instead).

## Build
`dotnet build src/Gheychi.App/Gheychi.App.csproj -c Debug -p:RestoreConfigFile=<scratchpad>\nuget.config -p:AndroidSdkDirectory=...` (the lead tells you the exact command).
Errors: `error AVLN####` = XAML, `CS####` = C#. Fix all errors in your files; warnings about your files too.

## Available shims (namespace `Gheychi.App`, already imported everywhere via a global using)
`Platform.AppContext` / `Platform.CurrentActivity`, `IPlatformApplication.Current?.Services` (DI container),
`Preferences.Default.Get/Set/Remove/ContainsKey`, `MainThread.BeginInvokeOnMainThread/InvokeOnMainThreadAsync`,
`FileSystem.AppDataDirectory/CacheDirectory/OpenAppPackageFileAsync`, `AppInfo.Current.VersionString/BuildString/ShowSettingsUI()`,
`Launcher.Default.OpenAsync(string|Uri)`, `Clipboard.Default.SetTextAsync`, `Permissions.CheckStatusAsync<T>/RequestAsync<T>/ShouldShowRationale<T>`,
`PermissionStatus`, `BasePlatformPermission`, `AppTheme` enum, `ThemeState.IsDark` (fixed per process), `Palette.Brush("#AARRGGBB")` / `Palette.Pick(light, dark)`
(cached frozen brushes - use these for colours computed in code, never `new SolidColorBrush` per row), `AppFonts.Regular/Bold/BadgeDigitOffsetY`.
MAUI-only things are gone: no `Shell`, `Application.Current.RequestedTheme`, `DisplayAlert`, `Handler`, `VisualElement`, `Microsoft.Maui.*`.

## XAML cheat sheet (root namespace `https://github.com/avaloniaui`, `x` = `http://schemas.microsoft.com/winfx/2006/xaml`)
Extra xmlns you will want: `xmlns:vm="clr-namespace:Gheychi.App.ViewModels"`, `xmlns:loc="clr-namespace:Gheychi.App.Localization"`,
`xmlns:theme="clr-namespace:Gheychi.App.Theming"`, `xmlns:ui="clr-namespace:Gheychi.App.Ui"`.
- `AppThemeBinding Light=#A, Dark=#B` -> `{theme:ThemeBrush Light=#A, Dark=#B}` (IBrush; use `{theme:ThemeColor ...}` only where a `Color` is required). Resolved once at load.
  Fixed colours: `Foreground="#EF4444"`. App colours: `{StaticResource PrimaryBrush}` etc (see `Styles/Colors.axaml`, brushes end in `Brush`).
- `{loc:Translate Key}` works unchanged (returns a string).
- Every data template and view root needs `x:DataType` (compiled bindings are the default and required for speed).
- `Label` -> `TextBlock` (`Text`, `TextColor`->`Foreground`, `FontSize`, `LineBreakMode=TailTruncation`->`TextTrimming="CharacterEllipsis"`, `MaxLines`,
  `HorizontalTextAlignment`->`TextAlignment`, `VerticalOptions=Center`->`VerticalAlignment="Center"`, `TextTransform=Uppercase` -> do it in the string, `CharacterSpacing` -> `LetterSpacing`,
  `FontFamily="{x:Static vm:ThreadItem.FontFamilyBold}"` -> `FontFamily="{x:Static ui:... }"` ... use `FontFamily="{x:Static local:AppFonts.Bold}"`-style (xmlns `clr-namespace:Gheychi.App`)).
  Default font family is set on the root (`MainView`), so only bold text sets `FontFamily`.
- `Grid`: `RowDefinitions="Auto,*"`, `ColumnDefinitions`, `RowSpacing`, `ColumnSpacing` exist. `Padding` on Grid does not exist: wrap in `Border Padding` or use a `Panel`/margins.
- `VerticalStackLayout/HorizontalStackLayout` -> `StackPanel` (`Orientation`, `Spacing`). `ScrollView` -> `ScrollViewer`. `AbsoluteLayout/FlexLayout` -> Grid/Canvas/WrapPanel.
- `Border StrokeShape="RoundRectangle 20" StrokeThickness=1 Stroke=.. BackgroundColor=..` -> `Border CornerRadius="20" BorderThickness="1" BorderBrush=".." Background=".."` (use `Panel`/`Grid` with Background when there is no border/corner).
  `BoxView` -> `Border`/`Rectangle`. `Border.Shadow` -> `BoxShadow="0 6 16 0 #40000000"` (only where it really shows; shadows are costly).
- `WidthRequest/HeightRequest` -> `Width/Height` (`MinWidth`, `MinHeight`). `HorizontalOptions/VerticalOptions` -> `HorizontalAlignment/VerticalAlignment` (`Fill`->`Stretch`, `Start/Center/End`).
  `InputTransparent="True"` -> `IsHitTestVisible="False"`. `IsVisible` same. `Margin/Padding` same syntax. `ZIndex` -> `ZIndex`.
- `Image Source="x.png"` + `IconTintBehavior` -> `<ui:Icon Data="{StaticResource Icon.X}" Width=".." Height=".." Foreground="{theme:ThemeBrush ...}" />` (icons are pure geometry in `Styles/Icons.axaml`, key `Icon.<PascalCase file name>`).
  Never use a bitmap for the app's own icons. Icons that change with state: bind `Data` to a `Geometry` property or swap `Foreground`.
- `Entry` / `Editor` -> `TextBox` (`Watermark`, `AcceptsReturn`, `TextWrapping`, `MaxLines`, no border: set `BorderThickness="0" Background="Transparent" Padding="0"` and remove the focus visuals via a style). `SearchBar` -> TextBox.
- `Switch` -> `ToggleSwitch`/custom, `CheckBox`, `RadioButton` same names. `ActivityIndicator` -> `ProgressBar IsIndeterminate`.
- `CollectionView`/`ListView` -> `ListBox` (virtualising; set `SelectionMode` none via `ItemsControl` + `ScrollViewer` + `VirtualizingStackPanel` when selection is not needed, which is faster) with `ItemTemplate`. Use the same row heights as before; fixed row height + no nested star Grids in rows.
  `ItemsLayout` horizontal -> `ItemsPanelTemplate` with `VirtualizingStackPanel Orientation=Horizontal`. Footers/headers go in the scroll content, not as list items, unless they must scroll with it.
  `DataTemplateSelector` -> `IDataTemplate` implementation (class with `Build`/`Match`) or `FuncDataTemplate`; keep one template per row kind.
- `TapGestureRecognizer Tapped=..` -> `Tapped="..."` on the control (`Avalonia.Input.TappedEventArgs`), the control needs `Background="Transparent"` to be hit-testable. Prefer ONE handler on the row, not one per child.
  Long press: `Holding` event (`IsHoldWithMouseEnabled="True"`), `HoldingState.Started`. Pointer capture/drag: `PointerPressed/Moved/Released`.
- `VisualStateManager`/`DataTrigger` -> bind to a computed property or use style classes (`Classes.foo="{Binding X}"`) + `<Style Selector="...">`.
- `Behaviors`: replace with attached properties/handlers in code-behind; do not use Avalonia.Xaml.Behaviors.
- `Shell.Current.DisplayAlert/ActionSheet/Prompt` -> `Ui.Dialogs` (lead provides: `Dialogs.AlertAsync(title, message, accept, cancel)`, `Dialogs.ActionSheetAsync(title, cancel, destruction, params string[] buttons)`).
- `Toast.Show(text)` stays (`Gheychi.App.Ui.Toast`, Android toast).
- Visual tree depth/size is the cost: avoid wrapper Borders/Grids that add nothing, avoid `Opacity`/`Effect`/`Clip` on big areas, avoid `DynamicResource` in rows.

## Animations
Overlay slides and fades use the composition API (`ElementComposition.GetElementVisual(control)`, `CompositionAnimation`/implicit animations on `Offset`/`Opacity`): they run on the render thread and do not wait for the UI thread.
The lead provides `Ui.OverlayAnimator` (SlideYAsync / SlideXAsync / FadeAsync) with the same durations as the legacy `OverlayAnimator`. Do not use `DispatcherTimer` loops for animation.

## Performance rules from the MAUI app that still hold (see /PERFORMANCE.md): cheap work before a slide, list fill after it; never `Reset` a list when a few rows changed;
colours/brushes are static or cached (`Palette`), never created in getters; reads/SQLite/text processing in `Task.Run`; compiled bindings everywhere.
What disappears with Avalonia: native tab bar hacks, window-inset re-dispatch, `HasFixedSize`, handler mappers, the overlay "warm-up" gymnastics (XAML is compiled; build overlays lazily but they no longer need staged warm-up -
keep it simple: create on first use, keep alive, `IsVisible=false` when parked).
