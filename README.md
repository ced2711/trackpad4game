# trackpad4game

A small Windows tray app for playing games with a laptop touchpad. While it is switched on:

- **V** acts as the **left** mouse button and **B** as the **right** mouse button. Holding the key holds the button, so
  actions like mining in Minecraft work.
- The touchpad's own clicks and taps are blocked, so a stray tap does not attack or place a block. Moving the pointer still works.
- Optionally it only acts while a game (by default Minecraft, `javaw.exe`) is the active window. Everywhere else, the keys and
  the touchpad behave normally.

All keys and options can be changed in the settings window.

## Install

1. Download `trackpad4game.exe` from the [latest release](../../releases/latest).
2. Put it in its own folder, for example `%LOCALAPPDATA%\Programs\trackpad4game\`, and run it.
   Windows SmartScreen may warn about an unknown publisher, because the exe is not code-signed.

It needs Windows 10 or 11. The .NET Framework 4.x it uses is part of Windows. Nothing else to install.

## Use

1. A touchpad icon appears in the system tray (click `^` near the clock if it is hidden).
2. Press **`** (backtick, left of `1`), or left-click the tray icon, to switch it on or off.
   A high beep means on, a low beep means off. The icon is green when on and grey when off.
3. Right-click the tray icon for the menu:
   - **Enabled**: switch on or off
   - **Block touchpad clicks while enabled**: switch the touchpad blocking on or off
   - **Settings...**: change the keys (click a key button, then press the new key), the game list and the beep
   - **Windows touchpad settings...**: opens the Windows touchpad page (see the tips below)
   - **Start with Windows**, **Exit**

Settings are saved in `settings.ini` next to the exe.

## Tips

- **Touchpad stops moving while you hold W/A/S/D?** That is Windows palm rejection. Open *Windows touchpad settings...*
  → *Taps* → *Touchpad sensitivity* and choose **Most sensitive**.
- **Key conflicts:** while it is on, the chosen keys become clicks and the game no longer sees them. If the game already uses
  V or B for something (for example a minimap waypoint key), rebind one of them.
- **Typing in chat:** switch it off before typing a message that contains the click keys.
- While blocking is on, an external mouse's left/right buttons are blocked too (Windows cannot tell them apart from the touchpad
  at this level). Turn off *Block touchpad clicks* if you plug in a mouse.

## Build and test

- Build: run `build.cmd`. It uses the C# compiler that ships with Windows, so there is nothing to install.
- Test: run `tests\run-tests.cmd`. It starts a throwaway copy configured with F13/F14/F15 (keys that type nothing), presses
  those keys and checks the clicks it injects. The test's own hook swallows the clicks, so nothing on screen is clicked.

## License

[MIT](LICENSE)
