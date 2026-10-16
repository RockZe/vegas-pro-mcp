const PROJECT_PATH_REQUIRED = new Set([
  "project.backup", "project.save", "project.undo", "project.redo", "project.snapshot",
  "project.set_settings", "project.set_selection", "project.insert_time",
  "project.set_cursor", "project.play", "project.pause", "project.stop",
]);

const EVENT_GUARDED = new Set([
  "timeline.move_event", "timeline.copy_event", "timeline.trim_event", "timeline.split_event",
  "timeline.delete_event", "timeline.set_event", "timeline.set_fades", "timeline.set_transition",
  "timeline.move_group", "timeline.copy_group", "timeline.split_group", "timeline.delete_group",
  "audio.set_event_gain_db", "audio.normalize", "audio.recalculate_normalize", "audio.set_channels",
  "audio.set_audio_properties", "audio.analyze_loudness", "audio.set_loudness",
  "effects.add_event_effect", "effects.remove_event_effect", "effects.set_ofx_parameter",
  "media.add_take", "media.set_active_take", "media.remove_take", "media.replace_event_media",
  "motion.set_event_motion_keyframe", "motion.clear_event_motion_keyframes",
]);

const READ_WITH_OPTIONAL_PROJECT = new Set([
  "project.inspect", "project.inspect_settings", "timeline.inspect", "timeline.find_events",
  "effects.list_plugins", "effects.list_presets", "media.inspect", "media.list", "audio.list_buses",
  "render.list", "render.status",
]);

function requireField(params, key, action) {
  const value = params[key];
  if (value === undefined || value === null || value === "") {
    throw new Error(`${action} requires params.${key}.`);
  }
  return value;
}

function requireEventIdentity(identity, label, action) {
  if (!identity || typeof identity !== "object" || Array.isArray(identity)) {
    throw new Error(`${action} requires ${label} to be an event identity object.`);
  }
  requireField(identity, "event_id", action);
  if (identity.expected_start_frames === undefined && identity.expected_start_seconds === undefined) {
    throw new Error(`${action} requires ${label}.expected_start_frames or expected_start_seconds.`);
  }
  if (identity.expected_length_frames === undefined && identity.expected_length_seconds === undefined) {
    throw new Error(`${action} requires ${label}.expected_length_frames or expected_length_seconds.`);
  }
}

export function validateRequest(action, params = {}) {
  if (!params || typeof params !== "object" || Array.isArray(params)) {
    throw new Error(`${action} params must be an object.`);
  }
  const domain = action.split(".")[0];
  const isDomainMutation = ["timeline", "audio", "effects", "media", "motion"].includes(domain)
    && !READ_WITH_OPTIONAL_PROJECT.has(action)
    && !["effects.list_event_effects", "effects.list_track_effects", "effects.list_bus_effects", "media.list_takes", "motion.inspect_event_motion", "motion.inspect_track_motion", "audio.inspect_envelopes", "audio.analyze_loudness"].includes(action);
  const guardedRead = ["effects.list_event_effects", "effects.list_track_effects", "effects.list_bus_effects", "media.list_takes", "motion.inspect_event_motion", "motion.inspect_track_motion", "audio.inspect_envelopes", "audio.analyze_loudness"].includes(action);
  if (PROJECT_PATH_REQUIRED.has(action) || isDomainMutation || guardedRead || ["timeline.clear_selection", "timeline.select_events", "render.start", "render.cancel"].includes(action)) {
    requireField(params, "project_path", action);
  }
  if (EVENT_GUARDED.has(action)) requireEventIdentity(params, "params", action);
  if (["timeline.group_events", "timeline.ungroup_events"].includes(action)) {
    if (!Array.isArray(params.events) || params.events.length < 1) throw new Error(`${action} requires params.events.`);
    params.events.forEach((identity, index) => requireEventIdentity(identity, `params.events[${index}]`, action));
  }
  if (action === "timeline.create_crossfade") {
    requireEventIdentity(params.left_event, "params.left_event", action);
    requireEventIdentity(params.right_event, "params.right_event", action);
  }
  if (action === "timeline.create_linked_crossfade") {
    for (const key of ["left_video_event", "right_video_event", "left_audio_event", "right_audio_event"]) {
      requireEventIdentity(params[key], `params.${key}`, action);
    }
  }
  if (["timeline.edit_marker", "timeline.delete_marker"].includes(action)) {
    if (params.expected_position_frames === undefined && params.expected_position_seconds === undefined) {
      throw new Error(`${action} requires expected_position_frames or expected_position_seconds.`);
    }
  }
  if (["timeline.edit_region", "timeline.delete_region"].includes(action)) {
    if (params.expected_position_frames === undefined && params.expected_position_seconds === undefined) {
      throw new Error(`${action} requires expected_position_frames or expected_position_seconds.`);
    }
    if (params.expected_length_frames === undefined && params.expected_length_seconds === undefined) {
      throw new Error(`${action} requires expected_length_frames or expected_length_seconds.`);
    }
  }
  if (action === "timeline.batch") {
    if (!Array.isArray(params.operations) || params.operations.length < 1 || params.operations.length > 100) {
      throw new Error("timeline.batch requires 1 to 100 operations.");
    }
    if (params.dry_run !== true && params.allow_batch !== true) {
      throw new Error("timeline.batch apply requires params.allow_batch=true.");
    }
  }
  if (action === "audio.set_loudness") requireField(params, "expected_analysis_fingerprint", action);
  if (action === "timeline.ripple_delete" && params.allow_ripple_delete !== true) {
    throw new Error("timeline.ripple_delete requires params.allow_ripple_delete=true; it affects every track.");
  }
  if (action === "project.set_settings") requireField(params, "expected_settings_token", action);
  if (action === "effects.copy_effect_chain") {
    requireField(params, "source", action);
    requireField(params, "target", action);
  }
  if (action === "render.start") {
    requireField(params, "output_path", action);
    if (!params.renderer_name && !params.renderer_class_id) throw new Error("render.start requires a renderer name or class ID.");
    if (!params.template_name && !params.template_guid) throw new Error("render.start requires a template name or GUID.");
  }
  return params;
}

export const validationContract = Object.freeze({ PROJECT_PATH_REQUIRED, EVENT_GUARDED, READ_WITH_OPTIONAL_PROJECT });
