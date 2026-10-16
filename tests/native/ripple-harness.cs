using System;
using System.Collections.Generic;

// Deliberately small model. Native media, undo, grouping and plug-in behavior
// still require tests inside VEGAS on a disposable project.
class Timecode : IComparable<Timecode>
{
    public long Nanos;
    public Timecode(long value) { Nanos = value; }
    public int CompareTo(Timecode other) { return Nanos.CompareTo(other.Nanos); }
    public static Timecode operator +(Timecode a, Timecode b) { return new Timecode(a.Nanos + b.Nanos); }
    public static Timecode operator -(Timecode a, Timecode b) { return new Timecode(a.Nanos - b.Nanos); }
}
class TrackEvent
{
    public Track Track;
    public Timecode Start, Length;
    public long SourceOffset;
    public bool Locked, IsGrouped;
    public Timecode End { get { return Start + Length; } }
    public TrackEvent Split(Timecode offset)
    {
        if (offset.Nanos <= 0 || offset.Nanos >= Length.Nanos) throw new Exception("Invalid split");
        TrackEvent right = new TrackEvent { Track = Track, Start = Start + offset, Length = Length - offset, SourceOffset = SourceOffset + offset.Nanos };
        Length = offset;
        Track.Events.Add(right);
        return right;
    }
}
class Track { public List<TrackEvent> Events = new List<TrackEvent>(); public List<object> Envelopes = new List<object>(); }
class Marker { public Timecode Position; }
class Region { public Timecode Position, Length; }
class Project
{
    public Timecode Length = new Timecode(100);
    public List<Track> Tracks = new List<Track>();
    public List<Marker> Markers = new List<Marker>();
    public List<Region> Regions = new List<Region>();
}
class Vegas { public Project Project = new Project(); }
class Harness
{
    static int passed;
    static bool GetBool(Dictionary<string, object> p, string key, bool fallback) { return p.ContainsKey(key) ? (bool)p[key] : fallback; }
    static Timecode RequireTime(Project project, Dictionary<string, object> p, string prefix, bool position) { return new Timecode((long)p[prefix + "_frames"]); }
    static double Seconds(Timecode value) { return value.Nanos; }
    static Dictionary<string, object> Result(string key, object value) { return new Dictionary<string, object> { { key, value } }; }
    // PRODUCTION_METHOD
    static Dictionary<string, object> Parameters()
    {
        return new Dictionary<string, object> { { "start_frames", 10L }, { "end_frames", 20L }, { "allow_ripple_delete", true } };
    }
    static TrackEvent Add(Vegas vegas, long start, long length)
    {
        Track track = new Track(); vegas.Project.Tracks.Add(track);
        TrackEvent ev = new TrackEvent { Track = track, Start = new Timecode(start), Length = new Timecode(length) };
        track.Events.Add(ev); return ev;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException ex) { Check(ex.Message.Contains(message), ex.Message); return; }
        throw new Exception("Expected refusal: " + message);
    }
    static void Main()
    {
        Test("events before the cut remain unchanged", delegate {
            Vegas v = new Vegas(); TrackEvent e = Add(v, 0, 10); RippleDeleteRange(v, Parameters());
            Check(e.Start.Nanos == 0 && e.Length.Nanos == 10, "Before-cut event changed");
        });
        Test("inside events are removed and later tracks shift equally", delegate {
            Vegas v = new Vegas(); TrackEvent inside = Add(v, 10, 10), a = Add(v, 20, 10), b = Add(v, 30, 5);
            RippleDeleteRange(v, Parameters());
            Check(inside.Track.Events.Count == 0 && a.Start.Nanos == 10 && b.Start.Nanos == 20, "Incorrect all-track movement");
        });
        Test("event spanning both edges preserves source offsets", delegate {
            Vegas v = new Vegas(); TrackEvent e = Add(v, 5, 20); RippleDeleteRange(v, Parameters());
            Check(e.Track.Events.Count == 2 && e.Length.Nanos == 5, "Left side incorrect");
            TrackEvent right = e.Track.Events[1];
            Check(right.Start.Nanos == 10 && right.Length.Nanos == 5 && right.SourceOffset == 15, "Right side incorrect");
        });
        Test("events crossing one edge trim correctly", delegate {
            Vegas v = new Vegas(); TrackEvent left = Add(v, 5, 10), right = Add(v, 15, 10);
            RippleDeleteRange(v, Parameters());
            Check(left.Length.Nanos == 5, "Left trim failed");
            Check(right.Track.Events.Count == 1 && right.Track.Events[0].Start.Nanos == 10 && right.Track.Events[0].SourceOffset == 5, "Right trim failed");
        });
        Test("markers inside clamp and markers after shift", delegate {
            Vegas v = new Vegas(); v.Project.Markers.Add(new Marker { Position = new Timecode(5) });
            v.Project.Markers.Add(new Marker { Position = new Timecode(15) }); v.Project.Markers.Add(new Marker { Position = new Timecode(25) });
            RippleDeleteRange(v, Parameters());
            Check(v.Project.Markers[0].Position.Nanos == 5 && v.Project.Markers[1].Position.Nanos == 10 && v.Project.Markers[2].Position.Nanos == 15, "Marker mapping failed");
        });
        Test("crossing regions shrink and inside regions collapse", delegate {
            Vegas v = new Vegas(); Region cross = new Region { Position = new Timecode(5), Length = new Timecode(20) };
            v.Project.Regions.Add(cross); v.Project.Regions.Add(new Region { Position = new Timecode(12), Length = new Timecode(3) });
            RippleDeleteRange(v, Parameters()); Check(cross.Length.Nanos == 10 && v.Project.Regions.Count == 1, "Region mapping failed");
        });
        Test("marker shifting can be disabled", delegate {
            Vegas v = new Vegas(); Marker m = new Marker { Position = new Timecode(25) }; v.Project.Markers.Add(m);
            var p = Parameters(); p["shift_markers"] = false; RippleDeleteRange(v, p); Check(m.Position.Nanos == 25, "Unexpected marker move");
        });
        Test("acknowledgement is required", delegate {
            var p = Parameters(); p["allow_ripple_delete"] = false; Reject(delegate { RippleDeleteRange(new Vegas(), p); }, "allow_ripple_delete");
        });
        Test("negative reversed empty and out-of-range cuts are rejected", delegate {
            foreach (long[] range in new long[][] { new long[] { -1, 20 }, new long[] { 20, 10 }, new long[] { 10, 10 }, new long[] { 10, 101 } }) {
                var p = Parameters(); p["start_frames"] = range[0]; p["end_frames"] = range[1];
                Reject(delegate { RippleDeleteRange(new Vegas(), p); }, "project length");
            }
        });
        Test("locked-event refusal happens before any edit on any track", delegate {
            Vegas v = new Vegas(); TrackEvent first = Add(v, 5, 20), locked = Add(v, 30, 5); locked.Locked = true;
            Reject(delegate { RippleDeleteRange(v, Parameters()); }, "locked");
            Check(first.Track.Events.Count == 1 && first.Length.Nanos == 20 && locked.Start.Nanos == 30, "Partial edit before refusal");
        });
        Test("groups require a separate explicit acknowledgement", delegate {
            Vegas v = new Vegas(); TrackEvent e = Add(v, 20, 5); e.IsGrouped = true;
            Reject(delegate { RippleDeleteRange(v, Parameters()); }, "allow_group_desync"); Check(e.Start.Nanos == 20, "Group changed on refusal");
            var p = Parameters(); p["allow_group_desync"] = true; RippleDeleteRange(v, p); Check(e.Start.Nanos == 10, "Acknowledged operation failed");
        });
        Test("track automation requires a separate explicit acknowledgement", delegate {
            Vegas v = new Vegas(); TrackEvent e = Add(v, 20, 5); e.Track.Envelopes.Add(new object());
            Reject(delegate { RippleDeleteRange(v, Parameters()); }, "allow_unshifted_automation");
            var p = Parameters(); p["allow_unshifted_automation"] = true; RippleDeleteRange(v, p); Check(e.Start.Nanos == 10, "Acknowledged operation failed");
        });
        Console.WriteLine(passed + " native algorithm scenarios passed.");
    }
}
