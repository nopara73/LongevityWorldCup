import { context, createServer, getServerPort, reddit, redis, settings } from "@devvit/web/server";
import { poll, type PollDependencies } from "./delivery.ts";

const bridgeUrl = "https://longevityworldcup.com/api/reddit/";
const stateKey = "lwc:reddit:deliveries";

async function request(secret: string, endpoint: string, body: object = {}): Promise<Response> {
  const response = await fetch(bridgeUrl + endpoint, {
    method: "POST",
    headers: { Authorization: `Bearer ${secret}`, "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(10_000),
  });
  if (!response.ok && !(endpoint === "begin" && response.status === 409)) throw new Error("BridgeRequestFailed");
  return response;
}

const server = createServer(async (req, res) => {
  if (req.method !== "POST" || req.url !== "/internal/scheduler/announcements") {
    res.writeHead(404).end();
    return;
  }
  try {
    const secret = await settings.get<string>("lwcBridgeSecret");
    if (!secret) {
      res.writeHead(200, { "Content-Type": "application/json" }).end('{"status":"ok"}');
      return;
    }
    const deps: PollDependencies = {
      subredditName: context.subredditName,
      appName: context.appSlug,
      next: async () => {
        const response = await request(secret, "next");
        return response.status === 204 ? null : response.json();
      },
      read: id => redis.hGet(stateKey, id),
      claim: async (id, value) => await redis.hSetNX(stateKey, id, value) === 1,
      save: async (id, value) => { await redis.hSet(stateKey, { [id]: value }); },
      release: async id => { await redis.hDel(stateKey, [id]); },
      begin: async deliveryId => {
        const response = await request(secret, "begin", { deliveryId });
        if (response.status === 200) return "Started";
        const body = await response.json() as { reason?: string };
        if (body.reason !== "AlreadyStarted" && body.reason !== "DailyLimit" && body.reason !== "NotPending") throw new Error("InvalidBeginResponse");
        return body.reason;
      },
      submit: async delivery => (await reddit.submitPost({
        subredditName: delivery.subreddit,
        title: delivery.title,
        text: delivery.text,
        runAs: "APP",
      })).id,
      confirm: async (deliveryId, postId) => { await request(secret, "receipts", { deliveryId, postId }); },
      uncertain: async deliveryId => { await request(secret, "uncertain", { deliveryId }); },
      now: () => Date.now(),
    };
    const outcome = await poll(deps);
    if (outcome !== "empty" && outcome !== "inactive" && outcome !== "in-flight") console.log(`LWC Reddit delivery: ${outcome}`);
    res.writeHead(200, { "Content-Type": "application/json" }).end('{"status":"ok"}');
  } catch {
    // Request bodies, authorization headers, SDK exceptions and credentials stay out of logs.
    console.error("LWC Reddit polling failed; the prepared delivery remains in the ledger.");
    res.writeHead(500, { "Content-Type": "application/json" }).end('{"status":"error"}');
  }
});

server.listen(getServerPort());
