# GPT Chat

A simple ChatGPT-style website that uses **your** OpenAI API key.
The key is kept on the server (as an environment variable), so visitors never see it.

```
public/index.html   the chat page
api/chat.js         backend for Vercel
server.py           backend for your PC / Render / Railway / any VPS
```

## Settings (environment variables)

| Name | Required | What it does |
|---|---|---|
| `OPENAI_API_KEY` | yes | Your key from https://platform.openai.com/api-keys |
| `OPENAI_MODEL` | no | Model name, default `gpt-4o-mini` |
| `APP_PASSWORD` | recommended | Visitors must type this password before chatting |

## Run on your PC

1. Copy `.env.example` to `.env` and put your key in it.
2. `python server.py`
3. Open http://localhost:8000

## Deploy on Vercel (free)

1. Put this folder in a GitHub repository (the `.env` file is ignored, so your key is not uploaded).
2. Go to https://vercel.com → **Add New → Project** → import the repository.
3. Framework preset: **Other**. Leave build settings empty.
4. Under **Environment Variables**, add `OPENAI_API_KEY` (and `APP_PASSWORD`, `OPENAI_MODEL` if you want).
5. Click **Deploy**. You get a link like `https://your-project.vercel.app`.

Changed a variable later? Redeploy (Deployments → ⋯ → Redeploy) so it takes effect.

## Deploy on Render (alternative)

New → **Web Service** → pick the repo → Runtime **Python**, Build command: *(empty)*,
Start command: `python server.py`. Add the same environment variables.
