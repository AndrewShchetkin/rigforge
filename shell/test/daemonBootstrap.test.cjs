'use strict';

const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');

const bootstrap = require('../src/daemonBootstrap.cjs');

function listen(handler) {
  return new Promise((resolve, reject) => {
    const server = http.createServer(handler);
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      resolve({ server, port: server.address().port });
    });
  });
}

function closeServer(server) {
  return new Promise((resolve) => server.close(() => resolve()));
}

function virtualClock() {
  let nowMs = 0;
  const sleeps = [];
  return {
    now: () => nowMs,
    sleep: async (ms) => {
      sleeps.push(ms);
      nowMs += ms;
    },
    sleeps,
    get nowMs() {
      return nowMs;
    },
  };
}

function json(res, statusCode, body) {
  const payload = typeof body === 'string' ? body : JSON.stringify(body);
  res.writeHead(statusCode, { 'content-type': 'application/json; charset=utf-8' });
  res.end(payload);
}

test('a live RigForge daemon is ready without spawning anything', async () => {
  const { server, port } = await listen((req, res) => {
    assert.equal(req.url, '/api/health');
    json(res, 200, {
      product: 'RigForge',
      version: '0.1.0',
      startedAt: '2026-11-15T00:00:00.000Z',
    });
  });

  try {
    let spawned = false;
    const states = [];
    const result = await bootstrap.ensureDaemon({
      host: '127.0.0.1',
      port,
      platform: 'win32',
      probeHealth: () => bootstrap.probeHealth({ host: '127.0.0.1', port }),
      startScheduledTask: async () => {
        spawned = true;
        return { code: 0 };
      },
      startElevatedExecutable: async () => {
        spawned = true;
        return { code: 0 };
      },
      sleep: async () => {
        throw new Error('health was already ready');
      },
      onState: (state) => states.push(state),
    });

    assert.equal(result.status, 'ready');
    assert.equal(spawned, false);
    assert.deepEqual(states, []);
  } finally {
    await closeServer(server);
  }
});

test('legacy health JSON without product is still RigForge', async () => {
  const { server, port } = await listen((_req, res) => {
    json(res, 200, { version: '0.0.9', startedAt: '2026-01-01T00:00:00.000Z' });
  });

  try {
    const result = await bootstrap.ensureDaemon({
      host: '127.0.0.1',
      port,
      platform: 'win32',
      probeHealth: () => bootstrap.probeHealth({ host: '127.0.0.1', port }),
      startScheduledTask: async () => {
        throw new Error('legacy daemon should not be restarted');
      },
    });
    assert.equal(result.status, 'ready');
  } finally {
    await closeServer(server);
  }
});

test('a foreign product on the port is a conflict even with version fields', async () => {
  const { server, port } = await listen((_req, res) => {
    json(res, 200, { product: 'OtherApp', version: '1.0.0', startedAt: '2026-01-01T00:00:00.000Z' });
  });

  try {
    let spawned = false;
    const result = await bootstrap.ensureDaemon({
      host: '127.0.0.1',
      port,
      platform: 'win32',
      probeHealth: () => bootstrap.probeHealth({ host: '127.0.0.1', port }),
      startScheduledTask: async () => {
        spawned = true;
        return { code: 0 };
      },
    });
    assert.equal(result.status, 'error');
    assert.equal(result.code, 'port-conflict');
    assert.equal(result.message, `Port ${port} is already in use by another application.`);
    assert.equal(spawned, false);
  } finally {
    await closeServer(server);
  }
});

test('non-JSON and non-200 responses are port conflicts and do not spawn the daemon', async () => {
  const plain = await listen((_req, res) => {
    res.writeHead(200, { 'content-type': 'text/plain' });
    res.end('not-json');
  });
  const failing = await listen((_req, res) => {
    res.writeHead(503);
    res.end('busy');
  });

  try {
    for (const { server, port } of [plain, failing]) {
      let spawned = false;
      const states = [];
      const result = await bootstrap.ensureDaemon({
        host: '127.0.0.1',
        port,
        platform: 'win32',
        probeHealth: () => bootstrap.probeHealth({ host: '127.0.0.1', port }),
        startScheduledTask: async () => {
          spawned = true;
          return { code: 0 };
        },
        onState: (state) => states.push(state),
      });
      assert.equal(result.code, 'port-conflict');
      assert.equal(spawned, false);
      assert.equal(states[0].phase, 'error');
      assert.equal(states[0].code, 'port-conflict');
      await closeServer(server);
    }
  } finally {
    if (plain.server.listening) await closeServer(plain.server);
    if (failing.server.listening) await closeServer(failing.server);
  }
});

