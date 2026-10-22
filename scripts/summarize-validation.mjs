// Produce a shareable summary; never publish the private full runtime report.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { ACTIONS } from "../src/action-catalog.mjs";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fixture = fs.readFileSync(path.join(root, "work/latest-live-fixture.txt"), "utf8").trim();
if (!path.resolve(fixture).startsWith(path.join(root, "work") + path.sep)) throw new Error("Unexpected fixture path.");
const report = JSON.parse(fs.readFileSync(path.join(fixture, "acceptance-report.json"), "utf8"));
const backup = JSON.parse(fs.readFileSync(path.join(fixture, "backup-reopen-report.json"), "utf8"));
if (!report.passed || !backup.passed) throw new Error("Successful native acceptance and backup reopening are required.");
const all = Object.entries(ACTIONS).flatMap(([domain, actions]) => actions.map(action => `${domain}.${action}`));
const exercised = [...new Set(report.calls.filter(c => !c.expected_refusal).map(c => c.action))].sort();
const summary = {
  version: JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8")).version,
  validation_date: report.finished_utc.slice(0, 10),
  host_file_version: report.host.host_file_version, api_file_version: report.host.api_file_version,
  reported_host: report.host.vegas_version, connector_version: report.host.connector_version,
  node_version: process.version, transport: "MCP STDIO -> authenticated same-user Windows named pipe -> native extension",
  native_scenarios_passed: report.checks.length, domain_tool_calls: report.calls.length,
  successful_direct_domain_actions: exercised,
  additional_batch_actions: ["timeline.set_fades"],
  actions_without_native_successful_execution: all.filter(action => !exercised.includes(action) && action !== "timeline.set_fades"),
  native_checks: report.checks.map(c => ({ name: c.name, passed: c.passed })),
  ofx_plugin: { name: report.ofx_plugin.name, unique_id: report.ofx_plugin.unique_id },
  renders: [report.render, report.video_render].map(r => ({ renderer: r.renderer, template: r.template, status: r.status, bytes: r.output_bytes })),
  backup_reopened_and_timeline_matched: true,
  excluded_from_this_summary: "Private absolute paths, tokens, full project metadata, request parameters and runtime logs",
};
const output = path.join(root, "docs/vegas2027-validation.json");
fs.writeFileSync(output, JSON.stringify(summary, null, 2) + "\n");
console.log(`Saved sanitized validation: ${exercised.length} direct actions, ${report.checks.length} native scenarios.`);
