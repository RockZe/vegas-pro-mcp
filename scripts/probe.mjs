import { callVegas, listInstances } from "../src/pipe-client.mjs";

const instances = listInstances();
console.log(JSON.stringify({ instances }, null, 2));
if (instances.length === 1) {
  console.log(JSON.stringify(await callVegas("status", {}), null, 2));
  console.log(JSON.stringify(await callVegas("project.inspect", {}), null, 2));
  console.log(JSON.stringify(await callVegas("timeline.inspect", { include_effects: false }), null, 2));
}