test('a refused connection is down, not a port conflict', async () => {
  const { server, port } = await listen((_req, res) => res.end('nope'));
  await closeServer(server);
  const health = await bootstrap.probeHealth({ host: '127.0.0.1', port, timeoutMs: 500 });
  assert.equal(health.status, 'down');
});

test('schtasks /Run starts the daemon without a UAC prompt', async () => {
  const clock = virtualClock();
  const states = [];
    let elevated = false;
    let probes = 0;
    const result = await bootstrap.ensureDaemon({
      port: 5757,
      platform: 'win32',
      now: clock.now,
      sleep: clock.sleep,
      probeHealth: async () => {
        probes += 1;
        return probes < 3 ? { status: 'down' } : { status: 'ready' };
      },
    startScheduledTask: async () => ({ code: 0 }),
    startElevatedExecutable: async () => {
      elevated = true;
      return { code: 0 };
    },
    onState: (state) => states.push(state),
  });

  assert.equal(result.status, 'ready');
  assert.equal(elevated, false);
  assert.equal(states[0].phase, 'starting');
  assert.equal(states[0].message, 'Starting RigForge hardware daemon…');
  assert.ok(clock.sleeps.length >= 1);
  assert.ok(clock.sleeps.every((ms) => ms > 0 && ms <= 350));
});

test('a missing scheduled task falls back to an elevated daemon launch', async () => {
  const clock = virtualClock();
  const launches = [];
  let daemonUp = false;
  const result = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'win32',
    now: clock.now,
    sleep: clock.sleep,
    probeHealth: async () => (daemonUp ? { status: 'ready' } : { status: 'down' }),
    startScheduledTask: async () => ({ code: 1 }),
    findDaemonExecutable: () => 'C:\\Program Files\\RigForge\\RigForge.Daemon.exe',
    startElevatedExecutable: async (info) => {
      launches.push(info);
      daemonUp = true;
      return { code: 0 };
    },
  });

  assert.equal(result.status, 'ready');
  assert.deepEqual(launches, [{
    exe: 'C:\\Program Files\\RigForge\\RigForge.Daemon.exe',
    cwd: 'C:\\Program Files\\RigForge',
  }]);
  assert.ok(clock.nowMs < 4000);
});

test('RunAs is used when the scheduled task does not answer within 4 seconds', async () => {
  const clock = virtualClock();
  let daemonUp = false;
  let elevated = 0;
  const result = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'win32',
    now: clock.now,
    sleep: clock.sleep,
    probeHealth: async () => (daemonUp ? { status: 'ready' } : { status: 'down' }),
    startScheduledTask: async () => ({ code: 0 }),
    findDaemonExecutable: () => 'D:\\RigForge\\RigForge.Daemon.exe',
    startElevatedExecutable: async () => {
      elevated += 1;
      daemonUp = true;
      return { code: 0 };
    },
  });

  assert.equal(result.status, 'ready');
  assert.equal(elevated, 1);
  assert.ok(clock.nowMs >= 4000);
  assert.ok(clock.nowMs < 25000);
});

test('cancelling UAC leaves an error and a later retry can start the daemon', async () => {
  const clock = virtualClock();
  const states = [];
  const cancelled = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'win32',
    now: clock.now,
    sleep: clock.sleep,
    probeHealth: async () => ({ status: 'down' }),
    startScheduledTask: async () => ({ code: 1 }),
    findDaemonExecutable: () => 'C:\\RigForge\\RigForge.Daemon.exe',
    startElevatedExecutable: async () => ({ cancelled: true, code: 1223 }),
    onState: (state) => states.push(state),
  });

  assert.equal(cancelled.status, 'error');
  assert.equal(cancelled.code, 'uac-cancelled');
  assert.match(cancelled.message, /permission|cancel/i);
  assert.equal(states.at(-1).phase, 'error');
  assert.equal(states.at(-1).code, 'uac-cancelled');
  assert.ok(clock.nowMs < 4000);

  let taskRuns = 0;
  const retried = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'win32',
    now: () => 0,
    sleep: async () => {},
    probeHealth: async () => (taskRuns === 0 ? { status: 'down' } : { status: 'ready' }),
    startScheduledTask: async () => {
      taskRuns += 1;
      return { code: 0 };
    },
    startElevatedExecutable: async () => {
      throw new Error('retry should use the scheduled task, not UAC');
    },
  });

  assert.equal(retried.status, 'ready');
  assert.equal(taskRuns, 1);
});

