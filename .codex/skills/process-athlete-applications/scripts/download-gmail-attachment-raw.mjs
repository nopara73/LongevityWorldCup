#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { basename, dirname, isAbsolute, join, resolve } from 'node:path';
import { existsSync, readdirSync, statSync } from 'node:fs';
import { mkdir, writeFile } from 'node:fs/promises';

const DEFAULT_TIMEOUT_MS = 120_000;

function printUsage() {
  console.error(`Usage:
  node download-gmail-attachment-raw.mjs --message-id <gmail-message-id> --out <path> [--filename <name> | --attachment-id <id>] [--thread-id <codex-thread-id>] [--cwd <path>] [--overwrite]

Downloads an attachment through the Codex Gmail connector without Gmail attachment ingestion.
The helper calls gmail.read_email with include_raw_mime=true through the local Codex app-server,
extracts the selected MIME part bytes locally, writes them to --out, and prints JSON metadata.`);
}

function parseArgs(argv) {
  const args = {
    cwd: process.cwd(),
    overwrite: false,
    timeoutMs: DEFAULT_TIMEOUT_MS,
  };

  for (let i = 2; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--help' || arg === '-h') {
      args.help = true;
      continue;
    }
    if (arg === '--overwrite') {
      args.overwrite = true;
      continue;
    }
    const next = argv[i + 1];
    if (!next || next.startsWith('--')) {
      throw new Error(`Missing value for ${arg}`);
    }
    i += 1;
    switch (arg) {
      case '--message-id':
        args.messageId = next;
        break;
      case '--attachment-id':
        args.attachmentId = next;
        break;
      case '--filename':
        args.filename = next;
        break;
      case '--out':
        args.out = next;
        break;
      case '--thread-id':
        args.threadId = next;
        break;
      case '--cwd':
        args.cwd = next;
        break;
      case '--app-server-url':
        args.appServerUrl = next;
        break;
      case '--timeout-ms':
        args.timeoutMs = Number.parseInt(next, 10);
        break;
      default:
        throw new Error(`Unknown argument: ${arg}`);
    }
  }

  if (args.help) {
    return args;
  }
  if (!args.messageId) {
    throw new Error('--message-id is required');
  }
  if (!args.out) {
    throw new Error('--out is required');
  }
  if (args.attachmentId && args.filename) {
    throw new Error('Pass either --attachment-id or --filename, not both');
  }
  if (!Number.isFinite(args.timeoutMs) || args.timeoutMs <= 0) {
    throw new Error('--timeout-ms must be a positive integer');
  }

  args.cwd = resolve(args.cwd);
  return args;
}

function withTimeout(promise, ms, label) {
  let timeout;
  const timer = new Promise((_, reject) => {
    timeout = setTimeout(() => reject(new Error(`${label} timed out after ${ms} ms`)), ms);
  });
  return Promise.race([promise, timer]).finally(() => clearTimeout(timeout));
}

async function getFreePort() {
  return new Promise((resolvePort, reject) => {
    const server = createServer();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      const port = address && typeof address === 'object' ? address.port : null;
      server.close(() => {
        if (port) {
          resolvePort(port);
        } else {
          reject(new Error('Unable to allocate a local app-server port'));
        }
      });
    });
  });
}

function findCodexCommand() {
  if (process.env.CODEX_CLI_PATH) {
    return process.env.CODEX_CLI_PATH;
  }

  const localAppData = process.env.LOCALAPPDATA;
  if (localAppData) {
    const binRoot = join(localAppData, 'OpenAI', 'Codex', 'bin');
    if (existsSync(binRoot)) {
      const candidates = [];
      for (const entry of readdirSync(binRoot, { withFileTypes: true })) {
        if (!entry.isDirectory()) {
          continue;
        }
        const candidate = join(binRoot, entry.name, process.platform === 'win32' ? 'codex.exe' : 'codex');
        if (existsSync(candidate)) {
          candidates.push({ path: candidate, mtimeMs: statSync(candidate).mtimeMs });
        }
      }
      candidates.sort((left, right) => right.mtimeMs - left.mtimeMs);
      if (candidates[0]) {
        return candidates[0].path;
      }
    }
  }

  return 'codex';
}

