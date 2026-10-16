import assert from "node:assert/strict";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { TOOL_NAMES, domainActionCount } from "../src/action-catalog.mjs";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const transport = new StdioClientTransport({
  command: process.execPath,
  args: [path.join(projectRoot, "src", "server.mjs")],
  cwd: os.tmpdir(),
  stderr: "pipe",
});
const client = new Client({ name: "vegas-mcp-smoke", version: "1.0.0" });

try {
  await client.connect(transport);
  const listed = await client.listTools();
  const names = listed.tools.map((tool) => tool.name).sort();
  assert.deepEqual(names, [...TOOL_NAMES].sort());
  const capabilities = await client.callTool({ name: "vegas_capabilities", arguments: {} });
  assert.equal(capabilities.isError, undefined);
  assert.match(capabilities.content[0].text, /every_content_mutation_creates_a_versioned_veg_backup/);
  assert.equal(capabilities.structuredContent.domain_action_count, domainActionCount());
  const invalid = await client.callTool({ name: "vegas_timeline", arguments: { action: "delete_event", params: {} } });
  assert.equal(invalid.isError, true);
  assert.match(invalid.content[0].text, /project_path/);
  const ripple = await client.callTool({ name: "vegas_timeline", arguments: { action: "ripple_delete", params: { project_path: "C:\\Projects\\Test.veg" } } });
  assert.equal(ripple.isError, true);
  assert.match(ripple.content[0].text, /allow_ripple_delete/);
  console.log(JSON.stringify({ tools: names, domain_actions: domainActionCount(), protocol: "ok" }, null, 2));
} finally {
  await client.close();
}
