"""Run the chat site without Vercel (on your PC, or on Render / Railway / any VPS).

    python server.py            -> http://localhost:8000

Needs only Python 3.9+ (no packages). Reads OPENAI_API_KEY, OPENAI_MODEL,
APP_PASSWORD and PORT from the environment or from a .env file next to this script.
"""
import json
import os
import urllib.error
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PUBLIC = ROOT / "public"
MAX_MESSAGES = 40
MAX_CHARS = 20000


def load_env():
    env_file = ROOT / ".env"
    if not env_file.exists():
        return
    for line in env_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


def clean_messages(messages):
    if not isinstance(messages, list) or not messages:
        return None
    out = []
    for m in messages[-MAX_MESSAGES:]:
        if not isinstance(m, dict) or m.get("role") not in ("system", "user", "assistant"):
            return None
        if not isinstance(m.get("content"), str):
            return None
        out.append({"role": m["role"], "content": m["content"][:MAX_CHARS]})
    return out


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(PUBLIC), **kwargs)

    def send_text(self, status, text):
        data = text.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        if self.path != "/api/chat":
            return self.send_text(404, "Not found")

        api_key = os.environ.get("OPENAI_API_KEY")
        if not api_key:
            return self.send_text(500, "Server is missing OPENAI_API_KEY.")

        password = os.environ.get("APP_PASSWORD")
        if password and self.headers.get("x-app-password") != password:
            return self.send_text(401, "Wrong or missing password.")

        try:
            length = int(self.headers.get("Content-Length", 0))
            body = json.loads(self.rfile.read(length) or b"{}")
        except (ValueError, json.JSONDecodeError):
            body = {}
        messages = clean_messages(body.get("messages") if isinstance(body, dict) else None)
        if not messages:
            return self.send_text(400, "Invalid messages.")

        payload = json.dumps({
            "model": os.environ.get("OPENAI_MODEL", "gpt-4o-mini"),
            "messages": messages,
            "stream": True,
        }).encode("utf-8")
        req = urllib.request.Request(
            "https://api.openai.com/v1/chat/completions",
            data=payload,
            headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
            method="POST",
        )
        try:
            upstream = urllib.request.urlopen(req, timeout=120)
        except urllib.error.HTTPError as e:
            # 401 is reserved for "wrong site password", so report a bad OpenAI key as 502.
            return self.send_text(502 if e.code == 401 else e.code, e.read().decode("utf-8", "replace"))
        except urllib.error.URLError as e:
            return self.send_text(502, f"Could not reach OpenAI: {e.reason}")

        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        with upstream:
            for raw in upstream:
                line = raw.decode("utf-8", "replace").strip()
                if not line.startswith("data:"):
                    continue
                data = line[5:].strip()
                if data == "[DONE]":
                    break
                try:
                    delta = json.loads(data)["choices"][0]["delta"].get("content")
                except (ValueError, KeyError, IndexError):
                    continue
                if delta:
                    self.wfile.write(delta.encode("utf-8"))
                    self.wfile.flush()


if __name__ == "__main__":
    load_env()
    port = int(os.environ.get("PORT", 8000))
    print(f"Chat running on http://localhost:{port}")
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()
