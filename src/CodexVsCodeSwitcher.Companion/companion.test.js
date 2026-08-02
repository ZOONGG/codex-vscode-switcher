'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { describeWorkspace, hasShortcutConflict, openSidebarWithRetry } = require('./companion-core');

test('reports folders, workspace files, changes, and empty windows without file contents', () => {
    assert.deepEqual(describeWorkspace(null, ['D:\\repo']), {
        workspaceType: 'Folder', workspacePath: 'D:\\repo', workspaceFolders: ['D:\\repo'], isEmpty: false,
    });
    assert.equal(describeWorkspace('D:\\project.code-workspace', ['D:\\repo']).workspaceType, 'WorkspaceFile');
    assert.equal(describeWorkspace('D:\\multi.code-workspace', ['D:\\one', 'D:\\two']).workspaceType, 'MultiRoot');
    assert.equal(describeWorkspace(null, []).isEmpty, true);
});

test('waits for extension activation and invokes the official sidebar command', async () => {
    let clock = 0;
    let lookups = 0;
    let activated = false;
    let commands = 0;
    const result = await openSidebarWithRetry({
        now: () => clock,
        delay: async value => { clock += value; },
        timeoutMs: 5000,
        retryDelayMs: 250,
        getExtension: () => ++lookups < 3 ? null : { isActive: false, activate: async () => { activated = true; } },
        executeCommand: async () => {
            commands += 1;
            if (!activated) {
                throw new Error('not active');
            }
        },
    });
    assert.equal(result.status, 'Succeeded');
    assert.equal(activated, true);
    assert.equal(commands, 2);
});

test('a hanging extension activation cannot escape the overall retry bound', async () => {
    const started = Date.now();
    const result = await openSidebarWithRetry({
        now: Date.now,
        delay: milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
        timeoutMs: 45,
        retryDelayMs: 5,
        attemptTimeoutMs: 10,
        getExtension: () => ({ isActive: false, activate: () => new Promise(() => {}) }),
        executeCommand: async () => { throw new Error('not ready'); },
    });
    assert.equal(result.status, 'Failed');
    assert.equal(result.failureCode, 'command-timeout');
    assert.ok(Date.now() - started < 250);
});

test('bounded retries stop and return a sidebar-only failure', async () => {
    let clock = 0;
    const result = await openSidebarWithRetry({
        now: () => clock,
        delay: async value => { clock += value; },
        timeoutMs: 1000,
        retryDelayMs: 250,
        getExtension: () => ({ isActive: true, activate: async () => {} }),
        executeCommand: async () => { throw new Error('not ready'); },
    });
    assert.equal(result.status, 'Failed');
    assert.equal(result.failureCode, 'command-timeout');
    assert.ok(result.attempts > 1);
    assert.ok(result.attempts <= 5);
});

test('Ctrl+Alt+C conflicts are detected but existing bindings are not overwritten', () => {
    const source = '[{"key":"ctrl+alt+c","command":"existing.command",},]';
    assert.equal(hasShortcutConflict(source, 'codexVsCodeSwitcher.openCodex'), true);
    assert.equal(source.includes('existing.command'), true);
    assert.equal(hasShortcutConflict(
        '[{"key":"ctrl+alt+c","command":"codexVsCodeSwitcher.openCodex"}]',
        'codexVsCodeSwitcher.openCodex'), false);
});
