using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static partial class VegasActions
    {
        private static object InspectMedia(Vegas vegas, Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(vegas.Project, p);
            Media media = ResolveMedia(vegas.Project, p);
            return Result("media", MediaData(media));
        }

        private static object ListMedia(Vegas vegas, Dictionary<string, object> p)
        {
            if (Has(p, "project_path")) GuardProject(vegas.Project, p);
            string contains = GetString(p, "contains", null);
            int limit = GetInt(p, "limit", 1000);
            if (limit < 1 || limit > 10000) throw new InvalidOperationException("limit must be between 1 and 10000.");
            List<object> mediaItems = new List<object>();
            foreach (Media media in EnumerateProjectMedia(vegas.Project))
            {
                string searchable = (media.FilePath ?? "") + " " + (media.Title ?? "");
                if (!String.IsNullOrWhiteSpace(contains) && searchable.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                mediaItems.Add(MediaData(media));
                if (mediaItems.Count >= limit) break;
            }
            Dictionary<string, object> result = Result("media", mediaItems);
            result["count"] = mediaItems.Count;
            result["bins"] = MediaBinData(vegas.Project.MediaPool.RootMediaBin);
            return result;
        }

        private static object CreateMediaBin(Vegas vegas, Dictionary<string, object> p)
        {
            string name = RequireString(p, "name");
            MediaBin parent = Has(p, "parent_bin_id")
                ? FindMediaBin(vegas.Project.MediaPool.RootMediaBin, Convert.ToUInt32(RequireLong(p, "parent_bin_id")))
                : vegas.Project.MediaPool.RootMediaBin;
            if (parent == null) throw new InvalidOperationException("The parent media bin was not found.");
            foreach (IMediaBinNode node in parent)
            {
                MediaBin existing = node as MediaBin;
                if (existing != null && String.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A media bin with that name already exists under the selected parent.");
            }
            MediaBin bin = new MediaBin(vegas.Project, name);
            parent.Add(bin);
            if (Has(p, "media_paths"))
            {
                foreach (string path in ReadStringList(p, "media_paths"))
                {
                    Dictionary<string, object> identity = new Dictionary<string, object>();
                    identity["media_path"] = path;
                    Media media = ResolveMedia(vegas.Project, identity);
                    bin.Add(media);
                }
            }
            return Result("bin", MediaBinData(bin));
        }

        private static object InsertImageSequence(Vegas vegas, Dictionary<string, object> p)
        {
            string firstImage = Path.GetFullPath(RequireString(p, "first_image_path"));
            if (!File.Exists(firstImage)) throw new FileNotFoundException("The first image in the sequence was not found.", firstImage);
            int imageCount = RequireInt(p, "image_count");
            double frameRate = RequireDouble(p, "frame_rate");
            if (imageCount < 1 || imageCount > 1000000) throw new InvalidOperationException("image_count is outside the supported range.");
            if (frameRate < 1 || frameRate > 240) throw new InvalidOperationException("frame_rate must be between 1 and 240.");
            VideoTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as VideoTrack;
            if (track == null) throw new InvalidOperationException("Image sequences require a video track.");
            Media media = vegas.Project.MediaPool.AddImageSequence(firstImage, imageCount, frameRate);
            VideoStream stream = media.GetVideoStreamByIndex(GetInt(p, "stream_index", 0));
            Timecode start = RequireTime(vegas.Project, p, "start", true);
            Timecode length = HasAnyTime(p, "length") ? RequireTime(vegas.Project, p, "length", false) : stream.Length;
            if (start.Nanos < 0 || length.Nanos <= 0) throw new InvalidOperationException("Image-sequence start and length are invalid.");
            VideoEvent ev = track.AddVideoEvent(start, length);
            ev.AddTake(stream, true, GetString(p, "take_name", Path.GetFileName(firstImage)));
            if (Has(p, "name")) ev.Name = RequireString(p, "name");
            return Result("event", EventData(ev, true));
        }

        private static object ListTakes(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            TrackEvent ev = FindEvent(vegas.Project, RequireLong(p, "event_id"));
            Dictionary<string, object> result = Result("event", EventData(ev, false));
            result["takes"] = TakeDataList(ev);
            return result;
        }

        private static object AddTake(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            string sourcePath = Path.GetFullPath(RequireString(p, "source_path"));
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Take source media was not found.", sourcePath);
            Media media = Media.CreateInstance(vegas.Project, sourcePath);
            int streamIndex = GetInt(p, "stream_index", 0);
            MediaStream stream;
            if (ev.IsAudio())
            {
                if (!media.HasAudio()) throw new InvalidOperationException("The source has no audio stream.");
                stream = media.GetAudioStreamByIndex(streamIndex);
            }
            else
            {
                if (!media.HasVideo()) throw new InvalidOperationException("The source has no video stream.");
                stream = media.GetVideoStreamByIndex(streamIndex);
            }
            Take take = ev.AddTake(stream, GetBool(p, "make_active", true), GetString(p, "name", Path.GetFileName(sourcePath)));
            if (HasAnyTime(p, "offset"))
            {
                Timecode offset = RequireTime(vegas.Project, p, "offset", false);
                if (offset.Nanos < 0) throw new InvalidOperationException("Take offset cannot be negative.");
                take.Offset = offset;
            }
            Dictionary<string, object> result = Result("event", EventData(ev, false));
            result["takes"] = TakeDataList(ev);
            result["added_take_index"] = take.Index;
            return result;
        }

        private static object SetActiveTake(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            int index = RequireInt(p, "take_index");
            Take take = GetTake(ev, index);
            if (Has(p, "expected_media_path") && !PathEquals(take.MediaPath, RequireString(p, "expected_media_path")))
                throw new InvalidOperationException("The selected take media changed since the probe.");
            ev.ActiveTake = take;
            Dictionary<string, object> result = Result("event", EventData(ev, false));
            result["takes"] = TakeDataList(ev);
            return result;
        }

        private static object RemoveTake(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            if (ev.Takes.Count <= 1) throw new InvalidOperationException("The last take on an event cannot be removed.");
            int index = RequireInt(p, "take_index");
            Take take = GetTake(ev, index);
            if (Has(p, "expected_media_path") && !PathEquals(take.MediaPath, RequireString(p, "expected_media_path")))
                throw new InvalidOperationException("The selected take media changed since the probe.");
            Dictionary<string, object> removed = TakeData(take, ev.IsAudio());
            if (take.IsActive)
            {
                int replacementIndex = index == 0 ? 1 : 0;
                ev.ActiveTake = ev.Takes[replacementIndex];
            }
            ev.Takes.RemoveAt(index);
            Dictionary<string, object> result = Result("event", EventData(ev, false));
            result["removed_take"] = removed;
            result["takes"] = TakeDataList(ev);
            return result;
        }

        private static object ReplaceEventMedia(Vegas vegas, Dictionary<string, object> p)
        {
            TrackEvent ev = GuardEvent(vegas.Project, p);
            Take original = ev.ActiveTake;
            if (original == null) throw new InvalidOperationException("The event has no active take to replace.");
            string sourcePath = Path.GetFullPath(RequireString(p, "source_path"));
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Replacement media was not found.", sourcePath);
            Media media = Media.CreateInstance(vegas.Project, sourcePath);
            int streamIndex = GetInt(p, "stream_index", 0);
            MediaStream stream;
            if (ev.IsAudio())
            {
                if (!media.HasAudio()) throw new InvalidOperationException("Replacement media has no audio stream.");
                stream = media.GetAudioStreamByIndex(streamIndex);
            }
            else
            {
                if (!media.HasVideo()) throw new InvalidOperationException("Replacement media has no video stream.");
                stream = media.GetVideoStreamByIndex(streamIndex);
            }
            Take replacement = ev.AddTake(stream, true, GetString(p, "name", Path.GetFileName(sourcePath)));
            if (GetBool(p, "preserve_offset", true) && original != null) replacement.Offset = original.Offset;
            if (HasAnyTime(p, "offset"))
            {
                Timecode offset = RequireTime(vegas.Project, p, "offset", false);
                if (offset.Nanos < 0) throw new InvalidOperationException("Replacement take offset cannot be negative.");
                replacement.Offset = offset;
            }
            if (GetBool(p, "remove_previous_take", false))
            {
                if (!GetBool(p, "allow_take_removal", false)) throw new InvalidOperationException("Removing the previous take requires allow_take_removal=true.");
                int originalIndex = original.Index;
                ev.Takes.RemoveAt(originalIndex);
            }
            Dictionary<string, object> result = Result("event", EventData(ev, false));
            result["takes"] = TakeDataList(ev);
            result["replacement_take_index"] = replacement.Index;
            return result;
        }

        private static object InspectEventMotion(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            VideoEvent ev = FindEvent(vegas.Project, RequireLong(p, "event_id")) as VideoEvent;
            if (ev == null) throw new InvalidOperationException("The target event is not video.");
            return Result("motion", EventMotionData(ev));
        }

        private static object SetEventMotionKeyframe(Vegas vegas, Dictionary<string, object> p)
        {
            VideoEvent ev = GuardEvent(vegas.Project, p) as VideoEvent;
            if (ev == null) throw new InvalidOperationException("The target event is not video.");
            Timecode position = RequireTime(vegas.Project, p, "position", false);
            if (position.Nanos < 0 || position > ev.Length) throw new InvalidOperationException("Event-motion keyframe position is outside the event.");
            VideoMotionKeyframe keyframe = FindEventMotionKeyframe(ev, position);
            if (keyframe == null)
            {
                keyframe = new VideoMotionKeyframe(vegas.Project, position);
                ev.VideoMotion.Keyframes.Add(keyframe);
            }
            if (Has(p, "interpolation")) keyframe.Type = ParseEnum<VideoKeyframeType>(RequireString(p, "interpolation"));
            if (Has(p, "smoothness")) keyframe.Smoothness = Convert.ToSingle(RequireDouble(p, "smoothness"));
            if (Has(p, "center_x") || Has(p, "center_y"))
            {
                VideoMotionVertex center = keyframe.Center;
                keyframe.Center = new VideoMotionVertex(
                    Convert.ToSingle(GetDouble(p, "center_x", center.X)),
                    Convert.ToSingle(GetDouble(p, "center_y", center.Y)));
            }
            if (Has(p, "move_x") || Has(p, "move_y"))
                keyframe.MoveBy(new VideoMotionVertex(Convert.ToSingle(GetDouble(p, "move_x", 0)), Convert.ToSingle(GetDouble(p, "move_y", 0))));
            if (Has(p, "scale_x") || Has(p, "scale_y"))
            {
                double x = GetDouble(p, "scale_x", 1);
                double y = GetDouble(p, "scale_y", 1);
                if (x <= 0 || y <= 0) throw new InvalidOperationException("Event-motion scale values must be positive.");
                keyframe.ScaleBy(new VideoMotionVertex(Convert.ToSingle(x), Convert.ToSingle(y)));
            }
            if (Has(p, "rotate_degrees")) keyframe.RotateBy(RequireDouble(p, "rotate_degrees") * Math.PI / 180.0);
            if (Has(p, "bounds")) keyframe.Bounds = ReadMotionBounds(p["bounds"]);
            if (Has(p, "scale_to_fill")) ev.VideoMotion.ScaleToFill = RequireBool(p, "scale_to_fill");
            return Result("motion", EventMotionData(ev));
        }

        private static object ClearEventMotionKeyframes(Vegas vegas, Dictionary<string, object> p)
        {
            VideoEvent ev = GuardEvent(vegas.Project, p) as VideoEvent;
            if (ev == null) throw new InvalidOperationException("The target event is not video.");
            ev.VideoMotion.Keyframes.Clear();
            return Result("motion", EventMotionData(ev));
        }

        private static object InspectTrackMotion(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            VideoTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as VideoTrack;
            if (track == null) throw new InvalidOperationException("The target track is not video.");
            // The 2027 API calls native TrackMotionInitialize even from the getter.
            // It requires an Undo context in application extensions. Cancel the
            // temporary context so inspection adds no Undo entry or project edit.
            using (UndoBlock undo = new UndoBlock("VEGAS MCP: inspect track motion"))
            {
                try { return Result("motion", TrackMotionData(track)); }
                finally { undo.Cancel = true; }
            }
        }

        private static object SetTrackMotionKeyframe(Vegas vegas, Dictionary<string, object> p)
        {
            VideoTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as VideoTrack;
            if (track == null) throw new InvalidOperationException("The target track is not video.");
            GuardTrackName(track, p);
            Timecode position = RequireTime(vegas.Project, p, "position", true);
            if (position.Nanos < 0) throw new InvalidOperationException("Track-motion keyframe position cannot be negative.");
            TrackMotionKeyframe keyframe = FindTrackMotionKeyframe(track, position);
            if (keyframe == null) keyframe = track.TrackMotion.InsertMotionKeyframe(position);
            if (Has(p, "interpolation")) keyframe.Type = ParseEnum<VideoKeyframeType>(RequireString(p, "interpolation"));
            if (Has(p, "smoothness")) keyframe.Smoothness = RequireDouble(p, "smoothness");
            if (Has(p, "position_x")) keyframe.PositionX = RequireDouble(p, "position_x");
            if (Has(p, "position_y")) keyframe.PositionY = RequireDouble(p, "position_y");
            if (Has(p, "position_z")) keyframe.PositionZ = RequireDouble(p, "position_z");
            if (Has(p, "width")) keyframe.Width = RequireDouble(p, "width");
            if (Has(p, "height")) keyframe.Height = RequireDouble(p, "height");
            if (Has(p, "depth")) keyframe.Depth = RequireDouble(p, "depth");
            if (Has(p, "rotation_x")) keyframe.RotationX = RequireDouble(p, "rotation_x");
            if (Has(p, "rotation_y")) keyframe.RotationY = RequireDouble(p, "rotation_y");
            if (Has(p, "rotation_z")) keyframe.RotationZ = RequireDouble(p, "rotation_z");
            return Result("motion", TrackMotionData(track));
        }

        private static object ClearTrackMotionKeyframes(Vegas vegas, Dictionary<string, object> p)
        {
            VideoTrack track = GetTrack(vegas.Project, RequireInt(p, "track_index")) as VideoTrack;
            if (track == null) throw new InvalidOperationException("The target track is not video.");
            GuardTrackName(track, p);
            track.TrackMotion.MotionKeyframes.Clear();
            return Result("motion", TrackMotionData(track));
        }

        private static Media ResolveMedia(Project project, Dictionary<string, object> p)
        {
            uint? mediaId = Has(p, "media_id") ? (uint?)Convert.ToUInt32(RequireLong(p, "media_id")) : null;
            string path = Has(p, "media_path") ? Path.GetFullPath(RequireString(p, "media_path")) : null;
            if (mediaId == null && path == null) throw new InvalidOperationException("Provide media_id or media_path.");
            if (path != null)
            {
                Media foundByPath = project.MediaPool.Find(path);
                if (foundByPath != null) return foundByPath;
            }
            foreach (Media media in EnumerateProjectMedia(project))
            {
                if (mediaId.HasValue && media.MediaID == mediaId.Value) return media;
                if (path != null && !String.IsNullOrWhiteSpace(media.FilePath) && PathEquals(media.FilePath, path)) return media;
            }
            throw new InvalidOperationException("Project media was not found.");
        }

        private static List<Media> EnumerateProjectMedia(Project project)
        {
            List<Media> result = new List<Media>();
            HashSet<uint> seen = new HashSet<uint>();
            try
            {
                IDictionaryEnumerator entries = project.MediaPool.GetEnumerator();
                while (entries.MoveNext())
                {
                    Media media = entries.Value as Media;
                    if (media != null && seen.Add(media.MediaID)) result.Add(media);
                }
            }
            catch (NotSupportedException)
            {
                CollectMedia(project.MediaPool.RootMediaBin, result, seen);
            }
            return result;
        }

        private static void CollectMedia(MediaBin bin, List<Media> result, HashSet<uint> seen)
        {
            foreach (IMediaBinNode node in bin)
            {
                Media media = node as Media;
                if (media != null)
                {
                    if (seen.Add(media.MediaID)) result.Add(media);
                    continue;
                }
                MediaBin child = node as MediaBin;
                if (child != null) CollectMedia(child, result, seen);
            }
        }

        private static Dictionary<string, object> MediaData(Media media)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["media_id"] = media.MediaID;
            data["node_id"] = media.NodeID;
            data["file_path"] = media.FilePath;
            data["title"] = media.Title;
            data["comment"] = media.Comment;
            data["length_seconds"] = Seconds(media.Length);
            data["use_count"] = media.UseCount;
            data["has_audio"] = media.HasAudio();
            data["has_video"] = media.HasVideo();
            data["is_generated"] = media.IsGenerated();
            data["is_image_sequence"] = media.IsImageSequence();
            data["is_offline"] = media.IsOffline();
            List<object> streams = new List<object>();
            foreach (MediaStream stream in media.Streams)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["index"] = stream.Index;
                item["kind"] = stream.MediaType.ToString();
                item["length_seconds"] = Seconds(stream.Length);
                item["format"] = stream.FormatFourCCString;
                item["offline"] = stream.IsOffline;
                AudioStream audio = stream as AudioStream;
                if (audio != null)
                {
                    item["channels"] = audio.Channels;
                    item["sample_rate"] = audio.SampleRate;
                    item["bit_depth"] = audio.BitDepth;
                    item["audio_format"] = audio.Format;
                }
                VideoStream video = stream as VideoStream;
                if (video != null)
                {
                    item["width"] = video.Width;
                    item["height"] = video.Height;
                    item["frame_rate"] = video.FrameRate;
                }
                streams.Add(item);
            }
            data["streams"] = streams;
            data["effects"] = EffectData(media.Effects);
            return data;
        }

        private static Dictionary<string, object> MediaBinData(MediaBin bin)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["node_id"] = bin.NodeID;
            data["name"] = bin.Name;
            List<object> children = new List<object>();
            foreach (IMediaBinNode node in bin)
            {
                MediaBin child = node as MediaBin;
                if (child != null) children.Add(MediaBinData(child));
                else
                {
                    Media media = node as Media;
                    if (media != null) children.Add(new Dictionary<string, object> { { "node_id", media.NodeID }, { "media_id", media.MediaID }, { "file_path", media.FilePath }, { "kind", "media" } });
                }
            }
            data["children"] = children;
            return data;
        }

        private static MediaBin FindMediaBin(MediaBin bin, uint nodeId)
        {
            if (bin.NodeID == nodeId) return bin;
            foreach (IMediaBinNode node in bin)
            {
                MediaBin child = node as MediaBin;
                if (child == null) continue;
                MediaBin found = FindMediaBin(child, nodeId);
                if (found != null) return found;
            }
            return null;
        }

        private static List<string> ReadStringList(Dictionary<string, object> p, string key)
        {
            object raw = Require(p, key);
            IEnumerable values = raw as IEnumerable;
            if (values == null || raw is string) throw new InvalidOperationException(key + " must be an array.");
            List<string> result = new List<string>();
            foreach (object value in values) result.Add(Convert.ToString(value));
            return result;
        }

        private static bool PathEquals(string left, string right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right)) return false;
            return String.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static Take GetTake(TrackEvent ev, int index)
        {
            if (index < 0 || index >= ev.Takes.Count) throw new InvalidOperationException("take_index is outside the event take range.");
            return ev.Takes[index];
        }

        private static List<object> TakeDataList(TrackEvent ev)
        {
            List<object> result = new List<object>();
            bool audio = ev.IsAudio();
            foreach (Take take in ev.Takes) result.Add(TakeData(take, audio));
            return result;
        }

        private static Dictionary<string, object> TakeData(Take take, bool audio)
        {
            Dictionary<string, object> result = new Dictionary<string, object> {
                { "index", take.Index }, { "name", take.Name }, { "is_active", take.IsActive },
                { "media_path", take.MediaPath }, { "stream_index", take.StreamIndex },
                { "offset_seconds", Seconds(take.Offset) }, { "length_seconds", Seconds(take.Length) },
                { "available_length_seconds", Seconds(take.AvailableLength) }
            };
            if (audio)
            {
                result["first_audio_channel_index"] = take.FirstAudioChannelIndex;
                result["audio_channel_count"] = take.AudioChannelCount;
            }
            return result;
        }

        private static VideoMotionKeyframe FindEventMotionKeyframe(VideoEvent ev, Timecode position)
        {
            foreach (VideoMotionKeyframe keyframe in ev.VideoMotion.Keyframes)
                if (Math.Abs(Seconds(keyframe.Position - position)) <= TimeToleranceSeconds) return keyframe;
            return null;
        }

        private static TrackMotionKeyframe FindTrackMotionKeyframe(VideoTrack track, Timecode position)
        {
            foreach (TrackMotionKeyframe keyframe in track.TrackMotion.MotionKeyframes)
                if (Math.Abs(Seconds(keyframe.Position - position)) <= TimeToleranceSeconds) return keyframe;
            return null;
        }

        private static Dictionary<string, object> EventMotionData(VideoEvent ev)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["event_id"] = ev.EventID.ToString();
            data["scale_to_fill"] = ev.VideoMotion.ScaleToFill;
            List<object> keyframes = new List<object>();
            int keyframeIndex = 0;
            foreach (VideoMotionKeyframe keyframe in ev.VideoMotion.Keyframes)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["index"] = keyframeIndex++;
                item["position_seconds"] = Seconds(keyframe.Position);
                item["position_frames"] = keyframe.Position.FrameCount;
                item["interpolation"] = keyframe.Type.ToString();
                item["smoothness"] = keyframe.Smoothness;
                item["rotation_radians"] = keyframe.Rotation;
                item["rotation_degrees"] = keyframe.Rotation * 180.0 / Math.PI;
                item["center"] = VertexData(keyframe.Center);
                item["bounds"] = BoundsData(keyframe.Bounds);
                keyframes.Add(item);
            }
            data["keyframes"] = keyframes;
            return data;
        }

        private static Dictionary<string, object> TrackMotionData(VideoTrack track)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["track_index"] = track.Index;
            data["track_name"] = track.Name;
            TrackMotion motion = track.TrackMotion;
            data["has_motion_data"] = motion.HasMotionData;
            TrackMotionScaleFactors scales = motion.ScaleFactors;
            data["scale_factors"] = new Dictionary<string, object> { { "x", scales.X }, { "y", scales.Y }, { "z", scales.Z }, { "width", scales.W }, { "height", scales.H }, { "depth", scales.D }, { "angle", scales.A }, { "percent", scales.P } };
            List<object> keyframes = new List<object>();
            foreach (TrackMotionKeyframe keyframe in motion.MotionKeyframes)
            {
                keyframes.Add(new Dictionary<string, object> {
                    { "index", keyframe.Index }, { "position_seconds", Seconds(keyframe.Position) }, { "position_frames", keyframe.Position.FrameCount },
                    { "interpolation", keyframe.Type.ToString() }, { "smoothness", keyframe.Smoothness },
                    { "position_x", keyframe.PositionX }, { "position_y", keyframe.PositionY }, { "position_z", keyframe.PositionZ },
                    { "width", keyframe.Width }, { "height", keyframe.Height }, { "depth", keyframe.Depth },
                    { "rotation_x", keyframe.RotationX }, { "rotation_y", keyframe.RotationY }, { "rotation_z", keyframe.RotationZ }
                });
            }
            data["keyframes"] = keyframes;
            return data;
        }

        private static Dictionary<string, object> VertexData(VideoMotionVertex vertex)
        {
            return new Dictionary<string, object> { { "x", vertex.X }, { "y", vertex.Y } };
        }

        private static Dictionary<string, object> BoundsData(VideoMotionBounds bounds)
        {
            return new Dictionary<string, object> { { "top_left", VertexData(bounds.TopLeft) }, { "top_right", VertexData(bounds.TopRight) }, { "bottom_right", VertexData(bounds.BottomRight) }, { "bottom_left", VertexData(bounds.BottomLeft) } };
        }

        private static VideoMotionBounds ReadMotionBounds(object raw)
        {
            Dictionary<string, object> data = raw as Dictionary<string, object>;
            if (data == null) throw new InvalidOperationException("bounds must be an object containing four vertices.");
            VideoMotionVertex topLeft = ReadVertex(data, "top_left");
            VideoMotionVertex topRight = ReadVertex(data, "top_right");
            VideoMotionVertex bottomRight = ReadVertex(data, "bottom_right");
            VideoMotionVertex bottomLeft = ReadVertex(data, "bottom_left");
            return new VideoMotionBounds(topLeft, topRight, bottomRight, bottomLeft);
        }

        private static VideoMotionVertex ReadVertex(Dictionary<string, object> data, string key)
        {
            object raw = Require(data, key);
            Dictionary<string, object> vertex = raw as Dictionary<string, object>;
            if (vertex == null) throw new InvalidOperationException(key + " must be an object with x and y.");
            return new VideoMotionVertex(Convert.ToSingle(RequireDouble(vertex, "x")), Convert.ToSingle(RequireDouble(vertex, "y")));
        }
    }
}
