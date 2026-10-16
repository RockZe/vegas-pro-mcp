import os from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";

export function resolveInstallRoot({
  env = process.env,
  platform = process.platform,
  home = os.homedir(),
  windowsDocuments = () => execFileSync(
    "powershell.exe",
    ["-NoProfile", "-NonInteractive", "-Command",
      "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); [Environment]::GetFolderPath('MyDocuments')"],
    { encoding: "utf8", windowsHide: true, timeout: 10_000 },
  ).trim(),
} = {}) {
  if (env.VEGAS_MCP_INSTALL_ROOT) return env.VEGAS_MCP_INSTALL_ROOT;
  // Match the installer's Windows Known Folder, including redirected Documents.
  // Guessing from OneDrive's presence can silently select the wrong installation.
  const documents = platform === "win32" ? windowsDocuments() : path.join(home, "Documents");
  if (!documents) throw new Error("Windows Documents could not be resolved. Set VEGAS_MCP_INSTALL_ROOT explicitly.");
  return path.join(documents, "Vegas Application Extensions");
}
