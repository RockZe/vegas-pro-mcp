import path from "node:path";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport, getDefaultEnvironment } from "@modelcontextprotocol/sdk/client/stdio.js";
import { assertTargetHost } from "../src/target-host.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const transport = new StdioClientTransport({
  command: process.execPath, args: [path.join(root, "src/server.mjs")], stderr: "inherit",
  env: { ...getDefaultEnvironment(), ...Object.fromEntries(Object.entries(process.env).filter(([k]) => k.startsWith("VEGAS_"))) },
});
const client = new Client({ name: "vegas-build31-doctor", version: "1.0.0" });
try {
  await client.connect(transport);
  const { tools } = await client.listTools();
  const status = await client.callTool({ name: "vegas_status", arguments: {} });
  if (status.isError) throw new Error(status.content[0].text);
  const data = status.structuredContent;
  if (!data?.connected) throw new Error(data?.needs_vegas_pid ? "Multiple matching instances: choose vegas_pid for a manual probe." : "No live Build 31 connector. Start or restart VEGAS after installation.");
  assertTargetHost(data);
  const results = { tools: tools.map(t => t.name), status: data };
  for (const [tool, action] of [["vegas_project", "inspect"], ["vegas_timeline", "inspect"], ["vegas_media", "list"], ["vegas_render", "status"]]) {
    const result = await client.callTool({ name: tool, arguments: { action, params: { vegas_pid: data.process_id } } });
    if (result.isError) throw new Error(`${tool}: ${result.content[0].text}`);
    results[tool] = result.structuredContent;
  }
  console.log(JSON.stringify(results, null, 2));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally { await client.close(); }
