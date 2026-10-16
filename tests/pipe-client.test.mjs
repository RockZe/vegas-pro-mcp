import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { chooseInstance, listInstances } from "../src/pipe-client.mjs";

test("listInstances ignores malformed and stale records", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "vegas-mcp-test-"));
  const instances = path.join(root, "UnitedTaps.VegasMcp.instances");
  fs.mkdirSync(instances);
  fs.writeFileSync(path.join(instances, "bad.json"), "not json");
  fs.writeFileSync(path.join(instances, "1.json"), JSON.stringify({ pid: 1, pipe_name: "stale" }));
  fs.writeFileSync(path.join(instances, "2.json"), JSON.stringify({ pid: 2, pipe_name: "UnitedTaps.VegasMcp.2" }));

  assert.deepEqual(listInstances({
    installRoot: root,
    alive: (pid) => pid === 2,
    processMatches: (pid) => pid === 2,
  }), [
    { pid: 2, pipe_name: "UnitedTaps.VegasMcp.2" },
  ]);
});

test("listInstances rejects a reused live PID belonging to another process", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "vegas-mcp-pid-reuse-test-"));
  const instances = path.join(root, "UnitedTaps.VegasMcp.instances");
  fs.mkdirSync(instances);
  fs.writeFileSync(path.join(instances, "18376.json"), JSON.stringify({
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
  const instances = [{ pid: 10 }, { pid: 20 }];
  assert.throws(() => chooseInstance(instances), /Multiple VEGAS instances/);
  assert.equal(chooseInstance(instances, 20).pid, 20);
  assert.throws(() => chooseInstance(instances, 30), /No live VEGAS MCP instance/);
});

test("chooseInstance explains the restart requirement when no extension is live", () => {
  assert.throws(() => chooseInstance([]), /Start or restart VEGAS Pro 2026/);
});
