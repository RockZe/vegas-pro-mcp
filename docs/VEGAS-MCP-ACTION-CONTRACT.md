# United Taps VEGAS MCP action contract

Status: ACTIVE

Version: 1.2

Updated: 2026-10-16

This is the active public beta action and safety contract
(2.1.0-beta.1; 100 domain actions). The executable catalog is
`src\action-catalog.mjs`; automated parity checks require every catalog action
to have a C# route.

## Safety contract

- The connector never modifies, replaces, transcodes, or deletes source media.
- Content actions require the exact active saved `.veg` path.
- Existing-event mutations require `event_id` and either exact prior frame values
  (`expected_start_frames`, `expected_length_frames`) or compatible prior seconds.
- Every content mutation creates a new project backup before entering a named
  VEGAS Undo block.
- `timeline.batch` dry runs inspect identities without a backup or edit. Apply
  requires `allow_batch=true`, accepts at most 100 operations, and creates one
  backup and one Undo block.
- Existing output files are never overwritten by snapshots or renders.
- Removing an envelope/take, clearing a target effect chain, flattening animated
  OFX state, or operating on grouped crossfades requires the corresponding
  explicit acknowledgement flag.
- Structural single-event move/copy/trim/split/delete refuses grouped events by
  default. Use the matching whole-group action or explicitly acknowledge an
  intentional one-event desynchronization.
- Regrouping already-grouped events and ungrouping only part of a group also
  require explicit acknowledgement.
- Multiple VEGAS instances require an exact `vegas_pid`.
- Live editing tests must use a saved disposable project and copies of test
  media. Never use a work project for editing tests.

## Time values

New and upgraded actions accept exactly one representation for each time field:

- `<name>_frames` for exact project-frame positioning;
- `<name>_timecode` using the project's ruler format; or
- `<name>_seconds` for compatibility.

Do not supply more than one representation for the same field. Frame identities
are preferred for probe/apply guards.

## Tools and actions

### `vegas_project`

`inspect`, `backup`, `save`, `undo`, `redo`, `set_cursor`, `play`, `pause`,
`stop`, `snapshot`, `inspect_settings`, `set_settings`, `set_selection`,
`insert_time`.

Settings apply requires the exact `settings_token` returned by
`inspect_settings`. Project selections and inserted time accept frame/timecode/
seconds inputs.

### `vegas_timeline`

`inspect`, `find_events`, `clear_selection`, `select_events`, `batch`,
`add_track`, `set_track`, `duplicate_track`, `insert_media`,
`insert_linked_media`, `move_event`, `copy_event`, `trim_event`, `split_event`,
`delete_event`, `set_event`, `move_group`, `copy_group`, `split_group`,
`delete_group`, `set_fades`, `set_transition`, `create_crossfade`,
`create_linked_crossfade`, `close_gaps`, `ripple_delete`, `insert_title`, `group_events`,
`ungroup_events`, `add_marker`, `edit_marker`, `delete_marker`, `add_region`,
`edit_region`, `delete_region`.

`find_events` filters by event IDs, tracks, type, name, media path, selection,
mute/lock state, and overlapping time range. `insert_linked_media` creates and
groups picture and sound events without changing the source file.
`create_linked_crossfade` requires two correctly grouped video/audio pairs.
Whole-group move, copy, split, and delete preserve linked A/V structure without
requiring the caller to edit each partner separately.
`close_gaps` refuses grouped events because single-track movement would break A/V
synchronization.

`ripple_delete` is an experimental all-track range edit. It requires
`allow_ripple_delete=true`, a nonnegative start, and an end within the project.
Locked affected events are refused. Grouped affected events require
`allow_group_desync=true`; track envelopes require
`allow_unshifted_automation=true`. These flags acknowledge limitations; they do
not enable automatic group repair or automation movement. Track/bus automation,
track-motion and effect keyframes do not shift. With `shift_markers=true`
(default), markers inside the cut clamp to its start, later markers shift,
region endpoints map through the cut, and fully collapsed regions are removed
from the project. Source files remain untouched. Inspect grouping, animation
and automation after use.

### `vegas_audio`

`set_event_gain_db`, `normalize`, `recalculate_normalize`, `set_channels`,
`set_audio_properties`, `set_track_mix`, `add_volume_envelope_point`,
`analyze_loudness`, `set_loudness`, `inspect_envelopes`, `set_envelope_point`,
`delete_envelope_point`, `clear_envelope`, `list_buses`, `add_bus`,
`route_track`, `set_bus_mix`.

Native VEGAS normalization remains peak normalization. LUFS analysis is a
separate FFmpeg measurement of the exact source event segment before VEGAS event
FX, track processing, buses, and master processing. `set_loudness` requires the
matching analysis fingerprint and sets non-destructive event gain. Final-program
loudness must be checked through a render with `generate_loudness_log=true`.

### `vegas_effects`

`list_plugins`, `list_presets`, `list_event_effects`, `add_event_effect`,
`remove_event_effect`, `set_ofx_parameter`, `list_track_effects`,
`add_track_effect`, `remove_track_effect`, `list_bus_effects`, `add_bus_effect`,
`remove_bus_effect`, `set_effect_state`, `set_target_ofx_parameter`,
`copy_effect_chain`.

Generic targets include events, tracks, audio buses/master, the video bus, and
project media. OFX values cover scalar, Boolean, string, choice, 2D/3D integer
and floating-point, RGB/RGBA, and custom values. Effect-chain copying refuses to
flatten animated parameters unless `allow_flatten_animated=true`; dedicated
animated keyframe-chain cloning is not claimed.

### `vegas_media`

`inspect`, `list`, `create_bin`, `insert_image_sequence`, `list_takes`,
`add_take`, `set_active_take`, `remove_take`, `replace_event_media`.

Event media replacement adds a new active take by default and preserves the old
take. Removing the previous take requires `remove_previous_take=true` and
`allow_take_removal=true`.

### `vegas_motion`

`inspect_event_motion`, `set_event_motion_keyframe`,
`clear_event_motion_keyframes`, `inspect_track_motion`,
`set_track_motion_keyframe`, `clear_track_motion_keyframes`.

Event motion exposes pan/crop bounds, center, translation, scale, rotation, and
interpolation. Track motion exposes VEGAS's native keyframe values plus the
reported scale factors needed to interpret them.

### `vegas_render`

`list`, `start`, `status`, `cancel`.

Render start requires exact project path, renderer, template, and a new output
path. Jobs are asynchronous and return a job ID. Status reports progress,
frames, elapsed/remaining time, errors, cancellation state, and output existence.

## Batch example

```json
{
  "action": "batch",
  "params": {
    "project_path": "C:\\Projects\\Disposable.veg",
    "dry_run": true,
    "operations": [
      {
        "action": "timeline.set_fades",
        "params": {
          "event_id": "123",
          "expected_start_frames": 0,
          "expected_length_frames": 300,
          "fade_out_frames": 15
        }
      }
    ]
  }
}
```

After inspecting the dry-run plan, replace `dry_run` with
`allow_batch=true` to apply. A dry run verifies the action allowlist, target
existence, and supplied event identities; action-specific values are validated
during apply.

## Known scripting boundaries

- VEGAS does not allow scripts to create audio-effect automation envelopes.
- Arbitrary third-party plug-in dialogs, interactive analysis buttons, AI tools,
  detailed color-panel gestures, and every possible custom parameter editor are
  not promised.
- Effect-chain copying preserves supported persisted current values and rejects
  animated flattening by default. Visual equivalence still requires inspection.
- Automated proof does not replace visual and listening checks in VEGAS.