async function startTemporaryAppServer(timeoutMs) {
  const port = await getFreePort();
  const url = `ws://127.0.0.1:${port}`;
  const command = findCodexCommand();
  const child = spawn(command, ['app-server', '--listen', url], {
    stdio: ['ignore', 'ignore', 'pipe'],
    windowsHide: true,
  });

  let exited = false;
  let stderr = '';
  child.stderr?.on('data', (chunk) => {
    stderr += chunk.toString();
    if (stderr.length > 4000) {
      stderr = stderr.slice(-4000);
    }
  });
  child.once('exit', (code, signal) => {
    exited = true;
    if (code !== 0 && signal == null) {
      console.error(`Temporary codex app-server exited with code ${code}${stderr ? `: ${stderr.trim()}` : ''}`);
    }
  });

  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    if (exited) {
      throw new Error(
        `Temporary codex app-server exited before accepting connections${stderr ? `: ${stderr.trim()}` : ''}`,
      );
    }
    try {
      const client = new AppServerClient(url, timeoutMs);
      await client.connect();
      client.close();
      return { url, child };
    } catch {
      await new Promise((resolveDelay) => setTimeout(resolveDelay, 200));
    }
  }

  child.kill();
  throw new Error(`Timed out waiting for temporary codex app-server at ${url}`);
}

class AppServerClient {
  constructor(url, timeoutMs) {
    this.url = url;
    this.timeoutMs = timeoutMs;
    this.nextId = 1;
    this.pending = new Map();
    this.ws = null;
  }

  async connect() {
    if (typeof WebSocket !== 'function') {
      throw new Error('This helper requires Node.js with a global WebSocket implementation');
    }

    this.ws = new WebSocket(this.url);
    this.ws.addEventListener('message', (event) => this.handleMessage(event));
    this.ws.addEventListener('error', (event) => {
      for (const { reject } of this.pending.values()) {
        reject(new Error(`WebSocket error: ${event.message || 'unknown error'}`));
      }
      this.pending.clear();
    });

    await withTimeout(
      new Promise((resolveOpen, reject) => {
        this.ws.addEventListener('open', resolveOpen, { once: true });
        this.ws.addEventListener('error', () => reject(new Error(`Unable to connect to ${this.url}`)), { once: true });
      }),
      this.timeoutMs,
      'app-server connect',
    );

    await this.call('initialize', {
      clientInfo: { name: 'lwc-gmail-raw-attachment-downloader', version: '0.1.0' },
      capabilities: { experimentalApi: true },
    });
  }

  handleMessage(event) {
    let message;
    try {
      message = JSON.parse(String(event.data));
    } catch {
      return;
    }

    if (!Object.prototype.hasOwnProperty.call(message, 'id')) {
      return;
    }

    const pending = this.pending.get(message.id);
    if (!pending) {
      return;
    }

    clearTimeout(pending.timeout);
    this.pending.delete(message.id);
    if (message.error) {
      pending.reject(new Error(message.error.message || JSON.stringify(message.error)));
    } else {
      pending.resolve(message.result);
    }
  }

  call(method, params) {
    const id = this.nextId;
    this.nextId += 1;
    const payload = { id, method, params };
    const promise = new Promise((resolveCall, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`${method} timed out after ${this.timeoutMs} ms`));
      }, this.timeoutMs);
      this.pending.set(id, { resolve: resolveCall, reject, timeout });
    });
    this.ws.send(JSON.stringify(payload));
    return promise;
  }

  close() {
    if (this.ws && this.ws.readyState <= 1) {
      this.ws.close();
    }
  }
}

