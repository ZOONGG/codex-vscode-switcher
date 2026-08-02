'use strict';

async function openSidebarWithRetry(options) {
    const startedAt = options.now();
    let attempts = 0;
    let sawExtension = false;
    do {
        const extension = options.getExtension();
        if (extension) {
            sawExtension = true;
            attempts += 1;
            try {
                if (!extension.isActive) {
                    await extension.activate();
                }
                await options.executeCommand();
                return { status: 'Succeeded', failureCode: null, attempts };
            } catch {
                // Extension activation and command registration can complete on different ticks.
            }
        }

        if (options.now() - startedAt >= options.timeoutMs) {
            break;
        }
        await options.delay(options.retryDelayMs);
    } while (options.now() - startedAt < options.timeoutMs);

    return {
        status: 'Failed',
        failureCode: sawExtension ? 'command-timeout' : 'extension-missing',
        attempts,
    };
}

function describeWorkspace(workspaceFile, folderPaths) {
    const folders = [...folderPaths];
    if (workspaceFile && folders.length > 1) {
        return { workspaceType: 'MultiRoot', workspacePath: workspaceFile, workspaceFolders: folders, isEmpty: false };
    }
    if (workspaceFile) {
        return { workspaceType: 'WorkspaceFile', workspacePath: workspaceFile, workspaceFolders: folders, isEmpty: false };
    }
    if (folders.length > 1) {
        return { workspaceType: 'MultiRoot', workspacePath: folders[0], workspaceFolders: folders, isEmpty: false };
    }
    if (folders.length === 1) {
        return { workspaceType: 'Folder', workspacePath: folders[0], workspaceFolders: folders, isEmpty: false };
    }
    return { workspaceType: 'Empty', workspacePath: null, workspaceFolders: [], isEmpty: true };
}

function hasShortcutConflict(source, commandId) {
    const parsed = JSON.parse(stripJsonCommentsAndTrailingCommas(source));
    return Array.isArray(parsed) && parsed.some(binding =>
        typeof binding === 'object'
        && binding !== null
        && String(binding.key || '').toLowerCase() === 'ctrl+alt+c'
        && binding.command !== commandId);
}

function stripJsonCommentsAndTrailingCommas(source) {
    return source
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/^\s*\/\/.*$/gm, '')
        .replace(/,\s*([}\]])/g, '$1');
}

module.exports = { describeWorkspace, hasShortcutConflict, openSidebarWithRetry };
