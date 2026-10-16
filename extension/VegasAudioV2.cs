using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static partial class VegasActions
    {
        private sealed class LoudnessMeasurement
        {
            public AudioEvent Event;
            public string MediaPath;
            public string FfmpegPath;
            public double InputIntegrated;
            public double InputTruePeak;
            public double InputRange;
            public double InputThreshold;
            public double TargetOffset;
            public double SourceStart;
            public double SourceDuration;
            public int StreamIndex;
            public string Fingerprint;
        }

        private sealed class EnvelopeOwner
        {
            public string Scope;
            public string Label;
            public Envelopes Envelopes;
            public Func<EnvelopeType, bool> CanAdd;
        }

        private static object AnalyzeLoudness(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            LoudnessMeasurement measurement = MeasureEventLoudness(vegas, p);
            double target = GetDouble(p, "target_lufs", -23.0);
            if (target < -70 || target > 0) throw new InvalidOperationException("target_lufs must be between -70 and 0.");
            return LoudnessData(measurement, target);
        }

        private static object SetLoudness(Vegas vegas, Dictionary<string, object> p)
        {
            LoudnessMeasurement measurement = MeasureEventLoudness(vegas, p);
            string expectedFingerprint = RequireString(p, "expected_analysis_fingerprint");
            if (!String.Equals(expectedFingerprint, measurement.Fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("The loudness analysis fingerprint changed. Probe the event again before applying gain.");
            double target = RequireDouble(p, "target_lufs");
            if (target < -70 || target > 0) throw new InvalidOperationException("target_lufs must be between -70 and 0.");
            double normalizationGain = measurement.Event.Normalize ? GainToDb(measurement.Event.NormalizeGain) : 0;
            double requiredGain = target - measurement.InputIntegrated - normalizationGain;
            if (requiredGain < -120 || requiredGain > 24)
                throw new InvalidOperationException("The required event gain is outside the supported -120 to +24 dB range: " + requiredGain.ToString("0.###", CultureInfo.InvariantCulture));
            double currentGain = GainToDb(measurement.Event.FadeIn.Gain);
            double maxAdjustment = GetDouble(p, "max_adjustment_db", 24);
            if (maxAdjustment <= 0 || maxAdjustment > 144) throw new InvalidOperationException("max_adjustment_db is outside the supported range.");
            if (Math.Abs(requiredGain - currentGain) > maxAdjustment)
                throw new InvalidOperationException("The requested loudness correction exceeds max_adjustment_db.");
            measurement.Event.FadeIn.Gain = (float)DbToGain(requiredGain);
            Dictionary<string, object> result = LoudnessData(measurement, target);
            result["applied_event_gain_db"] = requiredGain;
            result["event"] = EventData(measurement.Event, true);
            result["measurement_scope"] = "source event segment before VEGAS effects, track mix, buses, and master processing";
            return result;
        }

        private static LoudnessMeasurement MeasureEventLoudness(Vegas vegas, Dictionary<string, object> p)
        {
            AudioEvent audio = GuardEvent(vegas.Project, p) as AudioEvent;
            if (audio == null) throw new InvalidOperationException("The target event is not audio.");
            if (audio.ActiveTake == null || String.IsNullOrWhiteSpace(audio.ActiveTake.MediaPath))
                throw new InvalidOperationException("The active audio take has no file-backed media path.");
            string mediaPath = Path.GetFullPath(audio.ActiveTake.MediaPath);
            if (!File.Exists(mediaPath)) throw new FileNotFoundException("The active take media was not found.", mediaPath);
            string ffmpegPath = FindFfmpeg(GetString(p, "ffmpeg_path", null));
            double sourceStart = Seconds(audio.ActiveTake.Offset);
            double sourceDuration = Seconds(audio.Length) * audio.PlaybackRate;
            if (sourceDuration <= 0) throw new InvalidOperationException("The event has no positive source duration to analyze.");
            int streamIndex = audio.SourceStreamIndex;
            int timeoutSeconds = GetInt(p, "analysis_timeout_seconds", 300);
            if (timeoutSeconds < 5 || timeoutSeconds > 1800) throw new InvalidOperationException("analysis_timeout_seconds must be between 5 and 1800.");

            string arguments = "-hide_banner -nostats -loglevel info -i " + QuoteArg(mediaPath) +
                " -ss " + sourceStart.ToString("0.#########", CultureInfo.InvariantCulture) +
                " -t " + sourceDuration.ToString("0.#########", CultureInfo.InvariantCulture) +
                " -map 0:" + streamIndex.ToString(CultureInfo.InvariantCulture) +
                " -filter:a loudnorm=I=-23:TP=-1.5:LRA=11:print_format=json -f null NUL";
            ProcessStartInfo startInfo = new ProcessStartInfo(ffmpegPath, arguments);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            string stderr;
            using (Process process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("ffmpeg could not be started.");
                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(timeoutSeconds * 1000))
                {
                    try { process.Kill(); } catch { }
                    try { process.WaitForExit(5000); } catch { }
                    throw new TimeoutException("ffmpeg loudness analysis timed out.");
                }
                stderr = stderrTask.GetAwaiter().GetResult();
                stdoutTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("ffmpeg loudness analysis failed: " + LastUsefulLine(stderr));
            }

            LoudnessMeasurement result = new LoudnessMeasurement();
            result.Event = audio;
            result.MediaPath = mediaPath;
            result.FfmpegPath = ffmpegPath;
            result.InputIntegrated = ParseLoudnessValue(stderr, "input_i");
            result.InputTruePeak = ParseLoudnessValue(stderr, "input_tp");
            result.InputRange = ParseLoudnessValue(stderr, "input_lra");
            result.InputThreshold = ParseLoudnessValue(stderr, "input_thresh");
            result.TargetOffset = ParseLoudnessValue(stderr, "target_offset");
            result.SourceStart = sourceStart;
            result.SourceDuration = sourceDuration;
            result.StreamIndex = streamIndex;
            if (Double.IsInfinity(result.InputIntegrated) || Double.IsNaN(result.InputIntegrated))
                throw new InvalidOperationException("The analyzed event is silent or has no measurable integrated loudness.");
            FileInfo info = new FileInfo(mediaPath);
            string fingerprintInput = String.Join("|", new string[] {
                mediaPath.ToUpperInvariant(), info.Length.ToString(), info.LastWriteTimeUtc.Ticks.ToString(),
                audio.EventID.ToString(), Seconds(audio.Start).ToString("R"), Seconds(audio.Length).ToString("R"),
                audio.PlaybackRate.ToString("R"), sourceStart.ToString("R"), sourceDuration.ToString("R"),
                streamIndex.ToString(), result.InputIntegrated.ToString("R"), result.InputTruePeak.ToString("R"),
                GainToDb(audio.FadeIn.Gain).ToString("R"), audio.Normalize.ToString(),
                GainToDb(audio.NormalizeGain).ToString("R")
            });
            using (SHA256 sha = SHA256.Create())
                result.Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintInput))).Replace("-", "");
            return result;
        }

        private static Dictionary<string, object> LoudnessData(LoudnessMeasurement measurement, double target)
        {
            double currentGain = GainToDb(measurement.Event.FadeIn.Gain);
            double normalizationGain = measurement.Event.Normalize ? GainToDb(measurement.Event.NormalizeGain) : 0;
            double recommendedGain = target - measurement.InputIntegrated - normalizationGain;
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["event_id"] = measurement.Event.EventID.ToString();
            data["media_path"] = measurement.MediaPath;
            data["source_start_seconds"] = measurement.SourceStart;
            data["source_duration_seconds"] = measurement.SourceDuration;
            data["stream_index"] = measurement.StreamIndex;
            data["integrated_lufs"] = measurement.InputIntegrated;
            data["true_peak_dbfs"] = measurement.InputTruePeak;
            data["loudness_range_lu"] = measurement.InputRange;
            data["threshold_lufs"] = measurement.InputThreshold;
            data["target_offset_lu"] = measurement.TargetOffset;
            data["target_lufs"] = target;
            data["current_event_gain_db"] = currentGain;
            data["normalization_gain_db"] = normalizationGain;
            data["estimated_current_pre_fx_lufs"] = measurement.InputIntegrated + currentGain + normalizationGain;
            data["recommended_event_gain_db"] = recommendedGain;
            data["analysis_fingerprint"] = measurement.Fingerprint;
            data["measurement_scope"] = "source event segment before VEGAS effects, track mix, buses, and master processing";
            return data;
        }

        private static object InspectEnvelopes(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            EnvelopeOwner owner = ResolveEnvelopeOwner(vegas, p);
            return EnvelopeOwnerData(owner);
        }

        private static object SetEnvelopePoint(Vegas vegas, Dictionary<string, object> p)
        {
            EnvelopeOwner owner = ResolveEnvelopeOwner(vegas, p);
            EnvelopeType type = ParseEnum<EnvelopeType>(RequireString(p, "envelope_type"));
            Envelope envelope = owner.Envelopes.FindByType(type);
            if (envelope == null)
            {
                if (!GetBool(p, "create_if_missing", false)) throw new InvalidOperationException("The requested envelope does not exist. Set create_if_missing=true to create a supported envelope.");
                if (!owner.CanAdd(type)) throw new InvalidOperationException("VEGAS does not allow that envelope type on this target.");
                envelope = new Envelope(type);
                owner.Envelopes.Add(envelope);
            }
            Timecode position = RequireTime(vegas.Project, p, "position", true);
            if (position.Nanos < 0) throw new InvalidOperationException("Envelope point position cannot be negative.");
            double rawValue = EnvelopeInputValue(p, type);
            if (rawValue < envelope.Min || rawValue > envelope.Max) throw new InvalidOperationException("Envelope value is outside the supported range.");
            CurveType curve = ParseEnum<CurveType>(GetString(p, "curve", "Linear"));
            EnvelopePoint existing = FindEnvelopePoint(envelope, position);
            if (existing == null) envelope.Points.Add(new EnvelopePoint(position, rawValue, curve));
            else { existing.Y = rawValue; existing.Curve = curve; }
            return EnvelopeOwnerData(owner);
        }

        private static object DeleteEnvelopePoint(Vegas vegas, Dictionary<string, object> p)
        {
            EnvelopeOwner owner = ResolveEnvelopeOwner(vegas, p);
            EnvelopeType type = ParseEnum<EnvelopeType>(RequireString(p, "envelope_type"));
            Envelope envelope = owner.Envelopes.FindByType(type);
            if (envelope == null) throw new InvalidOperationException("The requested envelope does not exist.");
            Timecode position = RequireTime(vegas.Project, p, "position", true);
            EnvelopePoint point = FindEnvelopePoint(envelope, position);
            if (point == null) throw new InvalidOperationException("No envelope point exists at the requested position.");
            Dictionary<string, object> removed = EnvelopePointData(point, type);
            envelope.Points.Remove(point);
            Dictionary<string, object> result = EnvelopeOwnerData(owner);
            result["deleted_point"] = removed;
            return result;
        }

        private static object ClearEnvelope(Vegas vegas, Dictionary<string, object> p)
        {
            EnvelopeOwner owner = ResolveEnvelopeOwner(vegas, p);
            EnvelopeType type = ParseEnum<EnvelopeType>(RequireString(p, "envelope_type"));
            Envelope envelope = owner.Envelopes.FindByType(type);
            if (envelope == null) throw new InvalidOperationException("The requested envelope does not exist.");
            int count = envelope.Points.Count;
            envelope.Points.Clear();
            if (GetBool(p, "remove_envelope", false))
            {
                if (!GetBool(p, "allow_remove_envelope", false)) throw new InvalidOperationException("Removing the envelope requires allow_remove_envelope=true.");
                owner.Envelopes.Remove(envelope);
            }
            Dictionary<string, object> result = EnvelopeOwnerData(owner);
            result["cleared_point_count"] = count;
            return result;
        }

        private static object ListBuses(Vegas vegas, Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(vegas.Project, p);
            List<object> buses = new List<object>();
            buses.Add(BusData(vegas.Project.MasterBus, true));
            foreach (BusTrack bus in vegas.Project.BusTracks) buses.Add(BusData(bus, false));
            Dictionary<string, object> result = Result("buses", buses);
            result["count"] = buses.Count;
            return result;
        }

        private static object AddBus(Vegas vegas, Dictionary<string, object> p)
        {
            AudioBusTrack bus = vegas.Project.AddAudioBusTrack();
            if (Has(p, "description")) bus.Description = RequireString(p, "description");
            return Result("bus", BusData(bus, false));
        }

        private static object RouteTrack(Vegas vegas, Dictionary<string, object> p)
        {
            AudioTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as AudioTrack;
            if (track == null) throw new InvalidOperationException("Only audio tracks can be routed to audio buses.");
            GuardTrackName(track, p);
            BusTrack bus = ResolveBus(vegas.Project, p);
            track.BusTrack = bus;
            Dictionary<string, object> result = Result("track", TrackData(track));
            result["bus"] = BusData(bus, bus.IsMaster());
            return result;
        }

        private static object SetBusMix(Vegas vegas, Dictionary<string, object> p)
        {
            AudioBusTrack bus = ResolveBus(vegas.Project, p) as AudioBusTrack;
            if (bus == null) throw new InvalidOperationException("The target bus is not an audio bus.");
            if (Has(p, "mute")) bus.Mute = RequireBool(p, "mute");
            if (Has(p, "solo") && !bus.IsMaster()) bus.Solo = RequireBool(p, "solo");
            if (Has(p, "pan_type")) bus.PanType = ParseEnum<PanType>(RequireString(p, "pan_type"));
            Timecode position = HasAnyTime(p, "position") ? RequireTime(vegas.Project, p, "position", true) : ProjectTimecode.FromFrames(vegas.Project, 0);
            if (Has(p, "volume_db")) SetOwnerEnvelopePoint(bus.Envelopes, bus.CanAddEnvelope, EnvelopeType.Volume, position, DbToGain(RequireDouble(p, "volume_db")));
            if (Has(p, "pan"))
            {
                double pan = RequireDouble(p, "pan");
                if (pan < -1 || pan > 1) throw new InvalidOperationException("pan must be between -1 and 1.");
                SetOwnerEnvelopePoint(bus.Envelopes, bus.CanAddEnvelope, EnvelopeType.Pan, position, pan);
            }
            return Result("bus", BusData(bus, bus.IsMaster()));
        }

        private static EnvelopeOwner ResolveEnvelopeOwner(Vegas vegas, Dictionary<string, object> p)
        {
            string scope = GetString(p, "scope", "track").ToLowerInvariant();
            EnvelopeOwner result = new EnvelopeOwner();
            result.Scope = scope;
            if (scope == "track")
            {
                Track track = GetTrack(vegas.Project, RequireInt(p, "track_index"));
                GuardTrackName(track, p);
                result.Label = "track:" + track.Index + ":" + track.Name;
                result.Envelopes = track.Envelopes;
                result.CanAdd = track.CanAddEnvelope;
            }
            else
            {
                BusTrack bus = ResolveBus(vegas.Project, p);
                result.Label = bus.IsMaster() ? "master_audio_bus" : "bus:" + bus.Index + ":" + bus.Description;
                result.Envelopes = bus.Envelopes;
                result.CanAdd = bus.CanAddEnvelope;
            }
            return result;
        }

        private static BusTrack ResolveBus(Project project, Dictionary<string, object> p)
        {
            string scope = GetString(p, "scope", GetString(p, "bus", "master")).ToLowerInvariant();
            if (scope == "master" || scope == "master_audio" || scope == "master_audio_bus") return project.MasterBus;
            int index = RequireInt(p, "bus_index");
            if (index < 0 || index >= project.BusTracks.Count) throw new InvalidOperationException("bus_index is outside the project bus range.");
            BusTrack bus = project.BusTracks[index];
            if (Has(p, "expected_description") && !String.Equals(bus.Description, RequireString(p, "expected_description"), StringComparison.Ordinal))
                throw new InvalidOperationException("Bus description changed since the probe.");
            return bus;
        }

        private static Dictionary<string, object> EnvelopeOwnerData(EnvelopeOwner owner)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["scope"] = owner.Scope;
            result["owner"] = owner.Label;
            List<object> envelopes = new List<object>();
            foreach (Envelope envelope in owner.Envelopes) envelopes.Add(EnvelopeData(envelope));
            result["envelopes"] = envelopes;
            return result;
        }

        private static Dictionary<string, object> EnvelopeData(Envelope envelope)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["index"] = envelope.Index;
            data["name"] = envelope.Name;
            data["type"] = envelope.Type.ToString();
            data["min"] = envelope.Min;
            data["max"] = envelope.Max;
            data["neutral"] = envelope.Neutral;
            List<object> points = new List<object>();
            foreach (EnvelopePoint point in envelope.Points) points.Add(EnvelopePointData(point, envelope.Type));
            data["points"] = points;
            return data;
        }

        private static Dictionary<string, object> EnvelopePointData(EnvelopePoint point, EnvelopeType type)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["index"] = point.Index;
            data["position_seconds"] = Seconds(point.X);
            data["position_frames"] = point.X.FrameCount;
            data["value"] = point.Y;
            if (type == EnvelopeType.Volume) data["volume_db"] = GainToDb(point.Y);
            data["curve"] = point.Curve.ToString();
            return data;
        }

        private static Dictionary<string, object> BusData(BusTrack bus, bool master)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["index"] = bus.Index;
            data["is_master"] = master;
            data["kind"] = bus.IsAudio() ? "audio" : "video";
            data["name"] = bus.Name;
            data["description"] = bus.Description;
            AudioBusTrack audio = bus as AudioBusTrack;
            if (audio != null)
            {
                data["mute"] = audio.Mute;
                data["solo"] = audio.Solo;
                data["pan_type"] = audio.PanType.ToString();
            }
            data["effects"] = EffectData(bus.Effects);
            List<object> envelopes = new List<object>();
            foreach (Envelope envelope in bus.Envelopes) envelopes.Add(EnvelopeData(envelope));
            data["envelopes"] = envelopes;
            return data;
        }

        private static EnvelopePoint FindEnvelopePoint(Envelope envelope, Timecode position)
        {
            foreach (EnvelopePoint point in envelope.Points)
                if (Math.Abs(Seconds(point.X - position)) <= TimeToleranceSeconds) return point;
            return null;
        }

        private static double EnvelopeInputValue(Dictionary<string, object> p, EnvelopeType type)
        {
            if (type == EnvelopeType.Volume && Has(p, "volume_db")) return DbToGain(RequireDouble(p, "volume_db"));
            return RequireDouble(p, "value");
        }

        private static void SetOwnerEnvelopePoint(Envelopes envelopes, Func<EnvelopeType, bool> canAdd, EnvelopeType type, Timecode position, double value)
        {
            Envelope envelope = envelopes.FindByType(type);
            if (envelope == null)
            {
                if (!canAdd(type)) throw new InvalidOperationException("VEGAS does not allow the requested bus envelope.");
                envelope = new Envelope(type);
                envelopes.Add(envelope);
            }
            if (value < envelope.Min || value > envelope.Max) throw new InvalidOperationException("Bus envelope value is outside the supported range.");
            EnvelopePoint point = FindEnvelopePoint(envelope, position);
            if (point == null) envelope.Points.Add(new EnvelopePoint(position, value, CurveType.Linear));
            else point.Y = value;
        }

        private static string FindFfmpeg(string configured)
        {
            if (!String.IsNullOrWhiteSpace(configured))
            {
                string full = Path.GetFullPath(configured);
                if (!File.Exists(full)) throw new FileNotFoundException("Configured ffmpeg executable was not found.", full);
                return full;
            }
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                if (String.IsNullOrWhiteSpace(directory)) continue;
                string candidate;
                try { candidate = Path.Combine(directory.Trim().Trim('"'), "ffmpeg.exe"); }
                catch { continue; }
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("ffmpeg.exe was not found on the VEGAS process PATH. Pass ffmpeg_path explicitly.");
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static double ParseLoudnessValue(string output, string key)
        {
            Match match = Regex.Match(output, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"?([^\\\",\\s}]+)", RegexOptions.IgnoreCase);
            if (!match.Success) throw new InvalidOperationException("ffmpeg did not return loudness field: " + key);
            string raw = match.Groups[1].Value;
            if (String.Equals(raw, "-inf", StringComparison.OrdinalIgnoreCase)) return Double.NegativeInfinity;
            double value;
            if (!Double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException("ffmpeg returned an invalid loudness value for " + key + ": " + raw);
            return value;
        }

        private static string LastUsefulLine(string value)
        {
            string[] lines = (value ?? "").Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "no diagnostic output" : lines[lines.Length - 1].Trim();
        }
    }
}