test('a daemon that never answers within 25 seconds returns timeout', async () => {
  const clock = virtualClock();
  const result = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'win32',
    now: clock.now,
    sleep: clock.sleep,
    probeHealth: async () => ({ status: 'down' }),
    startScheduledTask: async () => ({ code: 0 }),
    findDaemonExecutable: () => 'C:\\RigForge\\RigForge.Daemon.exe',
    startElevatedExecutable: async () => ({ code: 0 }),
  });

  assert.equal(result.status, 'error');
  assert.equal(result.code, 'timeout');
  assert.ok(clock.nowMs >= 25000);
});

test('a missing daemon executable is reported when the scheduled task does not exist', async () => {
  let elevated = false;
  const result = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'win32',
    probeHealth: async () => ({ status: 'down' }),
    startScheduledTask: async () => ({ code: 1 }),
    findDaemonExecutable: () => null,
    startElevatedExecutable: async () => {
      elevated = true;
      return { code: 0 };
    },
    sleep: async () => {
      throw new Error('should not poll without a daemon');
    },
  });

  assert.equal(result.code, 'daemon-missing');
  assert.equal(elevated, false);
});

test('linux starts the user service and then waits for health', async () => {
  const clock = virtualClock();
  let started = false;
  let elevated = false;
  const result = await bootstrap.ensureDaemon({
    port: 5757,
    platform: 'linux',
    now: clock.now,
    sleep: clock.sleep,
    probeHealth: async () => (started ? { status: 'ready' } : { status: 'down' }),
    startUserService: async () => {
      started = true;
      return { code: 0 };
    },
    startElevatedExecutable: async () => {
      elevated = true;
      return { code: 0 };
    },
  });

  assert.equal(result.status, 'ready');
  assert.equal(elevated, false);
  assert.equal(clock.sleeps.length, 0);
});

test('scheduled task command is schtasks /Run with a hidden window', async () => {
  const calls = [];
  const start = bootstrap.createScheduledTaskStarter({
    spawn: fakeSpawn(calls, 0),
  });
  const result = await start();
  assert.equal(result.code, 0);
  assert.equal(calls[0].file, 'schtasks.exe');
  assert.deepEqual(calls[0].args, ['/Run', '/TN', 'RigForge']);
  assert.equal(calls[0].options.windowsHide, true);
});

test('elevated launch asks the daemon executable to show UAC', async () => {
  const calls = [];
  const exe = 'C:\\Program Files\\RigForge (x64)\\RigForge.Daemon.exe';
  const cwd = 'C:\\Program Files\\RigForge (x64)';
  const start = bootstrap.createElevatedStarter({
    spawn: fakeSpawn(calls, 0),
  });
  const result = await start({ exe, cwd });
  assert.equal(result.code, 0);
  assert.equal(calls[0].file, exe);
  assert.deepEqual(calls[0].args, ['--run-elevated']);
  assert.equal(calls[0].options.windowsHide, true);
  assert.equal(calls[0].options.cwd, cwd);
});

test('linux service starter calls systemctl --user', async () => {
  const calls = [];
  const start = bootstrap.createUserServiceStarter({
    spawn: fakeSpawn(calls, 0),
  });
  const result = await start();
  assert.equal(result.code, 0);
  assert.equal(calls[0].file, 'systemctl');
  assert.deepEqual(calls[0].args, ['--user', 'start', 'rigforge.service']);
});

test('daemon executable is resolved beside the app and from the dev repository', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'rigforge-daemon-'));
  try {
    const beside = path.join(root, 'RigForge.Daemon.exe');
    fs.writeFileSync(beside, '');
    assert.equal(bootstrap.findDaemonExecutable({
      execPath: path.join(root, 'RigForge.exe'),
      isPackaged: true,
    }), beside);

    const repo = fs.mkdtempSync(path.join(os.tmpdir(), 'rigforge-repo-'));
    try {
      const staged = path.join(repo, 'artifacts', 'stage', 'win-x64-daemon', 'RigForge.Daemon.exe');
      fs.mkdirSync(path.dirname(staged), { recursive: true });
      fs.writeFileSync(staged, '');
      assert.equal(bootstrap.findDaemonExecutable({
        execPath: path.join(repo, 'node_modules', 'electron', 'electron.exe'),
        isPackaged: false,
        repoRoot: repo,
      }), staged);
      assert.equal(bootstrap.findDaemonExecutable({
        execPath: path.join(repo, 'node_modules', 'electron', 'electron.exe'),
        isPackaged: true,
        repoRoot: repo,
      }), null);
    } finally {
      fs.rmSync(repo, { recursive: true, force: true });
    }
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

function fakeSpawn(calls, code) {
  return (file, args, options) => {
    calls.push({ file, args, options });
    const child = new EventEmitter();
    child.stdout = new EventEmitter();
    child.stderr = new EventEmitter();
    process.nextTick(() => child.emit('close', code));
    return child;
  };
}
