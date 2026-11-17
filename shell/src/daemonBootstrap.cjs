'use strict';

const { spawn } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

const HealthTimeoutMs = 1500;
const PollIntervalMs = 350;
const ReadyTimeoutMs = 25000;
const TaskGraceMs = 4000;
const MaxHealthBodyBytes = 65536;
const StartingMessage = 'Starting RigForge hardware daemon…';

function isRigForgeHealth(data) {
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    return false;
  }

  if (typeof data.product === 'string') {
    return data.product === 'RigForge';
  }

  return typeof data.version === 'string' && typeof data.startedAt === 'string';
}

function classifyHealthResponse(statusCode, body) {
  if (statusCode !== 200) {
    return { status: 'port-conflict' };
  }

  try {
    const data = JSON.parse(body);
    if (isRigForgeHealth(data)) {
      return { status: 'ready', data };
    }
  } catch {
    // A live HTTP server that does not speak RigForge JSON owns the port.
  }

  return { status: 'port-conflict' };
}

function probeHealth({ host = '127.0.0.1', port = 5757, timeoutMs = HealthTimeoutMs } = {}) {
  return new Promise((resolve) => {
    let settled = false;
    const finish = (value) => {
      if (settled) {
        return;
      }

      settled = true;
      resolve(value);
    };

    const request = http.get({
      host,
      port,
      path: '/api/health',
      timeout: timeoutMs,
      headers: { Accept: 'application/json' },
    }, (response) => {
      const chunks = [];
      let size = 0;
      response.on('data', (chunk) => {
        size += chunk.length;
        if (size <= MaxHealthBodyBytes) {
          chunks.push(chunk);
        }
      });
      response.on('end', () => {
        const body = Buffer.concat(chunks).toString('utf8');
        finish(classifyHealthResponse(response.statusCode ?? 0, body));
      });
      response.on('error', () => finish({ status: 'down' }));
    });

    request.on('error', () => finish({ status: 'down' }));
    request.on('timeout', () => {
      request.destroy();
      finish({ status: 'down' });
    });
  });
}

function portConflict(port) {
  return {
    status: 'error',
    code: 'port-conflict',
    message: `Port ${port} is already in use by another application.`,
  };
}

function fail(onState, code, message) {
  const result = { status: 'error', code, message };
  onState({ phase: 'error', code, message });
  return result;
}

function normalizeHealth(health) {
  if (health && (health.status === 'ready' || health.status === 'port-conflict' || health.status === 'down')) {
    return health;
  }

  return { status: 'down' };
}

async function pollUntil(probe, sleep, now, deadline) {
  while (now() < deadline) {
    const health = normalizeHealth(await probe());
    if (health.status === 'ready' || health.status === 'port-conflict') {
      return health;
    }

    const remaining = deadline - now();
    if (remaining <= 0) {
      return health;
    }

    await sleep(Math.min(PollIntervalMs, remaining));
  }

  return { status: 'down' };
}

function finishWait(health, onState, port) {
  if (health.status === 'ready') {
    onState({ phase: 'ready' });
    return { status: 'ready' };
  }

  if (health.status === 'port-conflict') {
    return fail(onState, 'port-conflict', portConflict(port).message);
  }

  return fail(
    onState,
    'timeout',
    'RigForge did not respond in time. Choose Start RigForge to try again.',
  );
}

function elevationWasCancelled(result) {
  if (!result || result.cancelled === true) {
    return true;
  }

  return typeof result.code === 'number' && result.code !== 0;
}

function runCommand(spawnImpl, file, args, options) {
  return new Promise((resolve) => {
    let settled = false;
    const finish = (result) => {
      if (settled) {
        return;
      }

      settled = true;
      resolve(result);
    };

    let child;
    try {
      child = spawnImpl(file, args, options);
    } catch (error) {
      finish({ code: 1, stderr: error instanceof Error ? error.message : String(error) });
      return;
    }

    const stderr = [];
    if (child.stderr && typeof child.stderr.on === 'function') {
      child.stderr.on('data', (chunk) => {
        stderr.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(String(chunk)));
      });
    }

    child.on('error', (error) => {
      finish({ code: 1, stderr: error instanceof Error ? error.message : String(error) });
    });
    child.on('close', (code) => {
      finish({
        code: typeof code === 'number' ? code : 1,
        stderr: Buffer.concat(stderr).toString('utf8'),
      });
    });
  });
}

function createScheduledTaskStarter({ spawn: spawnImpl = spawn } = {}) {
  return function startScheduledTask() {
    return runCommand(spawnImpl, 'schtasks.exe', ['/Run', '/TN', 'RigForge'], {
      windowsHide: true,
    });
  };
}

function createElevatedStarter({ spawn: spawnImpl = spawn } = {}) {
  return function startElevatedExecutable({ exe, cwd }) {
    return runCommand(spawnImpl, 'powershell.exe', [
      '-NoProfile',
      '-NonInteractive',
      '-WindowStyle',
      'Hidden',
      '-Command',
      'Start-Process -FilePath $env:RIGFORGE_DAEMON_EXE -WorkingDirectory $env:RIGFORGE_DAEMON_CWD -Verb RunAs',
    ], {
      windowsHide: true,
      env: {
        ...process.env,
        RIGFORGE_DAEMON_EXE: exe,
        RIGFORGE_DAEMON_CWD: cwd,
      },
    });
  };
}

