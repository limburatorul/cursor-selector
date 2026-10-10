# Cursor Selector

**Download and details:** [protagonistlabs.app/cursorselector](https://protagonistlabs.app/cursorselector/)

A portable app for changing the Windows cursor: a library of schemes in the app's folder, and a
graphical client that previews each scheme in all 17 roles before you apply it.

## Starting it

Double-click **`CursorSelector.exe`**.

A native .NET Framework executable: no installer, no administrator rights, no external
dependencies and no PowerShell. It can be pinned to the taskbar or to the Start menu.

## How the library works

Every **subfolder** of `Library\` is a scheme. Put the `.cur` / `.ani` files in it and that is all:

```
Library\
  Windows_11_dark\
    pointer.cur
    busy.ani
    ...
  My pack\
    Normal Select.cur
    ...
```

The app matches the files to the Windows roles on its own, in this order:

1. **`scheme.json`** in the folder, if there is one: manual control, takes priority
2. **`Install.inf`** in the folder, if the pack ships one: the author's official mapping
3. **the file names**: it recognises the usual conventions (`Normal Select`, `pointer`, `dgn1`,
   `Diagonal Resize 1`, `VertRes`, `busy`, `link`, `handwriting` and so on)

Files in the root of the folder take priority; subfolders are searched only if the root is empty
(otherwise a "bonus cursors" subfolder would steal roles from the main set).

### `scheme.json`: when you want to correct the mapping

```json
{
  "name": "The name shown in the list",
  "cursors": {
    "Arrow": "pointer.cur",
    "Wait": "busy.ani",
    "Hand": "link.cur"
  }
}
```

Roles you leave out stay unmapped. The role names are the ones in the Windows registry:
`Arrow, Help, AppStarting, Wait, Crosshair, IBeam, NWPen, No, SizeNS, SizeWE, SizeNWSE, SizeNESW,
SizeAll, UpArrow, Hand, Pin, Person`.

## The client

- **Left**: the schemes it found, each with its own arrow drawn beside the name. Search the list with
  the field above it (or `Ctrl+F`; `Down` moves into the list, `Esc` clears it). The schemes from
  `Library\` come first; a single heading marks where the ones already registered in Windows begin
  (Windows' own themes and packs installed through an `.inf`). Right-click a scheme to show its files
  or to remove it from the library — removal goes to the Recycle Bin, and only for folders in
  `Library\`.
- **Right**: the 17 roles in five columns by job — pointing, writing, status, sizing, extras — with
  the real cursor drawn at 52px, taken from the largest image inside the file. `.ani` cursors animate.
  Roles with no file appear dimmed, with `—`, and go back to the Windows default if you apply the
  scheme. Each cursor sits on a mid-grey chip: it is the only shade on which both black cursors
  (Capitaine, VS Cursors) and white ones (Windows Inverted) can be seen; on white or on black some of
  them would disappear. Click a tile to see that cursor at 128px with its file name, size and frame
  count.
- **Apply selected scheme** (it reads *Already applied*, and goes quiet, when the chosen scheme is
  the one in use): writes the values to `HKCU\Control Panel\Cursors`, notifies the system
  (`SPI_SETCURSORS`) and registers the scheme, so it also shows up in Windows' *Mouse Properties*.
  The effect is immediate, with no restart and no sign-out.
- **Restore saved cursors**: goes back to the configuration from before the first apply.
- **Import and apply…**: pick a `.zip` or any file in the folder of an unpacked pack
  (`.cur`, `.ani`, `Install.inf`); dragging and dropping a folder or a `.zip` anywhere on the window
  does the same. The pack is copied into `Library\` (unpacked, without the wrapper folder from the
  archive), selected and applied on the spot, so the download can be deleted afterwards. A name that
  already exists in the library is reused, not overwritten.
- The scheme applied right now carries an `ACTIVE` pill in the list, and applying or restoring is
  confirmed for three seconds in the status line at the bottom right.

The app's interface is in English. Everything can be reached from the keyboard: arrows to move
through the list, `Tab` between buttons, `Space` or `Enter` to press, with a visible focus ring.

## Visual identity

The interface follows the Protagonist Labs visual identity, with the same tokens as the sites:

- **Palette**: `ground #0a0d13`, accent `#4c8dff`, three steps of text (`#e8eef6` / `#8a97aa` /
  `#5d6879`). In the studio's branding note the panels and edges are white with alpha, so they sit
  the same over any background; here they are composited into opaque colours, because WinForms
  draws opaque controls and has no compositing layer under them.
