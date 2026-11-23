"use strict";
const $ = id => document.getElementById(id);
const safe = (fn, fallback = null) => { try { return fn(); } catch { return fallback; } };
const providers = {
  openai: { name: "OpenAI", base: "https://api.openai.com/v1", keys: "https://platform.openai.com/api-keys" },
  anthropic: { name: "Claude", base: "https://api.anthropic.com/v1", keys: "https://platform.claude.com/settings/keys" },
};
const state = Object.fromEntries(Object.keys(providers).map(p => [p, {
  key: safe(() => sessionStorage.getItem(p + "-key")) || safe(() => localStorage.getItem(p + "-key")) || "",
  model: safe(() => localStorage.getItem(p + "-model")) || "",
  models: [], history: [], effort: "default", status: "", error: false, loading: false, request: null,
}]));
let provider = safe(() => localStorage.getItem("chat-provider")) || "openai";
if (!providers[provider]) provider = "openai";
let active = null;
let keyProvider = provider;
const effortLabels = { default: "Model default", none: "None", minimal: "Minimal", low: "Low", medium: "Medium", high: "High", xhigh: "Extra High", max: "Max" };
const effortOrder = ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

// Conservative mappings: unknown models use the API default, never guessed parameters.
// Sources: OpenAI model reference; Anthropic's /models capability metadata.
function supportedEfforts(p, id) {
  if (p === "anthropic") {
    const cap = state[p].models.find(m => m.id === id)?.capabilities?.effort;
    return cap?.supported ? effortOrder.filter(e => cap[e]?.supported) : [];
  }
  const base = id.replace(/-\d{4}-\d{2}-\d{2}$/, "");
  if (["gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6.2-astra", "gpt-6.2-sol"].includes(base)) return ["low", "medium", "high", "xhigh", "max"];
  if (base === "gpt-5.2") return ["none", "low", "medium", "high", "xhigh"];
  if (base === "gpt-5.1") return ["none", "low", "medium", "high"];
  if (["gpt-5", "gpt-5-mini", "gpt-5-nano"].includes(base)) return ["minimal", "low", "medium", "high"];
  if (["o1", "o3", "o3-mini", "o4-mini"].includes(base)) return ["low", "medium", "high"];
  return [];
}
function modelName(p, id) {
  const name = state[p].models.find(m => m.id === id)?.display_name;
  if (name) return name;
  if (/^gpt-6(?:\.[12])?-(astra|sol|terra|luna)$/.test(id)) return id.replace(/^gpt-/, "GPT-").replace(/-(astra|sol|terra|luna)$/, (_, word) => " " + word[0].toUpperCase() + word.slice(1));
  return id || "Choose a model";
}
function setOptions(select, options) {
  select.replaceChildren(...options.map(([value, label]) => {
    const option = document.createElement("option"); option.value = value; option.textContent = label; return option;
  }));
}
function updateEffort() {
  const s = state[provider], choices = ["default", ...supportedEfforts(provider, s.model)];
  if (!choices.includes(s.effort)) s.effort = "default";
  setOptions($("effortSelect"), choices.map(e => [e, effortLabels[e]]));
  $("effortSelect").value = s.effort;
  $("effortSelect").disabled = choices.length === 1;
  $("effortRange").max = choices.length - 1;
  $("effortRange").value = choices.indexOf(s.effort);
  $("effortRange").disabled = choices.length === 1;
  $("effortRange").setAttribute("aria-valuetext", effortLabels[s.effort]);
  $("effortNote").textContent = choices.length > 1
    ? "Higher effort can take longer and use more paid tokens. Model default lets the provider decide."
    : "This model uses its default effort; adjustable effort is not supported or has not been verified.";
  $("currentModel").textContent = providers[provider].name + " · " + modelName(provider, s.model);
  $("currentEffort").textContent = effortLabels[s.effort];
  $("openModels").setAttribute("aria-label", "Choose model and effort. " + $("currentModel").textContent + ", " + effortLabels[s.effort]);
  $("chatContext").textContent = s.model ? modelName(provider, s.model) + " · " + effortLabels[s.effort] : "Choose your provider and model to start.";
}
function updatePicker() {
  const s = state[provider];
  $("providerSelect").value = provider;
  const items = s.models.map(m => [m.id, m.display_name ? m.display_name + " · " + m.id : m.id]);
  if (s.model && !s.models.some(m => m.id === s.model)) items.unshift([s.model, s.model + " (access not checked)"]);
  if (!items.length) items.push(["", s.key ? "No models loaded — refresh or enter an ID" : "Add your API key to load models"]);
  setOptions($("modelSelect"), items);
  $("modelSelect").value = s.model;
  $("modelSelect").disabled = !s.model && !s.models.length;
  $("modelStatus").textContent = s.status || (s.key ? "Refresh to load models from your account." : "Add your " + providers[provider].name + " API key to load its models.");
  $("modelStatus").className = s.error ? "muted error" : "muted";
  $("refreshModels").disabled = s.loading || !s.key;
  $("refreshModels").textContent = s.loading ? "Loading…" : "Refresh models";
  $("pickerKey").textContent = (s.key ? "Change " : "Add ") + providers[provider].name + " API key";
  $("customModel").placeholder = provider === "openai" ? "For example, gpt-6-astra" : "Exact Claude model ID";
  updateEffort();
}
function headersFor(p, key) {
  return p === "openai" ? { Authorization: "Bearer " + key } : {
    "x-api-key": key, "anthropic-version": "2023-06-01", "anthropic-dangerous-direct-browser-access": "true",
  };
}
function friendlyError(p, status, body) {
  let message = body;
  try { message = JSON.parse(body).error?.message || body; } catch {}
  if (/CORS requests are not allowed/i.test(message)) return "This Claude organization blocks direct browser requests. It needs a backend connection. " + message;
  if (status === 401) return providers[p].name + " rejected the API key. Open API key to replace it. " + message;
  if (status === 403) return "This key does not have permission. Check its model and API permissions. " + message;
  if (status === 429) return providers[p].name + " rate limit or credit limit reached. " + message;
  return "Error " + status + ": " + message;
}
function networkError(p, error) {
  return error instanceof TypeError ? "Could not reach " + providers[p].name + ". Check your connection, browser extensions, and whether your organization permits browser API access." : error.message;
}
async function loadModels(p = provider) {
  const s = state[p];
  s.request?.abort();
  s.loading = false;
  if (!s.key) { s.models = []; s.status = ""; s.error = false; if (p === provider) updatePicker(); return; }
  const request = new AbortController(), key = s.key;
  s.request = request; s.loading = true; s.error = false; s.status = "Loading models from " + providers[p].name + "…";
  if (p === provider) updatePicker();
  const timeout = setTimeout(() => request.abort(), 30000);
  try {
    let models = [], after = "";
    const cursors = new Set();
    do {
      const query = p === "anthropic" ? "?limit=100" + (after ? "&after_id=" + encodeURIComponent(after) : "") : "";
      const res = await fetch(providers[p].base + "/models" + query, { headers: headersFor(p, key), signal: request.signal, redirect: "error", cache: "no-store" });
      if (!res.ok) throw new Error(friendlyError(p, res.status, await res.text()));
      const data = await res.json();
      if (!Array.isArray(data.data)) throw new Error("The provider returned an unexpected model list.");
      models.push(...data.data.filter(m => typeof m.id === "string"));
      after = p === "anthropic" && data.has_more ? data.last_id : "";
      if (after && cursors.has(after)) throw new Error("The model list could not finish loading. Please refresh.");
      cursors.add(after);
    } while (after);
    if (s.request !== request || s.key !== key) return;
    if (p === "openai") models = models.filter(m => /^(gpt-\d|o[134](?:-|$)|codex-)/i.test(m.id) && !/embedding|tts|whisper|image|audio|realtime|transcrib|moderation|instruct|search|computer-use/i.test(m.id));
    else models = models.filter(m => m.lifecycle !== "retired");
    s.models = [...new Map(models.map(m => [m.id, m])).values()].sort((a, b) => (b.created || Date.parse(b.created_at) || 0) - (a.created || Date.parse(a.created_at) || 0) || a.id.localeCompare(b.id));
    if (!s.model && s.models.length) s.model = s.models.find(m => m.id === "gpt-4o-mini")?.id || s.models[0].id;
    safe(() => localStorage.setItem(p + "-model", s.model));
    s.status = s.models.length ? s.models.length + " models loaded. Availability is checked when you send." : "No chat models were returned. Check your key permissions or enter a model ID manually.";
  } catch (error) {
    if (s.request !== request) return;
    s.error = true; s.status = error.name === "AbortError" ? "Loading models timed out. Refresh to try again." : networkError(p, error);
  } finally {
    clearTimeout(timeout);
    if (s.request === request) { s.request = null; s.loading = false; if (p === provider) updatePicker(); }
  }
}
function chooseModel(id) {
  state[provider].model = id; state[provider].effort = "default";
  safe(() => localStorage.setItem(provider + "-model", id)); updatePicker();
}
function escapeHtml(text) { return text.replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }
function render(text) {
  return escapeHtml(text).split(/```/).map((part, i) => i % 2 ? "<pre><code>" + part.replace(/^[\w+-]*\n/, "") + "</code></pre>" : part.replace(/`([^`\n]+)`/g, "<code>$1</code>").replace(/\*\*([^*\n]+)\*\*/g, "<strong>$1</strong>")).join("");
}
function addBubble(role, text) {
  $("empty")?.remove();
  const element = document.createElement("div"); element.className = "msg " + role;
  if (role === "assistant") element.innerHTML = render(text); else element.textContent = text;
  $("messages").appendChild(element); $("chat").scrollTop = $("chat").scrollHeight; return element;
}
function drawHistory() {
  $("messages").replaceChildren();
  const s = state[provider];
  if (s.history.length) { for (const item of s.history) addBubble(item.role, item.content); return; }
  const empty = document.createElement("div"); empty.id = "empty"; empty.className = "empty";
  empty.innerHTML = '<h2>Your models. Your choice.</h2><p>Choose OpenAI or Claude, add your API key, and select a model.</p>';
  const button = document.createElement("button"); button.className = "primary"; button.textContent = "Choose provider & model";
  button.addEventListener("click", openModels); empty.appendChild(button); $("messages").appendChild(empty);
}
function openModels() { updatePicker(); $("modelDialog").showModal(); }
function openSettings() {
  keyProvider = provider;
  $("keyTitle").textContent = "Your " + providers[provider].name + " API key";
  $("keyNote").textContent = "Your key goes directly from this browser to " + providers[provider].name + ". It is not sent to GitHub. OpenAI and Claude require separate API keys.";
  $("keyLink").href = providers[provider].keys;
  $("keyInput").value = state[provider].key;
  $("remember").checked = !!safe(() => localStorage.getItem(provider + "-key"));
  $("settings").showModal(); $("keyInput").focus();
}
function saveKey(p, key, remember) {
  const s = state[p]; s.request?.abort(); s.request = null; s.loading = false;
  s.key = key; s.models = []; s.status = ""; s.error = false;
  safe(() => localStorage.removeItem(p + "-key")); safe(() => sessionStorage.removeItem(p + "-key"));
  if (key) safe(() => (remember ? localStorage : sessionStorage).setItem(p + "-key", key));
}
function buildRequest(p, history) {
  const s = state[p];
  const effort = supportedEfforts(p, s.model).includes(s.effort) ? s.effort : "default";
  const body = { model: s.model, stream: true };
  if (p === "openai") {
    body.input = history; body.store = false;
    if (effort !== "default") body.reasoning = { effort };
  } else {
    body.messages = history;
    const metadata = s.models.find(m => m.id === s.model);
    body.max_tokens = Math.min(metadata?.max_tokens || 8192, ["xhigh", "max"].includes(effort) ? 32768 : 8192);
    if (effort !== "default") body.output_config = { effort };
    if (metadata?.capabilities?.thinking?.types?.adaptive?.supported) body.thinking = { type: "adaptive" };
  }
  return { url: providers[p].base + (p === "openai" ? "/responses" : "/messages"), body };
}
// SSE frames can be split across chunks, use CRLF, or contain multiple data lines.
async function readEvents(body, onEvent) {
  const reader = body.getReader(), decoder = new TextDecoder(); let buffer = "";
  function dispatch(frame) {
    const data = frame.split(/\r?\n/).filter(line => line.startsWith("data:")).map(line => line.slice(5).trimStart()).join("\n");
    if (data && data !== "[DONE]") onEvent(JSON.parse(data));
  }
  try {
    for (;;) {
      const { done, value } = await reader.read(); buffer += done ? decoder.decode() : decoder.decode(value, { stream: true });
      let boundary;
      while ((boundary = /\r?\n\r?\n/.exec(buffer))) { dispatch(buffer.slice(0, boundary.index)); buffer = buffer.slice(boundary.index + boundary[0].length); }
      if (done) { if (buffer.trim()) dispatch(buffer); break; }
    }
  } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
}
function setBusy(busy) {
  for (const id of ["openModels", "openSettings", "newChat"]) $(id).disabled = busy;
  $("send").textContent = busy ? "Stop" : "Send";
}
async function ask() {
  const text = $("input").value.trim(); if (!text || active) return;
  const p = provider, s = state[p];
  if (!s.key) { openSettings(); return; }
  if (!s.model) { openModels(); return; }
  const request = new AbortController(); active = request; setBusy(true);
  $("input").value = ""; autosize();
  const userMessage = { role: "user", content: text }; s.history.push(userMessage); addBubble("user", text);
  const bubble = addBubble("assistant", ""); bubble.classList.add("cursor"); bubble.textContent = "Thinking…";
  let reply = "", completed = false, truncated = false;
  try {
    // Keep complete turns and always start the request with a user message.
    let start = Math.max(0, s.history.length - 40);
    while (start < s.history.length && s.history[start].role !== "user") start++;
    const api = buildRequest(p, s.history.slice(start));
    const res = await fetch(api.url, { method: "POST", headers: { ...headersFor(p, s.key), "Content-Type": "application/json" }, body: JSON.stringify(api.body), signal: request.signal, redirect: "error" });
    if (!res.ok) throw new Error(friendlyError(p, res.status, await res.text()));
    if (!res.body) throw new Error("This browser could not read the streaming response.");
    await readEvents(res.body, ev => {
      if (ev.type === "error" || ev.type === "response.failed") throw new Error(ev.error?.message || ev.response?.error?.message || ev.message || "The provider could not finish this response.");
      if (p === "openai") {
        if (ev.type === "response.output_text.delta") reply += ev.delta || "";
        if (ev.type === "response.refusal.delta") reply += ev.delta || "";
        if (ev.type === "response.completed") completed = true;
        if (ev.type === "response.incomplete") { completed = true; truncated = true; }
      } else {
        if (ev.type === "content_block_delta" && ev.delta?.type === "text_delta") reply += ev.delta.text || "";
        if (ev.type === "content_block_start" && ev.content_block?.type === "text") reply += ev.content_block.text || "";
        if (ev.type === "message_stop") completed = true;
        if (ev.type === "message_delta" && ev.delta?.stop_reason === "max_tokens") truncated = true;
      }
      if (reply) bubble.innerHTML = render(reply);
      $("chat").scrollTop = $("chat").scrollHeight;
    });
    if (!completed) throw new Error("The connection ended before the response finished. You can try again.");
    if (!reply) bubble.textContent = "No text was returned. Try a lower effort or another model.";
    if (truncated) addBubble("error", "The response reached its limit and may be incomplete. Try a lower effort or a shorter request.");
  } catch (error) {
    if (error.name === "AbortError") { if (!reply) bubble.textContent = "Stopped."; }
    else { if (!reply) bubble.remove(); addBubble("error", networkError(p, error)); }
  } finally {
    if (reply) s.history.push({ role: "assistant", content: reply });
    else { if (s.history.at(-1) === userMessage) s.history.pop(); if (!$("input").value) $("input").value = text; }
    bubble.classList.remove("cursor"); active = null; setBusy(false); autosize(); $("input").focus();
  }
}
function autosize() { $("input").style.height = "auto"; $("input").style.height = Math.min($("input").scrollHeight, 200) + "px"; }
$("openModels").addEventListener("click", openModels);
for (const id of ["closeModels", "doneModels"]) $(id).addEventListener("click", () => $("modelDialog").close());
$("providerSelect").addEventListener("change", () => {
  provider = $("providerSelect").value; safe(() => localStorage.setItem("chat-provider", provider));
  $("customModel").value = ""; drawHistory(); updatePicker(); if (!state[provider].models.length) loadModels();
});
$("modelSelect").addEventListener("change", () => chooseModel($("modelSelect").value));
$("refreshModels").addEventListener("click", () => loadModels());
$("useCustomModel").addEventListener("click", () => {
  const id = $("customModel").value.trim();
  if (!id || /\s/.test(id)) { $("customModel").setCustomValidity("Enter an API model ID without spaces."); $("customModel").reportValidity(); return; }
  $("customModel").setCustomValidity(""); chooseModel(id);
});
$("customModel").addEventListener("input", () => $("customModel").setCustomValidity(""));
$("effortSelect").addEventListener("change", () => { state[provider].effort = $("effortSelect").value; updateEffort(); });
$("effortRange").addEventListener("input", () => { state[provider].effort = ["default", ...supportedEfforts(provider, state[provider].model)][Number($("effortRange").value)]; updateEffort(); });
for (const id of ["openSettings", "pickerKey"]) $(id).addEventListener("click", openSettings);
$("cancel").addEventListener("click", () => $("settings").close());
$("settings").addEventListener("close", () => { $("keyInput").value = ""; });
$("settingsForm").addEventListener("submit", event => {
  event.preventDefault(); saveKey(keyProvider, $("keyInput").value.trim(), $("remember").checked);
  $("settings").close(); updatePicker(); loadModels(keyProvider);
  if (!$("modelDialog").open) openModels();
});
$("forget").addEventListener("click", () => { saveKey(keyProvider, "", false); $("settings").close(); updatePicker(); });
$("newChat").addEventListener("click", () => { state[provider].history = []; drawHistory(); $("input").focus(); });
$("form").addEventListener("submit", event => { event.preventDefault(); if (active) active.abort(); else ask(); });
$("input").addEventListener("keydown", event => { if (event.key === "Enter" && !event.shiftKey && !event.isComposing) { event.preventDefault(); if (!active) ask(); } });
$("input").addEventListener("input", autosize);
updatePicker(); drawHistory(); loadModels();
