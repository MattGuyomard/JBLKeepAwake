using System;
using System.Globalization;
using System.IO;

internal sealed class SignalSettings
{
    public const int MinimumFrequency = 40;
    public const int MaximumFrequency = 200;
    public const int DefaultFrequency = 80;
    public const int DefaultLevel = -54;
    public static readonly int[] Levels = { -72, -60, -54, -48, -42, -36, -30 };

    private readonly string path;
    public int Frequency = DefaultFrequency;
    public int Level = DefaultLevel;
    public string OutputId;
    public string OutputName;
    public bool StartSignalOnLaunch;

    public SignalSettings(string path)
    {
        this.path = path;
        try
        {
            string[] lines = File.ReadAllLines(path);
            int value;
            if (lines.Length > 0 && int.TryParse(lines[0], out value)
                && value >= MinimumFrequency && value <= MaximumFrequency)
                Frequency = value;

            if (lines.Length > 1 && int.TryParse(lines[1], out value) && Array.IndexOf(Levels, value) >= 0)
                Level = value;
            if (lines.Length > 2 && lines[2].Length > 0)
                OutputId = lines[2];
            if (lines.Length > 3 && lines[3].Length > 0)
                OutputName = lines[3];
            if (lines.Length > 4)
                bool.TryParse(lines[4], out StartSignalOnLaunch);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Save()
    {
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(temporaryPath, new[]
            {
                Frequency.ToString(CultureInfo.InvariantCulture),
                Level.ToString(CultureInfo.InvariantCulture),
                OutputId ?? string.Empty,
                (OutputName ?? string.Empty).Replace('\r', ' ').Replace('\n', ' '),
                StartSignalOnLaunch.ToString()
            });
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
