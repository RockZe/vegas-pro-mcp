import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { chooseInstance, listInstances } from "../src/pipe-client.mjs";
const host = { host_file_version: "2027.0.0.31", api_file_version: "2027.0.0.31", executable_path: "C:\\BorisFX\\Vegas Pro 2027\\vegas2027.exe" };

test("listInstances ignores malformed and stale records", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "vegas-mcp-test-"));
  const instances = path.join(root, "UnitedTaps.VegasMcp.instances");
  fs.mkdirSync(instances);
  fs.writeFileSync(path.join(instances, "bad.json"), "not json");
  fs.writeFileSync(path.join(instances, "1.json"), JSON.stringify({ pid: 1, pipe_name: "stale" }));
  fs.writeFileSync(path.join(instances, "2.json"), JSON.stringify({ ...host, pid: 2, pipe_name: "UnitedTaps.VegasMcp.2" }));

  assert.deepEqual(listInstances({
    installRoot: root,
    alive: (pid) => pid === 2,
    processMatches: (pid) => pid === 2,
  }), [
    { ...host, pid: 2, pipe_name: "UnitedTaps.VegasMcp.2" },
  ]);
});

test("listInstances rejects a reused live PID belonging to another process", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "vegas-mcp-pid-reuse-test-"));
  const instances = path.join(root, "UnitedTaps.VegasMcp.instances");
  fs.mkdirSync(instances);
  fs.writeFileSync(path.join(instances, "18376.json"), JSON.stringify({
    ...host,
    pid: 18376,
    pipe_name: "UnitedTaps.VegasMcp.18376",
  }));

  assert.deepEqual(listInstances({
    installRoot: root,
    alive: () => true,
    processMatches: () => false,
  }), []);
});

test("chooseInstance requires an explicit pid when more than one instance is live", () => {
  const instances = [{ ...host, pid: 10 }, { ...host, pid: 20 }];
  assert.throws(() => chooseInstance(instances), /Multiple VEGAS instances/);
  assert.equal(chooseInstance(instances, 20).pid, 20);
  assert.throws(() => chooseInstance(instances, 30), /No live VEGAS MCP instance/);
});

test("chooseInstance explains the restart requirement when no extension is live", () => {
  assert.throws(() => chooseInstance([]), /Start or restart VEGAS Pro 2027.0.0 Build 31/);
});

test("discovery refuses older, newer and unidentified VEGAS even if alive", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "vegas-target-"));
  const records = path.join(root, "UnitedTaps.VegasMcp.instances");
  fs.mkdirSync(records);
  const hosts = [ {}, { ...host, host_file_version: "2026.0.0.143" },
    { ...host, host_file_version: "2027.0.0.32" }, { ...host, api_file_version: "23.0.0.302" },
    { ...host, executable_path: "C:\\Other\\vegas230.exe" }, host ];
  for (const [index, value] of hosts.entries()) {
    const pid = index + 1;
    fs.writeFileSync(path.join(records, `${pid}.json`), JSON.stringify({ ...value, pid, pipe_name: `UnitedTaps.VegasMcp.${pid}` }));
  }
  const found = listInstances({ installRoot: root, alive: () => true, processMatches: () => true });
  assert.deepEqual(found.map(x => x.pid), [6]);
});

test("explicit pid cannot bypass the target host guard", () => {
  assert.throws(() => chooseInstance([{ pid: 20 }], 20), /Refusing incompatible/);
  assert.throws(() => chooseInstance([{ ...host, pid: 20, host_file_version: "2027.0.0.32" }]), /Refusing incompatible/);
});
