import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import { resolveInstallRoot } from "./install-path.mjs";
import { assertTargetHost, isTargetHost } from "./target-host.mjs";

export const INSTALL_ROOT = resolveInstallRoot();

export function isProcessAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error?.code === "EPERM";
  }
}

export function isVegasProcess(pid) {
  if (!Number.isInteger(pid) || pid <= 0 || process.platform !== "win32") return false;
  try {
    const output = execFileSync(
      "tasklist.exe",
      ["/FI", `PID eq ${pid}`, "/FO", "CSV", "/NH"],
      { encoding: "utf8", windowsHide: true, timeout: 10_000 },
    );
    const record = output.split(/\r?\n/).map((line) => line.trim()).find((line) => line.startsWith('"'));
    const match = record && /^"([^"]+)","(\d+)"/.exec(record);
    return Boolean(match && Number(match[2]) === pid && /^vegas2027\.exe$/i.test(match[1]));
  } catch {
    return false;
  }
}

export function listInstances({
  installRoot = INSTALL_ROOT,
  alive = isProcessAlive,
  processMatches = isVegasProcess,
} = {}) {
  const instancesRoot = path.join(installRoot, "UnitedTaps.VegasMcp.instances");
  if (!fs.existsSync(instancesRoot)) return [];

  const instances = [];
  for (const name of fs.readdirSync(instancesRoot)) {
    if (!name.endsWith(".json")) continue;
    try {
      const value = JSON.parse(fs.readFileSync(path.join(instancesRoot, name), "utf8"));
      const pid = Number(value.pid);
      const expectedName = `${pid}.json`;
      const expectedPipe = `UnitedTaps.VegasMcp.${pid}`;
      if (name === expectedName && value.pipe_name === expectedPipe && isTargetHost(value) && alive(pid) && processMatches(pid)) {
        instances.push({ ...value, pid });
      }
    } catch {
      // Ignore malformed or stale discovery records. They are never deleted automatically.
    }
  }
  return instances.sort((a, b) => a.pid - b.pid);
}

export function chooseInstance(instances, requestedPid) {
  if (requestedPid !== undefined && requestedPid !== null) {
    const pid = Number(requestedPid);
    const match = instances.find((item) => item.pid === pid);
    if (!match) {
      throw new Error(`No live VEGAS MCP instance matches vegas_pid=${requestedPid}.`);
    }
    assertTargetHost(match);
    return match;
  }
  if (instances.length === 0) {
    throw new Error(
      "No live VEGAS MCP extension was found. Start or restart VEGAS Pro 2027.0.0 Build 31 after installing the connector.",
    );
  }
  if (instances.length > 1) {
    throw new Error(
      `Multiple VEGAS instances are running (${instances.map((x) => x.pid).join(", ")}). Pass vegas_pid explicitly.`,
    );
  }
  assertTargetHost(instances[0]);
  return instances[0];
}

export function readToken(installRoot) {
  const tokenPath = process.env.VEGAS_MCP_TOKEN_FILE || path.join(installRoot, "UnitedTaps.VegasMcp.token");
  const token = fs.readFileSync(tokenPath, "utf8").trim();
  if (token.length < 32) throw new Error("The VEGAS MCP connector token is missing or invalid.");
  return token;
}

export async function callVegas(
  action,
  params = {},
  {
    installRoot = INSTALL_ROOT,
    timeoutMs = 180_000,
    instances = listInstances({ installRoot }),
  } = {},
) {
  const instance = chooseInstance(instances, params.vegas_pid);
  const cleanParams = { ...params };
  delete cleanParams.vegas_pid;
  const token = readToken(installRoot);
  return await callVegasPipe({ instance, action, params: cleanParams, token, timeoutMs });
}

export async function callVegasPipe({ instance, action, params, token, timeoutMs, pipePath = `\\\\.\\pipe\\${instance.pipe_name}` }) {
  const request = {
    request_id: crypto.randomUUID(),
    expires_at_utc: new Date(Date.now() + timeoutMs).toISOString(),
    token,
    action,
    params,
  };

  return await new Promise((resolve, reject) => {
    let socket;
    let connectRetry;
    const connectDeadline = Date.now() + Math.min(timeoutMs, 1500);
    let buffer = "";
    let settled = false;

    const finish = (fn, value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      clearTimeout(connectRetry);
      socket?.destroy();
      fn(value);
    };

    const timer = setTimeout(() => {
      finish(reject, new Error(`VEGAS did not answer ${action} within ${timeoutMs} ms.`));
    }, timeoutMs);

    function connect() {
      if (settled) return;
      socket = net.createConnection(pipePath);
      const current = socket;
      let connected = false;
      socket.setEncoding("utf8");
      socket.on("connect", () => { connected = true; socket.write(`${JSON.stringify(request)}\n`); });
      socket.on("data", (chunk) => {
        buffer += chunk;
        if (Buffer.byteLength(buffer, "utf8") > 8 * 1024 * 1024) {
          finish(reject, new Error("VEGAS response exceeded the 8 MB connector limit."));
          return;
        }
        const newline = buffer.indexOf("\n");
        if (newline < 0) return;
        try {
          const response = JSON.parse(buffer.slice(0, newline));
          if (response.request_id !== request.request_id) {
            throw new Error("VEGAS returned a mismatched request identifier.");
          }
          if (!response.ok) {
            throw new Error(response?.error?.message || "VEGAS rejected the command.");
          }
          finish(resolve, response.data);
        } catch (error) {
          finish(reject, error);
        }
      });
      socket.on("error", (error) => {
        // A single-instance native pipe has a brief gap while its listener reopens.
        // Retry only a connection that NEVER connected or sent bytes. Never replay
        // an edit after a timeout, disconnection or an uncertain execution result.
        if (!connected && ["ENOENT", "ECONNREFUSED", "EBUSY"].includes(error.code) && Date.now() < connectDeadline) {
          current.destroy();
          connectRetry = setTimeout(connect, 50);
        } else finish(reject, error);
      });
      socket.on("end", () => {
        if (!settled) finish(reject, new Error("VEGAS closed the connector pipe without a response."));
      });
    }
    connect();
  });
}
