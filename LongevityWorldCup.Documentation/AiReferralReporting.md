# AI referral and conversion reporting

Open `/internal/site-statistics.html?tab=Source%20Quality`. The **AI referrals** panel uses the existing dashboard window, Flow, Device, and Source filters. Select **AI · all providers**, an individual provider, or click a provider row. Traffic Overview, Source Quality details, event pagination, and CSV exports use the same source policy. The dashboard's existing access model is unchanged; its location under `internal` does not introduce authentication.

## Attribution policy

`AiReferralPolicy` is the single provider catalog and classifier. Ingestion, historical projections, aggregate SQL, event filters, and provider options use it. Browser tracking sends first-touch evidence; it does not maintain a second AI catalog.

1. A recognized, exact `utm_source` wins over the referrer, including a conflicting AI provider. Its basis is **campaign**.
2. Otherwise an exact recognized referrer host identifies the provider, with basis **referrer**. An unrelated campaign name or non-AI source tag does not suppress a recognized AI referrer.
3. Otherwise preserve the existing non-AI classification. An `ai` source label, a campaign name containing “AI”, a user-agent string, and ordinary Google/Bing/X traffic are insufficient evidence.

Host parsing uses URI host semantics, lowercase/IDN normalization, and a single trailing DNS dot. The catalog deliberately lists exact app hosts and their supported `www` variants. Arbitrary subdomains, suffix/prefix lookalikes, malformed domains, and credential-bearing URLs do not match. Campaign source tags are case-insensitive, trimmed exact matches, without punctuation repair or substring matching. Campaign parameter names are case-insensitive.

Accepted source tags are the catalog's app hosts plus `chatgpt`, `perplexity`, `claude`, `gemini`, `copilot`, `grok`, `deepseek`, `mistral`, and `lechat`. Apart from OpenAI's documented `utm_source=chatgpt.com`, these short tags are **LWC operator conventions**, not a claim that each provider automatically emits them. A copied/tagged link is evidence of campaign attribution, not proof of the visitor's origin.

The first browser touch is one atomic snapshot: landing route, referrer, source, and recognized campaign fields. Later internal navigation, tagged visits, or submissions cannot fill or overwrite that snapshot. A provisional server-only first touch can be replaced once by an explicit browser snapshot. Existing historical snapshots remain authoritative when the capture marker is added. The current document caches attribution in memory; cross-document continuity uses the existing `sessionStorage` session and first-touch keys. The same minimal snapshot accompanies same-origin API requests so a delayed or blocked event beacon does not change the submission source. When session storage is unavailable, continuity is limited to the current document.

## Measurement

- **Visit**: one distinct existing statistics session with any recorded event inside the selected UTC window and filters. These are active sessions, not necessarily sessions first acquired within that window, people, or cross-device identities. A tab session can span dates. Missing events remain missing.
- **Calculator use** (`calculator_used`): first trusted input, change, or submit on either calculator form. Stored at most once per session and clock. Focus, page views, restoration, and synthetic events do not count.
- **Result** (`calculator_result_generated`): the existing result-display event, counted once per session in the AI report. Recalculations/reloads may remain in raw event counts.
- **Application start** (`application_started`): first trusted input or change on the new-application form (`/apply`). Stored at most once per session. Viewing the form alone, a restored draft, proof-flow page views, and result/profile updates are not this event.
- **Application**: a distinct session with server-recorded `application_submit_succeeded` and `submissionKind=full-application`. This means the existing flow accepted the application; it does not mean athlete approval or completed payment. Client success claims are rejected. Validation failures, retry clicks, partial acceptance, result uploads, and profile edits do not count. The existing submission retry store still owns operation idempotency.
- **Apps / visits**: unique application sessions divided by all matching visits. **Starts → applications**: sessions with both recorded steps divided by application-start sessions. A zero denominator is unavailable, not 0%. These are independent step-reach metrics in the window, not an ordered sequence or acquisition-cohort analysis. Flow/device filters apply to the events in every numerator and denominator.

Provider and landing-page aggregates cover the complete window independently of event-page limits. Landing pages omit query strings. Visit/step counts are distinct sessions; page views, total events, and successful application events remain separate raw counts. Existing raw/clean diagnostics are unchanged; the AI report uses the same raw session population as Source Quality. It does not authenticate humans, bots, or crawler user agents.

Historical AI classification is computed from stored first-touch evidence without rewriting the original source fields. `SiteStatisticFeatures` stores the actual activation timestamp for the new use/start events. The UI exposes that coverage date; it does not infer older uses or starts from page views. Existing result and submission history remains available. Successful historical submissions lacking a known submission kind appear as unknown-type sessions and are excluded from the new-application rate; the original Source Quality submission report retains them.

## Privacy and operations

No third-party SDK, identity, consent behavior, retention schedule, or access-control change is introduced. No biomarker values, form contents, files, names, or email addresses are added to telemetry. Browser routes are reduced before transmission; calculator entry modes retain only flags, never query values. Only the existing campaign fields and referrer host are carried forward. Tracking/storage/network errors do not block calculators or submissions.

Production browser verification intercepts statistics writes and application requests, as in the existing onboarding verification workflow. It creates no production test applications, emails, social posts, or business telemetry. Test application outcomes use local fixtures.

## Provider evidence checked 2026-09-16

- ChatGPT: [OpenAI publisher FAQ](https://help.openai.com/en/articles/12627856) documents `utm_source=chatgpt.com`; [ChatGPT FAQ](https://help.openai.com/en/articles/12677804-what-is-chatgpt-faq) identifies `chatgpt.com`. The legacy [chat.openai.com](https://chat.openai.com) redirects there.
- Perplexity: the [official product site](https://www.perplexity.ai/hub) links its answer engine on `perplexity.ai`.
- Claude: [Anthropic's getting-started guide](https://support.claude.com/en/articles/8114491-get-started-with-claude) identifies `claude.ai`.
- Gemini: [Google's Gemini Apps Privacy Hub](https://support.google.com/gemini/answer/13594961?hl=en) identifies the web app as `gemini.google.com`.
- Copilot: [Microsoft's activity-history guidance](https://support.microsoft.com/en-us/privacy/manage-your-copilot-activity-history-in-the-privacy-dashboard) identifies `copilot.microsoft.com`.
- Grok: [xAI's overview](https://docs.x.ai/grok/overview) identifies `grok.com`.
- DeepSeek: [DeepSeek's web-search announcement](https://api-docs.deepseek.com/news/news1210/) identifies `chat.deepseek.com`.
- Mistral: [Mistral's product rename announcement](https://help.mistral.ai/en/articles/682992-le-chat-is-now-vibe) confirms the app remains at `chat.mistral.ai`.

These sources verify app domains and the stated convention; they do not promise that every app/browser sends a referrer. Direct/unknown traffic cannot reliably be reconstructed. Visits are not evidence of AI citations or crawl activity.
