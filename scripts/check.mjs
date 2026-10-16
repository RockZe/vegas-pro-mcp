import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
for (const directory of ["src", "scripts", "tests"]) {
  for (const name of fs.readdirSync(path.join(root, directory))) {
    if (!name.endsWith(".mjs")) continue;
    const result = spawnSync(process.execPath, ["--check", path.join(root, directory, name)], { stdio: "inherit" });
    if (result.status !== 0) process.exit(result.status || 1);
  }
}
const tests = fs.readdirSync(path.join(root, "tests"))
  .filter((name) => name.endsWith(".test.mjs"))
  .map((name) => path.join(root, "tests", name));
const result = spawnSync(process.execPath, ["--test", ...tests], { stdio: "inherit" });
process.exitCode = result.status || (result.error ? 1 : 0);
