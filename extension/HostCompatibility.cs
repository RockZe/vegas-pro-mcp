using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static class HostCompatibility
    {
        internal const string RequiredFileVersion = "2027.0.0.31";

        private static string FileVersion(string path)
        {
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(path);
            return String.Format("{0}.{1}.{2}.{3}", v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }

        internal static Dictionary<string, object> Describe()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string api = typeof(Vegas).Assembly.Location;
            return new Dictionary<string, object> {
                { "executable_path", exe }, { "host_file_version", FileVersion(exe) },
                { "api_file_version", FileVersion(api) },
                { "connector_version", typeof(HostCompatibility).Assembly.GetName().Version.ToString() }
            };
        }

        internal static void AssertCompatible()
        {
            var host = Describe();
            if (!String.Equals(Path.GetFileName((string)host["executable_path"]), "vegas2027.exe", StringComparison.OrdinalIgnoreCase) ||
                (string)host["host_file_version"] != RequiredFileVersion || (string)host["api_file_version"] != RequiredFileVersion)
                throw new InvalidOperationException("This fork only supports VEGAS Pro 2027.0.0 Build 31 (2027.0.0.31). This host will not accept MCP requests.");
        }
    }
}
