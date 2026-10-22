// Opt-in verification of a backup created solely by the disposable acceptance suite.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { callVegas, listInstances } from "../src/pipe-client.mjs";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fixture = fs.readFileSync(path.join(root, "work/latest-live-fixture.txt"), "utf8").trim();
assert.ok(path.resolve(fixture).startsWith(path.join(root, "work") + path.sep));
const report = JSON.parse(fs.readFileSync(path.join(fixture, "acceptance-report.json"), "utf8"));
assert.equal(report.passed, true);
const backup = report.calls.findLast(c => c.action === "project.backup").result.structuredContent.backup_path;
const expected = report.final_timeline ?? report.calls.findLast(c => c.action === "timeline.inspect").result.structuredContent;
if (!report.final_timeline) {
  const inserted = report.calls.findLast(c => c.action === "media.insert_image_sequence")?.result.structuredContent.event;
  if (inserted && !expected.tracks.flatMap(t => t.events).some(e => e.event_id === inserted.event_id)) expected.tracks[inserted.track_index].events.push(inserted);
}
const launch = project => execFileSync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path.join(root, "scripts/start-vegas.ps1"), "-ProjectPath", project], { stdio: "inherit", windowsHide: true });
const normalize = snapshot => snapshot.tracks.map(t => ({ name: t.name, kind: t.kind,
  events: t.events.map(e => ({ name: e.name, start: e.start_frames, length: e.length_frames, media: e.media_path, grouped: e.grouped })) }));
let alreadyOpen = false;
for (const instance of listInstances()) {
  const current = await callVegas("status", { vegas_pid: instance.pid });
  if (current.project_path?.toLowerCase() === backup.toLowerCase() && !current.is_modified) alreadyOpen = true;
}
if (!alreadyOpen) {
  execFileSync(process.execPath, [path.join(root, "scripts/close-live-test.mjs"), "--terminate-fixture"], { stdio: "inherit", windowsHide: true });
  launch(backup);
}
const deadline = Date.now() + 45000;
let status;
while (Date.now() < deadline) {
  const instance = listInstances().find(i => i.pid !== report.pid);
  if (instance) {
    status = await callVegas("status", { vegas_pid: instance.pid });
    if (status.project_path?.toLowerCase() === backup.toLowerCase()) break;
  }
  await new Promise(resolve => setTimeout(resolve, 250));
}
assert.equal(status?.project_path?.toLowerCase(), backup.toLowerCase(), "Backup did not open in the native editor.");
assert.equal(status.is_modified, false);
const actual = await callVegas("timeline.inspect", { vegas_pid: status.process_id, project_path: backup });
assert.deepEqual(normalize(actual), normalize(expected));
fs.writeFileSync(path.join(fixture, "backup-reopen-report.json"), JSON.stringify({ passed: true, checked_utc: new Date().toISOString(), status, timeline: normalize(actual) }, null, 2));
const final = await callVegas("status", { vegas_pid: status.process_id });
assert.equal(final.project_path, backup); assert.equal(final.is_modified, false);
execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", `$p = Get-Process -Id ${status.process_id} -ErrorAction Stop; Stop-Process -InputObject $p -ErrorAction Stop; if (-not $p.WaitForExit(15000)) { throw 'Fixture process did not exit.' }`], { stdio: "inherit", windowsHide: true });
launch(path.join(fixture, "acceptance.veg"));
const reopenDeadline = Date.now() + 45000;
while (Date.now() < reopenDeadline) {
  const instance = listInstances().find(i => i.pid !== status.process_id);
  if (instance) {
    const current = await callVegas("status", { vegas_pid: instance.pid });
    if (current.project_path?.toLowerCase() === path.join(fixture, "acceptance.veg").toLowerCase()) {
      fs.writeFileSync(path.join(fixture, "bootstrap.ok"), String(instance.pid));
      break;
    }
  }
  await new Promise(resolve => setTimeout(resolve, 250));
}
console.log("PASS native backup reopening: tracks, event ranges, media paths and grouping match; original disposable project reopened for inspection.");