function createUserServiceStarter({ spawn: spawnImpl = spawn } = {}) {
  return function startUserService() {
    return runCommand(spawnImpl, 'systemctl', ['--user', 'start', 'rigforge.service'], {
      windowsHide: true,
    });
  };
}

function findRepoRoot(startDir, existsSync) {
  let current = path.resolve(startDir);
  for (let depth = 0; depth < 8; depth += 1) {
    if (existsSync(path.join(current, 'Directory.Build.props'))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      break;
    }

    current = parent;
  }

  return null;
}

function findDaemonExecutable({
  execPath = process.execPath,
  isPackaged = false,
  existsSync = fs.existsSync,
  repoRoot = null,
} = {}) {
  const beside = path.join(path.dirname(execPath), 'RigForge.Daemon.exe');
  if (existsSync(beside)) {
    return beside;
  }

  if (isPackaged) {
    return null;
  }

  const root = repoRoot || findRepoRoot(path.dirname(execPath), existsSync);
  if (!root) {
    return null;
  }

  const candidates = [
    path.join(root, 'artifacts', 'stage', 'win-x64-daemon', 'RigForge.Daemon.exe'),
    path.join(root, 'artifacts', 'rigforge-win-x64', 'RigForge.Daemon.exe'),
    path.join(root, 'src', 'Host.Daemon', 'bin', 'Release', 'net10.0', 'win-x64', 'RigForge.Daemon.exe'),
    path.join(root, 'src', 'Host.Daemon', 'bin', 'Debug', 'net10.0', 'win-x64', 'RigForge.Daemon.exe'),
  ];

  for (const candidate of candidates) {
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return null;
}

async function ensureDaemon(options = {}) {
  const host = options.host ?? '127.0.0.1';
  const port = options.port ?? 5757;
  const platform = options.platform ?? process.platform;
  const probe = options.probeHealth ?? (() => probeHealth({ host, port }));
  const sleep = options.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
  const now = options.now ?? (() => Date.now());
  const onState = typeof options.onState === 'function' ? options.onState : () => {};
  const startScheduledTask = options.startScheduledTask ?? createScheduledTaskStarter();
  const startElevatedExecutable = options.startElevatedExecutable ?? createElevatedStarter();
  const startUserService = options.startUserService ?? createUserServiceStarter();
  const findDaemon = options.findDaemonExecutable ?? (() => findDaemonExecutable({
    execPath: options.execPath,
    isPackaged: options.packaged === true,
    repoRoot: options.repoRoot ?? null,
  }));

  const first = normalizeHealth(await probe());
  if (first.status === 'ready') {
    return { status: 'ready' };
  }

  if (first.status === 'port-conflict') {
    return fail(onState, 'port-conflict', portConflict(port).message);
  }

  onState({ phase: 'starting', message: StartingMessage });
  const startedAt = now();
  const deadline = startedAt + ReadyTimeoutMs;

  if (platform === 'linux') {
    try {
      await startUserService();
    } catch {
      // The health poll below still reports a timeout if the service cannot start.
    }

    return finishWait(await pollUntil(probe, sleep, now, deadline), onState, port);
  }

  if (platform !== 'win32') {
    return fail(
      onState,
      'unsupported',
      'Automatic daemon startup is not supported on this operating system.',
    );
  }

  let taskCode = 1;
  try {
    const task = await startScheduledTask();
    taskCode = task && typeof task.code === 'number' ? task.code : 1;
  } catch {
    taskCode = 1;
  }

  if (taskCode === 0) {
    const earlyDeadline = Math.min(deadline, startedAt + TaskGraceMs);
    const early = await pollUntil(probe, sleep, now, earlyDeadline);
    if (early.status === 'ready') {
      onState({ phase: 'ready' });
      return { status: 'ready' };
    }

    if (early.status === 'port-conflict') {
      return fail(onState, 'port-conflict', portConflict(port).message);
    }
  }

  const exe = findDaemon();
  if (!exe) {
    if (taskCode !== 0) {
      return fail(onState, 'daemon-missing', 'RigForge.Daemon.exe was not found next to the application.');
    }
  } else {
    let elevated;
    try {
      elevated = await startElevatedExecutable({ exe, cwd: path.dirname(exe) });
    } catch (error) {
      elevated = { cancelled: true, code: 1, stderr: error instanceof Error ? error.message : String(error) };
    }

    if (elevationWasCancelled(elevated)) {
      const late = normalizeHealth(await probe());
      if (late.status === 'ready') {
        onState({ phase: 'ready' });
        return { status: 'ready' };
      }

      if (late.status === 'port-conflict') {
        return fail(onState, 'port-conflict', portConflict(port).message);
      }

      return fail(
        onState,
        'uac-cancelled',
        'The Windows permission prompt was cancelled. RigForge needs approval to start the hardware daemon. Choose Start RigForge to try again.',
      );
    }
  }

  return finishWait(await pollUntil(probe, sleep, now, deadline), onState, port);
}

module.exports = {
  ensureDaemon,
  probeHealth,
  classifyHealthResponse,
  isRigForgeHealth,
  createScheduledTaskStarter,
  createElevatedStarter,
  createUserServiceStarter,
  findDaemonExecutable,
};
