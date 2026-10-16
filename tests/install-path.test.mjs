import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { resolveInstallRoot } from "../src/install-path.mjs";

test("explicit installation root takes precedence without probing Windows", () => {
  assert.equal(resolveInstallRoot({ env: { VEGAS_MCP_INSTALL_ROOT: "X:/Custom" }, windowsDocuments: () => { throw Error("unexpected probe"); } }), "X:/Custom");
});

test("discovery uses Windows Known Folder even when OneDrive is configured", () => {
  assert.equal(resolveInstallRoot({ platform: "win32", env: { OneDrive: "X:/Unrelated" }, windowsDocuments: () => "D:/Redirected Documents" }), path.join("D:/Redirected Documents", "Vegas Application Extensions"));
});

test("failed Windows Known Folder resolution requires explicit configuration", () => {
  assert.throws(() => resolveInstallRoot({ platform: "win32", env: {}, windowsDocuments: () => "" }), /VEGAS_MCP_INSTALL_ROOT/);
});
