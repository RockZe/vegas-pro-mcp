import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const extension = fs.readdirSync(path.join(projectRoot, "extension"))
  .filter((name) => name.endsWith(".cs"))
  .map((name) => fs.readFileSync(path.join(projectRoot, "extension", name), "utf8"))
  .join("\n");

test("connector source contains no permanent file deletion path", () => {
  assert.doesNotMatch(extension, /\bFile\.Delete\s*\(/);
  assert.doesNotMatch(extension, /\bDirectory\.Delete\s*\(/);
  assert.doesNotMatch(extension, /\bRemoveUnusedMedia\s*\(/);
  assert.doesNotMatch(extension, /\bReplaceWith\s*\(/);
  assert.doesNotMatch(extension, /MediaPool\.Values/);
});

test("content mutation backup is created before its undo block", () => {
  const actions = fs.readFileSync(path.join(projectRoot, "extension", "VegasActions.cs"), "utf8");
  const backup = actions.indexOf("string mutationBackup = CreateBackup");
  const undo = actions.indexOf("using (UndoBlock undo", backup);
  assert.ok(backup >= 0 && undo > backup);
});

test("render refuses overwrite before beginning the render", () => {
  const render = fs.readFileSync(path.join(projectRoot, "extension", "RenderManager.cs"), "utf8");
  assert.ok(render.indexOf("File.Exists(outputPath)") < render.indexOf("_vegas.BeginRender(args)"));
  assert.match(render, /StartsWith\("\*\."/);
  assert.match(render, /"audio_codec", SafeTemplateValue/);
  assert.match(render, /private static object SafeTemplateValue\(Func<object> reader\)/);
});

test("installer refuses every vegas-named process before copying the DLL", () => {
  const install = fs.readFileSync(path.join(projectRoot, "scripts", "install-extension.ps1"), "utf8");
  const helper = fs.readFileSync(path.join(projectRoot, "scripts", "vegas-installation.ps1"), "utf8");
  assert.match(helper, /\^vegas\.\*\$/i);
  assert.ok(install.indexOf("Assert-VegasClosed") < install.indexOf("Copy-Item -LiteralPath $distDll"));
  assert.match(helper, /Get-Process -ErrorAction Stop/);
});

test("native extension checks exact host before initialization and every dispatch", () => {
  const native = fs.readFileSync(path.join(projectRoot, "extension", "VegasMcpExtension.cs"), "utf8");
  assert.ok(native.indexOf("HostCompatibility.AssertCompatible()") < native.indexOf("_token = ReadToken()"));
  assert.ok(native.lastIndexOf("HostCompatibility.AssertCompatible()") < native.indexOf("_renderManager.Execute(action"));
  assert.match(native, /if \(!_enabled\) return new CustomCommand\[0\]/);
});

test("single-event structural edits refuse grouped events unless explicitly acknowledged", () => {
  const actions = fs.readFileSync(path.join(projectRoot, "extension", "VegasActions.cs"), "utf8");
  for (const action of ["move_event", "copy_event", "trim_event", "split_event", "delete_event"]) {
    assert.match(actions, new RegExp(`GuardSingleEventStructure\\(.*?"${action}"\\)`));
  }
  assert.match(actions, /allow_group_desync/);
  assert.match(actions, /allow_regroup/);
  assert.match(actions, /allow_partial_ungroup/);
});

test("loudness analysis drains both ffmpeg pipes before enforcing its timeout", () => {
  const audio = fs.readFileSync(path.join(projectRoot, "extension", "VegasAudioV2.cs"), "utf8");
  assert.match(audio, /StandardError\.ReadToEndAsync\(\)/);
  assert.match(audio, /StandardOutput\.ReadToEndAsync\(\)/);
  assert.ok(audio.indexOf("ReadToEndAsync") < audio.indexOf("WaitForExit(timeoutSeconds * 1000)"));
  assert.match(audio, /" -map 0:" \+ streamIndex/);
  assert.doesNotMatch(audio, /-map 0:a:/);
});

test("effect-chain animation preflight runs before target clearing or copying", () => {
  const effects = fs.readFileSync(path.join(projectRoot, "extension", "VegasEffectsV2.cs"), "utf8");
  const preflight = effects.indexOf("if (copyParameters && !allowFlatten)");
  const clear = effects.indexOf("target.Effects.Clear()", preflight);
  const copy = effects.indexOf("target.Effects.AddEffect", preflight);
  assert.ok(preflight >= 0 && preflight < clear && clear < copy);
  assert.match(effects, /sourceEffect\.PlugIn != null && sourceEffect\.PlugIn\.IsVideo/);
});

test("take metadata reads audio-only properties only for audio events", () => {
  const media = fs.readFileSync(path.join(projectRoot, "extension", "VegasMediaMotionV2.cs"), "utf8");
  const guard = media.indexOf("if (audio)");
  const firstChannel = media.indexOf('result["first_audio_channel_index"]', guard);
  const channelCount = media.indexOf('result["audio_channel_count"]', guard);
  assert.ok(guard >= 0 && firstChannel > guard && channelCount > firstChannel);
  assert.doesNotMatch(media, /TakeData\(take\)(?!\s*,)/);
});
