import assert from "node:assert/strict";
import test from "node:test";
import { ACTIONS, MUTATING_ACTIONS } from "../src/action-catalog.mjs";
import { validateRequest } from "../src/request-validation.mjs";

const identity = { event_id: "42", expected_start_frames: 10, expected_length_frames: 20 };

test("event mutations require exact probed identity", () => {
  assert.throws(() => validateRequest("timeline.split_event", { project_path: "C:\\x.veg", event_id: "42" }), /expected_start/);
  assert.doesNotThrow(() => validateRequest("timeline.split_event", { project_path: "C:\\x.veg", ...identity }));
});

test("crossfades require both guarded event identities", () => {
  assert.throws(() => validateRequest("timeline.create_crossfade", { project_path: "C:\\x.veg", left_event: identity }), /right_event/);
  assert.doesNotThrow(() => validateRequest("timeline.create_crossfade", { project_path: "C:\\x.veg", left_event: identity, right_event: identity }));
});

test("batch apply requires an explicit acknowledgement while dry-run does not", () => {
  const operations = [{ action: "timeline.set_event", params: identity }];
  assert.throws(() => validateRequest("timeline.batch", { project_path: "C:\\x.veg", operations }), /allow_batch/);
  assert.doesNotThrow(() => validateRequest("timeline.batch", { project_path: "C:\\x.veg", operations, dry_run: true }));
  assert.doesNotThrow(() => validateRequest("timeline.batch", { project_path: "C:\\x.veg", operations, allow_batch: true }));
});

test("render start requires exact output, renderer, template, and project", () => {
  assert.throws(() => validateRequest("render.start", {}), /project_path/);
  assert.doesNotThrow(() => validateRequest("render.start", {
    project_path: "C:\\x.veg", output_path: "C:\\out.mp4", renderer_name: "x", template_name: "y",
  }));
});

test("LUFS apply requires the prior analysis fingerprint", () => {
  assert.throws(() => validateRequest("audio.set_loudness", { project_path: "C:\\x.veg", ...identity }), /expected_analysis_fingerprint/);
});

test("marker and region mutations require prior exact identities", () => {
  assert.throws(() => validateRequest("timeline.delete_marker", { project_path: "C:\\x.veg", marker_index: 0 }), /expected_position/);
  assert.doesNotThrow(() => validateRequest("timeline.delete_marker", { project_path: "C:\\x.veg", marker_index: 0, expected_position_frames: 30 }));
  assert.throws(() => validateRequest("timeline.delete_region", { project_path: "C:\\x.veg", region_index: 0, expected_position_frames: 30 }), /expected_length/);
  assert.doesNotThrow(() => validateRequest("timeline.delete_region", { project_path: "C:\\x.veg", region_index: 0, expected_position_frames: 30, expected_length_frames: 60 }));
});
test("every catalog mutation requires an explicit active project path", () => {
  for (const action of MUTATING_ACTIONS) {
    assert.throws(() => validateRequest(action, {}), /project_path/, action);
  }
});

test("all-track ripple delete requires an explicit acknowledgement", () => {
  assert.ok(ACTIONS.timeline.includes("ripple_delete"));
  assert.throws(() => validateRequest("timeline.ripple_delete", { project_path: "C:\\Projects\\Test.veg" }), /allow_ripple_delete/);
  assert.doesNotThrow(() => validateRequest("timeline.ripple_delete", { project_path: "C:\\Projects\\Test.veg", allow_ripple_delete: true }));
});