async function ensureThreadLoaded(client, cwd, explicitThreadId) {
  let threadId = explicitThreadId;

  if (!threadId) {
    const list = await client.call('thread/list', {
      cwd,
      limit: 1,
      useStateDbOnly: true,
    });
    threadId = list?.data?.[0]?.id;
  }

  if (!threadId) {
    throw new Error('Unable to infer a Codex thread id. Pass --thread-id explicitly.');
  }

  const loaded = await client.call('thread/loaded/list', { limit: 100 });
  if (!loaded?.data?.includes(threadId)) {
    await client.call('thread/resume', { threadId, cwd });
  }

  return threadId;
}

async function callGmailReadEmail(client, threadId, messageId) {
  const result = await client.call('mcpServer/tool/call', {
    server: 'codex_apps',
    threadId,
    tool: 'gmail.read_email',
    arguments: {
      message_id: messageId,
      include_raw_mime: true,
    },
  });

  if (result?.isError) {
    const text = result.content?.map((item) => item.text || '').join('\n') || 'Gmail read_email failed';
    throw new Error(text);
  }

  if (!result?.structuredContent) {
    throw new Error('Gmail read_email returned no structured content');
  }

  return result.structuredContent;
}

function base64UrlToBuffer(value) {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalized + '='.repeat((4 - (normalized.length % 4)) % 4);
  return Buffer.from(padded, 'base64');
}

function unfoldHeaders(rawHeaders) {
  return rawHeaders.replace(/\r?\n[ \t]+/g, ' ');
}

function splitHeaderBody(message) {
  const match = /\r?\n\r?\n/.exec(message);
  if (!match) {
    return { headers: new Map(), body: '' };
  }

  const rawHeaders = unfoldHeaders(message.slice(0, match.index));
  const body = message.slice(match.index + match[0].length);
  const headers = new Map();
  for (const line of rawHeaders.split(/\r?\n/)) {
    const index = line.indexOf(':');
    if (index < 0) {
      continue;
    }
    const key = line.slice(0, index).trim().toLowerCase();
    const value = line.slice(index + 1).trim();
    if (!headers.has(key)) {
      headers.set(key, []);
    }
    headers.get(key).push(value);
  }
  return { headers, body };
}

function header(headers, name) {
  return headers.get(name.toLowerCase())?.[0] || '';
}

function splitHeaderParameters(value) {
  const parts = [];
  let current = '';
  let quoted = false;
  let escaped = false;

  for (const char of value) {
    if (escaped) {
      current += char;
      escaped = false;
      continue;
    }
    if (char === '\\' && quoted) {
      escaped = true;
      continue;
    }
    if (char === '"') {
      quoted = !quoted;
      current += char;
      continue;
    }
    if (char === ';' && !quoted) {
      parts.push(current.trim());
      current = '';
      continue;
    }
    current += char;
  }
  if (current.trim()) {
    parts.push(current.trim());
  }
  return parts;
}

