import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { parseAppConfig } from "@devvit/shared-types/schemas/config-file.v1.js";
import { appSlug, poll, targetSubreddit, type Delivery, type PollDependencies } from "./delivery.ts";

const delivery: Delivery = { deliveryId: "lwc-" + "a".repeat(64), eventId: "event-1", subreddit: targetSubreddit, title: "A new athlete joins!", text: "Established LWC copy.\n\nhttps://longevityworldcup.com/events?event=event-1" };

test("the SDK accepts the backend-only manifest and encrypted global secret", () => {
  const config = parseAppConfig(readFileSync(new URL("../../devvit.json", import.meta.url), "utf8"), false);
  assert.equal(config.server?.dir + "/" + config.server?.entry, "dist/server/index.js");
  const secret = config.settings?.global?.lwcBridgeSecret;
  if (!secret || secret.type !== "string") throw new Error("MissingBridgeSecret");
  assert.equal(secret.isSecret, true);
  assert.equal(config.post, undefined);
  assert.equal(config.triggers?.onAppInstall, undefined);
});

function fixture() {
  const states = new Map<string, string>();
  let submissions = 0;
  let confirmations = 0;
  let reviews = 0;
  const deps: PollDependencies = {
    subredditName: targetSubreddit, appName: appSlug, next: async () => delivery,
    read: async id => states.get(id),
    claim: async (id, value) => { if (states.has(id)) return false; states.set(id, value); return true; },
    save: async (id, value) => { states.set(id, value); },
    release: async id => { states.delete(id); },
    begin: async () => "Started",
    submit: async () => { submissions++; return "t3_abc123"; },
    confirm: async () => { confirmations++; },
    uncertain: async () => { reviews++; },
    now: () => 1_000_000,
  };
  return { deps, states, counts: () => ({ submissions, confirmations, reviews }) };
}

test("a lost acknowledgment reuses the saved receipt without another Reddit submission", async () => {
  const f = fixture();
  f.deps.confirm = async () => { throw new Error("ResponseLost"); };
  await assert.rejects(poll(f.deps));
  f.deps.confirm = async () => {};
  assert.equal(await poll(f.deps), "confirmed");
  assert.equal(f.counts().submissions, 1);
});

test("concurrent polls cannot both publish the same delivery", async () => {
  const f = fixture();
  await Promise.all([poll(f.deps), poll(f.deps)]);
  assert.equal(f.counts().submissions, 1);
});

test("an uncertain submit is held for review and never submitted again", async () => {
  const f = fixture();
  let attempts = 0;
  f.deps.submit = async () => { attempts++; throw new Error("AcceptedButResponseLost"); };
  assert.equal(await poll(f.deps), "review");
  f.deps.now = () => 1_000_000 + 46 * 60 * 1000;
  assert.equal(await poll(f.deps), "review");
  assert.equal(attempts, 1);
});

test("backend start markers prevent a duplicate after local Redis state is lost", async () => {
  const f = fixture();
  f.deps.begin = async () => "AlreadyStarted";
  assert.equal(await poll(f.deps), "review");
  assert.equal(f.counts().submissions, 0);
  assert.equal(f.counts().reviews, 1);
});

test("a daily quota rejection releases only the unsubmitted local marker", async () => {
  const f = fixture();
  f.deps.begin = async () => "DailyLimit";
  assert.equal(await poll(f.deps), "deferred");
  assert.equal(f.states.size, 0);
  assert.equal(f.counts().submissions, 0);
  f.deps.begin = async () => "Started";
  assert.equal(await poll(f.deps), "delivered");
});

test("a different installation cannot read the LWC queue", async () => {
  const f = fixture();
  f.deps.subredditName = "AnotherCommunity";
  f.deps.next = async () => { throw new Error("MustNotFetch"); };
  assert.equal(await poll(f.deps), "inactive");
});

test("invalid or misdirected bridge payloads cannot reach Reddit", async () => {
  const f = fixture();
  f.deps.next = async () => ({ ...delivery, subreddit: "AnotherCommunity" });
  await assert.rejects(poll(f.deps), /InvalidDelivery/);
  assert.equal(f.counts().submissions, 0);
});

test("a lost begin response cannot lead to a Reddit submission on retry", async () => {
  const f = fixture();
  f.deps.begin = async () => { throw new Error("BeginResponseLost"); };
  await assert.rejects(poll(f.deps));
  assert.equal(await poll(f.deps), "in-flight");
  assert.equal(f.counts().submissions, 0);
});
