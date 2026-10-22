#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import fs from "node:fs";
import { callVegas, listInstances } from "./pipe-client.mjs";
import { ACTIONS, domainActionCount } from "./action-catalog.mjs";
import { validateRequest } from "./request-validation.mjs";
import { TARGET_FILE_VERSION } from "./target-host.mjs";
import { diagnostic } from "./diagnostics.mjs";

const server = new McpServer({
  name: "vegas-pro-2027-build31",
  version: JSON.parse(fs.readFileSync(new URL("../package.json", import.meta.url), "utf8")).version,
});

const ParamsSchema = z.record(z.string(), z.unknown()).optional().default({});

function textResult(value) {
  return {
    content: [{ type: "text", text: JSON.stringify(value, null, 2) }],
    structuredContent: value,
  };
}

async function run(action, params) {
  const started = Date.now();
  try {
    validateRequest(action, params);
    const data = await callVegas(action, params);
    diagnostic("info", action, { elapsed_ms: Date.now() - started });
    return textResult(data);
  } catch (error) {
    diagnostic("error", action, { error_type: error?.name ?? "Error", elapsed_ms: Date.now() - started });
    return {
      isError: true,
      content: [{ type: "text", text: error instanceof Error ? error.message : String(error) }],
    };
  }
}

server.registerTool(
  "vegas_capabilities",
  {
    description: "List the installed VEGAS connector actions and their safety contract.",
    inputSchema: {},
  },
  async () =>
    textResult({
      safety: {
        local_only: true,
        source_media_is_never_modified: true,
        content_mutations_require_exact_project_path: true,
        event_mutations_require_event_id_and_expected_start_and_length: true,
        every_content_mutation_creates_a_versioned_veg_backup: true,
        every_content_mutation_is_a_named_VEGAS_undo_step: true,
        batch_content_mutations_share_one_backup_and_one_undo_step: true,
        render_and_snapshot_outputs_refuse_overwrite: true,
      },
      domain_action_count: domainActionCount(),
      required_host_file_version: TARGET_FILE_VERSION,
      tools: {
        vegas_status: ["list live instances", "inspect active project and transport"],
        vegas_project: ACTIONS.project,
        vegas_timeline: ACTIONS.timeline,
        vegas_audio: ACTIONS.audio,
        vegas_effects: ACTIONS.effects,
        vegas_media: ACTIONS.media,
        vegas_motion: ACTIONS.motion,
        vegas_render: ACTIONS.render,
      },
    }),
);

server.registerTool(
  "vegas_status",
  {
    description: "Discover and inspect VEGAS Pro 2027.0.0 Build 31 only. Other VEGAS versions are refused. Pass vegas_pid when multiple matching instances are open.",
    inputSchema: { vegas_pid: z.number().int().positive().optional() },
  },
  async ({ vegas_pid }) => {
    let instances;
    try { instances = listInstances(); }
    catch (error) { return { isError: true, content: [{ type: "text", text: error.message }] }; }
    if (instances.length === 0) return textResult({ live: false, instances: [] });
    if (vegas_pid === undefined && instances.length > 1) {
      return textResult({ live: true, instances, needs_vegas_pid: true });
    }
    return run("status", { vegas_pid });
  },
);

server.registerTool(
  "vegas_project",
  {
    description: "Inspect, back up, save, undo/redo, control transport, set the cursor, or save a preview snapshot. Mutating actions require params.project_path exactly as returned by inspect.",
    inputSchema: {
      action: z.enum(ACTIONS.project),
      params: ParamsSchema,
    },
  },
  async ({ action, params }) => run(`project.${action}`, params),
);

server.registerTool(
  "vegas_timeline",
  {
    description: "Perform guarded timeline inspection and editing. Inspect first. Existing-event mutations require event_id, expected_start_seconds, expected_length_seconds, and exact project_path.",
    inputSchema: {
      action: z.enum(ACTIONS.timeline),
      params: ParamsSchema,
    },
  },
  async ({ action, params }) => run(`timeline.${action}`, params),
);

server.registerTool(
  "vegas_audio",
  {
    description: "Perform guarded native VEGAS audio operations. Event mutations require exact event identity and prior start/length values from timeline.inspect.",
    inputSchema: {
      action: z.enum(ACTIONS.audio),
      params: ParamsSchema,
    },
  },
  async ({ action, params }) => run(`audio.${action}`, params),
);

server.registerTool(
  "vegas_effects",
  {
    description: "Inspect or change VEGAS/OFX event effects. Effect mutations require exact event identity and create a project backup plus an undo step.",
    inputSchema: {
      action: z.enum(ACTIONS.effects),
      params: ParamsSchema,
    },
  },
  async ({ action, params }) => run(`effects.${action}`, params),
);

server.registerTool(
  "vegas_media",
  {
    description: "Inspect and manage project media, bins, image sequences, event takes, and guarded media replacement.",
    inputSchema: { action: z.enum(ACTIONS.media), params: ParamsSchema },
  },
  async ({ action, params }) => run(`media.${action}`, params),
);

server.registerTool(
  "vegas_motion",
  {
    description: "Inspect and change guarded event pan/crop motion and video-track motion keyframes.",
    inputSchema: { action: z.enum(ACTIONS.motion), params: ParamsSchema },
  },
  async ({ action, params }) => run(`motion.${action}`, params),
);

server.registerTool(
  "vegas_render",
  {
    description: "List renderers/templates and start, inspect, or cancel a guarded asynchronous VEGAS render. Outputs never overwrite existing files.",
    inputSchema: { action: z.enum(ACTIONS.render), params: ParamsSchema },
  },
  async ({ action, params }) => run(`render.${action}`, params),
);

const transport = new StdioServerTransport();
await server.connect(transport);
