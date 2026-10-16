using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal sealed class RenderManager
    {
        private sealed class RenderJob
        {
            public string Id;
            public string ProjectPath;
            public string OutputPath;
            public string Renderer;
            public string Template;
            public string Status;
            public int PercentComplete;
            public string ErrorMessage;
            public string TimeElapsed;
            public string TimeLeft;
            public long CurrentFrame;
            public long TotalFrames;
            public long EstimatedBytes;
            public bool CancelRequested;
            public DateTime StartedUtc;
            public DateTime? FinishedUtc;
        }

        private readonly Vegas _vegas;
        private readonly object _sync = new object();
        private RenderJob _job;

        public RenderManager(Vegas vegas)
        {
            _vegas = vegas;
            _vegas.RenderStarted += HandleRenderStarted;
            _vegas.RenderProgress += HandleRenderProgress;
            _vegas.RenderFinished += HandleRenderFinished;
        }

        public object Execute(string action, Dictionary<string, object> p)
        {
            if (action == "render.list") return ListRenderers(p);
            if (action == "render.start") return StartRender(p);
            if (action == "render.status") return Status(p);
            if (action == "render.cancel") return Cancel(p);
            throw new InvalidOperationException("Unsupported render action: " + action);
        }

        private object ListRenderers(Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(p);
            string contains = GetString(p, "contains", null);
            bool includeInvalid = GetBool(p, "include_invalid_templates", false);
            List<object> renderers = new List<object>();
            foreach (Renderer renderer in _vegas.Renderers)
            {
                if (!String.IsNullOrWhiteSpace(contains) &&
                    renderer.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0 &&
                    renderer.FileTypeName.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Dictionary<string, object> item = RendererData(renderer);
                List<object> templates = new List<object>();
                foreach (RenderTemplate template in renderer.Templates)
                {
                    if (!includeInvalid && !template.IsValid()) continue;
                    templates.Add(TemplateData(template));
                }
                item["templates"] = templates;
                renderers.Add(item);
            }
            return new Dictionary<string, object> { { "renderers", renderers }, { "count", renderers.Count } };
        }

        private object StartRender(Dictionary<string, object> p)
        {
            GuardProject(p);
            lock (_sync)
            {
                if (_job != null && IsActiveStatus(_job.Status))
                    throw new InvalidOperationException("A VEGAS MCP render is already active: " + _job.Id);
            }
            Renderer renderer = FindRenderer(p);
            RenderTemplate template = FindTemplate(renderer, p);
            string outputPath = Path.GetFullPath(RequireString(p, "output_path"));
            if (File.Exists(outputPath)) throw new IOException("Render output already exists: " + outputPath);
            string directory = Path.GetDirectoryName(outputPath);
            if (String.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Render output must include a directory.");
            Directory.CreateDirectory(directory);
            ValidateOutputExtension(template, outputPath);

            RenderArgs args = new RenderArgs(_vegas.Project);
            args.OutputFile = outputPath;
            args.RenderTemplate = template;
            args.ShowOpenButtonsOnComplete = false;
            args.WaitForIdle = false;
            args.IncludeMarkers = GetBool(p, "include_markers", false);
            args.GenerateLoudnessLog = GetBool(p, "generate_loudness_log", false);
            args.SaveProjectPathLink = GetBool(p, "save_project_path_link", false);
            args.StretchToFill = GetBool(p, "stretch_to_fill", false);
            args.UseProjectRotation = GetBool(p, "use_project_rotation", true);
            args.SaveAsMono = GetBool(p, "save_as_mono", false);
            args.UseChannelMapping = GetBool(p, "use_channel_mapping", false);
            bool useSelection = GetBool(p, "use_selection", false);
            args.UseSelection = useSelection;
            if (!useSelection && HasAnyTime(p, "start"))
            {
                if (!HasAnyTime(p, "length")) throw new InvalidOperationException("A ranged render requires both start and length.");
                args.Start = RequireTime(_vegas.Project, p, "start", true);
                args.Length = RequireTime(_vegas.Project, p, "length", false);
                if (args.Start.Nanos < 0 || args.Length.Nanos <= 0 || args.Start + args.Length > _vegas.Project.Length)
                    throw new InvalidOperationException("The requested render range is outside the project.");
            }
            else if (!useSelection)
            {
                args.Start = ProjectTimecode.FromFrames(_vegas.Project, 0);
                args.Length = _vegas.Project.Length;
            }

            RenderJob job = new RenderJob();
            job.Id = Guid.NewGuid().ToString("N");
            job.ProjectPath = _vegas.Project.FilePath;
            job.OutputPath = outputPath;
            job.Renderer = renderer.Name;
            job.Template = template.Name;
            job.Status = "Starting";
            job.StartedUtc = DateTime.UtcNow;
            lock (_sync) _job = job;
            RenderStatus initial;
            try
            {
                initial = _vegas.BeginRender(args);
                lock (_sync)
                {
                    if (_job == job && _job.Status == "Starting") _job.Status = initial.ToString();
                    if (!IsActiveStatus(_job.Status) && _job.FinishedUtc == null) _job.FinishedUtc = DateTime.UtcNow;
                }
            }
            catch
            {
                lock (_sync) { job.Status = "Failed"; job.FinishedUtc = DateTime.UtcNow; }
                throw;
            }
            return JobData(job);
        }

        private object Status(Dictionary<string, object> p)
        {
            lock (_sync)
            {
                if (_job == null) return new Dictionary<string, object> { { "has_job", false } };
                GuardJobId(p, _job);
                Dictionary<string, object> data = JobData(_job);
                data["has_job"] = true;
                return data;
            }
        }

        private object Cancel(Dictionary<string, object> p)
        {
            GuardProject(p);
            lock (_sync)
            {
                if (_job == null) throw new InvalidOperationException("There is no VEGAS MCP render job to cancel.");
                GuardJobId(p, _job);
                if (!IsActiveStatus(_job.Status)) throw new InvalidOperationException("The render job is not active.");
                _job.CancelRequested = true;
            }
            _vegas.CancelAsynchronousTasks();
            return Status(p);
        }

        private void HandleRenderStarted(object sender, EventArgs args)
        {
            lock (_sync) if (_job != null) _job.Status = "Rendering";
        }

        private void HandleRenderProgress(object sender, RenderStatusEventArgs args)
        {
            lock (_sync)
            {
                if (_job == null) return;
                UpdateFromArgs(_job, args);
            }
        }

        private void HandleRenderFinished(object sender, RenderStatusEventArgs args)
        {
            lock (_sync)
            {
                if (_job == null) return;
                UpdateFromArgs(_job, args);
                _job.FinishedUtc = DateTime.UtcNow;
            }
        }

        private static void UpdateFromArgs(RenderJob job, RenderStatusEventArgs args)
        {
            job.Status = args.Status.ToString();
            job.PercentComplete = args.PercentComplete;
            job.ErrorMessage = args.ErrorMessage;
            job.TimeElapsed = args.TimeElapsed;
            job.TimeLeft = args.TimeLeft;
            job.CurrentFrame = args.iFrame;
            job.TotalFrames = args.cFrame;
            job.EstimatedBytes = args.totalBytes;
        }

        private Renderer FindRenderer(Dictionary<string, object> p)
        {
            string name = GetString(p, "renderer_name", null);
            string classIdText = GetString(p, "renderer_class_id", null);
            foreach (Renderer renderer in _vegas.Renderers)
            {
                if (!String.IsNullOrWhiteSpace(name) &&
                    (String.Equals(renderer.Name, name, StringComparison.OrdinalIgnoreCase) || String.Equals(renderer.FileTypeName, name, StringComparison.OrdinalIgnoreCase))) return renderer;
                Guid classId;
                if (!String.IsNullOrWhiteSpace(classIdText) && Guid.TryParse(classIdText, out classId) && renderer.ClassID == classId) return renderer;
            }
            throw new InvalidOperationException("The requested renderer was not found.");
        }

        private static RenderTemplate FindTemplate(Renderer renderer, Dictionary<string, object> p)
        {
            string name = GetString(p, "template_name", null);
            string guidText = GetString(p, "template_guid", null);
            foreach (RenderTemplate template in renderer.Templates)
            {
                if (!template.IsValid()) continue;
                if (!String.IsNullOrWhiteSpace(name) && String.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)) return template;
                Guid guid;
                if (!String.IsNullOrWhiteSpace(guidText) && Guid.TryParse(guidText, out guid) && template.TemplateGuid == guid) return template;
            }
            throw new InvalidOperationException("The requested render template was not found or is invalid.");
        }

        private static void ValidateOutputExtension(RenderTemplate template, string outputPath)
        {
            string extension = Path.GetExtension(outputPath);
            foreach (string allowed in template.FileExtensions)
            {
                string normalized = (allowed ?? "").Trim();
                if (normalized.StartsWith("*.", StringComparison.Ordinal)) normalized = normalized.Substring(1);
                else if (!normalized.StartsWith(".", StringComparison.Ordinal)) normalized = "." + normalized;
                if (String.Equals(extension, normalized, StringComparison.OrdinalIgnoreCase)) return;
            }
            throw new InvalidOperationException("The output extension does not match the selected render template.");
        }

        private static Dictionary<string, object> RendererData(Renderer renderer)
        {
            return new Dictionary<string, object> {
                { "name", renderer.Name }, { "file_type", renderer.FileTypeName }, { "class_id", renderer.ClassID.ToString() },
                { "file_extensions", renderer.FileExtensions }, { "supports_audio", renderer.SupportsAudio },
                { "supports_video", renderer.SupportsVideo }, { "supports_markers", renderer.SupportsMarkers },
                { "supports_multichannel", renderer.SupportsMultichannel }, { "supports_multistream", renderer.SupportsMultistream }
            };
        }

        private static Dictionary<string, object> TemplateData(RenderTemplate template)
        {
            return new Dictionary<string, object> {
                { "name", template.Name }, { "template_guid", template.TemplateGuid.ToString() }, { "description", template.Description },
                { "notes", template.Notes }, { "file_extensions", template.FileExtensions },
                { "video_streams", template.VideoStreamCount }, { "audio_streams", template.AudioStreamCount },
                { "video_width", SafeTemplateValue(delegate { return template.VideoWidth; }) },
                { "video_height", SafeTemplateValue(delegate { return template.VideoHeight; }) },
                { "video_frame_rate", SafeTemplateValue(delegate { return template.VideoFrameRate; }) },
                { "video_codec", SafeTemplateValue(delegate { return template.VideoCodec; }) },
                { "audio_codec", SafeTemplateValue(delegate { return template.AudioCodec; }) },
                { "audio_sample_rate", SafeTemplateValue(delegate { return template.AudioSampleRate; }) },
                { "audio_channels", SafeTemplateValue(delegate { return template.AudioChannelCount; }) },
                { "total_bitrate", SafeTemplateValue(delegate { return template.TotalBitrate; }) },
                { "hardware_acceleration", SafeTemplateValue(delegate { return template.HardwareAccelerationType.ToString(); }) },
                { "valid", template.IsValid() }
            };
        }

        private static object SafeTemplateValue(Func<object> reader)
        {
            try { return reader(); }
            catch { return null; }
        }

        private static Dictionary<string, object> JobData(RenderJob job)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["job_id"] = job.Id;
            data["project_path"] = job.ProjectPath;
            data["output_path"] = job.OutputPath;
            data["renderer"] = job.Renderer;
            data["template"] = job.Template;
            data["status"] = job.Status;
            data["active"] = IsActiveStatus(job.Status);
            data["percent_complete"] = job.PercentComplete;
            data["error_message"] = job.ErrorMessage;
            data["time_elapsed"] = job.TimeElapsed;
            data["time_left"] = job.TimeLeft;
            data["current_frame"] = job.CurrentFrame;
            data["total_frames"] = job.TotalFrames;
            data["estimated_bytes"] = job.EstimatedBytes;
            data["cancel_requested"] = job.CancelRequested;
            data["started_utc"] = job.StartedUtc.ToString("o");
            data["finished_utc"] = job.FinishedUtc.HasValue ? job.FinishedUtc.Value.ToString("o") : null;
            data["output_exists"] = File.Exists(job.OutputPath);
            if (File.Exists(job.OutputPath)) data["output_bytes"] = new FileInfo(job.OutputPath).Length;
            return data;
        }

        private static bool IsActiveStatus(string status)
        {
            return String.Equals(status, "Starting", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(status, "Rendering", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(status, "Stitching", StringComparison.OrdinalIgnoreCase);
        }

        private static void GuardJobId(Dictionary<string, object> p, RenderJob job)
        {
            if (Has(p, "job_id") && !String.Equals(RequireString(p, "job_id"), job.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("The render job identifier does not match the active or latest job.");
        }

        private void GuardProject(Dictionary<string, object> p)
        {
            if (_vegas.Project == null || _vegas.Project.IsUntitled || String.IsNullOrWhiteSpace(_vegas.Project.FilePath))
                throw new InvalidOperationException("Save the VEGAS project before using guarded render actions.");
            string expected = Path.GetFullPath(RequireString(p, "project_path"));
            string actual = Path.GetFullPath(_vegas.Project.FilePath);
            if (!String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active project changed. Expected " + expected + " but VEGAS has " + actual + ".");
        }

        private static bool Has(Dictionary<string, object> p, string key)
        {
            return p != null && p.ContainsKey(key) && p[key] != null;
        }

        private static object Require(Dictionary<string, object> p, string key)
        {
            if (!Has(p, key)) throw new InvalidOperationException("Missing required parameter: " + key);
            return p[key];
        }

        private static string RequireString(Dictionary<string, object> p, string key)
        {
            string value = Convert.ToString(Require(p, key));
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(key + " cannot be blank.");
            return value;
        }

        private static string GetString(Dictionary<string, object> p, string key, string fallback)
        {
            return Has(p, key) ? Convert.ToString(p[key]) : fallback;
        }

        private static bool GetBool(Dictionary<string, object> p, string key, bool fallback)
        {
            return Has(p, key) ? Convert.ToBoolean(p[key]) : fallback;
        }

        private static bool HasAnyTime(Dictionary<string, object> p, string prefix)
        {
            return Has(p, prefix + "_seconds") || Has(p, prefix + "_frames") || Has(p, prefix + "_timecode");
        }

        private static Timecode RequireTime(Project project, Dictionary<string, object> p, string prefix, bool position)
        {
            string secondsKey = prefix + "_seconds";
            string framesKey = prefix + "_frames";
            string timecodeKey = prefix + "_timecode";
            int supplied = (Has(p, secondsKey) ? 1 : 0) + (Has(p, framesKey) ? 1 : 0) + (Has(p, timecodeKey) ? 1 : 0);
            if (supplied != 1) throw new InvalidOperationException("Provide exactly one time representation for " + prefix + ".");
            if (Has(p, secondsKey)) return ProjectTimecode.FromSeconds(project, Convert.ToDouble(p[secondsKey]));
            if (Has(p, framesKey)) return ProjectTimecode.FromFrames(project, Convert.ToInt64(p[framesKey]));
            string value = RequireString(p, timecodeKey);
            return position ? ProjectTimecode.FromPositionString(project, value) : ProjectTimecode.FromString(project, value);
        }
    }
}
