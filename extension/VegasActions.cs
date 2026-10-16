using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static partial class VegasActions
    {
        private const double TimeToleranceSeconds = 0.000001;

        public static object Execute(Vegas vegas, string action, Dictionary<string, object> p)
        {
            if (vegas == null || vegas.Project == null)
                throw new InvalidOperationException("VEGAS has no active project.");
            vegas.WaitForIdle();

            if (action == "status")
                return Status(vegas);
            if (action == "project.inspect")
                return ProjectInspect(vegas);
            if (action == "project.inspect_settings")
                return InspectProjectSettings(vegas, p);
            if (action == "timeline.inspect")
                return TimelineInspect(vegas, p);
            if (action == "timeline.find_events")
                return FindEvents(vegas, p);
            if (action == "effects.list_plugins")
                return ListPlugins(vegas, p);
            if (action == "effects.list_presets")
                return ListPresets(vegas, p);
            if (action == "effects.list_event_effects")
                return ListEventEffects(vegas, p);
            if (action == "effects.list_track_effects")
                return ListTrackEffects(vegas, p);
            if (action == "effects.list_bus_effects")
                return ListBusEffects(vegas, p);
            if (action == "audio.analyze_loudness")
                return AnalyzeLoudness(vegas, p);
            if (action == "audio.inspect_envelopes")
                return InspectEnvelopes(vegas, p);
            if (action == "audio.list_buses")
                return ListBuses(vegas, p);
            if (action == "media.inspect")
                return InspectMedia(vegas, p);
            if (action == "media.list")
                return ListMedia(vegas, p);
            if (action == "media.list_takes")
                return ListTakes(vegas, p);
            if (action == "motion.inspect_event_motion")
                return InspectEventMotion(vegas, p);
            if (action == "motion.inspect_track_motion")
                return InspectTrackMotion(vegas, p);
            if (action == "timeline.batch" && GetBool(p, "dry_run", false))
            {
                GuardProject(vegas.Project, p);
                return PreflightBatch(vegas, p);
            }

            if (action == "project.backup")
            {
                GuardProject(vegas.Project, p);
                return Result("backup_path", CreateBackup(vegas.Project, GetString(p, "reason", "manual")));
            }

            if (action == "project.set_cursor" || action == "project.play" ||
                action == "project.pause" || action == "project.stop")
                return TransportAction(vegas, action, p);

            if (action == "project.snapshot")
                return Snapshot(vegas, p);

            if (action == "timeline.clear_selection" || action == "timeline.select_events")
            {
                GuardProject(vegas.Project, p);
                return SelectionAction(vegas, action, p);
            }

            if (action == "project.set_selection")
            {
                GuardProject(vegas.Project, p);
                return SetTimelineSelection(vegas, p);
            }

            if (action == "project.save" || action == "project.undo" || action == "project.redo")
            {
                GuardProject(vegas.Project, p);
                string backup = CreateBackup(vegas.Project, SafeActionName(action));
                Dictionary<string, object> result;
                if (action == "project.save")
                {
                    bool saved = vegas.SaveProject();
                    if (!saved) throw new InvalidOperationException("VEGAS did not save the active project.");
                    result = Result("saved", true);
                }
                else if (action == "project.undo")
                {
                    vegas.Project.Undo();
                    result = Result("undone", true);
                }
                else
                {
                    vegas.Project.Redo();
                    result = Result("redone", true);
                }
                result["backup_path"] = backup;
                result["project"] = ProjectSummary(vegas);
                vegas.UpdateUI_Ex();
                return result;
            }

            if (!IsContentMutation(action))
                throw new InvalidOperationException("Unsupported VEGAS MCP action: " + action);

            GuardProject(vegas.Project, p);
            string mutationBackup = CreateBackup(vegas.Project, SafeActionName(action));
            object data;
            string undoLabel = "VEGAS MCP: " + action.Substring(action.IndexOf('.') + 1).Replace('_', ' ');
            using (UndoBlock undo = new UndoBlock(undoLabel))
            {
                try
                {
                    data = ExecuteMutation(vegas, action, p);
                }
                catch
                {
                    undo.Cancel = true;
                    throw;
                }
            }

            Dictionary<string, object> wrapped = data as Dictionary<string, object>;
            if (wrapped == null)
                wrapped = Result("result", data);
            wrapped["backup_path"] = mutationBackup;
            wrapped["undo_label"] = undoLabel;
            wrapped["project_modified"] = vegas.Project.IsModified;
            vegas.UpdateUI_Ex();
            return wrapped;
        }

        private static bool IsContentMutation(string action)
        {
            return action.StartsWith("timeline.", StringComparison.Ordinal) ||
                   action.StartsWith("audio.", StringComparison.Ordinal) ||
                   action.StartsWith("effects.", StringComparison.Ordinal) ||
                   action.StartsWith("media.", StringComparison.Ordinal) ||
                   action.StartsWith("motion.", StringComparison.Ordinal) ||
                   action == "project.set_settings" ||
                   action == "project.insert_time";
        }

        private static object ExecuteMutation(Vegas vegas, string action, Dictionary<string, object> p)
        {
            switch (action)
            {
                case "timeline.add_track": return AddTrack(vegas, p);
                case "timeline.set_track": return SetTrack(vegas, p);
                case "timeline.duplicate_track": return DuplicateTrack(vegas, p);
                case "timeline.insert_media": return InsertMedia(vegas, p);
                case "timeline.insert_linked_media": return InsertLinkedMedia(vegas, p);
                case "timeline.move_event": return MoveEvent(vegas, p);
                case "timeline.copy_event": return CopyEvent(vegas, p);
                case "timeline.trim_event": return TrimEvent(vegas, p);
                case "timeline.split_event": return SplitEvent(vegas, p);
                case "timeline.delete_event": return DeleteEvent(vegas, p);
                case "timeline.set_event": return SetEvent(vegas, p);
                case "timeline.move_group": return MoveGroup(vegas, p);
                case "timeline.copy_group": return CopyGroup(vegas, p);
                case "timeline.split_group": return SplitGroup(vegas, p);
                case "timeline.delete_group": return DeleteGroup(vegas, p);
                case "timeline.set_fades": return SetFades(vegas, p);
                case "timeline.set_transition": return SetTransition(vegas, p);
                case "timeline.create_crossfade": return CreateCrossfade(vegas, p);
                case "timeline.create_linked_crossfade": return CreateLinkedCrossfade(vegas, p);
                case "timeline.close_gaps": return CloseGaps(vegas, p);
                case "timeline.ripple_delete": return RippleDeleteRange(vegas, p);
                case "timeline.insert_title": return InsertTitle(vegas, p);
                case "timeline.group_events": return GroupEvents(vegas, p);
                case "timeline.ungroup_events": return UngroupEvents(vegas, p);
                case "timeline.add_marker": return AddMarker(vegas, p);
                case "timeline.edit_marker": return EditMarker(vegas, p);
                case "timeline.delete_marker": return DeleteMarker(vegas, p);
                case "timeline.add_region": return AddRegion(vegas, p);
                case "timeline.edit_region": return EditRegion(vegas, p);
                case "timeline.delete_region": return DeleteRegion(vegas, p);
                case "timeline.batch": return ExecuteBatch(vegas, p);
                case "audio.set_event_gain_db": return SetEventGain(vegas, p);
                case "audio.normalize": return Normalize(vegas, p, false);
                case "audio.recalculate_normalize": return Normalize(vegas, p, true);
                case "audio.set_channels": return SetChannels(vegas, p);
                case "audio.set_audio_properties": return SetAudioProperties(vegas, p);
                case "audio.set_track_mix": return SetTrackMix(vegas, p);
                case "audio.add_volume_envelope_point": return AddVolumeEnvelopePoint(vegas, p);
                case "audio.set_loudness": return SetLoudness(vegas, p);
                case "audio.set_envelope_point": return SetEnvelopePoint(vegas, p);
                case "audio.delete_envelope_point": return DeleteEnvelopePoint(vegas, p);
                case "audio.clear_envelope": return ClearEnvelope(vegas, p);
                case "audio.add_bus": return AddBus(vegas, p);
                case "audio.route_track": return RouteTrack(vegas, p);
                case "audio.set_bus_mix": return SetBusMix(vegas, p);
                case "effects.add_event_effect": return AddEventEffect(vegas, p);
                case "effects.remove_event_effect": return RemoveEventEffect(vegas, p);
                case "effects.set_ofx_parameter": return SetOfxParameter(vegas, p);
                case "effects.add_track_effect": return AddTrackEffect(vegas, p);
                case "effects.remove_track_effect": return RemoveTrackEffect(vegas, p);
                case "effects.add_bus_effect": return AddBusEffect(vegas, p);
                case "effects.remove_bus_effect": return RemoveBusEffect(vegas, p);
                case "effects.set_effect_state": return SetEffectState(vegas, p);
                case "effects.set_target_ofx_parameter": return SetTargetOfxParameter(vegas, p);
                case "effects.copy_effect_chain": return CopyEffectChain(vegas, p);
                case "media.create_bin": return CreateMediaBin(vegas, p);
                case "media.insert_image_sequence": return InsertImageSequence(vegas, p);
                case "media.add_take": return AddTake(vegas, p);
                case "media.set_active_take": return SetActiveTake(vegas, p);
                case "media.remove_take": return RemoveTake(vegas, p);
                case "media.replace_event_media": return ReplaceEventMedia(vegas, p);
                case "motion.set_event_motion_keyframe": return SetEventMotionKeyframe(vegas, p);
                case "motion.clear_event_motion_keyframes": return ClearEventMotionKeyframes(vegas, p);
                case "motion.set_track_motion_keyframe": return SetTrackMotionKeyframe(vegas, p);
                case "motion.clear_track_motion_keyframes": return ClearTrackMotionKeyframes(vegas, p);
                case "project.set_settings": return SetProjectSettings(vegas, p);
                case "project.insert_time": return InsertProjectTime(vegas, p);
                default: throw new InvalidOperationException("Unsupported mutation: " + action);
            }
        }

        private static Dictionary<string, object> Status(Vegas vegas)
        {
            Dictionary<string, object> result = ProjectSummary(vegas);
            result["connector"] = VegasMcpExtension.ProductName;
            result["connected"] = true;
            result["process_id"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            result["is_playing"] = vegas.Transport.IsPlaying;
            result["is_recording"] = vegas.Transport.IsRecording;
            result["cursor_seconds"] = Seconds(vegas.Transport.CursorPosition);
            return result;
        }

        private static Dictionary<string, object> ProjectInspect(Vegas vegas)
        {
            Dictionary<string, object> result = ProjectSummary(vegas);
            result["selection_start_seconds"] = Seconds(vegas.SelectionStart);
            result["selection_length_seconds"] = Seconds(vegas.SelectionLength);
            result["markers"] = MarkerData(vegas.Project);
            result["regions"] = RegionData(vegas.Project);
            result["backup_root"] = BackupRoot();
            return result;
        }

        private static Dictionary<string, object> ProjectSummary(Vegas vegas)
        {
            Project project = vegas.Project;
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["vegas_version"] = vegas.Version;
            result["vegas_build"] = vegas.BuildNumber;
            result["project_path"] = project.IsUntitled ? null : project.FilePath;
            result["project_name"] = project.IsUntitled ? "Untitled" : Path.GetFileNameWithoutExtension(project.FilePath);
            result["is_untitled"] = project.IsUntitled;
            result["is_modified"] = project.IsModified;
            result["length_seconds"] = Seconds(project.Length);
            result["length_frames"] = project.Length.FrameCount;
            result["track_count"] = project.Tracks.Count;
            result["cursor_seconds"] = Seconds(vegas.Transport.CursorPosition);
            result["cursor_frames"] = vegas.Transport.CursorPosition.FrameCount;
            return result;
        }

        private static Dictionary<string, object> TimelineInspect(Vegas vegas, Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(vegas.Project, p);
            bool includeEffects = GetBool(p, "include_effects", false);
            List<object> tracks = new List<object>();
            foreach (Track track in vegas.Project.Tracks)
            {
                Dictionary<string, object> item = TrackData(track);
                List<object> events = new List<object>();
                foreach (TrackEvent trackEvent in track.Events)
                    events.Add(EventData(trackEvent, includeEffects));
                item["events"] = events;
                tracks.Add(item);
            }
            Dictionary<string, object> result = ProjectSummary(vegas);
            result["tracks"] = tracks;
            return result;
        }

        private static Dictionary<string, object> TrackData(Track track)
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["index"] = track.Index;
            item["display_index"] = track.DisplayIndex;
            item["kind"] = track.IsAudio() ? "audio" : "video";
            item["name"] = track.Name;
            item["selected"] = track.Selected;
            item["mute"] = track.Mute;
            item["solo"] = track.Solo;
            item["event_count"] = track.Events.Count;
            AudioTrack audio = track as AudioTrack;
            if (audio != null)
            {
                item["volume_db"] = GainToDb(audio.Volume);
                item["pan"] = audio.PanX;
                item["pan_y"] = audio.PanY;
                item["pan_center"] = audio.PanCenter;
                item["invert_phase"] = audio.InvertPhase;
                item["bus_index"] = audio.BusTrack == null ? (object)null : audio.BusTrack.Index;
                item["bus_is_master"] = audio.BusTrack != null && audio.BusTrack.IsMaster();
            }
            VideoTrack video = track as VideoTrack;
            if (video != null)
            {
                item["composite_level"] = video.CompositeLevel;
                item["composite_mode"] = video.CompositeMode.ToString();
                item["bypass_motion_blur"] = video.BypassMotionBlur;
                item["is_adjustment_track"] = video.IsAdjustmentTrack;
            }
            return item;
        }

        private static Dictionary<string, object> EventData(TrackEvent ev, bool includeEffects)
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["event_id"] = ev.EventID.ToString();
            item["track_index"] = ev.Track.Index;
            item["event_index"] = ev.Index;
            item["kind"] = ev.IsAudio() ? "audio" : "video";
            item["name"] = ev.Name;
            item["start_seconds"] = Seconds(ev.Start);
            item["start_frames"] = ev.Start.FrameCount;
            item["start_timecode"] = ev.Start.ToPositionString();
            item["length_seconds"] = Seconds(ev.Length);
            item["length_frames"] = ev.Length.FrameCount;
            item["length_timecode"] = ev.Length.ToString();
            item["end_seconds"] = Seconds(ev.End);
            item["end_frames"] = ev.End.FrameCount;
            item["selected"] = ev.Selected;
            item["mute"] = ev.Mute;
            item["locked"] = ev.Locked;
            item["loop"] = ev.Loop;
            item["playback_rate"] = ev.PlaybackRate;
            item["snap_offset_seconds"] = Seconds(ev.SnapOffset);
            item["snap_offset_frames"] = ev.SnapOffset.FrameCount;
            item["fade_in_seconds"] = Seconds(ev.FadeIn.Length);
            item["fade_in_frames"] = ev.FadeIn.Length.FrameCount;
            item["fade_out_seconds"] = Seconds(ev.FadeOut.Length);
            item["fade_out_frames"] = ev.FadeOut.Length.FrameCount;
            item["event_gain_db"] = GainToDb(ev.FadeIn.Gain);
            item["grouped"] = ev.IsGrouped;
            item["take_count"] = ev.Takes.Count;
            if (ev.ActiveTake != null)
            {
                item["active_take_name"] = ev.ActiveTake.Name;
                item["media_path"] = ev.ActiveTake.MediaPath;
                item["take_offset_seconds"] = Seconds(ev.ActiveTake.Offset);
            }
            AudioEvent audio = ev as AudioEvent;
            if (audio != null)
            {
                item["normalize"] = audio.Normalize;
                item["normalize_gain_db"] = GainToDb(audio.NormalizeGain);
                item["channels"] = audio.Channels.ToString();
                item["pitch_semis"] = audio.PitchSemis;
                // VEGAS 2026 throws E_FAIL when FormantShift is read on ordinary
                // PCM and MP3 events, so it is intentionally omitted from generic
                // event inspection. The guarded setter remains available for media
                // and stretch modes that support it.
            }
            VideoEvent video = ev as VideoEvent;
            if (video != null)
            {
                item["event_opacity"] = video.FadeIn.Gain;
                item["maintain_aspect_ratio"] = video.MaintainAspectRatio;
                item["reduce_interlace"] = video.ReduceInterlace;
                item["resample_mode"] = video.ResampleMode.ToString();
                item["under_sample_rate"] = video.UnderSampleRate;
            }
            if (includeEffects)
                item["effects"] = EffectData(GetEffects(ev));
            return item;
        }

        private static object TransportAction(Vegas vegas, string action, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            if (action == "project.set_cursor")
            {
                Timecode cursor = HasAnyTime(p, "cursor")
                    ? RequireTime(vegas.Project, p, "cursor", true)
                    : ProjectTimecode.FromSeconds(vegas.Project, RequireDouble(p, "seconds"));
                if (cursor.Nanos < 0) throw new InvalidOperationException("Cursor time cannot be negative.");
                vegas.Transport.CursorPosition = cursor;
                bool center = GetBool(p, "center", true);
                vegas.Transport.ViewCursor(center);
            }
            else if (action == "project.play") vegas.Transport.Play();
            else if (action == "project.pause") vegas.Transport.Pause();
            else vegas.Transport.Stop();
            return Status(vegas);
        }

        private static object Snapshot(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VEGAS MCP Outputs", "Snapshots");
            Directory.CreateDirectory(root);
            string output = GetString(p, "output_path", null);
            string formatName = GetString(p, "format", "png").ToLowerInvariant();
            ImageFileFormat format = formatName == "jpg" || formatName == "jpeg" ? ImageFileFormat.JPEG : ImageFileFormat.PNG;
            string extension = format == ImageFileFormat.JPEG ? ".jpg" : ".png";
            if (String.IsNullOrWhiteSpace(output))
            {
                string projectName = vegas.Project.IsUntitled ? "Untitled" : Path.GetFileNameWithoutExtension(vegas.Project.FilePath);
                output = Path.Combine(root, SafeFileName(projectName) + "__" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + extension);
            }
            output = Path.GetFullPath(output);
            if (File.Exists(output)) throw new IOException("Snapshot output already exists: " + output);
            if (!String.Equals(Path.GetExtension(output), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Snapshot output extension must match the requested image format: " + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Timecode atTime = HasAnyTime(p, "at") ? RequireTime(vegas.Project, p, "at", true) : vegas.Transport.CursorPosition;
            RenderStatus status = vegas.SaveSnapshot(output, format, atTime);
            Dictionary<string, object> result = Result("output_path", output);
            result["render_status"] = status.ToString();
            result["exists"] = File.Exists(output);
            result["at_seconds"] = Seconds(atTime);
            result["at_frames"] = atTime.FrameCount;
            return result;
        }

        private static object SelectionAction(Vegas vegas, string action, Dictionary<string, object> p)
        {
            string mode = "replace";
            HashSet<long> ids = new HashSet<long>();
            if (action == "timeline.clear_selection")
            {
                mode = "replace";
            }
            else
            {
                mode = GetString(p, "mode", "replace").ToLowerInvariant();
                ids = new HashSet<long>(ReadEventIds(p, "event_ids"));
                if (ids.Count == 0) throw new InvalidOperationException("event_ids must contain at least one event.");
                foreach (long id in ids) FindEvent(vegas.Project, id);
            }
            if (mode != "add" && mode != "remove" && mode != "toggle" && mode != "replace")
                throw new InvalidOperationException("mode must be replace, add, remove, or toggle.");

            WindowsFocus.EnsureVegasTimelineForeground(vegas);
            using (UndoBlock undo = new UndoBlock("VEGAS MCP: select events"))
            {
                try
                {
                    foreach (Track track in vegas.Project.Tracks)
                    {
                        foreach (TrackEvent ev in track.Events)
                        {
                            bool targeted = ids.Contains(ev.EventID);
                            if (mode == "replace") ev.Selected = targeted;
                            else if (mode == "add" && targeted) ev.Selected = true;
                            else if (mode == "remove" && targeted) ev.Selected = false;
                            else if (mode == "toggle" && targeted) ev.Selected = !ev.Selected;
                        }
                    }
                }
                catch
                {
                    undo.Cancel = true;
                    throw;
                }
            }
            vegas.UpdateUI_Ex();
            return Result("selected_event_ids", SelectedEventIds(vegas.Project));
        }

        private static object AddTrack(Vegas vegas, Dictionary<string, object> p)
        {
            string kind = RequireString(p, "kind").ToLowerInvariant();
            Track track;
            if (kind == "audio") track = vegas.Project.AddAudioTrack();
            else if (kind == "video") track = vegas.Project.AddVideoTrack();
            else if (kind == "adjustment") track = vegas.Project.AddVideoAdjustmentTrack();
            else throw new InvalidOperationException("kind must be audio, video, or adjustment.");
            if (Has(p, "name")) track.Name = RequireString(p, "name");
            if (Has(p, "index"))
            {
                int index = RequireInt(p, "index");
                if (index < 0 || index >= vegas.Project.Tracks.Count)
                    throw new InvalidOperationException("Track index is outside the project track range.");
                vegas.Project.Tracks.Remove(track);
                vegas.Project.Tracks.Insert(index, track);
            }
            return Result("track", TrackData(track));
        }

        private static object SetTrack(Vegas vegas, Dictionary<string, object> p)
        {
            Track track = GetTrack(vegas.Project, RequireInt(p, "track_index"));
            if (Has(p, "expected_name") && !String.Equals(track.Name, RequireString(p, "expected_name"), StringComparison.Ordinal))
                throw new InvalidOperationException("Track name changed since the probe.");
            if (Has(p, "name")) track.Name = RequireString(p, "name");
            if (Has(p, "mute")) track.Mute = RequireBool(p, "mute");
            if (Has(p, "solo")) track.Solo = RequireBool(p, "solo");
            if (Has(p, "selected")) track.Selected = RequireBool(p, "selected");
            AudioTrack audio = track as AudioTrack;
            if (Has(p, "volume_db"))
            {
                if (audio == null) throw new InvalidOperationException("volume_db is only valid for audio tracks.");
                audio.Volume = (float)DbToGain(CheckedGainDb(RequireDouble(p, "volume_db"), "volume_db"));
            }
            if (Has(p, "pan"))
            {
                if (audio == null) throw new InvalidOperationException("pan is only valid for audio tracks.");
                double pan = RequireDouble(p, "pan");
                if (pan < -1 || pan > 1) throw new InvalidOperationException("pan must be between -1 and 1.");
                audio.PanX = (float)pan;
            }
            if (Has(p, "pan_y"))
            {
                if (audio == null) throw new InvalidOperationException("pan_y is only valid for audio tracks.");
                audio.PanY = (float)RequireDouble(p, "pan_y");
            }
            if (Has(p, "pan_center"))
            {
                if (audio == null) throw new InvalidOperationException("pan_center is only valid for audio tracks.");
                audio.PanCenter = (float)RequireDouble(p, "pan_center");
            }
            VideoTrack video = track as VideoTrack;
            if (Has(p, "composite_level"))
            {
                if (video == null) throw new InvalidOperationException("composite_level is only valid for video tracks.");
                double level = RequireDouble(p, "composite_level");
                if (level < 0 || level > 1) throw new InvalidOperationException("composite_level must be between 0 and 1.");
                video.CompositeLevel = (float)level;
            }
            if (Has(p, "composite_mode"))
            {
                if (video == null) throw new InvalidOperationException("composite_mode is only valid for video tracks.");
                if (!video.SetCompositeMode(ParseEnum<CompositeMode>(RequireString(p, "composite_mode")), false))
                    throw new InvalidOperationException("VEGAS refused the requested composite mode.");
            }
            if (Has(p, "bypass_motion_blur"))
            {
                if (video == null) throw new InvalidOperationException("bypass_motion_blur is only valid for video tracks.");
                video.BypassMotionBlur = RequireBool(p, "bypass_motion_blur");
            }
            return Result("track", TrackData(track));
        }

        private static object InsertMedia(Vegas vegas, Dictionary<string, object> p)
        {
            string sourcePath = Path.GetFullPath(RequireString(p, "source_path"));
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source media was not found.", sourcePath);
            Track track = GetTrack(vegas.Project, RequireInt(p, "track_index"));
            Timecode start = RequireTime(vegas.Project, p, "start", true);
            if (start.Nanos < 0) throw new InvalidOperationException("start cannot be negative.");
            Media media = Media.CreateInstance(vegas.Project, sourcePath);
            TrackEvent ev;
            if (track.IsVideo())
            {
                if (!media.HasVideo()) throw new InvalidOperationException("The source has no video stream.");
                VideoStream stream = media.GetVideoStreamByIndex(GetInt(p, "stream_index", 0));
                Timecode length = HasAnyTime(p, "length") ? RequireTime(vegas.Project, p, "length", false) : stream.Length;
                if (length.Nanos <= 0) throw new InvalidOperationException("Inserted media length must be positive.");
                VideoEvent video = ((VideoTrack)track).AddVideoEvent(start, length);
                video.AddTake(stream, true, GetString(p, "take_name", Path.GetFileName(sourcePath)));
                ev = video;
            }
            else
            {
                if (!media.HasAudio()) throw new InvalidOperationException("The source has no audio stream.");
                AudioStream stream = media.GetAudioStreamByIndex(GetInt(p, "stream_index", 0));
                Timecode length = HasAnyTime(p, "length") ? RequireTime(vegas.Project, p, "length", false) : stream.Length;
                if (length.Nanos <= 0) throw new InvalidOperationException("Inserted media length must be positive.");
                AudioEvent audio = ((AudioTrack)track).AddAudioEvent(start, length);
                audio.AddTake(stream, true, GetString(p, "take_name", Path.GetFileName(sourcePath)));
                ev = audio;
            }
            if (HasAnyTime(p, "take_offset"))
            {
                Timecode offset = RequireTime(vegas.Project, p, "take_offset", false);
                if (offset.Nanos < 0) throw new InvalidOperationException("take_offset cannot be negative.");
                ev.ActiveTake.Offset = offset;
            }
            if (Has(p, "name")) ev.Name = RequireString(p, "name");
            return Result("event", EventData(ev, true));
        }

        private static object MoveEvent(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            GuardSingleEventStructure(ev, p, "move_event");
            if (Has(p, "new_track_index"))
            {
                Track destination = GetTrack(vegas.Project, RequireInt(p, "new_track_index"));
                if (destination.IsAudio() != ev.IsAudio()) throw new InvalidOperationException("Destination track media type does not match the event.");
                ev.Track = destination;
            }
            if (HasAnyTime(p, "new_start"))
            {
                Timecode start = RequireTime(vegas.Project, p, "new_start", true);
                if (start.Nanos < 0) throw new InvalidOperationException("new_start cannot be negative.");
                ev.Start = start;
            }
            return Result("event", EventData(ev, true));
        }

        private static object CopyEvent(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent source = GuardEvent(vegas.Project, p);
            GuardSingleEventStructure(source, p, "copy_event");
            Track destination = GetTrack(vegas.Project, GetInt(p, "new_track_index", source.Track.Index));
            if (destination.IsAudio() != source.IsAudio()) throw new InvalidOperationException("Destination track media type does not match the event.");
            Timecode start = RequireTime(vegas.Project, p, "new_start", true);
            if (start.Nanos < 0) throw new InvalidOperationException("new_start cannot be negative.");
            TrackEvent copy = source.Copy(destination, start);
            return Result("event", EventData(copy, true));
        }

        private static object TrimEvent(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            GuardSingleEventStructure(ev, p, "trim_event");
            Timecode start = HasAnyTime(p, "new_start") ? RequireTime(vegas.Project, p, "new_start", true) : ev.Start;
            Timecode length;
            if (HasAnyTime(p, "new_length")) length = RequireTime(vegas.Project, p, "new_length", false);
            else if (HasAnyTime(p, "new_end")) length = RequireTime(vegas.Project, p, "new_end", true) - start;
            else throw new InvalidOperationException("Provide new_length_seconds or new_end_seconds.");
            if (start.Nanos < 0 || length.Nanos <= 0) throw new InvalidOperationException("Trim result must have a nonnegative start and positive length.");
            ev.AdjustStartLength(start, length, true);
            return Result("event", EventData(ev, true));
        }

        // Remove a time range on ALL tracks and close the gap in one undo step.
        // Events crossing the range edges are split; the inside pieces are removed; everything after moves left.
        private static object RippleDeleteRange(Vegas vegas, Dictionary<string, object> p)
        {
            Project project = vegas.Project;
            Timecode a = RequireTime(project, p, "start", true);
            Timecode b = RequireTime(project, p, "end", true);
            if (!GetBool(p, "allow_ripple_delete", false))
                throw new InvalidOperationException("ripple_delete affects every track and requires allow_ripple_delete=true.");
            if (a.Nanos < 0 || b.Nanos <= a.Nanos || b.Nanos > project.Length.Nanos)
                throw new InvalidOperationException("ripple_delete requires 0 <= start < end <= project length.");
            // Preflight every affected track before splitting or removing any event.
            foreach (Track track in project.Tracks)
            {
                if (track.Envelopes.Count > 0 && !GetBool(p, "allow_unshifted_automation", false))
                    throw new InvalidOperationException("ripple_delete does not shift track automation. Inspect it and pass allow_unshifted_automation=true to acknowledge this limitation.");
                foreach (TrackEvent ev in track.Events)
                {
                    if (ev.End.Nanos <= a.Nanos) continue;
                    if (ev.Locked) throw new InvalidOperationException("ripple_delete refuses an affected locked event.");
                    if (ev.IsGrouped && !GetBool(p, "allow_group_desync", false))
                        throw new InvalidOperationException("ripple_delete may change event grouping when splitting. Inspect the groups and pass allow_group_desync=true to acknowledge this limitation.");
                }
            }
            Timecode len = b - a;
            int splits = 0, removed = 0, moved = 0;
            foreach (Track track in project.Tracks)
            {
                List<TrackEvent> evs = new List<TrackEvent>();
                foreach (TrackEvent ev in track.Events) evs.Add(ev);
                foreach (TrackEvent ev in evs)
                {
                    if (ev.End.Nanos <= a.Nanos || ev.Start.Nanos >= b.Nanos) continue;
                    TrackEvent mid = ev;
                    if (mid.Start.Nanos < a.Nanos) { mid = mid.Split(a - mid.Start); splits++; }
                    if (mid.End.Nanos > b.Nanos) { mid.Split(b - mid.Start); splits++; }
                    if (!track.Events.Remove(mid)) throw new InvalidOperationException("VEGAS did not remove an event inside the range.");
                    removed++;
                }
            }
            foreach (Track track in project.Tracks)
            {
                List<TrackEvent> later = new List<TrackEvent>();
                foreach (TrackEvent ev in track.Events) if (ev.Start.Nanos >= b.Nanos) later.Add(ev);
                later.Sort(delegate(TrackEvent x, TrackEvent y) { return x.Start.CompareTo(y.Start); });
                foreach (TrackEvent ev in later) { ev.Start = ev.Start - len; moved++; }
            }
            int markers = 0;
            if (GetBool(p, "shift_markers", true))
            {
                foreach (Marker m in project.Markers)
                {
                    if (m.Position.Nanos <= a.Nanos) continue;
                    m.Position = m.Position.Nanos < b.Nanos ? a : m.Position - len;
                    markers++;
                }
                // Map both region endpoints. A region entirely inside the cut collapses
                // and is removed from the project; no source file is touched.
                List<Region> collapsed = new List<Region>();
                foreach (Region r in project.Regions)
                {
                    Timecode oldStart = r.Position;
                    Timecode oldEnd = r.Position + r.Length;
                    Timecode newStart = oldStart.Nanos <= a.Nanos ? oldStart : oldStart.Nanos < b.Nanos ? a : oldStart - len;
                    Timecode newEnd = oldEnd.Nanos <= a.Nanos ? oldEnd : oldEnd.Nanos < b.Nanos ? a : oldEnd - len;
                    if (newStart.Nanos == oldStart.Nanos && newEnd.Nanos == oldEnd.Nanos) continue;
                    if (newEnd.Nanos <= newStart.Nanos) collapsed.Add(r);
                    else { r.Position = newStart; r.Length = newEnd - newStart; }
                    markers++;
                }
                foreach (Region r in collapsed) project.Regions.Remove(r);
            }
            Dictionary<string, object> result = Result("removed_seconds", Seconds(len));
            result["split_count"] = splits;
            result["removed_events"] = removed;
            result["moved_events"] = moved;
            result["moved_markers_regions"] = markers;
            return result;
        }

        private static object SplitEvent(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            GuardSingleEventStructure(ev, p, "split_event");
            Timecode timeline = RequireTime(vegas.Project, p, "at", true);
            Timecode offset = timeline - ev.Start;
            if (offset.Nanos <= 0 || offset.Nanos >= ev.Length.Nanos) throw new InvalidOperationException("Split point must be strictly inside the event.");
            TrackEvent right = ev.Split(offset);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["left_event"] = EventData(ev, true);
            result["right_event"] = EventData(right, true);
            return result;
        }

        private static object DeleteEvent(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            GuardSingleEventStructure(ev, p, "delete_event");
            string id = ev.EventID.ToString();
            bool ripple = GetBool(p, "ripple", false);
            bool removed;
            if (ripple) removed = ev.RemoveWithRipple();
            else removed = ev.Track.Events.Remove(ev);
            if (!removed) throw new InvalidOperationException("VEGAS did not remove the event.");
            Dictionary<string, object> result = Result("deleted_event_id", id);
            result["ripple"] = ripple;
            return result;
        }

        private static object SetEvent(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            if (Has(p, "name")) ev.Name = RequireString(p, "name");
            if (Has(p, "mute")) ev.Mute = RequireBool(p, "mute");
            if (Has(p, "locked")) ev.Locked = RequireBool(p, "locked");
            if (Has(p, "loop")) ev.Loop = RequireBool(p, "loop");
            if (Has(p, "playback_rate"))
            {
                if (ev.IsGrouped && !GetBool(p, "allow_group_desync", false))
                    throw new InvalidOperationException("Changing playback rate on a grouped event requires allow_group_desync=true or a coordinated batch for every grouped partner.");
                double rate = RequireDouble(p, "playback_rate");
                if (rate <= 0) throw new InvalidOperationException("playback_rate must be positive.");
                ev.PlaybackRate = rate;
            }
            if (HasAnyTime(p, "snap_offset")) ev.SnapOffset = RequireTime(vegas.Project, p, "snap_offset", false);
            VideoEvent video = ev as VideoEvent;
            if (Has(p, "event_opacity"))
            {
                if (video == null) throw new InvalidOperationException("event_opacity is only valid for video events.");
                double opacity = RequireDouble(p, "event_opacity");
                if (opacity < 0 || opacity > 1) throw new InvalidOperationException("event_opacity must be between 0 and 1.");
                video.FadeIn.Gain = (float)opacity;
            }
            if (Has(p, "maintain_aspect_ratio"))
            {
                if (video == null) throw new InvalidOperationException("maintain_aspect_ratio is only valid for video events.");
                video.MaintainAspectRatio = RequireBool(p, "maintain_aspect_ratio");
            }
            if (Has(p, "reduce_interlace"))
            {
                if (video == null) throw new InvalidOperationException("reduce_interlace is only valid for video events.");
                video.ReduceInterlace = RequireBool(p, "reduce_interlace");
            }
            if (Has(p, "resample_mode"))
            {
                if (video == null) throw new InvalidOperationException("resample_mode is only valid for video events.");
                video.ResampleMode = ParseEnum<VideoResampleMode>(RequireString(p, "resample_mode"));
            }
            if (Has(p, "under_sample_rate"))
            {
                if (video == null) throw new InvalidOperationException("under_sample_rate is only valid for video events.");
                double rate = RequireDouble(p, "under_sample_rate");
                if (rate <= 0 || rate > 1) throw new InvalidOperationException("under_sample_rate must be greater than 0 and at most 1.");
                video.UnderSampleRate = rate;
            }
            return Result("event", EventData(ev, true));
        }

        private static object SetFades(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            if (HasAnyTime(p, "fade_in")) ev.FadeIn.Length = CheckedFade(RequireTime(vegas.Project, p, "fade_in", false), ev);
            if (HasAnyTime(p, "fade_out")) ev.FadeOut.Length = CheckedFade(RequireTime(vegas.Project, p, "fade_out", false), ev);
            if (Has(p, "gain_db")) ev.FadeIn.Gain = (float)DbToGain(RequireDouble(p, "gain_db"));
            if (Has(p, "fade_in_curve")) ev.FadeIn.Curve = ParseEnum<CurveType>(RequireString(p, "fade_in_curve"));
            if (Has(p, "fade_out_curve")) ev.FadeOut.Curve = ParseEnum<CurveType>(RequireString(p, "fade_out_curve"));
            return Result("event", EventData(ev, true));
        }

        private static Timecode CheckedFade(Timecode value, TrackEvent ev)
        {
            if (value.Nanos < 0 || value.Nanos > ev.Length.Nanos) throw new InvalidOperationException("Fade length must be between zero and the event length.");
            return value;
        }

        private static object SetTransition(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            if (!ev.IsVideo()) throw new InvalidOperationException("Transitions are supported only on video events.");
            string direction = GetString(p, "direction", "in").ToLowerInvariant();
            if (direction != "in" && direction != "out") throw new InvalidOperationException("direction must be in or out.");
            Fade fade = direction == "out" ? ev.FadeOut : ev.FadeIn;
            string pluginName = GetString(p, "plugin_name", null);
            if (String.IsNullOrWhiteSpace(pluginName))
            {
                fade.RemoveTransition();
            }
            else
            {
                PlugInNode node = FindPlugin(vegas.Transitions, pluginName);
                if (node == null) throw new InvalidOperationException("Transition plug-in was not found: " + pluginName);
                Effect effect = new Effect(node);
                if (Has(p, "preset")) effect.Preset = RequireString(p, "preset");
                fade.Transition = effect;
            }
            if (HasAnyTime(p, "length")) fade.Length = CheckedFade(RequireTime(vegas.Project, p, "length", false), ev);
            Dictionary<string, object> result = Result("event", EventData(ev, true));
            result["direction"] = direction;
            result["transition"] = fade.Transition == null ? null : fade.Transition.ToString();
            return result;
        }

        private static object InsertTitle(Vegas vegas, Dictionary<string, object> p)
        {
            Track track = GetTrack(vegas.Project, RequireInt(p, "track_index"));
            VideoTrack videoTrack = track as VideoTrack;
            if (videoTrack == null) throw new InvalidOperationException("Titles require a video track.");
            string pluginName = GetString(p, "generator", "Titles & Text");
            PlugInNode node = FindPlugin(vegas.Generators, pluginName);
            if (node == null) throw new InvalidOperationException("Generator was not found: " + pluginName);
            string preset = GetString(p, "preset", "(Default)");
            Media media = Media.CreateInstance(vegas.Project, node, preset);
            OFXStringParameter textParameter = null;
            if (media.Generator != null && media.Generator.IsOFX)
                textParameter = media.Generator.OFXEffect.FindParameterByName("Text") as OFXStringParameter;
            if (textParameter == null) throw new InvalidOperationException("The selected generator does not expose a Text parameter.");
            using (RichTextBox rtf = new RichTextBox())
            {
                try { if (!String.IsNullOrWhiteSpace(textParameter.Value)) rtf.Rtf = textParameter.Value; }
                catch { rtf.Clear(); }
                rtf.Text = RequireString(p, "text");
                textParameter.Value = rtf.Rtf;
            }
            if (Has(p, "ofx_parameters"))
            {
                Dictionary<string, object> parameterValues = p["ofx_parameters"] as Dictionary<string, object>;
                if (parameterValues == null) throw new InvalidOperationException("ofx_parameters must be an object keyed by parameter name.");
                foreach (KeyValuePair<string, object> entry in parameterValues)
                {
                    OFXParameter parameter = media.Generator.OFXEffect.FindParameterByName(entry.Key);
                    if (parameter == null) throw new InvalidOperationException("Generator OFX parameter was not found: " + entry.Key);
                    ApplyOfxParameterValue(parameter, entry.Value, null);
                    parameter.ParameterChanged();
                }
                media.Generator.OFXEffect.AllParametersChanged();
            }
            Timecode start = RequireTime(vegas.Project, p, "start", true);
            Timecode length = HasAnyTime(p, "length") ? RequireTime(vegas.Project, p, "length", false) : ProjectTimecode.FromSeconds(vegas.Project, 5.0);
            if (start.Nanos < 0 || length.Nanos <= 0) throw new InvalidOperationException("Title start and length are invalid.");
            VideoEvent ev = videoTrack.AddVideoEvent(start, length);
            ev.AddTake(media.GetVideoStreamByIndex(0), true, GetString(p, "name", "Title"));
            if (Has(p, "name")) ev.Name = RequireString(p, "name");
            return Result("event", EventData(ev, true));
        }

        private static object GroupEvents(Vegas vegas, Dictionary<string, object> p)
        {
            List<TrackEvent> events = GuardEventList(vegas.Project, p);
            if (events.Count < 2) throw new InvalidOperationException("At least two events are required for grouping.");
            if (!GetBool(p, "allow_regroup", false))
                foreach (TrackEvent ev in events)
                    if (ev.IsGrouped) throw new InvalidOperationException("group_events refuses events that already belong to a group unless allow_regroup=true.");
            TrackEventGroup group = new TrackEventGroup(vegas.Project);
            vegas.Project.TrackEventGroups.Add(group);
            foreach (TrackEvent ev in events) group.Add(ev);
            return Result("events", EventDataList(events));
        }

        private static object UngroupEvents(Vegas vegas, Dictionary<string, object> p)
        {
            List<TrackEvent> events = GuardEventList(vegas.Project, p);
            if (!GetBool(p, "allow_partial_ungroup", false))
            {
                HashSet<long> requested = new HashSet<long>();
                foreach (TrackEvent ev in events) requested.Add(ev.EventID);
                foreach (TrackEvent ev in events)
                    if (ev.Group != null)
                        foreach (TrackEvent member in ev.Group)
                            if (!requested.Contains(member.EventID))
                                throw new InvalidOperationException("ungroup_events must include every member of each affected group, or set allow_partial_ungroup=true.");
            }
            foreach (TrackEvent ev in events)
            {
                TrackEventGroup group = ev.Group;
                if (group != null) group.Remove(ev);
            }
            return Result("events", EventDataList(events));
        }

        private static object AddMarker(Vegas vegas, Dictionary<string, object> p)
        {
            Timecode position = RequireTime(vegas.Project, p, "position", true);
            if (position.Nanos < 0) throw new InvalidOperationException("Marker position cannot be negative.");
            Marker marker = new Marker(position, GetString(p, "label", ""));
            vegas.Project.Markers.Add(marker);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["index"] = marker.Index;
            result["position_seconds"] = Seconds(marker.Position);
            result["label"] = marker.Label;
            return Result("marker", result);
        }

        private static object AddRegion(Vegas vegas, Dictionary<string, object> p)
        {
            Timecode position = RequireTime(vegas.Project, p, "position", true);
            Timecode length = RequireTime(vegas.Project, p, "length", false);
            if (position.Nanos < 0 || length.Nanos <= 0) throw new InvalidOperationException("Region position or length is invalid.");
            Region region = new Region(position, length, GetString(p, "label", ""));
            vegas.Project.Regions.Add(region);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["index"] = region.Index;
            result["position_seconds"] = Seconds(region.Position);
            result["length_seconds"] = Seconds(region.Length);
            result["label"] = region.Label;
            return Result("region", result);
        }

        private static object SetEventGain(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            if (!ev.IsAudio()) throw new InvalidOperationException("The target event is not audio.");
            double db = RequireDouble(p, "gain_db");
            if (db < -120 || db > 24) throw new InvalidOperationException("gain_db must be between -120 and +24 dB.");
            ev.FadeIn.Gain = (float)DbToGain(db);
            return Result("event", EventData(ev, true));
        }

        private static object Normalize(Vegas vegas, Dictionary<string, object> p, bool forceRecalculate)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            AudioEvent audio = ev as AudioEvent;
            if (audio == null) throw new InvalidOperationException("The target event is not audio.");
            bool enable = GetBool(p, "enable", true);
            audio.Normalize = enable;
            double recalculated = audio.NormalizeGain;
            if (enable && (forceRecalculate || GetBool(p, "recalculate", false)))
                recalculated = audio.RecalculateNorm();
            Dictionary<string, object> result = Result("event", EventData(audio, true));
            result["recalculated_gain_db"] = GainToDb(recalculated);
            return result;
        }

        private static object SetChannels(Vegas vegas, Dictionary<string, object> p)
        {
            AudioEvent audio = GuardEvent(vegas.Project, p) as AudioEvent;
            if (audio == null) throw new InvalidOperationException("The target event is not audio.");
            audio.Channels = ParseEnum<ChannelRemapping>(RequireString(p, "channels"));
            return Result("event", EventData(audio, true));
        }

        private static object SetAudioProperties(Vegas vegas, Dictionary<string, object> p)
        {
            AudioEvent audio = GuardEvent(vegas.Project, p) as AudioEvent;
            if (audio == null) throw new InvalidOperationException("The target event is not audio.");
            if (Has(p, "pitch_semis")) audio.PitchSemis = RequireDouble(p, "pitch_semis");
            if (Has(p, "pitch_lock")) audio.PitchLock = RequireBool(p, "pitch_lock");
            if (Has(p, "formant_shift")) audio.FormantShift = RequireDouble(p, "formant_shift");
            if (Has(p, "formant_lock")) audio.FormantLock = RequireBool(p, "formant_lock");
            if (Has(p, "invert_phase")) audio.InvertPhase = RequireBool(p, "invert_phase");
            return Result("event", EventData(audio, true));
        }

        private static object SetTrackMix(Vegas vegas, Dictionary<string, object> p)
        {
            AudioTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as AudioTrack;
            if (track == null) throw new InvalidOperationException("The target track is not audio.");
            if (Has(p, "expected_name") && !String.Equals(track.Name, RequireString(p, "expected_name"), StringComparison.Ordinal))
                throw new InvalidOperationException("Track name changed since the probe.");
            if (Has(p, "volume_db")) track.Volume = (float)DbToGain(CheckedGainDb(RequireDouble(p, "volume_db"), "volume_db"));
            if (Has(p, "pan"))
            {
                double pan = RequireDouble(p, "pan");
                if (pan < -1 || pan > 1) throw new InvalidOperationException("pan must be between -1 and 1.");
                track.PanX = (float)pan;
            }
            if (Has(p, "pan_y")) track.PanY = (float)RequireDouble(p, "pan_y");
            if (Has(p, "pan_center")) track.PanCenter = (float)RequireDouble(p, "pan_center");
            if (Has(p, "invert_phase")) track.InvertPhase = RequireBool(p, "invert_phase");
            return Result("track", TrackData(track));
        }

        private static object AddVolumeEnvelopePoint(Vegas vegas, Dictionary<string, object> p)
        {
            AudioTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as AudioTrack;
            if (track == null) throw new InvalidOperationException("The target track is not audio.");
            Envelope envelope = null;
            foreach (Envelope candidate in track.Envelopes)
                if (candidate.Type == EnvelopeType.Volume) { envelope = candidate; break; }
            if (envelope == null)
            {
                envelope = new Envelope(EnvelopeType.Volume);
                track.Envelopes.Add(envelope);
            }
            Timecode position = RequireTime(vegas.Project, p, "position", true);
            double gain = DbToGain(RequireDouble(p, "volume_db"));
            if (gain < envelope.Min || gain > envelope.Max) throw new InvalidOperationException("volume_db is outside the envelope's supported range.");
            CurveType curve = ParseEnum<CurveType>(GetString(p, "curve", "Linear"));
            EnvelopePoint existing = null;
            foreach (EnvelopePoint point in envelope.Points)
                if (Math.Abs(Seconds(point.X - position)) <= TimeToleranceSeconds) { existing = point; break; }
            if (existing == null) envelope.Points.Add(new EnvelopePoint(position, gain, curve));
            else { existing.Y = gain; existing.Curve = curve; }
            Dictionary<string, object> result = Result("track", TrackData(track));
            result["position_seconds"] = Seconds(position);
            result["position_frames"] = position.FrameCount;
            result["volume_db"] = RequireDouble(p, "volume_db");
            return result;
        }

        private static object ListPlugins(Vegas vegas, Dictionary<string, object> p)
        {
            string kind = GetString(p, "kind", "video").ToLowerInvariant();
            PlugInNode root;
            if (kind == "audio") root = vegas.AudioFX;
            else if (kind == "transition") root = vegas.Transitions;
            else if (kind == "generator") root = vegas.Generators;
            else if (kind == "video") root = vegas.VideoFX;
            else throw new InvalidOperationException("kind must be audio, video, transition, or generator.");
            int limit = GetInt(p, "limit", 500);
            string contains = GetString(p, "contains", null);
            List<object> values = new List<object>();
            CollectPlugins(root, "", contains, limit, values);
            Dictionary<string, object> result = Result("plugins", values);
            result["kind"] = kind;
            result["truncated"] = values.Count >= limit;
            return result;
        }

        private static void CollectPlugins(PlugInNode node, string parent, string contains, int limit, List<object> values)
        {
            foreach (PlugInNode child in node)
            {
                if (values.Count >= limit) return;
                string path = String.IsNullOrEmpty(parent) ? child.Name : parent + " / " + child.Name;
                if (!child.IsContainer && (String.IsNullOrWhiteSpace(contains) || child.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["name"] = child.Name;
                    item["path"] = path;
                    item["unique_id"] = child.UniqueID;
                    item["is_ofx"] = child.IsOFX;
                    item["is_audio"] = child.IsAudio;
                    item["is_video"] = child.IsVideo;
                    values.Add(item);
                }
                if (child.Count > 0) CollectPlugins(child, path, contains, limit, values);
            }
        }

        private static object ListEventEffects(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            TrackEvent ev = FindEvent(vegas.Project, RequireLong(p, "event_id"));
            return Result("effects", EffectData(GetEffects(ev)));
        }

        private static object AddEventEffect(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            Effects effects = GetEffects(ev);
            PlugInNode root = ev.IsAudio() ? vegas.AudioFX : vegas.VideoFX;
            string pluginName = RequireString(p, "plugin_name");
            PlugInNode node = FindPlugin(root, pluginName);
            if (node == null) throw new InvalidOperationException("Effect plug-in was not found: " + pluginName);
            Effect effect = effects.AddEffect(node);
            if (Has(p, "preset")) effect.Preset = RequireString(p, "preset");
            Dictionary<string, object> result = Result("event", EventData(ev, true));
            result["effect_index"] = effect.Index;
            return result;
        }

        private static object RemoveEventEffect(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            Effects effects = GetEffects(ev);
            int index = RequireInt(p, "effect_index");
            if (index < 0 || index >= effects.Count) throw new InvalidOperationException("effect_index is outside the event effect chain.");
            string description = effects[index].Description;
            effects.RemoveAt(index);
            Dictionary<string, object> result = Result("event", EventData(ev, true));
            result["removed_effect"] = description;
            return result;
        }

        private static object SetOfxParameter(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            Effects effects = GetEffects(ev);
            int index = RequireInt(p, "effect_index");
            if (index < 0 || index >= effects.Count) throw new InvalidOperationException("effect_index is outside the event effect chain.");
            Effect effect = effects[index];
            if (!effect.IsOFX) throw new InvalidOperationException("The selected effect is not OFX.");
            string parameterName = RequireString(p, "parameter_name");
            OFXParameter parameter = effect.OFXEffect.FindParameterByName(parameterName);
            if (parameter == null) throw new InvalidOperationException("OFX parameter was not found: " + parameterName);
            Timecode time = Has(p, "at_seconds") ? ProjectTimecode.FromSeconds(vegas.Project, RequireDouble(p, "at_seconds")) : null;
            ApplyOfxParameterValue(parameter, Require(p, "value"), time);
            parameter.ParameterChanged();
            Dictionary<string, object> result = Result("event", EventData(ev, true));
            result["parameter"] = ParameterData(parameter);
            return result;
        }

        private static int ConvertToIntOrMinusOne(object value)
        {
            int result;
            return Int32.TryParse(Convert.ToString(value), out result) ? result : -1;
        }

        private static List<object> EffectData(Effects effects)
        {
            List<object> values = new List<object>();
            if (effects == null) return values;
            foreach (Effect effect in effects)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["index"] = effect.Index;
                item["description"] = effect.Description;
                item["plugin_name"] = effect.PlugIn == null ? null : effect.PlugIn.Name;
                item["plugin_id"] = effect.PlugIn == null ? null : effect.PlugIn.UniqueID;
                item["bypass"] = effect.Bypass;
                item["preset"] = effect.CurrentPreset == null ? null : effect.CurrentPreset.Name;
                item["apply_before_pan_crop"] = effect.ApplyBeforePanCrop;
                item["apply_after_composite"] = effect.ApplyAfterComposite;
                item["is_ofx"] = effect.IsOFX;
                if (effect.IsOFX)
                {
                    List<object> parameters = new List<object>();
                    foreach (OFXParameter parameter in effect.OFXEffect.Parameters)
                        if (!parameter.Secret) parameters.Add(ParameterData(parameter));
                    item["parameters"] = parameters;
                }
                values.Add(item);
            }
            return values;
        }

        private static Dictionary<string, object> ParameterData(OFXParameter parameter)
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["name"] = parameter.Name;
            item["label"] = parameter.Label;
            item["type"] = parameter.ParameterType.ToString();
            item["enabled"] = parameter.Enabled;
            item["can_animate"] = parameter.CanAnimate;
            item["is_animated"] = parameter.IsAnimated;
            OFXDoubleParameter d = parameter as OFXDoubleParameter;
            OFXIntegerParameter i = parameter as OFXIntegerParameter;
            OFXBooleanParameter b = parameter as OFXBooleanParameter;
            OFXStringParameter s = parameter as OFXStringParameter;
            OFXChoiceParameter c = parameter as OFXChoiceParameter;
            if (d != null) { item["value"] = d.Value; item["min"] = d.Min; item["max"] = d.Max; }
            else if (i != null) { item["value"] = i.Value; item["min"] = i.Min; item["max"] = i.Max; }
            else if (b != null) item["value"] = b.Value;
            else if (s != null) item["value"] = s.Value;
            else if (c != null)
            {
                item["value"] = c.Value == null ? null : c.Value.Name;
                List<object> choices = new List<object>();
                foreach (OFXChoice choice in c.Choices) choices.Add(new Dictionary<string, object> { { "name", choice.Name }, { "index", choice.Index } });
                item["choices"] = choices;
            }
            else
            {
                object advanced = ReadOfxValue(parameter);
                if (advanced != null) item["value"] = advanced;
            }
            return item;
        }

        private static Effects GetEffects(TrackEvent ev)
        {
            AudioEvent audio = ev as AudioEvent;
            if (audio != null) return audio.Effects;
            VideoEvent video = ev as VideoEvent;
            if (video != null) return video.Effects;
            throw new InvalidOperationException("The event has no effect chain.");
        }

        private static PlugInNode FindPlugin(PlugInNode root, string nameOrId)
        {
            foreach (PlugInNode child in root)
            {
                if (String.Equals(child.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(child.UniqueID, nameOrId, StringComparison.OrdinalIgnoreCase)) return child;
                if (child.Count > 0)
                {
                    PlugInNode nested = FindPlugin(child, nameOrId);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static TrackEvent GuardEvent(Project project, Dictionary<string, object> p)
        {
            TrackEvent ev = FindEvent(project, RequireLong(p, "event_id"));
            if (Has(p, "expected_start_frames"))
            {
                if (ev.Start.FrameCount != RequireLong(p, "expected_start_frames")) throw new InvalidOperationException("Event start changed since the probe.");
            }
            else
            {
                double expectedStart = RequireDouble(p, "expected_start_seconds");
                if (Math.Abs(Seconds(ev.Start) - expectedStart) > TimeToleranceSeconds) throw new InvalidOperationException("Event start changed since the probe.");
            }
            if (Has(p, "expected_length_frames"))
            {
                if (ev.Length.FrameCount != RequireLong(p, "expected_length_frames")) throw new InvalidOperationException("Event length changed since the probe.");
            }
            else
            {
                double expectedLength = RequireDouble(p, "expected_length_seconds");
                if (Math.Abs(Seconds(ev.Length) - expectedLength) > TimeToleranceSeconds) throw new InvalidOperationException("Event length changed since the probe.");
            }
            if (Has(p, "expected_track_index") && ev.Track.Index != RequireInt(p, "expected_track_index"))
                throw new InvalidOperationException("Event track changed since the probe.");
            return ev;
        }

        private static void GuardSingleEventStructure(TrackEvent ev, Dictionary<string, object> p, string action)
        {
            if (ev.IsGrouped && !GetBool(p, "allow_group_desync", false))
                throw new InvalidOperationException(action + " refuses a grouped event by default. Use the matching group action, or set allow_group_desync=true for an intentional one-event operation.");
        }

        private static List<TrackEvent> GuardEventList(Project project, Dictionary<string, object> p)
        {
            object raw = Require(p, "events");
            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable == null || raw is string) throw new InvalidOperationException("events must be an array of guarded event identities.");
            List<TrackEvent> events = new List<TrackEvent>();
            foreach (object value in enumerable)
            {
                Dictionary<string, object> item = value as Dictionary<string, object>;
                if (item == null) throw new InvalidOperationException("Each events item must be an object.");
                events.Add(GuardEvent(project, item));
            }
            return events;
        }

        private static TrackEvent FindEvent(Project project, long id)
        {
            foreach (Track track in project.Tracks)
                foreach (TrackEvent ev in track.Events)
                    if (ev.EventID == id) return ev;
            throw new InvalidOperationException("Event was not found: " + id);
        }

        private static Track GetTrack(Project project, int index)
        {
            if (index < 0 || index >= project.Tracks.Count)
                throw new InvalidOperationException("Track index is outside the project track range: " + index);
            return project.Tracks[index];
        }

        private static void GuardProject(Project project, Dictionary<string, object> p)
        {
            if (project.IsUntitled || String.IsNullOrWhiteSpace(project.FilePath))
                throw new InvalidOperationException("Save the VEGAS project before using guarded connector actions.");
            string expected = Path.GetFullPath(RequireString(p, "project_path"));
            string actual = Path.GetFullPath(project.FilePath);
            if (!String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active project changed. Expected " + expected + " but VEGAS has " + actual + ".");
        }

        private static string CreateBackup(Project project, string reason)
        {
            if (project.IsUntitled || String.IsNullOrWhiteSpace(project.FilePath))
                throw new InvalidOperationException("Save the VEGAS project before creating a connector backup.");
            string projectName = SafeFileName(Path.GetFileNameWithoutExtension(project.FilePath));
            string directory = Path.Combine(BackupRoot(), projectName);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, projectName + "__vegas-mcp__" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "__" + SafeFileName(reason) + ".veg");
            Project clone = project.CloneProject(path);
            try
            {
                if (!File.Exists(path)) clone.SaveProject(path);
            }
            finally
            {
                if (clone != null) clone.Dispose();
            }
            if (!File.Exists(path)) throw new IOException("VEGAS did not create the project backup: " + path);
            return path;
        }

        private static string BackupRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VEGAS MCP Backups");
        }

        private static string SafeActionName(string action)
        {
            return SafeFileName(action.Replace('.', '-'));
        }

        private static string SafeFileName(string value)
        {
            string cleaned = Regex.Replace(value ?? "item", "[^A-Za-z0-9._-]+", "-").Trim('-');
            return String.IsNullOrWhiteSpace(cleaned) ? "item" : cleaned;
        }

        private static List<object> MarkerData(Project project)
        {
            List<object> values = new List<object>();
            foreach (Marker marker in project.Markers)
                values.Add(MarkerItem(marker));
            return values;
        }

        private static List<object> RegionData(Project project)
        {
            List<object> values = new List<object>();
            foreach (Region region in project.Regions)
                values.Add(RegionItem(region));
            return values;
        }

        private static List<object> EventDataList(IEnumerable<TrackEvent> events)
        {
            List<object> values = new List<object>();
            foreach (TrackEvent ev in events) values.Add(EventData(ev, true));
            return values;
        }

        private static List<object> SelectedEventIds(Project project)
        {
            List<object> values = new List<object>();
            foreach (Track track in project.Tracks)
                foreach (TrackEvent ev in track.Events)
                    if (ev.Selected) values.Add(ev.EventID.ToString());
            return values;
        }

        private static List<long> ReadEventIds(Dictionary<string, object> p, string key)
        {
            object raw = Require(p, key);
            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable == null || raw is string) throw new InvalidOperationException(key + " must be an array.");
            List<long> values = new List<long>();
            foreach (object value in enumerable) values.Add(Convert.ToInt64(value));
            return values;
        }

        private static Dictionary<string, object> Result(string key, object value)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result[key] = value;
            return result;
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

        private static double RequireDouble(Dictionary<string, object> p, string key)
        {
            return Convert.ToDouble(Require(p, key));
        }

        private static double GetDouble(Dictionary<string, object> p, string key, double fallback)
        {
            return Has(p, key) ? Convert.ToDouble(p[key]) : fallback;
        }

        private static int RequireInt(Dictionary<string, object> p, string key)
        {
            return Convert.ToInt32(Require(p, key));
        }

        private static int GetInt(Dictionary<string, object> p, string key, int fallback)
        {
            return Has(p, key) ? Convert.ToInt32(p[key]) : fallback;
        }

        private static long RequireLong(Dictionary<string, object> p, string key)
        {
            return Convert.ToInt64(Require(p, key));
        }

        private static bool RequireBool(Dictionary<string, object> p, string key)
        {
            return Convert.ToBoolean(Require(p, key));
        }

        private static bool GetBool(Dictionary<string, object> p, string key, bool fallback)
        {
            return Has(p, key) ? Convert.ToBoolean(p[key]) : fallback;
        }

        private static double Seconds(Timecode value)
        {
            return value == null ? 0 : value.ToMilliseconds() / 1000.0;
        }

        private static double DbToGain(double db)
        {
            return db <= -120 ? 0 : Math.Pow(10.0, db / 20.0);
        }

        private static double CheckedGainDb(double db, string name)
        {
            if (Double.IsNaN(db) || Double.IsInfinity(db) || db < -120 || db > 24)
                throw new InvalidOperationException(name + " must be between -120 and +24 dB.");
            return db;
        }

        private static double GainToDb(double gain)
        {
            return gain <= 0 ? -120 : 20.0 * Math.Log10(gain);
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            T parsed;
            if (!Enum.TryParse<T>(value, true, out parsed))
                throw new InvalidOperationException("Unsupported " + typeof(T).Name + " value: " + value);
            return parsed;
        }
    }
}
