import assert from "node:assert/strict";
import fs from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import test from "node:test";
import { callVegasPipe } from "../src/pipe-client.mjs";

async function fixture(handler, operation) {
  const pipePath = process.platform === "win32"
    ? `\\\\.\\pipe\\vegas-mcp-test-${crypto.randomUUID()}`
    : path.join(fs.mkdtempSync(path.join(os.tmpdir(), "vegas-transport-")), "pipe.sock");
  const sockets = new Set();
  const server = net.createServer((socket) => {
    sockets.add(socket);
    socket.on("error", () => {});
    socket.on("close", () => sockets.delete(socket));
    let request = "";
    socket.on("data", (data) => {
      request += data;
      if (request.includes("\n")) handler(socket, JSON.parse(request));
    });
  });
  await new Promise((resolve, reject) => { server.once("error", reject); server.listen(pipePath, resolve); });
  try {
    await operation(() => callVegasPipe({ instance: {}, pipePath, action: "project.inspect", params: {}, token: "test-only-token", timeoutMs: 1000 }));
  } finally {
    for (const socket of sockets) socket.destroy();
    await new Promise((resolve) => server.close(resolve));
  }
}

test("pipe transport accepts a fragmented matching response", async () => {
  await fixture((socket, request) => {
    assert.equal(request.action, "project.inspect");
    assert.ok(Date.parse(request.expires_at_utc) > Date.now());
    const response = JSON.stringify({ request_id: request.request_id, ok: true, data: { connected: true } });
    socket.write(response.slice(0, 12));
    setTimeout(() => socket.end(response.slice(12) + "\n"), 5);
  }, async (call) => assert.deepEqual(await call(), { connected: true }));
});

test("pipe transport rejects response identity mismatches", async () => {
  await fixture((socket) => socket.end(JSON.stringify({ request_id: "wrong", ok: true }) + "\n"),
    async (call) => assert.rejects(call(), /mismatched request/));
});

test("pipe transport surfaces native rejection and early disconnect", async () => {
  await fixture((socket, request) => socket.end(JSON.stringify({ request_id: request.request_id, ok: false, error: { message: "Active project changed" } }) + "\n"),
    async (call) => assert.rejects(call(), /Active project changed/));
  await fixture((socket) => socket.end(), async (call) => assert.rejects(call(), /without a response/));
});

test("pipe transport rejects a peer that never answers", async () => {
  await fixture(() => {}, async (call) => assert.rejects(call(), /did not answer/));
});

test("pipe connection retries a reopening listener before sending exactly once", async () => {
  const pipePath = process.platform === "win32" ? `\\\\.\\pipe\\vegas-mcp-late-${crypto.randomUUID()}`
    : path.join(fs.mkdtempSync(path.join(os.tmpdir(), "vegas-late-")), "pipe.sock");
  let received = 0;
  const server = net.createServer(socket => {
    socket.on("data", data => {
      received++;
      const request = JSON.parse(data.toString());
      socket.end(JSON.stringify({ request_id: request.request_id, ok: true, data: { once: true } }) + "\n");
    });
  });
  const call = callVegasPipe({ instance: {}, pipePath, action: "timeline.add_track", params: {}, token: "test", timeoutMs: 2000 });
  await new Promise(resolve => setTimeout(resolve, 150));
  await new Promise(resolve => server.listen(pipePath, resolve));
  try { assert.deepEqual(await call, { once: true }); assert.equal(received, 1); }
  finally { await new Promise(resolve => server.close(resolve)); }
});
