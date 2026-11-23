# GPT Chat

A simple ChatGPT-style website that runs entirely in the browser, so it can be hosted free on
**GitHub Pages**. No server and no environment variables are needed.

Each visitor clicks **API key**, pastes their own OpenAI key, and chats. The key is saved only in
that visitor's browser and is sent only to `api.openai.com`; it is never uploaded to GitHub.

## Put it online (GitHub Pages)

1. On GitHub, open this repository → **Settings** → **Pages**.
2. Under **Build and deployment**: Source **Deploy from a branch**, Branch **main**, folder **/ (root)** → **Save**.
3. Wait about a minute. Your site is at `https://<your-username>.github.io/<repo-name>/`.

## Notes

- Don't put your API key in any file in this repository: the repository is public.
- On a shared computer, untick **Remember on this device** (the key is then forgotten when the tab
  closes), or click **API key → Remove key** when you're done.
- Change the model in the **API key** dialog (default `gpt-4o-mini`).
