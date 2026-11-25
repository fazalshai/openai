# AI Chat

**Want an agent that can work on your computer?** [Download Desk Agent for Windows](https://fazalshai.github.io/openai/desktop.html). The [desktop preview](desktop/README.md) supports both OpenAI and Claude with file tools, approved terminal commands and a task browser. Each person brings their own API key.

A static chat website for GitHub Pages. Choose **OpenAI or Claude**, select an API model, and adjust reasoning effort where supported. No backend or environment variables are needed for accounts that permit direct browser API requests.

## Use it

1. Click the model name at the top, or **Choose provider & model**.
2. Select **OpenAI · GPT** or **Anthropic · Claude**.
3. Click **Add API key**, enter that provider's key, and save. OpenAI keys cannot call Claude.
4. Choose a model from the loaded list. **Refresh models** retries a failed lookup; errors appear below the model dropdown.
5. Select effort with the dropdown or slider. **Extra High** appears only for models with verified support. **Model default** omits the effort override. Unknown models use their default effort.
6. Click **Done**, type a message, and send.

If listing models fails, expand **Enter a model ID manually**. Your key must have access to the exact model and API endpoint; appearing in the list does not guarantee a successful request.

Each provider has separate chat history and a separate key. Switching providers does not forward the other provider's conversation. Chat history stays in memory and clears on reload. Keys stay in this tab by default; **Remember on this device** opts into local storage. If storage is blocked, the key works in memory until reload. **Remove key** clears that provider's saved key.

## Publish to GitHub Pages

Commit and push **both `index.html` and `app.js`** to the repository root. The files must stay together.

Open **Settings → Pages → Build and deployment**. Choose **Deploy from a branch**, **main**, **/(root)**, then **Save**. GitHub displays the live URL when deployment finishes. For `fazalshai/openai`, the standard URL is `https://fazalshai.github.io/openai/`.

## API behavior

- OpenAI uses `/v1/models` and streaming `/v1/responses`. Selected effort is sent as `reasoning.effort`.
- Claude uses paginated `/v1/models` and streaming `/v1/messages`. Effort options come from model capabilities and are sent as `output_config.effort`.
- Claude output is capped at 8,192 tokens normally, or up to 32,768 for Extra High/Max when model metadata permits it. Thinking counts toward this cap. Truncated responses are reported.
- OpenAI effort mappings are conservative; update them against official model docs when adding new model families.
- Claude browser calls use `anthropic-dangerous-direct-browser-access`. Some organization settings block direct browser access even with a valid key; those accounts need a backend.

API usage is billed by the selected provider. Never put a real key in repository files. Keys entered at runtime go only to the selected provider, but scripts on this site's origin can read browser-stored keys. This is a personal bring-your-own-key client. A public service sharing your key needs a backend.

References: [OpenAI reasoning](https://developers.openai.com/api/docs/guides/reasoning), [Claude authentication](https://platform.claude.com/docs/en/manage-claude/authentication), [Claude models](https://platform.claude.com/docs/en/api/models/list).
