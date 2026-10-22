// Opt-in real editor acceptance. Requires prepare-live-test.ps1 and its saved fixture.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import net from "node:net";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport, getDefaultEnvironment } from "@modelcontextprotocol/sdk/client/stdio.js";
import { assertTargetHost } from "../src/target-host.mjs";
import { INSTALL_ROOT, readToken } from "../src/pipe-client.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fixture = path.resolve(process.argv[2] ?? fs.readFileSync(path.join(root, "work/latest-live-fixture.txt"), "utf8").trim());
assert.ok(fixture.startsWith(path.join(root, "work") + path.sep), "Fixture must be inside this repository's work directory.");
const project = path.join(fixture, "acceptance.veg");
const pid = Number(fs.readFileSync(path.join(fixture, "bootstrap.ok"), "utf8"));
const report = { started_utc: new Date().toISOString(), fixture, pid, checks: [], calls: [] };
const reportPath = path.join(fixture, "acceptance-report.json");
const persist = () => fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
const client = new Client({ name: "vegas-build31-acceptance", version: "1.0.0" });
const transport = new StdioClientTransport({ command: process.execPath, args: [path.join(root, "src/server.mjs")], stderr: "inherit",
  env: { ...getDefaultEnvironment(), ...Object.fromEntries(Object.entries(process.env).filter(([k]) => k.startsWith("VEGAS_"))) } });

async function check(name, operation) {
  try { await operation(); report.checks.push({ name, passed: true }); console.log(`PASS ${name}`); }
  catch (error) { report.checks.push({ name, passed: false, error: error.stack }); throw error; }
  finally { persist(); }
}
async function call(domain, action, params = {}, refusal) {
  const result = await client.callTool({ name: `vegas_${domain}`, arguments: { action, params: { project_path: project, vegas_pid: pid, ...params } } }, undefined, { timeout: 120_000 });
  report.calls.push({ action: `${domain}.${action}`, expected_refusal: Boolean(refusal), result });
  persist();
  if (refusal) { assert.equal(result.isError, true); assert.match(result.content[0].text, refusal); return result; }
  if (result.isError) throw new Error(`${domain}.${action}: ${result.content[0].text}`);
  const data = result.structuredContent;
  if (data?.backup_path) assert.ok(fs.statSync(data.backup_path).size > 0, "Backup must exist and contain bytes.");
  return data;
}
const timeline = () => call("timeline", "inspect");
const events = async () => (await timeline()).tracks.flatMap(t => t.events);
async function identity(name) {
  const ev = (await events()).find(e => e.name === name);
  assert.ok(ev, `Missing fixture event ${name}`);
  return { event_id: ev.event_id, expected_start_frames: ev.start_frames, expected_length_frames: ev.length_frames };
}
async function track(name) { const item = (await timeline()).tracks.find(t => t.name === name); assert.ok(item, name); return item.index; }
const hash = file => crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
function createAssets() {
  const image = Buffer.alloc(54 + 320 * 180 * 3); image.write("BM"); image.writeUInt32LE(image.length, 2); image.writeUInt32LE(54, 10);
  image.writeUInt32LE(40, 14); image.writeInt32LE(320, 18); image.writeInt32LE(180, 22); image.writeUInt16LE(1, 26); image.writeUInt16LE(24, 28);
  for (let y = 0; y < 180; y++) for (let x = 0; x < 320; x++) { const i = 54 + (y * 320 + x) * 3; image[i] = x % 256; image[i + 1] = y; image[i + 2] = 180; }
  fs.writeFileSync(path.join(fixture, "frame001.bmp"), image); fs.writeFileSync(path.join(fixture, "frame002.bmp"), image);
  const samples = 48000 * 4, wav = Buffer.alloc(44 + samples * 2);
  wav.write("RIFF"); wav.writeUInt32LE(wav.length - 8, 4); wav.write("WAVEfmt ", 8); wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22); wav.writeUInt32LE(48000, 24); wav.writeUInt32LE(96000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34);
  wav.write("data", 36); wav.writeUInt32LE(samples * 2, 40);
  for (let i = 0; i < samples; i++) wav.writeInt16LE(Math.round(6000 * Math.sin(2 * Math.PI * 440 * i / 48000)), 44 + i * 2);
  fs.writeFileSync(path.join(fixture, "tone.wav"), wav);
}

