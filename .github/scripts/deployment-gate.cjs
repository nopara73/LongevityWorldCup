// Deployment consumes completed push CI for the same master commit. Manual
// audits can replace the test suite, so their successful runs never qualify.
const workflows = [
    { path: '.github/workflows/dotnet.yml', name: '.NET', required: true },
    { path: '.github/workflows/codeql.yml', name: 'CodeQL', required: true },
    // Image delivery has path filters; the full .NET suite also runs its tests.
    { path: '.github/workflows/image-delivery.yml', name: 'Image delivery', required: false },
    { path: '.github/workflows/reddit.yml', name: 'Reddit app', required: false }
];

function assess(runs, sha, repository) {
    const checked = [];
    const pending = [];
    for (const workflow of workflows) {
        const latest = runs.filter(run => run.path === workflow.path &&
            run.head_sha === sha && run.head_branch === 'master' && run.event === 'push' &&
            run.head_repository?.full_name === repository)
            .sort((a, b) => b.id - a.id)[0];
        if (!latest) {
            if (workflow.required) pending.push(workflow.name);
            continue;
        }
        checked.push(latest);
        if (latest.status !== 'completed') pending.push(workflow.name);
        else if (latest.conclusion !== 'success') {
            return { state: 'blocked', reason: `${workflow.name} ended with ${latest.conclusion}.`, checked };
        }
    }
    if (pending.length) return { state: 'waiting', reason: `Waiting for ${pending.join(', ')}.`, checked };
    return { state: 'ready', reason: 'Every applicable CI workflow passed for this commit.', checked };
}

async function gate({ github, context, core, sleep = ms => new Promise(resolve => setTimeout(resolve, ms)),
    now = Date.now, timeoutMs = 30 * 60 * 1000 }) {
    core.setOutput('deploy', 'false');
    const repository = `${context.repo.owner}/${context.repo.repo}`;
    const automatic = context.eventName === 'workflow_run';
    const trigger = context.payload.workflow_run;
    if (automatic && (trigger?.event !== 'push' || trigger.head_branch !== 'master' ||
        trigger.head_repository?.full_name !== repository || trigger.conclusion !== 'success' ||
        !workflows.some(workflow => workflow.name === trigger.name))) {
        core.setFailed('Deployment requires successful push CI from this repository’s master branch.');
        return;
    }
    if (!automatic && (!['workflow_dispatch', 'repository_dispatch'].includes(context.eventName) ||
        context.ref !== 'refs/heads/master')) {
        core.setFailed('Manual deployment must target master.');
        return;
    }
    const sha = automatic ? trigger.head_sha : context.sha;
    if (!/^[a-f0-9]{40}$/.test(sha || '')) throw new Error('Invalid deployment commit.');
    const deadline = now() + timeoutMs;
    let previousMessage;
    while (true) {
        const { data: branch } = await github.rest.git.getRef({ ...context.repo, ref: 'heads/master' });
        if (branch.object.sha !== sha) {
            core.info(`Skipping superseded commit ${sha}; master is ${branch.object.sha}.`);
            return;
        }
        const runs = await github.paginate(github.rest.actions.listWorkflowRunsForRepo, {
            ...context.repo, head_sha: sha, branch: 'master', event: 'push', per_page: 100
        });
        const result = assess(runs, sha, repository);
        if (result.reason !== previousMessage) core.info(result.reason);
        previousMessage = result.reason;
        if (result.state === 'ready') {
            // Each CI workflow emits a completion event. Only the last one may
            // deploy, even if earlier events start late in the concurrency queue.
            const last = [...result.checked].sort((a, b) =>
                b.updated_at.localeCompare(a.updated_at) || b.id - a.id)[0];
            if (automatic && last.id !== trigger.id) {
                core.info(`Run ${last.id} owns deployment after the final CI completion.`);
                return;
            }
            core.setOutput('sha', sha);
            core.setOutput('deploy', 'true');
            core.info(`Approved ${sha}: ${result.checked.map(run => run.html_url).join(', ')}`);
            return;
        }
        if (result.state === 'blocked') {
            core.setFailed(result.reason);
            return;
        }
        // Completion events will wake deployment again. Explicit manual requests
        // wait here, but cannot bypass missing, running, failed or cancelled CI.
        if (automatic) return;
        if (now() >= deadline) {
            core.setFailed(`Timed out waiting for CI on ${sha}.`);
            return;
        }
        await sleep(30000);
    }
}

module.exports = { assess, gate };
