export const ACTIONS = Object.freeze({
  project: Object.freeze([
    "inspect", "backup", "save", "undo", "redo", "set_cursor", "play", "pause", "stop", "snapshot",
    "inspect_settings", "set_settings", "set_selection", "insert_time",
  ]),
  timeline: Object.freeze([
    "inspect", "find_events", "clear_selection", "select_events", "batch",
    "add_track", "set_track", "duplicate_track", "insert_media", "insert_linked_media",
    "move_event", "copy_event", "trim_event", "split_event", "delete_event", "set_event",
    "move_group", "copy_group", "split_group", "delete_group",
    "set_fades", "set_transition", "create_crossfade", "create_linked_crossfade", "close_gaps", "ripple_delete", "insert_title",
    "group_events", "ungroup_events", "add_marker", "edit_marker", "delete_marker",
    "add_region", "edit_region", "delete_region",
  ]),
  audio: Object.freeze([
    "set_event_gain_db", "normalize", "recalculate_normalize", "set_channels", "set_audio_properties",
    "set_track_mix", "add_volume_envelope_point", "analyze_loudness", "set_loudness",
    "inspect_envelopes", "set_envelope_point", "delete_envelope_point", "clear_envelope",
    "list_buses", "add_bus", "route_track", "set_bus_mix",
  ]),
  effects: Object.freeze([
    "list_plugins", "list_presets", "list_event_effects", "add_event_effect", "remove_event_effect", "set_ofx_parameter",
    "list_track_effects", "add_track_effect", "remove_track_effect",
    "list_bus_effects", "add_bus_effect", "remove_bus_effect",
    "set_effect_state", "set_target_ofx_parameter", "copy_effect_chain",
  ]),
  media: Object.freeze([
    "inspect", "list", "create_bin", "insert_image_sequence", "list_takes", "add_take",
    "set_active_take", "remove_take", "replace_event_media",
  ]),
  motion: Object.freeze([
    "inspect_event_motion", "set_event_motion_keyframe", "clear_event_motion_keyframes",
    "inspect_track_motion", "set_track_motion_keyframe", "clear_track_motion_keyframes",
  ]),
  render: Object.freeze(["list", "start", "status", "cancel"]),
});

export const TOOL_NAMES = Object.freeze([
  "vegas_status",
  "vegas_project",
  "vegas_timeline",
  "vegas_audio",
  "vegas_effects",
  "vegas_media",
  "vegas_motion",
  "vegas_render",
  "vegas_capabilities",
]);

export const MUTATING_ACTIONS = Object.freeze(new Set([
  "project.backup", "project.save", "project.undo", "project.redo", "project.set_cursor",
  "project.play", "project.pause", "project.stop", "project.snapshot", "project.set_settings",
  "project.set_selection", "project.insert_time",
  "timeline.clear_selection", "timeline.select_events", "timeline.batch",
  ...ACTIONS.timeline.filter((action) => !["inspect", "find_events", "clear_selection", "select_events", "batch"].includes(action)).map((action) => `timeline.${action}`),
  ...ACTIONS.audio.filter((action) => !["analyze_loudness", "inspect_envelopes", "list_buses"].includes(action)).map((action) => `audio.${action}`),
  ...ACTIONS.effects.filter((action) => !["list_plugins", "list_presets", "list_event_effects", "list_track_effects", "list_bus_effects"].includes(action)).map((action) => `effects.${action}`),
  ...ACTIONS.media.filter((action) => !["inspect", "list", "list_takes"].includes(action)).map((action) => `media.${action}`),
  ...ACTIONS.motion.filter((action) => !action.startsWith("inspect_")).map((action) => `motion.${action}`),
  "render.start", "render.cancel",
]));

export function domainActionCount() {
  return Object.values(ACTIONS).reduce((total, actions) => total + actions.length, 0);
}
