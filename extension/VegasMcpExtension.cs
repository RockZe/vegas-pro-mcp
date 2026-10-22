using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    public sealed class VegasMcpExtension : ICustomCommandModule
    {
        internal const string ProductName = "United Taps VEGAS MCP";
        private Vegas _vegas;
        private Control _dispatcher;
        private Thread _listenerThread;
        private string _installRoot;
        private string _token;
        private string _pipeName;
        private CustomCommand _statusCommand;
        private RenderManager _renderManager;
        private bool _enabled;

        public void InitializeModule(Vegas vegas)
        {
            _vegas = vegas;
            _installRoot = Path.GetDirectoryName(typeof(VegasMcpExtension).Assembly.Location);
            try { HostCompatibility.AssertCompatible(); }
            catch (Exception ex) { Log("Connector disabled: " + ex.Message); return; }
            _token = ReadToken();
            _pipeName = "UnitedTaps.VegasMcp." + Process.GetCurrentProcess().Id;
            _renderManager = new RenderManager(_vegas);

            _dispatcher = new Control();
            _dispatcher.CreateControl();

            WriteDiscoveryRecord();
            _listenerThread = new Thread(ListenLoop);
            _listenerThread.IsBackground = true;
            _listenerThread.Name = "UnitedTaps VEGAS MCP pipe listener";
            _listenerThread.Start();
            _enabled = true;
            Log("Extension initialized on " + _pipeName + ".");
        }

        public ICollection GetCustomCommands()
        {
            if (!_enabled) return new CustomCommand[0];
            _statusCommand = new CustomCommand(CommandCategory.Tools, "UnitedTapsVegasMcpStatusV1");
            _statusCommand.DisplayName = "United Taps VEGAS MCP Status";
            _statusCommand.Invoked += HandleStatusInvoked;
            return new CustomCommand[] { _statusCommand };
        }

        private void HandleStatusInvoked(object sender, EventArgs args)
        {
            string project = _vegas.Project == null || _vegas.Project.IsUntitled
                ? "Untitled project"
                : _vegas.Project.FilePath;
            MessageBox.Show(
                ProductName + " is running.\r\n\r\nPipe: " + _pipeName + "\r\nProject: " + project,
                ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private string ReadToken()
        {
            string path = Path.Combine(_installRoot, "UnitedTaps.VegasMcp.token");
            if (!File.Exists(path))
                throw new InvalidOperationException("Missing connector token: " + path);
            string value = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (value.Length < 32)
                throw new InvalidOperationException("The connector token is invalid.");
            return value;
        }

        private void WriteDiscoveryRecord()
        {
            string instances = Path.Combine(_installRoot, "UnitedTaps.VegasMcp.instances");
            Directory.CreateDirectory(instances);
            var record = new Dictionary<string, object>();
            record["pid"] = Process.GetCurrentProcess().Id;
            record["pipe_name"] = _pipeName;
            record["vegas_version"] = _vegas.Version;
            record["vegas_build"] = _vegas.BuildNumber;
            record["started_utc"] = DateTime.UtcNow.ToString("o");
            foreach (var item in HostCompatibility.Describe()) record[item.Key] = item.Value;
            string json = CreateSerializer().Serialize(record);
            File.WriteAllText(
                Path.Combine(instances, Process.GetCurrentProcess().Id + ".json"),
                json,
                new UTF8Encoding(false));
        }

        private void ListenLoop()
        {
            while (true)
            {
                try
                {
                    using (NamedPipeServerStream pipe = CreatePipe())
                    {
                        pipe.WaitForConnection();
                        HandleClient(pipe);
                    }
                }
                catch (Exception ex)
                {
                    Log("Listener error: " + ex);
                    Thread.Sleep(250);
                }
            }
        }

        private NamedPipeServerStream CreatePipe()
        {
            PipeSecurity security = new PipeSecurity();
            SecurityIdentifier sid = WindowsIdentity.GetCurrent().User;
            security.SetOwner(sid);
            security.AddAccessRule(new PipeAccessRule(
                sid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                65536,
                65536,
                security);
        }

        private void HandleClient(NamedPipeServerStream pipe)
        {
            pipe.ReadMode = PipeTransmissionMode.Byte;
            using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true))
            using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, true))
            {
                writer.AutoFlush = true;
                string line = ReadRequestLine(reader);
                if (line == null)
                    return;
                if (line.Length > 4 * 1024 * 1024)
                    throw new InvalidOperationException("Request exceeded the 4 MB connector limit.");

                Dictionary<string, object> request = CreateSerializer().Deserialize<Dictionary<string, object>>(line);
                string requestId = GetString(request, "request_id");
                string suppliedToken = GetString(request, "token");
                string action = GetString(request, "action");
                Dictionary<string, object> parameters = GetDictionary(request, "params");

                Dictionary<string, object> response = new Dictionary<string, object>();
                response["request_id"] = requestId;

                if (!FixedTimeEquals(_token, suppliedToken))
                {
                    response["ok"] = false;
                    response["error"] = Error("Unauthorized", "The local connector token did not match.");
                    writer.WriteLine(CreateSerializer().Serialize(response));
                    return;
                }

                object data = null;
                Exception commandError = null;
                DateTime expires = DateTime.UtcNow.AddMinutes(3);
                object expiryValue;
                DateTime suppliedExpiry;
                if (request.TryGetValue("expires_at_utc", out expiryValue) &&
                    DateTime.TryParse(Convert.ToString(expiryValue), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out suppliedExpiry) && suppliedExpiry < expires)
                    expires = suppliedExpiry;
                // A completion task stays valid if a native operation outlives the
                // client's timeout. A disposed wait handle could crash the UI thread.
                TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
                {
                    _dispatcher.BeginInvoke(new MethodInvoker(delegate
                    {
                        try
                        {
                            HostCompatibility.AssertCompatible();
                            if (DateTime.UtcNow >= expires)
                                throw new TimeoutException("The connector request expired before VEGAS could execute it. Inspect the project before retrying.");
                            data = action.StartsWith("render.", StringComparison.Ordinal)
                                ? _renderManager.Execute(action, parameters)
                                : VegasActions.Execute(_vegas, action, parameters);
                        }
                        catch (Exception ex)
                        {
                            commandError = ex;
                        }
                        finally
                        {
                            completion.TrySetResult(true);
                        }
                    }));

                    TimeSpan remaining = expires - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || !completion.Task.Wait(remaining))
                    {
                        commandError = new TimeoutException("VEGAS did not complete the command before its deadline. An operation already executing may still finish; inspect before retrying.");
                    }
                }

                if (commandError == null)
                {
                    response["ok"] = true;
                    response["data"] = data;
                }
                else
                {
                    Log("Command " + action + " failed: " + commandError);
                    response["ok"] = false;
                    response["error"] = Error(commandError.GetType().Name, commandError.Message);
                }

                writer.WriteLine(CreateSerializer().Serialize(response));
                Log("Request " + requestId + " action=" + action + " ok=" + (commandError == null));
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 8 * 1024 * 1024;
            serializer.RecursionLimit = 100;
            return serializer;
        }

        private static string ReadRequestLine(StreamReader reader)
        {
            // Bound reads before allocating the entire line; an idle local client
            // must not monopolize the only listener indefinitely.
            var line = new StringBuilder();
            var buffer = new char[4096];
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                Task<int> read = reader.ReadAsync(buffer, 0, buffer.Length);
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !read.Wait(remaining))
                    throw new TimeoutException("Timed out reading connector request.");
                int count = read.Result;
                if (count == 0) return line.Length == 0 ? null : line.ToString();
                int end = Array.IndexOf(buffer, '\n', 0, count);
                int length = end < 0 ? count : end;
                if (line.Length + length > 4 * 1024 * 1024)
                    throw new InvalidOperationException("Request exceeded the 4 MB connector limit.");
                line.Append(buffer, 0, length);
                if (end >= 0) return line.ToString();
            }
        }

        private static Dictionary<string, object> Error(string type, string message)
        {
            var error = new Dictionary<string, object>();
            error["type"] = type;
            error["message"] = message;
            return error;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
                throw new InvalidOperationException("Missing request field: " + key);
            return Convert.ToString(value);
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
                return new Dictionary<string, object>();
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary == null)
                throw new InvalidOperationException("Request field is not an object: " + key);
            return dictionary;
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            if (expected == null || actual == null)
                return false;
            byte[] a = Encoding.UTF8.GetBytes(expected);
            byte[] b = Encoding.UTF8.GetBytes(actual);
            int difference = a.Length ^ b.Length;
            int count = Math.Max(a.Length, b.Length);
            for (int i = 0; i < count; i++)
            {
                byte av = i < a.Length ? a[i] : (byte)0;
                byte bv = i < b.Length ? b[i] : (byte)0;
                difference |= av ^ bv;
            }
            return difference == 0;
        }

        private void Log(string message)
        {
            try
            {
                string runtime = Path.Combine(_installRoot, "UnitedTaps.VegasMcp.runtime");
                Directory.CreateDirectory(runtime);
                string logPath = Path.Combine(runtime, "vegas-extension.log");
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 2 * 1024 * 1024)
                    File.WriteAllText(logPath, "Log truncated at 2 MiB." + Environment.NewLine, new UTF8Encoding(false));
                File.AppendAllText(
                    logPath,
                    DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics must never crash VEGAS.
            }
        }
    }
}
