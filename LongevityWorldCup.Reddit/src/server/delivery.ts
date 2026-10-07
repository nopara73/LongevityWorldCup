export const targetSubreddit = "LongevityWorldCup";
export const appSlug = "longevityworldcup";
const reviewAfterMs = 45 * 60 * 1000;

export type Delivery = {
  deliveryId: string;
  eventId: string;
  subreddit: string;
  title: string;
  text: string;
};

export type DeliveryState =
  | { state: "started"; startedAt: number }
  | { state: "sent"; postId: string };

export type PollDependencies = {
  subredditName: string | undefined;
  appName: string | undefined;
  next: () => Promise<unknown | null>;
  read: (id: string) => Promise<string | undefined>;
  claim: (id: string, state: string) => Promise<boolean>;
  save: (id: string, state: string) => Promise<void>;
  release: (id: string) => Promise<void>;
  begin: (id: string) => Promise<"Started" | "AlreadyStarted" | "DailyLimit" | "NotPending">;
  submit: (delivery: Delivery) => Promise<string>;
  confirm: (id: string, postId: string) => Promise<void>;
  uncertain: (id: string) => Promise<void>;
  now: () => number;
};

export function parseDelivery(value: unknown): Delivery {
  if (!value || typeof value !== "object") throw new Error("InvalidDelivery");
  const d = value as Partial<Delivery>;
  if (
    typeof d.deliveryId !== "string" || !/^lwc-[a-f0-9]{64}$/.test(d.deliveryId) ||
    typeof d.eventId !== "string" || d.eventId.length === 0 || d.eventId.length > 256 ||
    d.subreddit !== targetSubreddit ||
    typeof d.title !== "string" || d.title.trim().length === 0 || d.title.length > 300 ||
    typeof d.text !== "string" || d.text.trim().length === 0 || d.text.length > 40_000
  ) throw new Error("InvalidDelivery");
  return d as Delivery;
}

export async function poll(deps: PollDependencies): Promise<string> {
  // Installation in another community cannot fetch the queue or publish an LWC post.
  if (deps.subredditName?.toLowerCase() !== targetSubreddit.toLowerCase() || deps.appName !== appSlug) return "inactive";
  const value = await deps.next();
  if (value === null) return "empty";
  const delivery = parseDelivery(value);
  const previous = await deps.read(delivery.deliveryId);
  if (previous) {
    const saved = JSON.parse(previous) as DeliveryState;
    if (saved.state === "sent" && /^t3_[a-z0-9]{1,32}$/.test(saved.postId)) {
      // Keep the receipt indefinitely and retry only the acknowledgment after a lost response.
      await deps.confirm(delivery.deliveryId, saved.postId);
      return "confirmed";
    }
    if (saved.state !== "started" || !Number.isFinite(saved.startedAt)) throw new Error("InvalidDeliveryState");
    if (deps.now() - saved.startedAt >= reviewAfterMs) {
      await deps.uncertain(delivery.deliveryId);
      return "review";
    }
    return "in-flight";
  }

  const marker = JSON.stringify({ state: "started", startedAt: deps.now() } satisfies DeliveryState);
  if (!await deps.claim(delivery.deliveryId, marker)) return "in-flight";
  const begin = await deps.begin(delivery.deliveryId);
  if (begin === "DailyLimit") {
    // No Reddit call was made. The pending prepared request can wait for the next day.
    await deps.release(delivery.deliveryId);
    return "deferred";
  }
  if (begin !== "Started") {
    if (begin === "AlreadyStarted") await deps.uncertain(delivery.deliveryId);
    return "review";
  }

  let postId: string;
  try {
    postId = await deps.submit(delivery);
    if (!/^t3_[a-z0-9]{1,32}$/.test(postId)) throw new Error("InvalidRedditReceipt");
  } catch {
    // Reddit does not offer a submit idempotency key. Preserve both start markers;
    // a timeout can mean the post was accepted, so never repeat the submission.
    await deps.uncertain(delivery.deliveryId);
    return "review";
  }
  await deps.save(delivery.deliveryId, JSON.stringify({ state: "sent", postId } satisfies DeliveryState));
  await deps.confirm(delivery.deliveryId, postId);
  return "delivered";
}
