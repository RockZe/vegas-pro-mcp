// Execute the production range-edit method against a small deterministic timeline
// model. This verifies its algorithm; it does not emulate VEGAS's native engine.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

if (process.platform !== "win32") throw new Error("Native algorithm tests require Windows and the .NET Framework C# compiler.");
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = fs.readFileSync(path.join(root, "extension", "VegasActions.cs"), "utf8");
const start = source.indexOf("private static object RippleDeleteRange(");
const end = source.indexOf("private static object SplitEvent(", start);
assert.ok(start >= 0 && end > start, "Production range-delete method was not found");
const template = fs.readFileSync(path.join(root, "tests", "native", "ripple-harness.cs"), "utf8");
fs.mkdirSync(path.join(root, "work"), { recursive: true });
const output = fs.mkdtempSync(path.join(root, "work", "native-"));
const harness = path.join(output, "ripple.cs");
const executable = path.join(output, "ripple.exe");
fs.writeFileSync(harness, template.replace("// PRODUCTION_METHOD", source.slice(start, end)));
const compiler = path.join(process.env.WINDIR, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
for (const [command, args] of [[compiler, ["/nologo", `/out:${executable}`, harness]], [executable, []]]) {
  const result = spawnSync(command, args, { encoding: "utf8", windowsHide: true });
  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  assert.equal(result.status, 0, result.error?.message || "Native algorithm check failed");
}
