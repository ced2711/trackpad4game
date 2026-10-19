# trackpad4game

A small Windows tray app for playing games with a laptop touchpad. While it is switched on:

- **V** acts as the **left** mouse button and **B** as the **right** mouse button. Holding the key holds the button, so
  actions like mining in Minecraft work.
- The touchpad's own clicks and taps are blocked, so a stray tap does not attack or place a block. Moving the pointer still
  works, and the taskbar and tray stay clickable.
- Palm rejection is turned off, so the pointer keeps moving while you hold a click key or W/A/S/D
  (Windows 11 with a precision touchpad; your own setting is put back when it switches off or quits).
- By default it only acts while a game (Minecraft Java `javaw.exe` or Bedrock `Minecraft.Windows.exe`) is the active
  window. Everywhere else, the keys and the touchpad behave normally.

## Install

1. Download `trackpad4game.exe` from the [latest release](../../releases/latest).
2. Put it in its own folder, for example `%LOCALAPPDATA%\Programs\trackpad4game\`, and run it.
   Windows SmartScreen may warn about an unknown publisher, because the exe is not code-signed.

It needs Windows 10 or 11. The .NET Framework 4.x it uses is part of Windows. Nothing else to install.

## Use

1. A touchpad icon appears in the system tray (click `^` near the clock if it is hidden). It lights up green when on.
2. Press **`** (backtick, left of `1`) in your game to switch it on or off. A short sound plays and a small badge shows
   the new state. Middle-clicking the tray icon also switches it.
3. Click the tray icon for the settings panel:
   - the on/off switch and what it is doing right now
   - **Keys**: click a key cap, then press the new key (Esc cancels). Picking a key that is already used swaps the two.
   - **Block touchpad clicks and taps**, **Keep the pointer moving** (palm rejection off while on)
   - **Only in these games**: remove a game with ×. To add one, open the game, then click the tray icon: it is offered
     as **+ name.exe**.
   - sound, **Start with Windows**, language (English / 中文), Windows touchpad settings, Quit

Right-click the icon for a short menu. Settings are saved in `%APPDATA%\trackpad4game\settings.ini`, or in a
`settings.ini` next to the exe if one is there (portable use, and where older versions kept it).
In game-only mode the app remembers whether it was on; in every-app mode it always starts off.

## Tips

- **Key conflicts:** while it is on, the chosen keys become clicks and the game no longer sees them. If the game already uses
  V or B for something (for example a minimap waypoint key), rebind one of them.
- **Typing in chat:** switch it off before typing a message that contains the click keys.
- **External mouse:** while blocking is on, a plugged-in mouse's left/right buttons are blocked too (Windows cannot tell them
  apart from the touchpad at this level). Turn off *Block touchpad clicks* if you use a mouse.
- **Games run as administrator:** Windows does not let a normal app see or send input to them. Run trackpad4game as
  administrator too in that case.
- **Stopped reacting?** Switch it off and on again: that also reinstalls its keyboard and mouse hooks.

## Build and test

- Build: run `build.cmd`. It uses the C# compiler that ships with Windows, so there is nothing to install.
- Test: run `tests\run-tests.cmd`. It runs unit tests for the key and click rules and the settings file, then starts a
  throwaway copy configured with F13/F14/F15 (keys that type nothing), presses those keys, sends fake touchpad clicks and
  checks what gets through. Its own hooks swallow every click, so nothing on screen is clicked; the pointer is moved for a
  moment and put back.

## License

[MIT](LICENSE)
