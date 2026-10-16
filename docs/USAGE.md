# Usage examples

These are tool-call arguments. Replace example paths, event IDs, frame values,
and renderer/template names with exact values from your own inspection.

## Inspect before editing

Call `vegas_status` with `{}`. If several instances are listed, choose the intended
PID. Call `vegas_project` with `{"action":"inspect"}` and `vegas_timeline` with
`{"action":"inspect","params":{"include_effects":false}}`.

Most tools take `action` and `params`. `vegas_status` takes `vegas_pid` directly.
Other actions accept `vegas_pid` in `params`. The caller must select a PID when
multiple instances are live; no instance is chosen by recency or window focus.

## Split an inspected event

Pass this to `vegas_timeline`:

```json
{
  "action": "split_event",
  "params": {
    "project_path": "C:\\Projects\\Disposable.veg",
    "event_id": "12345",
    "expected_start_frames": 300,
    "expected_length_frames": 240,
    "at_frames": 420
  }
}
```

The split position is on the project timeline. Start/length guards must match the
last inspection. Grouped events are refused by single-event structural edits;
use the corresponding whole-group action to preserve linked picture and sound.
The response includes a backup path, Undo label, and resulting event identities.

## Insert linked picture and sound

Pass this to `vegas_timeline` on a disposable saved project:

```json
{
  "action": "insert_linked_media",
  "params": {
    "project_path": "C:\\Projects\\Disposable.veg",
    "source_path": "C:\\Media\\Example.mp4",
    "video_track_index": 0,
    "audio_track_index": 1,
    "start_frames": 0,
    "length_frames": 150,
    "group": true
  }
}
```

Inspect the tracks first and use their returned zero-based indices. To create
missing tracks, set `create_missing_tracks=true`. The source must contain the
requested streams. The source file is read and added to the project, not changed.

## Experimental all-track range removal

`ripple_delete` removes the interval `[start,end)` on every track and moves later
events earlier by its duration. It is broader than deleting one selected clip.

```json
{
  "action": "ripple_delete",
  "params": {
    "project_path": "C:\\Projects\\Disposable.veg",
    "start_frames": 300,
    "end_frames": 450,
    "allow_ripple_delete": true,
    "shift_markers": true
  }
}
```

The range must lie inside the current project. Affected locked events are refused.
Groups require a separate `allow_group_desync=true` acknowledgement because split
pieces are not automatically regrouped. Track envelopes require a separate
`allow_unshifted_automation=true` acknowledgement. Track/bus automation, track
motion, and effect keyframes remain at their original timeline positions; inspect
them manually. These flags do not correct those limitations.

When marker shifting is enabled, markers inside the interval clamp to its start,
markers at/after its end move earlier, region endpoints map through the interval,
and regions fully inside it collapse and are removed from the project. Source
files are not removed. Review picture, sound, grouping and automation after use.

## Render

Call `vegas_render` with `{"action":"list"}` to obtain exact renderer/template
names or IDs. Then use `start` with `project_path`, `renderer_name` (or
`renderer_class_id`), `template_name` (or `template_guid`), and a new `output_path`.
Optionally supply both `start_frames` and `length_frames` for a bounded render.
Use the returned `job_id` with `status`; cancellation also needs the exact
`project_path`. Existing output files are refused.

## Time and recovery

For each time field choose only one of `_frames`, `_timecode`, or `_seconds`.
Frame values use the current project's frame rate. An event ID is an ephemeral
host identity; inspect again after reload, Undo/Redo, or other timeline changes.

Use VEGAS Undo for a verified reversible edit, or recover the separate `.veg`
backup reported by the connector. Backups refer to source media and do not copy
it. Do not repeatedly retry a timed-out edit: inspect first because a native
operation already in progress may still complete.
