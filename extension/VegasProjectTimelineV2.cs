using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static partial class VegasActions
    {
        private static object InspectProjectSettings(Vegas vegas, Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(vegas.Project, p);
            Project project = vegas.Project;
            Dictionary<string, object> video = new Dictionary<string, object>();
            video["width"] = project.Video.Width;
            video["height"] = project.Video.Height;
            video["frame_rate"] = project.Video.FrameRate;
            video["pixel_aspect_ratio"] = project.Video.PixelAspectRatio;
            video["field_order"] = project.Video.FieldOrder.ToString();
            video["render_quality"] = project.Video.RenderQuality.ToString();
            video["deinterlace_method"] = project.Video.DeinterlaceMethod.ToString();
            video["pixel_format"] = project.Video.PixelFormat.ToString();
            video["output_rotation"] = project.Video.OutputRotation.ToString();
            video["hdr_mode"] = project.Video.HDRMode.ToString();

            Dictionary<string, object> audio = new Dictionary<string, object>();
            audio["sample_rate"] = project.Audio.SampleRate;
            audio["bit_depth"] = project.Audio.BitDepth;
            audio["resample_quality"] = project.Audio.ResampleQuality.ToString();
            audio["master_bus_mode"] = project.Audio.MasterBusMode.ToString();

            Dictionary<string, object> ruler = new Dictionary<string, object>();
            ruler["format"] = project.Ruler.Format.ToString();
            ruler["start_time_seconds"] = Seconds(project.Ruler.StartTime);
            ruler["beats_per_minute"] = project.Ruler.BeatsPerMinute;
            ruler["beats_per_measure"] = project.Ruler.BeatsPerMeasure;
            ruler["beat_value"] = project.Ruler.BeatValue.ToString();

            Dictionary<string, object> result = ProjectSummary(vegas);
            result["video"] = video;
            result["audio"] = audio;
            result["ruler"] = ruler;
            result["settings_token"] = ProjectSettingsToken(project);
            return result;
        }

        private static object SetProjectSettings(Vegas vegas, Dictionary<string, object> p)
        {
            Project project = vegas.Project;
            string expectedToken = RequireString(p, "expected_settings_token");
            string actualToken = ProjectSettingsToken(project);
            if (!String.Equals(expectedToken, actualToken, StringComparison.Ordinal))
                throw new InvalidOperationException("Project settings changed since the settings probe.");

            if (Has(p, "width"))
            {
                int value = RequireInt(p, "width");
                if (value < 16 || value > 16384) throw new InvalidOperationException("width must be between 16 and 16384.");
                project.Video.Width = value;
            }
            if (Has(p, "height"))
            {
                int value = RequireInt(p, "height");
                if (value < 16 || value > 16384) throw new InvalidOperationException("height must be between 16 and 16384.");
                project.Video.Height = value;
            }
            if (Has(p, "frame_rate"))
            {
                double value = RequireDouble(p, "frame_rate");
                if (value < 1 || value > 240) throw new InvalidOperationException("frame_rate must be between 1 and 240.");
                project.Video.FrameRate = value;
            }
            if (Has(p, "pixel_aspect_ratio"))
            {
                double value = RequireDouble(p, "pixel_aspect_ratio");
                if (value <= 0 || value > 10) throw new InvalidOperationException("pixel_aspect_ratio is outside the supported range.");
                project.Video.PixelAspectRatio = value;
            }
            if (Has(p, "field_order")) project.Video.FieldOrder = ParseEnum<VideoFieldOrder>(RequireString(p, "field_order"));
            if (Has(p, "render_quality")) project.Video.RenderQuality = ParseEnum<VideoRenderQuality>(RequireString(p, "render_quality"));
            if (Has(p, "deinterlace_method")) project.Video.DeinterlaceMethod = ParseEnum<VideoDeinterlaceMethod>(RequireString(p, "deinterlace_method"));
            if (Has(p, "pixel_format")) project.Video.PixelFormat = ParseEnum<PixelFormat>(RequireString(p, "pixel_format"));
            if (Has(p, "output_rotation")) project.Video.OutputRotation = ParseEnum<VideoOutputRotation>(RequireString(p, "output_rotation"));
            if (Has(p, "hdr_mode")) project.Video.HDRMode = ParseEnum<HDRMode>(RequireString(p, "hdr_mode"));

            if (Has(p, "sample_rate"))
            {
                int value = RequireInt(p, "sample_rate");
                if (value < 8000 || value > 768000) throw new InvalidOperationException("sample_rate is outside the supported range.");
                project.Audio.SampleRate = Convert.ToUInt32(value);
            }
            if (Has(p, "bit_depth"))
            {
                int value = RequireInt(p, "bit_depth");
                if (value < 8 || value > 64) throw new InvalidOperationException("bit_depth is outside the supported range.");
                project.Audio.BitDepth = Convert.ToUInt32(value);
            }
            if (Has(p, "resample_quality")) project.Audio.ResampleQuality = ParseEnum<AudioResampleQuality>(RequireString(p, "resample_quality"));
            if (Has(p, "master_bus_mode")) project.Audio.MasterBusMode = ParseEnum<AudioBusMode>(RequireString(p, "master_bus_mode"));
            if (Has(p, "ruler_format")) project.Ruler.Format = ParseEnum<RulerFormat>(RequireString(p, "ruler_format"));
            if (Has(p, "beats_per_minute")) project.Ruler.BeatsPerMinute = RequireDouble(p, "beats_per_minute");
            if (Has(p, "beats_per_measure")) project.Ruler.BeatsPerMeasure = Convert.ToUInt32(RequireInt(p, "beats_per_measure"));
            if (Has(p, "beat_value")) project.Ruler.BeatValue = ParseEnum<BeatValue>(RequireString(p, "beat_value"));
            return InspectProjectSettings(vegas, p);
        }

        private static string ProjectSettingsToken(Project project)
        {
            string value = String.Join("|", new string[] {
                project.Video.Width.ToString(), project.Video.Height.ToString(), project.Video.FrameRate.ToString("R"),
                project.Video.PixelAspectRatio.ToString("R"), project.Video.FieldOrder.ToString(), project.Video.RenderQuality.ToString(),
                project.Video.DeinterlaceMethod.ToString(), project.Video.PixelFormat.ToString(), project.Video.OutputRotation.ToString(),
                project.Video.HDRMode.ToString(), project.Audio.SampleRate.ToString(), project.Audio.BitDepth.ToString(),
                project.Audio.ResampleQuality.ToString(), project.Audio.MasterBusMode.ToString(), project.Ruler.Format.ToString(),
                project.Ruler.BeatsPerMinute.ToString("R"), project.Ruler.BeatsPerMeasure.ToString(), project.Ruler.BeatValue.ToString()
            });
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
            }
        }

        private static object SetTimelineSelection(Vegas vegas, Dictionary<string, object> p)
        {
            Timecode start = RequireTime(vegas.Project, p, "start", true);
            Timecode length = RequireTime(vegas.Project, p, "length", false);
            if (start.Nanos < 0 || length.Nanos < 0) throw new InvalidOperationException("Timeline selection values cannot be negative.");
            vegas.SelectionStart = start;
            vegas.SelectionLength = length;
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["selection_start_seconds"] = Seconds(start);
            result["selection_start_frames"] = start.FrameCount;
            result["selection_length_seconds"] = Seconds(length);
            result["selection_length_frames"] = length.FrameCount;
            return result;
        }

        private static object InsertProjectTime(Vegas vegas, Dictionary<string, object> p)
        {
            Timecode start = RequireTime(vegas.Project, p, "start", true);
            Timecode length = RequireTime(vegas.Project, p, "length", false);
            if (start.Nanos < 0 || length.Nanos <= 0) throw new InvalidOperationException("Inserted time must have a nonnegative start and positive length.");
            bool ignoreSelection = GetBool(p, "ignore_event_selection", true);
            vegas.Project.InsertTime(start, length, ignoreSelection);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["start_seconds"] = Seconds(start);
            result["length_seconds"] = Seconds(length);
            result["ignore_event_selection"] = ignoreSelection;
            result["project"] = ProjectSummary(vegas);
            return result;
        }

        private static object FindEvents(Vegas vegas, Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(vegas.Project, p);
            HashSet<long> ids = Has(p, "event_ids") ? new HashSet<long>(ReadEventIds(p, "event_ids")) : null;
            HashSet<int> trackIndexes = Has(p, "track_indexes") ? ReadIntSet(p, "track_indexes") : null;
            string kind = GetString(p, "kind", null);
            string nameContains = GetString(p, "name_contains", null);
            string pathEquals = Has(p, "media_path") ? Path.GetFullPath(RequireString(p, "media_path")) : null;
            bool hasSelected = Has(p, "selected");
            bool selected = hasSelected && RequireBool(p, "selected");
            bool hasMuted = Has(p, "mute");
            bool muted = hasMuted && RequireBool(p, "mute");
            bool hasLocked = Has(p, "locked");
            bool locked = hasLocked && RequireBool(p, "locked");
            double rangeStart = Has(p, "range_start_seconds") ? RequireDouble(p, "range_start_seconds") : Double.NegativeInfinity;
            double rangeEnd = Has(p, "range_end_seconds") ? RequireDouble(p, "range_end_seconds") : Double.PositiveInfinity;
            if (rangeEnd < rangeStart) throw new InvalidOperationException("range_end_seconds cannot precede range_start_seconds.");
            int limit = GetInt(p, "limit", 1000);
            if (limit < 1 || limit > 10000) throw new InvalidOperationException("limit must be between 1 and 10000.");

            List<object> matches = new List<object>();
            foreach (Track track in vegas.Project.Tracks)
            {
                if (trackIndexes != null && !trackIndexes.Contains(track.Index)) continue;
                foreach (TrackEvent ev in track.Events)
                {
                    if (ids != null && !ids.Contains(ev.EventID)) continue;
                    if (!String.IsNullOrWhiteSpace(kind) && !String.Equals(kind, ev.IsAudio() ? "audio" : "video", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!String.IsNullOrWhiteSpace(nameContains) && (ev.Name == null || ev.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    if (hasSelected && ev.Selected != selected) continue;
                    if (hasMuted && ev.Mute != muted) continue;
                    if (hasLocked && ev.Locked != locked) continue;
                    if (Seconds(ev.End) <= rangeStart || Seconds(ev.Start) >= rangeEnd) continue;
                    if (pathEquals != null)
                    {
                        string actual = ev.ActiveTake == null || String.IsNullOrWhiteSpace(ev.ActiveTake.MediaPath) ? null : Path.GetFullPath(ev.ActiveTake.MediaPath);
                        if (!String.Equals(actual, pathEquals, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    matches.Add(EventData(ev, GetBool(p, "include_effects", false)));
                    if (matches.Count >= limit) break;
                }
                if (matches.Count >= limit) break;
            }
            Dictionary<string, object> result = Result("events", matches);
            result["count"] = matches.Count;
            result["truncated"] = matches.Count >= limit;
            return result;
        }

        private static object DuplicateTrack(Vegas vegas, Dictionary<string, object> p)
        {
            Track source = GetTrack(vegas.Project, RequireInt(p, "track_index"));
            GuardTrackName(source, p);
            int targetIndex = GetInt(p, "target_index", source.Index + 1);
            if (targetIndex < 0 || targetIndex > vegas.Project.Tracks.Count)
                throw new InvalidOperationException("target_index is outside the project track range.");
            Track duplicate = vegas.Project.DuplicateTrack(source, targetIndex);
            if (Has(p, "name")) duplicate.Name = RequireString(p, "name");
            return Result("track", TrackWithEventsData(duplicate, true));
        }

        private static object InsertLinkedMedia(Vegas vegas, Dictionary<string, object> p)
        {
            string sourcePath = Path.GetFullPath(RequireString(p, "source_path"));
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source media was not found.", sourcePath);
            Media media = Media.CreateInstance(vegas.Project, sourcePath);
            bool wantVideo = GetBool(p, "include_video", media.HasVideo());
            bool wantAudio = GetBool(p, "include_audio", media.HasAudio());
            if (wantVideo && !media.HasVideo()) throw new InvalidOperationException("The source has no video stream.");
            if (wantAudio && !media.HasAudio()) throw new InvalidOperationException("The source has no audio stream.");
            if (!wantVideo && !wantAudio) throw new InvalidOperationException("At least one source stream must be included.");

            VideoTrack videoTrack = null;
            AudioTrack audioTrack = null;
            if (wantVideo) videoTrack = ResolveOrCreateVideoTrack(vegas.Project, p, "video_track_index", GetBool(p, "create_missing_tracks", false));
            if (wantAudio) audioTrack = ResolveOrCreateAudioTrack(vegas.Project, p, "audio_track_index", GetBool(p, "create_missing_tracks", false));

            int videoStreamIndex = GetInt(p, "video_stream_index", 0);
            int audioStreamIndex = GetInt(p, "audio_stream_index", 0);
            VideoStream videoStream = wantVideo ? media.GetVideoStreamByIndex(videoStreamIndex) : null;
            AudioStream audioStream = wantAudio ? media.GetAudioStreamByIndex(audioStreamIndex) : null;
            Timecode start = RequireTime(vegas.Project, p, "start", true);
            if (start.Nanos < 0) throw new InvalidOperationException("start cannot be negative.");
            Timecode length;
            if (HasAnyTime(p, "length")) length = RequireTime(vegas.Project, p, "length", false);
            else if (videoStream != null && audioStream != null) length = videoStream.Length.Nanos <= audioStream.Length.Nanos ? videoStream.Length : audioStream.Length;
            else length = videoStream != null ? videoStream.Length : audioStream.Length;
            if (length.Nanos <= 0) throw new InvalidOperationException("Inserted media length must be positive.");

            string takeName = GetString(p, "take_name", Path.GetFileName(sourcePath));
            List<TrackEvent> created = new List<TrackEvent>();
            if (videoTrack != null)
            {
                VideoEvent video = videoTrack.AddVideoEvent(start, length);
                video.AddTake(videoStream, true, takeName);
                created.Add(video);
            }
            if (audioTrack != null)
            {
                AudioEvent audio = audioTrack.AddAudioEvent(start, length);
                audio.AddTake(audioStream, true, takeName);
                created.Add(audio);
            }
            if (HasAnyTime(p, "take_offset"))
            {
                Timecode offset = RequireTime(vegas.Project, p, "take_offset", false);
                if (offset.Nanos < 0) throw new InvalidOperationException("take_offset cannot be negative.");
                foreach (TrackEvent ev in created) ev.ActiveTake.Offset = offset;
            }
            if (Has(p, "name")) foreach (TrackEvent ev in created) ev.Name = RequireString(p, "name");
            if (created.Count > 1 && GetBool(p, "group", true))
            {
                TrackEventGroup group = new TrackEventGroup(vegas.Project);
                vegas.Project.TrackEventGroups.Add(group);
                foreach (TrackEvent ev in created) group.Add(ev);
            }
            if (GetBool(p, "select", false)) foreach (TrackEvent ev in created) ev.Selected = true;
            Dictionary<string, object> result = Result("events", EventDataList(created));
            result["source_path"] = sourcePath;
            result["grouped"] = created.Count > 1 && created[0].IsGrouped;
            result["start_frames"] = start.FrameCount;
            result["length_frames"] = length.FrameCount;
            return result;
        }

        private static object CreateCrossfade(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> leftIdentity = Require(p, "left_event") as Dictionary<string, object>;
            Dictionary<string, object> rightIdentity = Require(p, "right_event") as Dictionary<string, object>;
            if (leftIdentity == null || rightIdentity == null) throw new InvalidOperationException("left_event and right_event must be guarded event identity objects.");
            TrackEvent left = GuardEvent(vegas.Project, leftIdentity);
            TrackEvent right = GuardEvent(vegas.Project, rightIdentity);
            if (left.EventID == right.EventID) throw new InvalidOperationException("A crossfade requires two different events.");
            if (left.Track.Index != right.Track.Index) throw new InvalidOperationException("Crossfade events must be on the same track.");
            if (left.IsAudio() != right.IsAudio()) throw new InvalidOperationException("Crossfade event media types do not match.");
            if ((left.IsGrouped || right.IsGrouped) && !GetBool(p, "allow_grouped", false))
                throw new InvalidOperationException("Grouped crossfade events require allow_grouped=true and a matching operation for every grouped A/V partner.");
            if (right.Start < left.Start) { TrackEvent swap = left; left = right; right = swap; }
            Timecode duration = RequireTime(vegas.Project, p, "duration", false);
            if (duration.Nanos <= 0 || duration.Nanos >= left.Length.Nanos || duration.Nanos >= right.Length.Nanos)
                throw new InvalidOperationException("Crossfade duration must be positive and shorter than both events.");
            Timecode newRightStart = left.End - duration;
            if (newRightStart.Nanos < 0) throw new InvalidOperationException("Crossfade would move the right event before the project start.");

            if (!GetBool(p, "allow_other_overlaps", false))
            {
                foreach (TrackEvent candidate in left.Track.Events)
                {
                    if (candidate.EventID == left.EventID || candidate.EventID == right.EventID) continue;
                    if (candidate.Start < left.End && candidate.End > newRightStart)
                        throw new InvalidOperationException("Another event occupies the proposed crossfade range: " + candidate.EventID);
                }
            }
            right.Start = newRightStart;
            left.FadeOut.Length = duration;
            right.FadeIn.Length = duration;
            CurveType curve = ParseEnum<CurveType>(GetString(p, "curve", "Linear"));
            left.FadeOut.Curve = curve;
            right.FadeIn.Curve = curve;
            if (left.IsVideo())
            {
                string pluginName = GetString(p, "plugin_name", "Dissolve");
                PlugInNode node = FindPlugin(vegas.Transitions, pluginName);
                if (node == null) throw new InvalidOperationException("Transition plug-in was not found: " + pluginName);
                Effect effect = new Effect(node);
                if (Has(p, "preset")) effect.Preset = RequireString(p, "preset");
                right.FadeIn.Transition = effect;
            }
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["left_event"] = EventData(left, true);
            result["right_event"] = EventData(right, true);
            result["overlap_seconds"] = Seconds(duration);
            result["overlap_frames"] = duration.FrameCount;
            return result;
        }

        private static object MoveGroup(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent anchor = GuardEvent(vegas.Project, p);
            List<TrackEvent> members = RequireGroupMembers(anchor);
            Timecode newStart = RequireTime(vegas.Project, p, "new_start", true);
            Timecode delta = newStart - anchor.Start;
            foreach (TrackEvent member in members)
                if ((member.Start + delta).Nanos < 0) throw new InvalidOperationException("The group move would place an event before the project start.");
            foreach (TrackEvent member in members) member.Start += delta;
            return GroupActionData("events", members, anchor.EventID);
        }

        private static object CopyGroup(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent anchor = GuardEvent(vegas.Project, p);
            List<TrackEvent> members = RequireGroupMembers(anchor);
            Timecode newStart = RequireTime(vegas.Project, p, "new_start", true);
            Timecode delta = newStart - anchor.Start;
            List<TrackEvent> copies = new List<TrackEvent>();
            foreach (TrackEvent member in members)
            {
                Timecode copyStart = member.Start + delta;
                if (copyStart.Nanos < 0) throw new InvalidOperationException("The group copy would place an event before the project start.");
                copies.Add(member.Copy(member.Track, copyStart));
            }
            GroupEventsTogether(vegas.Project, copies);
            return GroupActionData("events", copies, copies[0].EventID);
        }

        private static object SplitGroup(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent anchor = GuardEvent(vegas.Project, p);
            List<TrackEvent> members = RequireGroupMembers(anchor);
            Timecode timeline = RequireTime(vegas.Project, p, "at", true);
            foreach (TrackEvent member in members)
            {
                Timecode offset = timeline - member.Start;
                if (offset.Nanos <= 0 || offset.Nanos >= member.Length.Nanos)
                    throw new InvalidOperationException("The split point must be strictly inside every grouped event.");
            }
            TrackEventGroup originalGroup = anchor.Group;
            List<TrackEvent> left = new List<TrackEvent>();
            List<TrackEvent> right = new List<TrackEvent>();
            foreach (TrackEvent member in members)
            {
                left.Add(member);
                right.Add(member.Split(timeline - member.Start));
            }
            foreach (TrackEvent member in left) if (member.Group != null) member.Group.Remove(member);
            foreach (TrackEvent member in right) if (member.Group != null) member.Group.Remove(member);
            vegas.Project.TrackEventGroups.Remove(originalGroup);
            GroupEventsTogether(vegas.Project, left);
            GroupEventsTogether(vegas.Project, right);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["left_events"] = EventDataList(left);
            result["right_events"] = EventDataList(right);
            result["split_frames"] = timeline.FrameCount;
            return result;
        }

        private static object DeleteGroup(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent anchor = GuardEvent(vegas.Project, p);
            List<TrackEvent> members = RequireGroupMembers(anchor);
            List<object> removed = EventDataList(members);
            foreach (TrackEvent member in members)
                if (!member.Track.Events.Remove(member)) throw new InvalidOperationException("VEGAS did not remove grouped event " + member.EventID + ".");
            Dictionary<string, object> result = Result("deleted_events", removed);
            result["deleted_count"] = members.Count;
            return result;
        }

        private static List<TrackEvent> RequireGroupMembers(TrackEvent anchor)
        {
            if (anchor.Group == null || !anchor.IsGrouped) throw new InvalidOperationException("The target event is not grouped.");
            List<TrackEvent> members = new List<TrackEvent>();
            foreach (TrackEvent member in anchor.Group) members.Add(member);
            if (members.Count < 2) throw new InvalidOperationException("The event group has fewer than two members.");
            return members;
        }

        private static void GroupEventsTogether(Project project, List<TrackEvent> events)
        {
            if (events.Count < 2) return;
            TrackEventGroup group = new TrackEventGroup(project);
            project.TrackEventGroups.Add(group);
            foreach (TrackEvent ev in events) group.Add(ev);
        }

        private static Dictionary<string, object> GroupActionData(string key, List<TrackEvent> events, long anchorEventId)
        {
            Dictionary<string, object> result = Result(key, EventDataList(events));
            result["anchor_event_id"] = anchorEventId.ToString();
            result["event_count"] = events.Count;
            return result;
        }

        private static object CreateLinkedCrossfade(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> leftVideoIdentity = Require(p, "left_video_event") as Dictionary<string, object>;
            Dictionary<string, object> rightVideoIdentity = Require(p, "right_video_event") as Dictionary<string, object>;
            Dictionary<string, object> leftAudioIdentity = Require(p, "left_audio_event") as Dictionary<string, object>;
            Dictionary<string, object> rightAudioIdentity = Require(p, "right_audio_event") as Dictionary<string, object>;
            if (leftVideoIdentity == null || rightVideoIdentity == null || leftAudioIdentity == null || rightAudioIdentity == null)
                throw new InvalidOperationException("All four linked crossfade event identities must be objects.");
            TrackEvent leftVideo = GuardEvent(vegas.Project, leftVideoIdentity);
            TrackEvent rightVideo = GuardEvent(vegas.Project, rightVideoIdentity);
            TrackEvent leftAudio = GuardEvent(vegas.Project, leftAudioIdentity);
            TrackEvent rightAudio = GuardEvent(vegas.Project, rightAudioIdentity);
            if (!leftVideo.IsVideo() || !rightVideo.IsVideo() || !leftAudio.IsAudio() || !rightAudio.IsAudio())
                throw new InvalidOperationException("Linked crossfade identities do not match their required video/audio types.");
            if (leftVideo.Group == null || leftVideo.Group != leftAudio.Group || rightVideo.Group == null || rightVideo.Group != rightAudio.Group)
                throw new InvalidOperationException("Each video/audio pair must belong to the same event group.");

            Dictionary<string, object> videoParams = CloneDictionary(p);
            videoParams["left_event"] = leftVideoIdentity;
            videoParams["right_event"] = rightVideoIdentity;
            videoParams["allow_grouped"] = true;
            Dictionary<string, object> audioParams = CloneDictionary(p);
            audioParams["left_event"] = leftAudioIdentity;
            audioParams["right_event"] = rightAudioIdentity;
            audioParams["allow_grouped"] = true;
            object videoResult = CreateCrossfade(vegas, videoParams);
            object audioResult = CreateCrossfade(vegas, audioParams);
            return new Dictionary<string, object> { { "video", videoResult }, { "audio", audioResult }, { "linked", true } };
        }

        private static object CloseGaps(Vegas vegas, Dictionary<string, object> p)
        {
            Track track = GetTrack(vegas.Project, RequireInt(p, "track_index"));
            GuardTrackName(track, p);
            double minimumGap = GetDouble(p, "minimum_gap_seconds", 0.000001);
            if (minimumGap < 0) throw new InvalidOperationException("minimum_gap_seconds cannot be negative.");
            List<TrackEvent> events = new List<TrackEvent>();
            foreach (TrackEvent ev in track.Events)
            {
                if (GetBool(p, "selected_only", false) && !ev.Selected) continue;
                if (ev.IsGrouped)
                    throw new InvalidOperationException("close_gaps intentionally refuses grouped events because moving one track would break A/V synchronization.");
                events.Add(ev);
            }
            events.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.CompareTo(b.Start); });
            List<object> moved = new List<object>();
            if (events.Count > 0)
            {
                Timecode cursor = HasAnyTime(p, "start") ? RequireTime(vegas.Project, p, "start", true) : events[0].Start;
                foreach (TrackEvent ev in events)
                {
                    double gap = Seconds(ev.Start - cursor);
                    if (gap >= minimumGap)
                    {
                        ev.Start = cursor;
                        moved.Add(EventData(ev, false));
                    }
                    if (ev.End > cursor) cursor = ev.End;
                }
            }
            Dictionary<string, object> result = Result("moved_events", moved);
            result["moved_count"] = moved.Count;
            return result;
        }

        private static object EditMarker(Vegas vegas, Dictionary<string, object> p)
        {
            Marker marker = GetMarker(vegas.Project, RequireInt(p, "marker_index"));
            GuardMarkerPosition(marker, p);
            if (HasAnyTime(p, "position")) marker.Position = RequireTime(vegas.Project, p, "position", true);
            if (Has(p, "label")) marker.Label = Convert.ToString(p["label"]);
            return Result("marker", MarkerItem(marker));
        }

        private static object DeleteMarker(Vegas vegas, Dictionary<string, object> p)
        {
            Marker marker = GetMarker(vegas.Project, RequireInt(p, "marker_index"));
            GuardMarkerPosition(marker, p);
            Dictionary<string, object> removed = MarkerItem(marker);
            vegas.Project.Markers.Remove(marker);
            return Result("deleted_marker", removed);
        }

        private static object EditRegion(Vegas vegas, Dictionary<string, object> p)
        {
            Region region = GetRegion(vegas.Project, RequireInt(p, "region_index"));
            GuardRegionIdentity(region, p);
            if (HasAnyTime(p, "position")) region.Position = RequireTime(vegas.Project, p, "position", true);
            if (HasAnyTime(p, "length")) region.Length = RequireTime(vegas.Project, p, "length", false);
            if (region.Position.Nanos < 0 || region.Length.Nanos <= 0) throw new InvalidOperationException("Region position or length is invalid.");
            if (Has(p, "label")) region.Label = Convert.ToString(p["label"]);
            return Result("region", RegionItem(region));
        }

        private static object DeleteRegion(Vegas vegas, Dictionary<string, object> p)
        {
            Region region = GetRegion(vegas.Project, RequireInt(p, "region_index"));
            GuardRegionIdentity(region, p);
            Dictionary<string, object> removed = RegionItem(region);
            vegas.Project.Regions.Remove(region);
            return Result("deleted_region", removed);
        }

        private static object ExecuteBatch(Vegas vegas, Dictionary<string, object> p)
        {
            if (!GetBool(p, "allow_batch", false)) throw new InvalidOperationException("Batch apply requires allow_batch=true.");
            List<Dictionary<string, object>> operations = ReadOperations(p);
            List<object> results = new List<object>();
            foreach (Dictionary<string, object> operation in operations)
            {
                string action = RequireString(operation, "action");
                if (action.IndexOf('.') < 0) action = "timeline." + action;
                if (!IsBatchMutationAllowed(action)) throw new InvalidOperationException("Action is not permitted inside a content batch: " + action);
                Dictionary<string, object> parameters = Has(operation, "params") ? operation["params"] as Dictionary<string, object> : new Dictionary<string, object>();
                if (parameters == null) throw new InvalidOperationException("Each batch params value must be an object.");
                results.Add(new Dictionary<string, object> { { "action", action }, { "data", ExecuteMutation(vegas, action, parameters) } });
            }
            Dictionary<string, object> result = Result("operations", results);
            result["operation_count"] = results.Count;
            return result;
        }

        private static object PreflightBatch(Vegas vegas, Dictionary<string, object> p)
        {
            List<Dictionary<string, object>> operations = ReadOperations(p);
            List<object> planned = new List<object>();
            foreach (Dictionary<string, object> operation in operations)
            {
                string action = RequireString(operation, "action");
                if (action.IndexOf('.') < 0) action = "timeline." + action;
                if (!IsBatchMutationAllowed(action)) throw new InvalidOperationException("Action is not permitted inside a content batch: " + action);
                Dictionary<string, object> parameters = Has(operation, "params") ? operation["params"] as Dictionary<string, object> : new Dictionary<string, object>();
                if (parameters == null) throw new InvalidOperationException("Each batch params value must be an object.");
                List<object> guarded = new List<object>();
                if (Has(parameters, "event_id")) guarded.Add(EventData(GuardEvent(vegas.Project, parameters), false));
                if (Has(parameters, "events")) foreach (TrackEvent ev in GuardEventList(vegas.Project, parameters)) guarded.Add(EventData(ev, false));
                if (Has(parameters, "left_event"))
                {
                    Dictionary<string, object> identity = parameters["left_event"] as Dictionary<string, object>;
                    if (identity == null) throw new InvalidOperationException("left_event must be an object.");
                    guarded.Add(EventData(GuardEvent(vegas.Project, identity), false));
                }
                if (Has(parameters, "right_event"))
                {
                    Dictionary<string, object> identity = parameters["right_event"] as Dictionary<string, object>;
                    if (identity == null) throw new InvalidOperationException("right_event must be an object.");
                    guarded.Add(EventData(GuardEvent(vegas.Project, identity), false));
                }
                if (Has(parameters, "track_index")) GetTrack(vegas.Project, RequireInt(parameters, "track_index"));
                planned.Add(new Dictionary<string, object> { { "action", action }, { "guarded_events", guarded } });
            }
            Dictionary<string, object> result = Result("operations", planned);
            result["operation_count"] = planned.Count;
            result["dry_run"] = true;
            result["preflight_scope"] = "action allowlist, target existence, and supplied event identity checks; action-specific value validation occurs during apply";
            return result;
        }

        private static List<Dictionary<string, object>> ReadOperations(Dictionary<string, object> p)
        {
            object raw = Require(p, "operations");
            IEnumerable values = raw as IEnumerable;
            if (values == null || raw is string) throw new InvalidOperationException("operations must be an array.");
            List<Dictionary<string, object>> operations = new List<Dictionary<string, object>>();
            foreach (object value in values)
            {
                Dictionary<string, object> item = value as Dictionary<string, object>;
                if (item == null) throw new InvalidOperationException("Each batch operation must be an object.");
                operations.Add(item);
                if (operations.Count > 100) throw new InvalidOperationException("A batch may contain at most 100 operations.");
            }
            if (operations.Count == 0) throw new InvalidOperationException("operations must contain at least one operation.");
            return operations;
        }

        private static bool IsBatchMutationAllowed(string action)
        {
            if (action == "timeline.batch") return false;
            string[] disallowed = new string[] {
                "timeline.inspect", "timeline.find_events", "timeline.clear_selection", "timeline.select_events",
                "audio.analyze_loudness", "audio.inspect_envelopes", "audio.list_buses",
                "effects.list_plugins", "effects.list_presets", "effects.list_event_effects", "effects.list_track_effects", "effects.list_bus_effects",
                "media.inspect", "media.list", "media.list_takes", "motion.inspect_event_motion", "motion.inspect_track_motion"
            };
            foreach (string value in disallowed) if (action == value) return false;
            return action.StartsWith("timeline.", StringComparison.Ordinal) ||
                   action.StartsWith("audio.", StringComparison.Ordinal) ||
                   action.StartsWith("effects.", StringComparison.Ordinal) ||
                   action.StartsWith("media.", StringComparison.Ordinal) ||
                   action.StartsWith("motion.", StringComparison.Ordinal);
        }

        private static Track ResolveTrackByOptionalIndex(Project project, Dictionary<string, object> p, string key, bool audio)
        {
            if (!Has(p, key)) return null;
            Track track = GetTrack(project, RequireInt(p, key));
            if (track.IsAudio() != audio) throw new InvalidOperationException(key + " points to the wrong track type.");
            return track;
        }

        private static VideoTrack ResolveOrCreateVideoTrack(Project project, Dictionary<string, object> p, string key, bool create)
        {
            Track requested = ResolveTrackByOptionalIndex(project, p, key, false);
            if (requested != null) return requested as VideoTrack;
            if (!create) throw new InvalidOperationException("Provide " + key + " or set create_missing_tracks=true.");
            return project.AddVideoTrack();
        }

        private static AudioTrack ResolveOrCreateAudioTrack(Project project, Dictionary<string, object> p, string key, bool create)
        {
            Track requested = ResolveTrackByOptionalIndex(project, p, key, true);
            if (requested != null) return requested as AudioTrack;
            if (!create) throw new InvalidOperationException("Provide " + key + " or set create_missing_tracks=true.");
            return project.AddAudioTrack();
        }

        private static Dictionary<string, object> TrackWithEventsData(Track track, bool includeEffects)
        {
            Dictionary<string, object> data = TrackData(track);
            List<object> events = new List<object>();
            foreach (TrackEvent ev in track.Events) events.Add(EventData(ev, includeEffects));
            data["events"] = events;
            return data;
        }

        private static void GuardTrackName(Track track, Dictionary<string, object> p)
        {
            if (Has(p, "expected_name") && !String.Equals(track.Name, RequireString(p, "expected_name"), StringComparison.Ordinal))
                throw new InvalidOperationException("Track name changed since the probe.");
        }

        private static Marker GetMarker(Project project, int index)
        {
            if (index < 0 || index >= project.Markers.Count) throw new InvalidOperationException("marker_index is outside the marker range.");
            return project.Markers[index];
        }

        private static void GuardMarkerPosition(Marker marker, Dictionary<string, object> p)
        {
            if (Has(p, "expected_position_frames"))
            {
                if (marker.Position.FrameCount != RequireLong(p, "expected_position_frames")) throw new InvalidOperationException("Marker position changed since the probe.");
            }
            else if (Math.Abs(Seconds(marker.Position) - RequireDouble(p, "expected_position_seconds")) > TimeToleranceSeconds)
                throw new InvalidOperationException("Marker position changed since the probe.");
        }

        private static Region GetRegion(Project project, int index)
        {
            if (index < 0 || index >= project.Regions.Count) throw new InvalidOperationException("region_index is outside the region range.");
            return project.Regions[index];
        }

        private static void GuardRegionIdentity(Region region, Dictionary<string, object> p)
        {
            if (Has(p, "expected_position_frames"))
            {
                if (region.Position.FrameCount != RequireLong(p, "expected_position_frames")) throw new InvalidOperationException("Region position changed since the probe.");
            }
            else if (Math.Abs(Seconds(region.Position) - RequireDouble(p, "expected_position_seconds")) > TimeToleranceSeconds)
                throw new InvalidOperationException("Region position changed since the probe.");
            if (Has(p, "expected_length_frames"))
            {
                if (region.Length.FrameCount != RequireLong(p, "expected_length_frames")) throw new InvalidOperationException("Region length changed since the probe.");
            }
            else if (Math.Abs(Seconds(region.Length) - RequireDouble(p, "expected_length_seconds")) > TimeToleranceSeconds)
                throw new InvalidOperationException("Region length changed since the probe.");
        }

        private static Dictionary<string, object> MarkerItem(Marker marker)
        {
            return new Dictionary<string, object> { { "index", marker.Index }, { "position_seconds", Seconds(marker.Position) }, { "position_frames", marker.Position.FrameCount }, { "label", marker.Label } };
        }

        private static Dictionary<string, object> RegionItem(Region region)
        {
            return new Dictionary<string, object> { { "index", region.Index }, { "position_seconds", Seconds(region.Position) }, { "position_frames", region.Position.FrameCount }, { "length_seconds", Seconds(region.Length) }, { "length_frames", region.Length.FrameCount }, { "label", region.Label } };
        }

        private static HashSet<int> ReadIntSet(Dictionary<string, object> p, string key)
        {
            object raw = Require(p, key);
            IEnumerable values = raw as IEnumerable;
            if (values == null || raw is string) throw new InvalidOperationException(key + " must be an array.");
            HashSet<int> result = new HashSet<int>();
            foreach (object value in values) result.Add(Convert.ToInt32(value));
            return result;
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
            if (supplied == 0) throw new InvalidOperationException("Provide exactly one of " + secondsKey + ", " + framesKey + ", or " + timecodeKey + ".");
            if (supplied > 1) throw new InvalidOperationException("Provide only one time representation for " + prefix + ".");
            if (Has(p, secondsKey)) return ProjectTimecode.FromSeconds(project, RequireDouble(p, secondsKey));
            if (Has(p, framesKey)) return ProjectTimecode.FromFrames(project, RequireLong(p, framesKey));
            string value = RequireString(p, timecodeKey);
            return position ? ProjectTimecode.FromPositionString(project, value) : ProjectTimecode.FromString(project, value);
        }
    }
}
