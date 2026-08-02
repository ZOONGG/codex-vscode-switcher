'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const vscode = require('vscode');
const { describeWorkspace, hasShortcutConflict, openSidebarWithRetry } = require('./companion-core');

const PROTOCOL_VERSION = 1;
const CODEX_EXTENSION_ID = 'openai.chatgpt';
const OPEN_CODEX_COMMAND = 'chatgpt.openSidebar';
const BRIDGE_PATH_VARIABLE = 'CODEX_VSCODE_SWITCHER_BRIDGE_PATH';
const SESSION_VARIABLE = 'CODEX_VSCODE_SWITCHER_SESSION_ID';
const OPEN_CODEX_VARIABLE = 'CODEX_VSCODE_SWITCHER_OPEN_CODEX';
const USER_DATA_VARIABLE = 'CODEX_VSCODE_SWITCHER_USER_DATA_DIR';
const STATE_FILE = 'workspace-state.json';
const COMMAND_FILE = 'command-request.json';
const RETRY_DELAY_MS = 750;
const SIDEBAR_TIMEOUT_MS = 20000;

let bridgeDirectory;
let sessionId;
let windowId;
let sidebarStatus = 'NotRequested';
let sidebarFailureCode = null;
let shortcutConflict = false;
let lastCommandRequestId = null;

async function activate(context) {
    bridgeDirectory = process.env[BRIDGE_PATH_VARIABLE];
    sessionId = process.env[SESSION_VARIABLE];
    if (!bridgeDirectory || !sessionId) {
        return;
    }

    windowId = context.globalState.get('installationWindowId');
    if (!windowId) {
        windowId = crypto.randomUUID();
        await context.globalState.update('installationWindowId', windowId);
    }

    shortcutConflict = await detectShortcutConflict();
    const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 20);
    status.name = 'Codex';
    status.text = '$(comment-discussion) Codex';
    status.tooltip = 'Open and focus Codex';
    status.command = 'codexVsCodeSwitcher.openCodex';
    status.show();
    context.subscriptions.push(status);

    context.subscriptions.push(vscode.commands.registerCommand(
        'codexVsCodeSwitcher.openCodex',
        () => openCodexWithRetry()));
    context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(() => reportState()));
    context.subscriptions.push(vscode.window.onDidChangeWindowState(() => reportState()));

    const commandTimer = setInterval(() => pollCommandFile(), 500);
    context.subscriptions.push({ dispose: () => clearInterval(commandTimer) });

    await reportState();
    if (process.env[OPEN_CODEX_VARIABLE] === '1') {
        void openCodexWithRetry();
    }
}

async function openCodexWithRetry() {
    sidebarStatus = 'Pending';
    sidebarFailureCode = null;
    await reportState();
    const result = await openSidebarWithRetry({
        now: Date.now,
        delay,
        timeoutMs: SIDEBAR_TIMEOUT_MS,
        retryDelayMs: RETRY_DELAY_MS,
        attemptTimeoutMs: 3000,
        getExtension: () => vscode.extensions.getExtension(CODEX_EXTENSION_ID),
        executeCommand: () => vscode.commands.executeCommand(OPEN_CODEX_COMMAND),
    });
    sidebarStatus = result.status;
    sidebarFailureCode = result.failureCode;
    await reportState();
    return result.status === 'Succeeded';
}

async function reportState() {
    if (!bridgeDirectory || !sessionId) {
        return;
    }

    const folders = (vscode.workspace.workspaceFolders || [])
        .filter(folder => folder.uri.scheme === 'file')
        .map(folder => path.normalize(folder.uri.fsPath));
    const workspaceFile = vscode.workspace.workspaceFile?.scheme === 'file'
        ? path.normalize(vscode.workspace.workspaceFile.fsPath)
        : null;
    const workspace = describeWorkspace(workspaceFile, folders);

    const message = {
        protocolVersion: PROTOCOL_VERSION,
        sessionId,
        windowId,
        timestampUtc: new Date().toISOString(),
        ...workspace,
        codexExtensionInstalled: Boolean(vscode.extensions.getExtension(CODEX_EXTENSION_ID)),
        sidebarStatus,
        shortcutConflict,
        sidebarFailureCode,
    };
    await writeJsonAtomically(path.join(bridgeDirectory, STATE_FILE), message);
}

async function pollCommandFile() {
    const commandPath = path.join(bridgeDirectory, COMMAND_FILE);
    let command;
    try {
        const stat = await fs.promises.stat(commandPath);
        if (stat.size <= 0 || stat.size > 64 * 1024) {
            return;
        }
        command = JSON.parse(await fs.promises.readFile(commandPath, 'utf8'));
    } catch {
        return;
    }

    if (command.protocolVersion !== PROTOCOL_VERSION
        || command.sessionId !== sessionId
        || typeof command.requestId !== 'string'
        || command.requestId === lastCommandRequestId) {
        return;
    }

    lastCommandRequestId = command.requestId;
    if (command.action === 'openCodex') {
        await openCodexWithRetry();
    } else if (command.action === 'configureShortcut') {
        await vscode.commands.executeCommand('workbench.action.openGlobalKeybindings');
    }
}

async function detectShortcutConflict() {
    const userDataDirectory = process.env[USER_DATA_VARIABLE];
    if (!userDataDirectory) {
        return false;
    }

    const keybindingsPath = path.join(userDataDirectory, 'User', 'keybindings.json');
    try {
        const stat = await fs.promises.stat(keybindingsPath);
        if (stat.size > 1024 * 1024) {
            return true;
        }
        const source = await fs.promises.readFile(keybindingsPath, 'utf8');
        return hasShortcutConflict(source, 'codexVsCodeSwitcher.openCodex');
    } catch {
        return false;
    }
}

async function writeJsonAtomically(target, value) {
    await fs.promises.mkdir(path.dirname(target), { recursive: true });
    const temporary = path.join(
        path.dirname(target),
        `.${path.basename(target)}-${crypto.randomUUID()}.tmp`);
    try {
        await fs.promises.writeFile(temporary, JSON.stringify(value, null, 2), {
            encoding: 'utf8',
            flag: 'wx',
        });
        await fs.promises.rename(temporary, target);
    } finally {
        await fs.promises.rm(temporary, { force: true }).catch(() => {});
    }
}

function delay(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function deactivate() {}

module.exports = { activate, deactivate };
