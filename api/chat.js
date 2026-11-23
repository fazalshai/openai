// Vercel serverless function: POST /api/chat
// Your OpenAI key lives ONLY here (as the OPENAI_API_KEY environment variable),
// never in the browser.

const MAX_MESSAGES = 40;
const MAX_CHARS = 20000;

export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.statusCode = 405;
    return res.end("Method not allowed");
  }

  const apiKey = process.env.OPENAI_API_KEY;
  if (!apiKey) {
    res.statusCode = 500;
    return res.end("Server is missing OPENAI_API_KEY.");
  }

  const password = process.env.APP_PASSWORD;
  if (password && req.headers["x-app-password"] !== password) {
    res.statusCode = 401;
    return res.end("Wrong or missing password.");
  }

  let body = req.body;
  if (typeof body === "string") {
    try { body = JSON.parse(body); } catch { body = null; }
  }
  const messages = cleanMessages(body && body.messages);
  if (!messages) {
    res.statusCode = 400;
    return res.end("Invalid messages.");
  }

  const upstream = await fetch("https://api.openai.com/v1/chat/completions", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${apiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      model: process.env.OPENAI_MODEL || "gpt-4o-mini",
      messages,
      stream: true,
    }),
  });

  if (!upstream.ok) {
    // 401 is reserved for "wrong site password", so report a bad OpenAI key as 502.
    res.statusCode = upstream.status === 401 ? 502 : upstream.status;
    return res.end(await upstream.text());
  }

  res.setHeader("Content-Type", "text/plain; charset=utf-8");
  res.setHeader("Cache-Control", "no-cache");

  // Turn OpenAI's server-sent events into a plain stream of text.
  const reader = upstream.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop();
    for (const line of lines) {
      const text = line.trim();
      if (!text.startsWith("data:")) continue;
      const data = text.slice(5).trim();
      if (data === "[DONE]") continue;
      try {
        const delta = JSON.parse(data).choices?.[0]?.delta?.content;
        if (delta) res.write(delta);
      } catch {
        /* ignore partial / non-JSON lines */
      }
    }
  }
  res.end();
}

function cleanMessages(messages) {
  if (!Array.isArray(messages) || messages.length === 0) return null;
  const out = [];
  for (const m of messages.slice(-MAX_MESSAGES)) {
    if (!m || !["system", "user", "assistant"].includes(m.role)) return null;
    if (typeof m.content !== "string") return null;
    out.push({ role: m.role, content: m.content.slice(0, MAX_CHARS) });
  }
  return out;
}
