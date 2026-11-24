# JBL Keep Awake

A small Windows 10/11 utility that plays a very quiet, continuous tone to help prevent a JBL speaker from switching itself off during silence.

## Use

1. Select your JBL speaker as the default output in Windows Sound settings.
2. Open `JBLKeepAwake.exe` and click **OFF** to start playback. Click **ON** to stop it.
3. Click **×** to hide the window while playback continues. Look for the app icon near the clock, under the **^** arrow if Windows hides it.
4. Double-click the icon to reopen the window, or right-click it and select **Exit** to stop playback and close the app.

The default signal level is **−54 dBFS**. How audible it is and whether it keeps the speaker awake depend on the speaker and volume settings. If the speaker still switches off, try **Signal level** in the tray menu and raise it one step at a time. Lower it if you can hear the tone. After switching outputs or reconnecting the speaker, turn keep-alive off and back on to reopen the Windows audio output.

## Build

Run `build.bat` from Command Prompt, or `.\build.bat` from PowerShell. The executable is written to `dist\JBLKeepAwake.exe`. The build uses the Windows .NET Framework C# compiler. The executable runs on Windows with .NET Framework 4.x and needs no installer, administrator permissions, or third-party packages.

Windows loops a 9.6 KB PCM buffer through its audio API. The app has no playback timer or dedicated audio thread, and releases the audio output when switched off. The signal mixes with other Windows audio without adjusting the system volume.

JBL Keep Awake is an independent utility.