function unquote(value) {
  const trimmed = value.trim();
  if (trimmed.startsWith('"') && trimmed.endsWith('"')) {
    return trimmed.slice(1, -1).replace(/\\(["\\])/g, '$1');
  }
  return trimmed;
}

function decodeRfc2231(value) {
  const unquoted = unquote(value);
  const match = /^([^']*)'[^']*'(.*)$/.exec(unquoted);
  if (!match) {
    return unquoted;
  }
  const charset = match[1].toLowerCase();
  const encoded = match[2];
  if (charset && charset !== 'utf-8' && charset !== 'us-ascii') {
    return decodeURIComponent(encoded);
  }
  return decodeURIComponent(encoded);
}

function parseParameterizedHeader(value) {
  const parts = splitHeaderParameters(value);
  const main = (parts.shift() || '').trim().toLowerCase();
  const params = new Map();

  for (const part of parts) {
    const index = part.indexOf('=');
    if (index < 0) {
      continue;
    }
    const key = part.slice(0, index).trim().toLowerCase();
    const rawValue = part.slice(index + 1).trim();
    params.set(key, key.endsWith('*') ? decodeRfc2231(rawValue) : unquote(rawValue));
  }

  if (params.has('filename*') && !params.has('filename')) {
    params.set('filename', params.get('filename*'));
  }
  if (params.has('name*') && !params.has('name')) {
    params.set('name', params.get('name*'));
  }

  return { main, params };
}

function splitMultipart(body, boundary) {
  const marker = `--${boundary}`;
  const endMarker = `--${boundary}--`;
  const lines = body.split(/\r?\n/);
  const parts = [];
  let collecting = false;
  let current = [];

  for (const line of lines) {
    if (line === marker || line === endMarker) {
      if (collecting) {
        parts.push(current.join('\r\n'));
      }
      if (line === endMarker) {
        break;
      }
      collecting = true;
      current = [];
      continue;
    }
    if (collecting) {
      current.push(line);
    }
  }

  return parts;
}

function decodeQuotedPrintable(value) {
  const compact = value.replace(/=\r?\n/g, '');
  const bytes = [];
  for (let i = 0; i < compact.length; i += 1) {
    if (compact[i] === '=' && /^[0-9A-Fa-f]{2}$/.test(compact.slice(i + 1, i + 3))) {
      bytes.push(Number.parseInt(compact.slice(i + 1, i + 3), 16));
      i += 2;
    } else {
      bytes.push(compact.charCodeAt(i) & 0xff);
    }
  }
  return Buffer.from(bytes);
}

function decodeBody(body, encoding) {
  const normalizedEncoding = encoding.trim().toLowerCase();
  if (normalizedEncoding === 'base64') {
    return Buffer.from(body.replace(/\s+/g, ''), 'base64');
  }
  if (normalizedEncoding === 'quoted-printable') {
    return decodeQuotedPrintable(body);
  }
  return Buffer.from(body.replace(/\r?\n$/, ''), 'latin1');
}

function collectMimeAttachments(messageText) {
  const attachments = [];

  function visit(partText) {
    const { headers, body } = splitHeaderBody(partText);
    const contentType = parseParameterizedHeader(header(headers, 'content-type') || 'text/plain');
    const disposition = parseParameterizedHeader(header(headers, 'content-disposition'));

    if (contentType.main.startsWith('multipart/')) {
      const boundary = contentType.params.get('boundary');
      if (!boundary) {
        return;
      }
      for (const child of splitMultipart(body, boundary)) {
        visit(child);
      }
      return;
    }

    const filename = disposition.params.get('filename') || contentType.params.get('name') || null;
    const isAttachment = disposition.main === 'attachment' || filename !== null;
    if (!isAttachment) {
      return;
    }

    const transferEncoding = header(headers, 'content-transfer-encoding') || '7bit';
    attachments.push({
      filename,
      mimeType: contentType.main || 'application/octet-stream',
      bytes: decodeBody(body, transferEncoding),
      transferEncoding,
    });
  }

  visit(messageText);
  return attachments;
}

function selectAttachment({ mimeAttachments, gmailMessage, attachmentId, filename }) {
  const summaries = gmailMessage.attachments || [];
  let selectedSummary = null;
  let selectedFilename = filename || null;

  if (attachmentId) {
    selectedSummary = summaries.find((item) => item.attachment_id === attachmentId);
    if (!selectedSummary) {
      if (summaries.length === 1) {
        selectedSummary = summaries[0];
      } else {
        throw new Error(`No Gmail attachment metadata matched attachment_id ${attachmentId}`);
      }
    }
    selectedFilename = selectedSummary.filename;
  } else if (filename) {
    selectedSummary = summaries.find((item) => item.filename === filename) || null;
  } else if (summaries.length === 1) {
    selectedSummary = summaries[0];
    selectedFilename = selectedSummary.filename;
  } else if (mimeAttachments.length === 1) {
    selectedFilename = mimeAttachments[0].filename;
  } else {
    throw new Error('Multiple attachments found. Pass --filename or --attachment-id.');
  }

  const matches = selectedFilename
    ? mimeAttachments.filter((item) => item.filename === selectedFilename)
    : mimeAttachments;

  if (matches.length === 0) {
    throw new Error(`No MIME attachment matched filename ${selectedFilename}`);
  }
  if (matches.length > 1) {
    throw new Error(`Multiple MIME attachments matched filename ${selectedFilename}`);
  }

  const selected = matches[0];
  if (selectedSummary?.size_bytes != null && selected.bytes.length !== selectedSummary.size_bytes) {
    throw new Error(
      `Decoded attachment size ${selected.bytes.length} did not match Gmail metadata size ${selectedSummary.size_bytes}`,
    );
  }

  return {
    attachment: selected,
    summary: selectedSummary,
  };
}

function resolveOutputPath(out, filename) {
  const absoluteOut = isAbsolute(out) ? out : resolve(out);
  if (existsSync(absoluteOut) && statSync(absoluteOut).isDirectory()) {
    if (!filename) {
      throw new Error('--out is a directory but the selected attachment has no filename');
    }
    return join(absoluteOut, basename(filename));
  }
  if (out.endsWith('/') || out.endsWith('\\')) {
    if (!filename) {
      throw new Error('--out is a directory but the selected attachment has no filename');
    }
    return join(absoluteOut, basename(filename));
  }
  return absoluteOut;
}

async function main() {
  const args = parseArgs(process.argv);
  if (args.help) {
    printUsage();
    return;
  }

  let temporaryServer = null;
  const appServerUrl = args.appServerUrl || process.env.CODEX_APP_SERVER_URL;
  let url = appServerUrl;
  if (!url) {
    temporaryServer = await startTemporaryAppServer(args.timeoutMs);
    url = temporaryServer.url;
  }

  const client = new AppServerClient(url, args.timeoutMs);
  try {
    await client.connect();
    const threadId = await ensureThreadLoaded(client, args.cwd, args.threadId);
    const gmailMessage = await callGmailReadEmail(client, threadId, args.messageId);

    const rawMime = gmailMessage.raw_mime_base64url
      ? base64UrlToBuffer(gmailMessage.raw_mime_base64url).toString('latin1')
      : gmailMessage.raw_mime;
    if (!rawMime) {
      throw new Error('Gmail read_email did not return raw MIME. Ensure include_raw_mime is supported for this message.');
    }

    const mimeAttachments = collectMimeAttachments(rawMime);
    if (mimeAttachments.length === 0) {
      throw new Error('No attachments found in raw MIME payload');
    }

    const { attachment, summary } = selectAttachment({
      mimeAttachments,
      gmailMessage,
      attachmentId: args.attachmentId,
      filename: args.filename,
    });

    const savedPath = resolveOutputPath(args.out, attachment.filename);
    if (existsSync(savedPath) && !args.overwrite) {
      throw new Error(`${savedPath} already exists. Pass --overwrite to replace it.`);
    }

    await mkdir(dirname(savedPath), { recursive: true });
    await writeFile(savedPath, attachment.bytes);

    const sha256 = createHash('sha256').update(attachment.bytes).digest('hex');
    const output = {
      savedPath,
      filename: attachment.filename || summary?.filename || null,
      mimeType: summary?.mime_type || attachment.mimeType || null,
      size: attachment.bytes.length,
      sha256,
      messageId: gmailMessage.id || args.messageId,
      attachmentId: summary?.attachment_id || args.attachmentId || null,
    };
    console.log(JSON.stringify(output, null, 2));
  } finally {
    client.close();
    if (temporaryServer?.child && !temporaryServer.child.killed) {
      temporaryServer.child.kill();
    }
  }
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
