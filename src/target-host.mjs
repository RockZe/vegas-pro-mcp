export const TARGET_FILE_VERSION = "2027.0.0.31";

export function isTargetHost(instance) {
  return instance?.host_file_version === TARGET_FILE_VERSION
    && instance?.api_file_version === TARGET_FILE_VERSION
    && /(?:^|[\\/])vegas2027\.exe$/i.test(instance?.executable_path ?? "");
}

export function assertTargetHost(instance) {
  if (!isTargetHost(instance)) {
    throw new Error(`Refusing incompatible or unverified VEGAS instance. Required: VEGAS Pro 2027.0.0 Build 31 (${TARGET_FILE_VERSION}).`);
  }
}
