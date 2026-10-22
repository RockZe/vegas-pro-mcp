// MCP stdout belongs exclusively to JSON-RPC. Never log params, tokens or paths.
export function diagnostic(level, action, extra = {}) {
  if (level === "info" && process.env.VEGAS_MCP_LOG_LEVEL !== "info") return;
  process.stderr.write(JSON.stringify({ time: new Date().toISOString(), level, action, ...extra }) + "\n");
}
