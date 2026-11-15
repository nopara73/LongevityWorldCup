const test = require('node:test');
const assert = require('node:assert/strict');
const { assess, gate } = require('./deployment-gate.cjs');

const sha = 'a'.repeat(40);
const otherSha = 'b'.repeat(40);
const repository = 'owner/site';
function run(file, id, overrides = {}) {
    return { id, path: `.github/workflows/${file}.yml`, name: { dotnet: '.NET', codeql: 'CodeQL', 'image-delivery': 'Image delivery' }[file],
        head_sha: sha, head_branch: 'master', head_repository: { full_name: repository }, event: 'push',
        status: 'completed', conclusion: 'success', updated_at: `2026-11-15T10:00:0${id}Z`,
        html_url: `https://github.com/${repository}/actions/runs/${id}`, ...overrides };
}
const complete = () => [run('dotnet', 3), run('codeql', 2), run('image-delivery', 1)];

test('all applicable push CI must pass for the exact commit', () => {
    assert.equal(assess(complete(), sha, repository).state, 'ready');
    for (const file of ['dotnet', 'codeql']) {
        assert.equal(assess(complete().filter(r => !r.path.endsWith(`/${file}.yml`)), sha, repository).state, 'waiting');
    }
});

for (const status of ['queued', 'in_progress', 'waiting', 'pending']) {
    test(`${status} CI cannot deploy even with an old success conclusion`, () => {
        assert.equal(assess([run('dotnet', 3, { status }), run('codeql', 2)], sha, repository).state, 'waiting');
    });
}
for (const conclusion of ['failure', 'cancelled', 'timed_out', 'skipped', 'neutral', 'action_required', null]) {
    test(`${conclusion} CI blocks deployment`, () => {
        assert.equal(assess([run('dotnet', 3, { conclusion }), run('codeql', 2)], sha, repository).state, 'blocked');
    });
}
test('a newer failed push run overrides an older green run', () => {
    assert.equal(assess([...complete(), run('dotnet', 4, { conclusion: 'failure' })], sha, repository).state, 'blocked');
});
test('a rerun must finish before an earlier success can authorize deployment', () => {
    assert.equal(assess([run('dotnet', 3, { run_attempt: 2, status: 'in_progress', conclusion: null }), run('codeql', 2)], sha, repository).state, 'waiting');
});
for (const wrong of [{ head_sha: otherSha }, { head_branch: 'feature' }, { head_repository: { full_name: 'fork/site' } },
    { event: 'pull_request' }, { event: 'workflow_dispatch' }, { event: 'schedule' }]) {
    test(`unrelated CI cannot satisfy the gate: ${JSON.stringify(wrong)}`, () => {
        assert.equal(assess([run('dotnet', 3, wrong), run('codeql', 2)], sha, repository).state, 'waiting');
    });
}
test('path-filtered image CI is optional only when absent', () => {
    const required = [run('dotnet', 3), run('codeql', 2)];
    assert.equal(assess(required, sha, repository).state, 'ready');
    assert.equal(assess([...required, run('image-delivery', 1, { status: 'in_progress' })], sha, repository).state, 'waiting');
    assert.equal(assess([...required, run('image-delivery', 1, { conclusion: 'failure' })], sha, repository).state, 'blocked');
});
test('a previous failed deployment is not a CI dependency on itself', () => {
    assert.equal(assess([...complete(), run('deploy', 4, { conclusion: 'failure' })], sha, repository).state, 'ready');
});

function harness({ event = 'workflow_run', runs = complete(), trigger = run('dotnet', 3), master = sha } = {}) {
    const outputs = {}, failures = [], messages = [];
    let requests = 0;
    return {
        outputs, failures, messages,
        context: { eventName: event, repo: { owner: 'owner', repo: 'site' }, ref: 'refs/heads/master',
            sha: event === 'workflow_run' ? otherSha : sha, payload: { workflow_run: trigger } },
        github: {
            rest: { git: { getRef: async () => ({ data: { object: { sha: master } } }) }, actions: { listWorkflowRunsForRepo() {} } },
            paginate: async (_method, options) => {
                assert.equal(options.head_sha, sha);
                assert.equal(options.event, 'push');
                return typeof runs === 'function' ? runs(++requests) : runs;
            }
        },
        core: { setOutput: (key, value) => { outputs[key] = value; }, setFailed: value => failures.push(value), info: value => messages.push(value) },
        sleep: async () => { throw new Error('Unexpected waiting'); }
    };
}

