import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { callVegas } from "../src/pipe-client.mjs";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fixture = fs.readFileSync(path.join(root, "work/latest-live-fixture.txt"), "utf8").trim();
if (!path.resolve(fixture).startsWith(path.join(root, "work") + path.sep)) throw new Error("Unexpected fixture path.");
const pid = Number(fs.readFileSync(path.join(fixture, "bootstrap.ok"), "utf8"));
const status = await callVegas("status", { vegas_pid: pid });
const project = path.join(fixture, "acceptance.veg");
if (status.project_path.toLowerCase() !== project.toLowerCase()) throw new Error("Refusing to close VEGAS: active project is no longer the fixture.");
await callVegas("project.save", { vegas_pid: pid, project_path: project });
if (process.argv.includes("--terminate-fixture")) {
  const saved = await callVegas("status", { vegas_pid: pid });
  if (saved.project_path.toLowerCase() !== project.toLowerCase() || saved.is_modified) throw new Error("Fixture must still be active and saved before termination.");
  execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", `$p = Get-Process -Id ${pid} -ErrorAction Stop; Stop-Process -InputObject $p -ErrorAction Stop; if (-not $p.WaitForExit(15000)) { throw 'Fixture process did not exit.' }; $deadline = [DateTime]::UtcNow.AddSeconds(5); while (Get-Process -Id ${pid} -ErrorAction SilentlyContinue) { if ([DateTime]::UtcNow -ge $deadline) { throw 'Fixture process is still listed.' }; Start-Sleep -Milliseconds 100 }`], { stdio: "inherit", windowsHide: true });
  console.log("Closed the verified saved disposable fixture process.");
  process.exit(0);
}
execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", `$p = Get-Process -Id ${pid} -ErrorAction Stop; if (-not $p.CloseMainWindow()) { throw 'Could not close fixture window.' }; if (-not $p.WaitForExit(15000)) { throw 'Fixture editor is still open; inspect it manually.' }`], { stdio: "inherit", windowsHide: true });
console.log("Saved and closed the verified disposable VEGAS fixture.");
