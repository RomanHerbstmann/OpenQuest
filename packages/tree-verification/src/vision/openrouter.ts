export interface ChatContentPart {
  type: "text" | "image_url";
  text?: string;
  image_url?: { url: string };
}

export interface ChatRequest {
  model: string;
  messages: { role: "system" | "user"; content: string | ChatContentPart[] }[];
  response_format?: unknown;
  max_tokens?: number;
}

export interface ChatResult {
  content: string;
  costUsd: number;
  latencyMs: number;
}

export interface OpenRouterClientOptions {
  apiKey: string;
  baseUrl?: string;
  appUrl?: string;
  appName?: string;
  fetch?: typeof fetch;
  maxRetries?: number;
}

export class OpenRouterError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message);
    this.name = "OpenRouterError";
  }
}

const RETRYABLE = new Set([408, 429, 500, 502, 503, 504]);
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/**
 * Minimal OpenRouter chat client. No `temperature`: several reasoning models reject it
 * when `provider.require_parameters` is set, which we need for strict structured output.
 */
export function createOpenRouterClient(opts: OpenRouterClientOptions) {
  const baseUrl = (opts.baseUrl ?? "https://openrouter.ai/api").replace(/\/+$/, "");
  const doFetch = opts.fetch ?? fetch;
  const maxRetries = opts.maxRetries ?? 2;

  async function chat(req: ChatRequest, signal?: AbortSignal): Promise<ChatResult> {
    const started = Date.now();
    let attempt = 0;
    for (;;) {
      const res = await doFetch(`${baseUrl}/v1/chat/completions`, {
        method: "POST",
        signal,
        headers: {
          Authorization: `Bearer ${opts.apiKey}`,
          "Content-Type": "application/json",
          ...(opts.appUrl ? { "HTTP-Referer": opts.appUrl } : {}),
          ...(opts.appName ? { "X-Title": opts.appName } : {}),
        },
        body: JSON.stringify({ ...req, provider: { require_parameters: true } }),
      });

      if (RETRYABLE.has(res.status) && attempt < maxRetries) {
        attempt++;
        await sleep(400 * 2 ** attempt);
        continue;
      }
      const body = (await res.json().catch(() => ({}))) as {
        error?: { message?: string };
        choices?: { message?: { content?: string } }[];
        usage?: { cost?: number };
      };
      if (!res.ok || body.error) {
        throw new OpenRouterError(body.error?.message ?? `HTTP ${res.status}`, res.status);
      }
      const content = body.choices?.[0]?.message?.content;
      if (!content) throw new OpenRouterError("empty completion");
      return { content, costUsd: body.usage?.cost ?? 0, latencyMs: Date.now() - started };
    }
  }

  return { chat };
}

export type OpenRouterClient = ReturnType<typeof createOpenRouterClient>;
