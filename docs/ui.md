---
title: Interface toolkit
nav_order: 11
---

# Interface toolkit

`SharpProspero.Ui` builds screens out of controls — labels, buttons, lists, checkboxes and progress
bars — and drives them with the controller, so an application does not draw pixels by hand. Add the
controls to a screen, and each frame hand it the input and let it draw. It runs on the same drawing
surface as the rest of the SDK, so it composes with anything you draw yourself.

## The idea

A `UiScreen` holds a tree of controls, usually a `StackPanel` of them. Each frame you:

1. **Lay it out** into a rectangle (`Layout`), which places every control and finds the focusable ones.
2. **Update** it with the frame's input (`Update`), which offers the input to the focused control and
   otherwise moves focus in the pressed direction.
3. **Draw** it (`Draw`), which renders the tree and highlights the focused control.

The controller drives it: the d-pad moves focus, cross confirms, and circle goes back.

## A screen in a frame loop

```csharp
using SharpProspero.Application;
using SharpProspero.Graphics;
using SharpProspero.Ui;

internal sealed class Menu : ProsperoApp
{
    private UiScreen _screen = null!;

    protected override void OnLoad()
    {
        var panel = new StackPanel()
            .Add(new Label("My Application") { Scale = 4 })
            .Add(new Button("Start", () => StartGame()))
            .Add(new Checkbox("Fullscreen", true))
            .Add(new Button("Quit", () => Exit()));

        _screen = new UiScreen(panel);
    }

    protected override void OnFrame(FrameContext context)
    {
        Surface surface = context.Surface;

        _screen.Layout(new UiRect(80, 80, surface.Width - 160, surface.Height - 160));
        _screen.Update(UiInput.From(context.Input, context.PreviousInput));

        surface.Clear(_screen.Theme.Background);
        _screen.Draw(surface);
    }

    private void StartGame() { /* ... */ }
    private void Exit() => /* request exit on the next OnFrame's context */ _exit = true;
    private bool _exit;
}
```

`UiInput.From` reads the d-pad, cross and circle from this frame's sample and the previous one, so each
press counts once. `Render` combines the layout and draw steps when you do not need them apart.

## Controls

| Control | What it is |
|---|---|
| `Label` | A line of text, for titles and read-only values. Not focusable. |
| `Button` | Activates on confirm and calls its action. Can be disabled. |
| `Checkbox` | An on/off setting the user toggles with confirm. |
| `ListView` | A vertical list the user moves through and opens with confirm; it scrolls to keep the selection in view. |
| `ProgressBar` | A bar that fills from the left to show a fraction. |
| `Image` | A picture drawn from a surface (for example a decoded `PngImage`). |
| `StackPanel` | Stacks its children top to bottom with a gap between them; the usual root of a screen. |

A `ListView` handles up and down itself to move its selection, but leaves them unused at its top and
bottom rows, so pressing up on the first row or down on the last moves focus to the control above or
below the list. This is what lets a list sit among other controls.

## Focus moves the way you expect

Pressing a direction moves focus to the nearest focusable control that way, measured from the centers
and favouring one that stays on the line over one that drifts to the side. A screen laid out as a
column moves straight down it; a screen with controls side by side moves across. The focused control is
offered the input first, so a button activates and a list scrolls before focus moves.

## Theming

`UiTheme` holds the colors and spacing. Use `UiTheme.Default` for a dark theme, or change what you want
with an object initializer and pass it to the screen:

```csharp
var theme = new UiTheme
{
    Accent = Color.FromRgb(255, 170, 60),
    TextScale = 3,
};
var screen = new UiScreen(panel, theme);
```

## A control of your own

Derive from `UiElement` for a control the built-in set does not cover. Override `Measure` to say how
tall it wants to be, `Draw` to render within its `Bounds`, and, if it is interactive, `IsFocusable` and
`HandleInput`. Return true from `HandleInput` when you use the input so the screen does not also move
focus with it.

## Saving what is on screen

Anything drawn to a surface can be saved as a PNG with [`PngEncoder`](bindings.md), so a screen can
offer a screenshot:

```csharp
using var module = SystemModule.Load(SystemModuleId.PngEnc);
PngEncoder.Save(context.Surface, "/data/screenshot.png");
```