test('automatic deployment uses the tested head SHA, not workflow_run’s default-branch SHA', async () => {
    const h = harness();
    await gate(h);
    assert.deepEqual(h.outputs, { deploy: 'true', sha });
    assert.deepEqual(h.failures, []);
});
test('early or late duplicate completion events cannot deploy', async () => {
    for (const runs of [complete(), [run('dotnet', 3, { status: 'in_progress' }), run('codeql', 2)]]) {
        const h = harness({ runs, trigger: run('codeql', 2) });
        await gate(h);
        assert.deepEqual(h.outputs, { deploy: 'false' });
    }
});
test('a successful rerun can own deployment after all other CI passes', async () => {
    const trigger = run('codeql', 2, { run_attempt: 2, updated_at: '2026-11-15T10:01:00Z' });
    const h = harness({ trigger, runs: [run('dotnet', 3), trigger] });
    await gate(h);
    assert.equal(h.outputs.deploy, 'true');
});
test('a superseded commit cannot deploy', async () => {
    const h = harness({ master: otherSha });
    h.github.paginate = async () => { throw new Error('Stale candidates must stop before CI lookup'); };
    await gate(h);
    assert.deepEqual(h.outputs, { deploy: 'false' });
});
test('untrusted completion events stop before reading repository state', async () => {
    for (const overrides of [{ event: 'pull_request' }, { head_repository: { full_name: 'fork/site' } },
        { head_branch: 'feature' }, { conclusion: 'failure' }, { name: 'Unrelated workflow' }]) {
        const h = harness({ trigger: run('dotnet', 3, overrides) });
        h.github.rest.git.getRef = async () => { throw new Error('Unexpected API call'); };
        await gate(h);
        assert.deepEqual(h.outputs, { deploy: 'false' });
        assert.equal(h.failures.length, 1);
    }
});
for (const event of ['workflow_dispatch', 'repository_dispatch']) {
    test(`${event} waits for full CI and has no bypass`, async () => {
        let waits = 0;
        const h = harness({ event, runs: attempt => attempt === 1 ? [run('dotnet', 3, { status: 'in_progress' }), run('codeql', 2)] : complete() });
        h.sleep = async () => { waits++; assert.equal(h.outputs.deploy, 'false'); };
        await gate(h);
        assert.equal(waits, 1);
        assert.equal(h.outputs.deploy, 'true');
        const failed = harness({ event, runs: [run('dotnet', 3, { conclusion: 'failure' }), run('codeql', 2)] });
        await gate(failed);
        assert.deepEqual(failed.outputs, { deploy: 'false' });
        assert.equal(failed.failures.length, 1);
    });
}
test('manual deployment from another branch is rejected', async () => {
    const h = harness({ event: 'workflow_dispatch' });
    h.context.ref = 'refs/heads/feature';
    await gate(h);
    assert.equal(h.failures.length, 1);
    assert.equal(h.outputs.deploy, 'false');
});
test('missing CI eventually fails a manual request instead of silently deploying', async () => {
    const h = harness({ event: 'workflow_dispatch', runs: [] });
    let time = 0;
    h.now = () => time++;
    h.timeoutMs = 0;
    await gate(h);
    assert.equal(h.failures.length, 1);
    assert.equal(h.outputs.deploy, 'false');
});
test('an API error fails closed', async () => {
    const h = harness();
    h.github.paginate = async () => { throw new Error('API unavailable'); };
    await assert.rejects(gate(h), /API unavailable/);
    assert.deepEqual(h.outputs, { deploy: 'false' });
});
