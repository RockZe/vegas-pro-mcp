import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { ACTIONS, MUTATING_ACTIONS, TOOL_NAMES, domainActionCount } from "../src/action-catalog.mjs";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

test("V2 action catalog is unique and complete", () => {
  const fullActions = Object.entries(ACTIONS).flatMap(([domain, actions]) =>
    actions.map((action) => `${domain}.${action}`),
  );
  assert.equal(fullActions.length, 100);
  assert.equal(new Set(fullActions).size, fullActions.length);
  assert.equal(domainActionCount(), fullActions.length);
  assert.equal(new Set(TOOL_NAMES).size, TOOL_NAMES.length);
});

test("every catalog action has a C# dispatch route", () => {
  const extensionRoot = path.join(projectRoot, "extension");
  const source = fs.readdirSync(extensionRoot)
    .filter((name) => name.endsWith(".cs"))
    .map((name) => fs.readFileSync(path.join(extensionRoot, name), "utf8"))
    .join("\n");
  for (const [domain, actions] of Object.entries(ACTIONS)) {
    for (const action of actions) {
      const full = `${domain}.${action}`;
      assert.ok(source.includes(`"${full}"`), `Missing C# dispatch route for ${full}`);
    }
  }
});

test("read-only inspection actions are not mislabeled as mutations", () => {
  const readOnly = [
    "project.inspect", "project.inspect_settings", "timeline.inspect", "timeline.find_events",
    "audio.analyze_loudness", "audio.inspect_envelopes", "audio.list_buses",
    "effects.list_plugins", "effects.list_presets", "effects.list_event_effects", "effects.list_track_effects", "effects.list_bus_effects",
    "media.inspect", "media.list", "media.list_takes",
    "motion.inspect_event_motion", "motion.inspect_track_motion", "render.list", "render.status",
  ];
  for (const action of readOnly) assert.equal(MUTATING_ACTIONS.has(action), false, action);
});