async function rawRequest(request) {
  return await new Promise((resolve, reject) => {
    const socket = net.createConnection(`\\\\.\\pipe\\UnitedTaps.VegasMcp.${pid}`);
    let text = "";
    socket.setEncoding("utf8"); socket.setTimeout(20000, () => socket.destroy(new Error("Raw request timed out.")));
    socket.on("connect", () => socket.write(JSON.stringify(request) + "\n"));
    socket.on("error", reject);
    socket.on("data", chunk => { text += chunk; if (text.includes("\n")) { socket.destroy(); resolve(JSON.parse(text.slice(0, text.indexOf("\n")))); } });
  });
}

try {
  await client.connect(transport);
  await check("exact host, saved fixture and nine MCP tools", async () => {
    const status = await client.callTool({ name: "vegas_status", arguments: { vegas_pid: pid } });
    assert.ok(!status.isError, status.content[0].text); report.host = status.structuredContent; assertTargetHost(report.host);
    assert.equal(report.host.project_path.toLowerCase(), project.toLowerCase()); assert.equal(report.host.track_count, 0);
    assert.equal((await client.listTools()).tools.length, 9);
    const caps = await client.callTool({ name: "vegas_capabilities", arguments: {} }); assert.equal(caps.structuredContent.domain_action_count, 100);
  });
  createAssets();
  const bmp = path.join(fixture, "frame001.bmp"), wav = path.join(fixture, "tone.wav");
  const before = [hash(bmp), hash(wav)];
  await check("wrong-project refusal before editing", async () => { await call("timeline", "add_track", { project_path: project + ".wrong", kind: "video" }, /Active project changed/); assert.equal((await timeline()).track_count, 0); });
  await check("native authentication and expired queued request refusal", async () => {
    const base = { request_id: crypto.randomUUID(), expires_at_utc: new Date(Date.now() - 1000).toISOString(), action: "timeline.add_track", params: { project_path: project, kind: "video" } };
    const unauthorized = await rawRequest({ ...base, token: "invalid-test-token" }); assert.equal(unauthorized.ok, false); assert.equal(unauthorized.error.type, "Unauthorized");
    const expired = await rawRequest({ ...base, token: readToken(INSTALL_ROOT) }); assert.equal(expired.ok, false); assert.match(expired.error.message, /expired|deadline/);
    assert.equal((await timeline()).track_count, 0);
  });
  await check("backup, named Undo, native Undo and Redo", async () => {
    const added = await call("timeline", "add_track", { kind: "video", name: "MCP video" }); assert.match(added.undo_label, /add track/);
    await call("project", "undo"); assert.equal((await timeline()).track_count, 0);
    await call("project", "redo"); assert.equal((await timeline()).track_count, 1);
    await call("timeline", "add_track", { kind: "audio", name: "MCP audio" });
  });
  await check("project settings token and stale-settings refusal", async () => {
    const settings = await call("project", "inspect_settings");
    await call("project", "set_settings", { expected_settings_token: "stale", width: 320 }, /settings changed/);
    const result = await call("project", "set_settings", { expected_settings_token: settings.settings_token, width: 640, height: 360, frame_rate: 25 }); assert.equal(result.video.width, 640);
  });
  await check("insert picture and PCM audio without changing source", async () => {
    await call("timeline", "insert_media", { track_index: await track("MCP video"), source_path: bmp, start_frames: 0, length_frames: 75, name: "picture" });
    await call("timeline", "insert_media", { track_index: await track("MCP audio"), source_path: wav, start_frames: 0, length_frames: 75, name: "sound" });
    assert.equal((await events()).length, 2);
  });
  await check("stale event identity and cancelled mutation leave timeline unchanged", async () => {
    const snapshot = await events(); await call("timeline", "move_event", { ...await identity("picture"), expected_length_frames: 1000, new_start_frames: 25 }, /changed since/);
    assert.deepEqual(await events(), snapshot);
  });
  await check("move, trim, split, copy, delete and readback", async () => {
    await call("timeline", "move_event", { ...await identity("picture"), new_start_frames: 5 }); assert.equal((await events()).find(e => e.name === "picture").start_frames, 5);
    await call("timeline", "trim_event", { ...await identity("picture"), new_start_frames: 0, new_length_frames: 75 });
    const copied = await call("timeline", "copy_event", { ...await identity("picture"), new_start_frames: 100 });
    const ev = copied.event; assert.ok(ev); await call("timeline", "delete_event", { event_id: ev.event_id, expected_start_frames: ev.start_frames, expected_length_frames: ev.length_frames });
    const split = await call("timeline", "split_event", { ...await identity("sound"), at_frames: 50 }); assert.ok(split);
    await call("project", "undo"); assert.equal((await events()).filter(e => e.name === "sound").length, 1);
  });
  await check("fades and batch dry-run/apply/undo", async () => {
    const operation = { action: "timeline.set_fades", params: { ...await identity("picture"), fade_in_frames: 5, fade_out_frames: 5 } };
    const prior = await events(); await call("timeline", "batch", { dry_run: true, operations: [operation] }); assert.deepEqual(await events(), prior);
    await call("timeline", "batch", { operations: [operation] }, /allow_batch/);
    const applied = await call("timeline", "batch", { allow_batch: true, operations: [operation] }); assert.equal(applied.operation_count, 1);
    assert.equal((await events()).find(e => e.name === "picture").fade_in_frames, 5);
    await call("project", "undo"); assert.equal((await events()).find(e => e.name === "picture").fade_in_frames, 0);
  });
  await check("event gain, native peak normalize and track mix", async () => {
    await call("audio", "set_event_gain_db", { ...await identity("sound"), gain_db: -6 });
    assert.ok(Math.abs((await events()).find(e => e.name === "sound").event_gain_db + 6) < 0.001);
    await call("audio", "normalize", { ...await identity("sound"), enable: true, recalculate: true });
    await call("audio", "normalize", { ...await identity("sound"), enable: false });
    await call("audio", "set_track_mix", { track_index: await track("MCP audio"), volume_db: -3, pan: 0.1 });
  });
  await check("audio envelopes and bus routing", async () => {
    const t = await track("MCP audio");
    await call("audio", "set_envelope_point", { track_index: t, scope: "track", envelope_type: "Volume", position_frames: 25, volume_db: -6, create_if_missing: true });
    assert.ok((await call("audio", "inspect_envelopes", { track_index: t })).envelopes.length > 0);
    await call("audio", "delete_envelope_point", { track_index: t, envelope_type: "Volume", position_frames: 25 });
    const bus = await call("audio", "add_bus", { description: "MCP fixture bus" });
    await call("audio", "route_track", { track_index: t, scope: "bus", bus_index: bus.bus.index });
    await call("audio", "set_bus_mix", { scope: "bus", bus_index: bus.bus.index, volume_db: -2, mute: false });
    assert.ok((await call("audio", "list_buses")).count >= 2);
  });
  await check("media bins, takes and non-destructive replacement", async () => {
    assert.ok((await call("media", "list")).count >= 2);
    await call("media", "inspect", { media_path: bmp }); await call("media", "create_bin", { name: "MCP fixture", media_paths: [bmp, wav] });
    await call("media", "add_take", { ...await identity("picture"), source_path: bmp, make_active: false });
    await call("media", "set_active_take", { ...await identity("picture"), take_index: 1 });
    await call("media", "remove_take", { ...await identity("picture"), take_index: 0 });
    await call("media", "replace_event_media", { ...await identity("picture"), source_path: bmp });
    assert.equal((await call("media", "list_takes", await identity("picture"))).takes.length, 2);
  });
  await check("Pan/Crop and track-motion animation keyframes", async () => {
    await call("motion", "set_event_motion_keyframe", { ...await identity("picture"), position_frames: 25, scale_x: 0.9, scale_y: 0.9, rotate_degrees: 5, interpolation: "Linear" });
    assert.ok((await call("motion", "inspect_event_motion", await identity("picture"))).motion.keyframes.length >= 2);
    await call("motion", "set_track_motion_keyframe", { track_index: await track("MCP video"), position_frames: 25, position_x: 10, interpolation: "Linear" });
    const t = await track("MCP video");
    const read = await call("motion", "inspect_track_motion", { track_index: t }); assert.ok(read.motion.keyframes.length >= 2);
    assert.equal(read.motion.keyframes.find(k => k.position_frames === 25).position_x, 10);
    await call("project", "save");
    await call("motion", "inspect_track_motion", { track_index: t }); assert.equal((await call("project", "inspect")).is_modified, false);
    await call("project", "undo");
    const undone = await call("motion", "inspect_track_motion", { track_index: t }); assert.equal(undone.motion.keyframes.length, 1);
    await call("project", "redo"); assert.ok((await call("motion", "inspect_track_motion", { track_index: t })).motion.keyframes.length >= 2);
  });
  await check("native OFX discovery, parameter edit and animation", async () => {
    const plugins = await call("effects", "list_plugins", { kind: "video", contains: "Brightness", limit: 100 });
    const plugin = plugins.plugins.find(p => p.is_ofx && /com\.vegascreativesoftware:brightnessandcontrast/i.test(p.unique_id)); assert.ok(plugin, "Built-in brightness OFX must be installed.");
    report.ofx_plugin = plugin; await call("effects", "list_presets", { kind: "video", plugin_name: plugin.unique_id });
    const added = await call("effects", "add_event_effect", { ...await identity("picture"), plugin_name: plugin.unique_id });
    const effect = added.event.effects[added.effect_index]; const parameter = effect.parameters.find(p => p.type === "Double" && p.can_animate && p.enabled); assert.ok(parameter);
    const value = Math.max(parameter.min, Math.min(parameter.max, 0.1));
    const updated = await call("effects", "set_ofx_parameter", { ...await identity("picture"), effect_index: effect.index, parameter_name: parameter.name, value, at_seconds: 1 });
    assert.ok(updated.parameter.is_animated); await call("effects", "list_event_effects", await identity("picture"));
    await call("effects", "add_track_effect", { track_index: await track("MCP video"), plugin_name: plugin.unique_id });
    const chain = await call("effects", "list_track_effects", { track_index: await track("MCP video") }); assert.ok(chain.effects.length > 0);
    await call("effects", "set_effect_state", { scope: "track", track_index: await track("MCP video"), effect_index: 0, bypass: true });
  });
  await check("markers, regions and transport cursor", async () => {
    await call("timeline", "add_marker", { position_frames: 20, label: "MCP marker" });
    await call("timeline", "add_region", { position_frames: 5, length_frames: 20, label: "MCP region" });
    const inspected = await call("project", "inspect"); assert.ok(inspected.markers.length && inspected.regions.length);
    await call("project", "set_cursor", { cursor_frames: 25 }); await call("project", "set_selection", { start_frames: 0, length_frames: 50 });
  });
  await check("snapshot output and overwrite refusal", async () => {
    const output = path.join(fixture, "snapshot.png"); const snapshot = await call("project", "snapshot", { output_path: output, at_frames: 25 });
    assert.ok(snapshot.exists && fs.statSync(output).size > 100); await call("project", "snapshot", { output_path: output, at_frames: 25 }, /already exists/);
  });
  await check("renderer discovery and real asynchronous PCM WAV render", async () => {
    const list = await call("render", "list", { contains: "Wave" });
    const renderer = list.renderers.find(r => r.file_extensions.some(e => /wav/i.test(e))); assert.ok(renderer, "Wave renderer required");
    const template = renderer.templates.find(t => t.valid && t.audio_streams > 0); assert.ok(template);
    const output = path.join(fixture, "render.wav");
    const start = await call("render", "start", { renderer_class_id: renderer.class_id, template_guid: template.template_guid, output_path: output, start_frames: 0, length_frames: 50 });
    let job = start;
    for (let attempt = 0; job.active && attempt < 60; attempt++) { await new Promise(resolve => setTimeout(resolve, 500)); job = await call("render", "status", { job_id: start.job_id }); }
    assert.equal(job.active, false); assert.equal(job.status, "Complete"); assert.ok(job.output_bytes > 1000); report.render = job;
    await call("render", "start", { renderer_class_id: renderer.class_id, template_guid: template.template_guid, output_path: output }, /already exists/);
  });
  await check("real uncompressed AVI video/audio render and linked A/V group edits", async () => {
    const renderers = await call("render", "list", { contains: "Video for Windows" });
    const renderer = renderers.renderers.find(r => r.file_extensions.some(e => /avi/i.test(e))); assert.ok(renderer);
    const template = renderer.templates.find(t => /uncompressed/i.test(t.name) && t.video_streams > 0 && t.audio_streams > 0); assert.ok(template);
    const output = path.join(fixture, "render.avi");
    const start = await call("render", "start", { renderer_class_id: renderer.class_id, template_guid: template.template_guid, output_path: output, start_frames: 0, length_frames: 25 });
    let job = start;
    for (let attempt = 0; job.active && attempt < 90; attempt++) { await new Promise(resolve => setTimeout(resolve, 500)); job = await call("render", "status", { job_id: start.job_id }); }
    assert.equal(job.status, "Complete"); assert.ok(job.output_bytes > 10000); report.video_render = job;
    const added = await call("timeline", "insert_linked_media", { source_path: output, video_track_index: await track("MCP video"), audio_track_index: await track("MCP audio"), start_frames: 100, length_frames: 25, name: "linked" });
    assert.ok(added.grouped); assert.equal(added.events.length, 2); assert.ok(added.events.every(e => e.grouped));
    const groupId = () => identity("linked");
    await call("timeline", "move_event", { ...await groupId(), new_start_frames: 125 }, /grouped/);
    await call("timeline", "move_group", { ...await groupId(), new_start_frames: 125 });
    assert.ok((await events()).filter(e => e.name === "linked").every(e => e.start_frames === 125));
    const copy = await call("timeline", "copy_group", { ...await groupId(), new_start_frames: 175 }); assert.equal(copy.event_count, 2);
    const copied = copy.events[0];
    const deleted = await call("timeline", "delete_group", { event_id: copied.event_id, expected_start_frames: copied.start_frames, expected_length_frames: copied.length_frames }); assert.equal(deleted.deleted_count, 2);
    const split = await call("timeline", "split_group", { ...await groupId(), at_frames: 135 }); assert.equal(split.left_events.length, 2); assert.equal(split.right_events.length, 2);
    await call("project", "undo"); assert.equal((await events()).filter(e => e.name === "linked").length, 2);
  });
  await check("image-sequence insertion and source PCM output integrity", async () => {
    const inserted = await call("media", "insert_image_sequence", { first_image_path: bmp, image_count: 2, frame_rate: 25, track_index: await track("MCP video"), start_frames: 200, name: "sequence" });
    assert.equal(inserted.event.length_frames, 2);
    const buffer = fs.readFileSync(path.join(fixture, "render.wav")); assert.equal(buffer.toString("ascii", 0, 4), "RIFF"); assert.equal(buffer.toString("ascii", 8, 12), "WAVE");
    assert.equal(fs.readFileSync(path.join(fixture, "render.avi")).toString("ascii", 8, 12), "AVI ");
  });
  await check("save, explicit backup and source-media hashes", async () => {
    await call("project", "save"); await call("project", "backup", { reason: "acceptance-final" });
    assert.deepEqual([hash(bmp), hash(wav)], before); report.final_project = await call("project", "inspect");
    report.final_timeline = await timeline();
    assert.equal(report.final_project.is_modified, false);
  });
  await check("idle pipe client expires and listener accepts the next request", async () => {
    const elapsed = await new Promise((resolve, reject) => {
      const started = Date.now(); const socket = net.createConnection(`\\\\.\\pipe\\UnitedTaps.VegasMcp.${pid}`);
      socket.setTimeout(20000, () => socket.destroy(new Error("Idle peer did not expire.")));
      socket.on("error", error => error.code === "ECONNRESET" ? resolve(Date.now() - started) : reject(error));
      socket.on("close", () => resolve(Date.now() - started));
    });
    assert.ok(elapsed >= 14000 && elapsed < 20000, `Unexpected idle deadline ${elapsed}`);
    await call("project", "inspect");
  });
  report.passed = true;
} catch (error) { report.passed = false; report.error = error.stack; console.error(error.stack); process.exitCode = 1; }
finally { report.finished_utc = new Date().toISOString(); persist(); await client.close(); console.log(`Acceptance report: ${reportPath}`); }