- **Typography by role**: display for the title, body for prose and buttons, mono for figures,
  paths and labels. The faces are Bricolage Grotesque, IBM Plex Sans and IBM Plex Mono, and they
  **travel inside the executable** (`fonts\`, embedded by `build.cmd`, OFL — licences beside them):
  on a machine where none of them is installed, the app used to fall back to Segoe UI and looked
  like any other WinForms program. A copy installed on the machine still wins over the embedded one.
  They are registered twice at startup, once for GDI+ and once for GDI, because `TextRenderer` draws
  through the second one and ignores fonts registered only with the first.
- **Aura**: two faint radial pools behind the content, accent on the left and violet on the right,
  the same shape the websites put behind their hero.
- **Section rail**: an accent dot with a halo, an eyebrow label and a line that fades out to the
  right. GDI+ has no `letter-spacing`, so the labels are drawn character by character.
- **Pill buttons**: the primary one with an accent background, `#06101f` text and a coloured halo;
  the ghost one with an `edge-bright` border over `panel`.
- **The selected row**: an accent tint plus a 3px bar on the leading edge, not a 1px border: on a
  wide row, a border would read as a focused input.
- **DWM**: dark title bar (attribute `20`), rounded corners (`33`) and border colour (`34`, in
  `0x00BBGGRR`, not the order of the palette hex).

Two things from the note can **not** be applied here:

- **Acrylic glass.** The note says so explicitly: WinForms is in the same situation as Tk, where
  GDI paints the client area opaque over the DWM effect. It would take WPF.
- **The scroll bar** stays the one from the Windows dark theme, not the 6px translucent white
  thumb. WinForms has no re-template equivalent; it would mean a scrollbar drawn entirely by hand.

## Files

| File | Role |
|---|---|
| `CursorSelector.exe` | the application |
| `Library\` | the library of schemes, one subfolder per scheme |
| `backup.json` | the cursor configuration from before the first apply; written only once |
| `src\CursorSelector.cs` | the source code |
| `build.cmd` | rebuilds the executable |
| `app.ico` | the icon, embedded in the executable at build time |
| `fonts\` | the brand's typefaces (OFL), embedded in the executable at build time, with their licences |

Deleting a folder from `Library\` removes the scheme from the list. If that scheme was the one
applied, Windows falls back to the default cursors for the missing files: use *Restore saved
cursors* or apply another scheme.

## Rebuilding

After changes in `src\CursorSelector.cs`:

```
build.cmd
```

It uses the C# compiler in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\`, which ships with
Windows, so no SDK needs installing.

The source is plain ASCII: the special characters in the interface (`—`, `·`, `…`) are written as
`\uXXXX` escapes. That way it does not depend on a BOM and does not break if you open it in an
editor that strips one.

## More from Protagonist Labs

- [Frostpane](https://protagonistlabs.app/frostpane/?utm_source=github&utm_medium=readme&utm_campaign=cursorselector): desktop icons in glass panes that stay blurred over an animated wallpaper.
- [Glaze](https://protagonistlabs.app/glaze/?utm_source=github&utm_medium=readme&utm_campaign=cursorselector): a desktop player for YouTube, free.
- [All apps](https://protagonistlabs.app/?utm_source=github&utm_medium=readme&utm_campaign=cursorselector): Windows apps that each do one job properly.
