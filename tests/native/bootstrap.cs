using System;
using System.Diagnostics;
using System.IO;
using ScriptPortal.Vegas;

// Only launched explicitly by test:live:prepare into a fresh VEGAS process.
public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string directory = Path.GetDirectoryName(Script.File);
        try
        {
            string version = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName).FileVersion;
            if (version != "2027.0.0.31") throw new InvalidOperationException("Wrong VEGAS host: " + version);
            if (!vegas.Project.IsUntitled || vegas.Project.IsModified || vegas.Project.Tracks.Count != 0)
                throw new InvalidOperationException("Refusing to replace a nonempty or modified project. Close VEGAS and launch the fixture again.");
            string path = Path.Combine(directory, "acceptance.veg");
            if (File.Exists(path)) throw new InvalidOperationException("Fixture project already exists.");
            vegas.Project.Video.Width = 640;
            vegas.Project.Video.Height = 360;
            vegas.Project.Video.FrameRate = 25;
            vegas.Project.Audio.SampleRate = 48000;
            vegas.Project.Audio.BitDepth = 16;
            if (!vegas.SaveProject(path)) throw new InvalidOperationException("Could not save fixture project.");
            File.WriteAllText(Path.Combine(directory, "bootstrap.ok"), Process.GetCurrentProcess().Id.ToString());
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "bootstrap.error.txt"), ex.ToString());
        }
    }
}
