"use strict";
const $ = id => document.getElementById(id);
const bridge = window.chrome?.webview;
let appState = { provider: "openai", model: "", effort: "default", hasKeys: {}, workspace: "" }, running = false, pendingApproval = null;
const modelsByProvider = { openai: [], anthropic: [] };
const labels = { default: "Model default", none: "None", minimal: "Minimal", low: "Low", medium: "Medium", high: "High", xhigh: "Extra High", max: "Max" };
function send(data) { if (bridge) bridge.postMessage(data); else showError("Open DeskAgent.exe to use the desktop tools."); }
function showError(text) { $("notice").textContent = text; $("notice").hidden = false; }
function hideError() { $("notice").hidden = true; }
function option(value, text) { const el = document.createElement("option"); el.value = value; el.textContent = text; return el; }
function populateModels() {
  const provider = $("provider").value, models = modelsByProvider[provider];
  $("modelList").replaceChildren(option("", "Choose a model or enter its ID below"), ...models.map(m => option(m.id, m.display_name ? `${m.display_name} · ${m.id}` : m.id)));
  $("modelList").value = $("modelId").value;
}
function efforts() {
  const id = $("modelId").value.trim(), p = $("provider").value;
  if (p === "anthropic") { const c = modelsByProvider[p].find(m => m.id === id)?.capabilities?.effort; return c?.supported ? ["low", "medium", "high", "xhigh", "max"].filter(e => c[e]?.supported) : []; }
  const base = id.replace(/-\d{4}-\d{2}-\d{2}$/, "");
  if (["gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6.2-astra", "gpt-6.2-sol"].includes(base)) return ["low", "medium", "high", "xhigh", "max"];
  if (base === "gpt-5.2") return ["none", "low", "medium", "high", "xhigh"];
  if (base === "gpt-5.1") return ["none", "low", "medium", "high"];
  if (["gpt-5", "gpt-5-mini", "gpt-5-nano"].includes(base)) return ["minimal", "low", "medium", "high"];
  if (["o1", "o3", "o3-mini", "o4-mini"].includes(base)) return ["low", "medium", "high"];
  return [];
}
function populateEfforts(preferred = "default") { const choices = ["default", ...efforts()]; $("effort").replaceChildren(...choices.map(e => option(e, labels[e]))); $("effort").value = choices.includes(preferred) ? preferred : "default"; }
function keyStatus() { const has = appState.hasKeys[$("provider").value]; $("keyStatus").textContent = has ? "A key is available. Leave this field blank to keep it." : "Your key is sent only to this provider. OpenAI and Claude need separate keys."; }
function openSettings() {
  $("provider").value = appState.provider; $("modelId").value = appState.model; $("apiKey").value = ""; $("remember").checked = !!appState.savedKeys?.[appState.provider];
  $("permissionMode").value = appState.autoRead ? "read" : "ask"; $("commands").checked = !!appState.commands; $("browser").checked = !!appState.browser;
  keyStatus(); populateModels(); populateEfforts(appState.effort); $("settings").showModal();
}
function addMessage(role, text) {
  $("welcome")?.remove(); const el = document.createElement("article"); el.className = "message " + role;
  const who = document.createElement("span"); who.className = "who"; who.textContent = role === "user" ? "YOU" : role === "error" ? "NEEDS ATTENTION" : "DESK AGENT";
  const body = document.createElement("div"); body.textContent = text; el.append(who, body); $("conversation").append(el); el.scrollIntoView({ block: "end" });
}
function logTool(name, text) { $("welcome")?.remove(); const el = document.createElement("details"); el.className = "tool-log"; const title = document.createElement("summary"); title.textContent = (/^(Denied|Tool error)/.test(text) ? "! " : "✓ ") + name; const result = document.createElement("pre"); result.textContent = text; el.append(title, result); $("conversation").append(el); el.scrollIntoView({ block: "end" }); }
function setRunning(value) {
  running = value; $("send").hidden = value; $("stop").hidden = !value; $("prompt").disabled = value;
  for (const id of ["newTask", "chooseFolder", "openSettings", "modelPill"]) $(id).disabled = value;
  if (!value) { pendingApproval = null; $("approval").hidden = true; }
}
function onMessage(data) {
  switch (data.type) {
    case "state":
      appState = data; $("folderPath").textContent = data.workspace;
      $("folderName").textContent = data.workspace ? data.workspace.split(/[\\/]/).filter(Boolean).at(-1) : "Choose a folder";
      $("modelPill").textContent = data.model ? `${data.provider === "openai" ? "GPT" : "Claude"} · ${data.model} · ${labels[data.effort]}` : "Choose a model ↗";
      if (!running) $("runStatus").textContent = data.workspace ? "Ready · approvals enabled" : "Choose a workspace to begin";
      keyStatus(); break;
    case "models":
      modelsByProvider[data.provider] = data.models; if (data.provider === $("provider").value) { populateModels(); populateEfforts(); $("modelsStatus").textContent = data.models.length + " models loaded. Select one below."; } break;
    case "modelsFinished": $("loadModels").disabled = false; $("loadModels").textContent = "Load models"; break;
    case "settingsSaved": $("apiKey").value = ""; $("settings").close(); hideError(); break;
    case "started": hideError(); setRunning(true); addMessage("user", data.text); $("prompt").value = ""; break;
    case "assistant": addMessage("assistant", data.text); break;
    case "tool-result": logTool(data.name, data.text); break;
    case "tool-start": $("runStatus").textContent = "Preparing " + data.name + "…"; break;
    case "status": $("runStatus").textContent = data.text; break;
    case "finished": setRunning(false); break;
    case "error": showError(data.text); if (running) addMessage("error", data.text); if ($("settings").open) $("modelsStatus").textContent = data.text; break;
    case "resetConversation": $("conversation").replaceChildren(); break;
    case "approval":
      pendingApproval = data.id; $("approvalTitle").textContent = data.summary; $("approvalWarning").textContent = data.warning; $("approvalDetails").textContent = data.details;
      $("approval").hidden = false; $("allow").disabled = false; $("deny").disabled = false; $("runStatus").textContent = "Waiting for your approval"; $("deny").focus(); break;
    case "approvalResolved": if (pendingApproval === data.id) { pendingApproval = null; $("approval").hidden = true; } break;
  }
}
if (bridge) bridge.addEventListener("message", event => onMessage(event.data));
$("chooseFolder").onclick = () => send({ type: "chooseFolder" });
$("newTask").onclick = () => send({ type: "newTask" });
$("showBrowser").onclick = () => send({ type: "showBrowser" });
$("openSettings").onclick = openSettings; $("modelPill").onclick = openSettings;
$("closeSettings").onclick = () => $("settings").close();
$("settings").addEventListener("close", () => { $("apiKey").value = ""; });
$("provider").onchange = () => { $("apiKey").value = ""; $("remember").checked = !!appState.savedKeys?.[$("provider").value]; $("modelId").value = ""; $("modelsStatus").textContent = ""; keyStatus(); populateModels(); populateEfforts(); };
$("modelList").onchange = () => { $("modelId").value = $("modelList").value; populateEfforts(); };
$("modelId").oninput = () => populateEfforts();
$("loadModels").onclick = () => { hideError(); $("loadModels").disabled = true; $("loadModels").textContent = "Loading…"; $("modelsStatus").textContent = "Loading from your provider…"; send({ type: "loadModels", provider: $("provider").value, apiKey: $("apiKey").value }); };
$("forgetKey").onclick = () => { $("apiKey").value = ""; send({ type: "forgetKey", provider: $("provider").value }); };
$("settingsForm").onsubmit = event => { event.preventDefault(); send({ type: "saveSettings", provider: $("provider").value, apiKey: $("apiKey").value, remember: $("remember").checked, model: $("modelId").value, effort: $("effort").value, autoRead: $("permissionMode").value === "read", commands: $("commands").checked, browser: $("browser").checked }); };
$("composer").onsubmit = event => { event.preventDefault(); if (!running && $("prompt").value.trim()) send({ type: "run", text: $("prompt").value.trim() }); };
$("prompt").addEventListener("keydown", event => { if (event.key === "Enter" && !event.shiftKey && !event.isComposing) { event.preventDefault(); $("composer").requestSubmit(); } });
$("stop").onclick = () => { send({ type: "stop" }); $("runStatus").textContent = "Stopping…"; };
for (const [id, allow] of [["allow", true], ["deny", false]]) $(id).onclick = () => { if (pendingApproval) { $("allow").disabled = true; $("deny").disabled = true; send({ type: "approval", id: pendingApproval, allow }); } };
for (const button of document.querySelectorAll("[data-prompt]")) button.onclick = () => { $("prompt").value = button.dataset.prompt; $("prompt").focus(); };
send({ type: "ready" });
