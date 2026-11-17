const path = require('node:path');
const { app, BrowserWindow, Menu, ipcMain } = require('electron');
const bootstrap = require('./daemonBootstrap.cjs');

const FallbackDashboardUrl = 'http://127.0.0.1:5757/#/dashboard';
const HealthMissLimit = 3;
const HealthIntervalMs = 2000;

/** Only http://127.0.0.1 and http://localhost may be loaded. The path is always the dashboard. */
function normalizeDashboardUrl(candidate) {
  if (typeof candidate !== 'string' || candidate.length === 0) {
    return null;
  }

  let url;
  try {
    url = new URL(candidate);
  } catch {
    return null;
  }

  if (url.protocol !== 'http:' || url.username !== '' || url.password !== '') {
    return null;
  }

  const host = url.hostname;
  if (host !== '127.0.0.1' && host !== 'localhost') {
    return null;
  }

  const port = url.port === '' ? '' : `:${url.port}`;
  return `http://${host}${port}/#/dashboard`;
}

function dashboardUrlFromArgs(argv) {
  for (const arg of argv) {
    const url = normalizeDashboardUrl(arg);
    if (url) {
      return url;
    }
  }

  return FallbackDashboardUrl;
}

function sameOrigin(target, dashboardUrl) {
  let next;
  let allowed;
  try {
    next = new URL(target);
    allowed = new URL(dashboardUrl);
  } catch {
    return false;
  }

  return next.protocol === 'http:' && next.hostname === allowed.hostname && next.port === allowed.port;
}

function endpoint(dashboardUrl) {
  const url = new URL(dashboardUrl);
  return {
    host: url.hostname,
    port: url.port === '' ? 80 : Number(url.port),
  };
}

function isLocalSplash(value) {
  try {
    const url = new URL(value);
    return url.protocol === 'file:' && url.pathname.replaceAll('\\', '/').toLowerCase().endsWith('/splash.html');
  } catch {
    return false;
  }
}

function navigationAllowed(target) {
  return isLocalSplash(target) || sameOrigin(target, currentUrl);
}

function iconPath() {
  if (app.isPackaged) {
    return path.join(process.resourcesPath, 'rigforge.ico');
  }

  return path.join(__dirname, '..', 'assets', 'rigforge.ico');
}

let window = null;
let currentUrl = FallbackDashboardUrl;
let healthTimer = null;
let splashActive = false;
let bootstrapGeneration = 0;

function watchDaemon(dashboardUrl) {
  const target = endpoint(dashboardUrl);
  let misses = 0;
  let pending = false;
  clearInterval(healthTimer);
  healthTimer = setInterval(() => {
    if (pending) {
      return;
    }

    pending = true;
    bootstrap.probeHealth(target).then((health) => {
      pending = false;
      if (!healthTimer) {
        return;
      }

      if (health.status === 'ready') {
        misses = 0;
        return;
      }

      misses += 1;
      if (misses >= HealthMissLimit) {
        clearInterval(healthTimer);
        healthTimer = null;
        app.quit();
      }
    }).catch(() => {
      pending = false;
    });
  }, HealthIntervalMs);
}

function showWindow() {
  if (!window || window.isDestroyed()) {
    return;
  }

  if (window.isMinimized()) {
    window.restore();
  }

  window.show();
  window.focus();
}

function createWindow() {
  window = new BrowserWindow({
    width: 1280,
    height: 800,
    minWidth: 960,
    minHeight: 640,
    title: 'RigForge',
    backgroundColor: '#09090b',
    icon: iconPath(),
    autoHideMenuBar: true,
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  window.removeMenu();
  window.once('ready-to-show', () => showWindow());
  window.webContents.on('did-fail-load', () => showWindow());
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-attach-webview', (event) => event.preventDefault());
  window.webContents.on('will-navigate', (event, url) => {
    if (!navigationAllowed(url)) {
      event.preventDefault();
    }
  });
  window.webContents.on('will-redirect', (event, url) => {
    if (!navigationAllowed(url)) {
      event.preventDefault();
    }
  });
  window.on('closed', () => {
    window = null;
  });
}

function sendSplash(state) {
  if (!splashActive || !window || window.isDestroyed()) {
    return;
  }

  window.webContents.send('splash:state', state);
}

async function showDashboard(dashboardUrl) {
  splashActive = false;
  currentUrl = dashboardUrl;
  watchDaemon(dashboardUrl);
  await window.loadURL(dashboardUrl);
}

async function bootstrapDashboard(dashboardUrl) {
  const generation = ++bootstrapGeneration;
  splashActive = true;
  currentUrl = dashboardUrl;
  const target = endpoint(dashboardUrl);

  try {
    const result = await bootstrap.ensureDaemon({
      host: target.host,
      port: target.port,
      platform: process.platform,
      probeHealth: () => bootstrap.probeHealth(target),
      startScheduledTask: bootstrap.createScheduledTaskStarter(),
      startElevatedExecutable: bootstrap.createElevatedStarter(),
      startUserService: bootstrap.createUserServiceStarter(),
      execPath: process.execPath,
      packaged: app.isPackaged,
      repoRoot: path.resolve(__dirname, '..', '..'),
      onState: (state) => {
        if (generation !== bootstrapGeneration) {
          return;
        }

        sendSplash(state);
      },
    });

    if (generation !== bootstrapGeneration) {
      return;
    }

    if (result.status === 'ready') {
      await showDashboard(dashboardUrl);
    }
  } catch (error) {
    if (generation !== bootstrapGeneration) {
      return;
    }

    sendSplash({
      phase: 'error',
      code: 'start-failed',
      message: error instanceof Error ? error.message : 'RigForge could not start the hardware daemon.',
    });
  }
}

function splashCommandAllowed(event) {
  return splashActive && isLocalSplash(event.sender.getURL());
}

ipcMain.on('splash:retry', (event) => {
  if (!splashCommandAllowed(event)) {
    return;
  }

  bootstrapDashboard(currentUrl).catch((error) => {
    console.error(error);
  });
});

ipcMain.on('splash:quit', (event) => {
  if (!splashCommandAllowed(event)) {
    return;
  }

  app.quit();
});

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  Menu.setApplicationMenu(null);
  app.setAppUserModelId('com.rigforge.shell');

  app.on('second-instance', (_event, argv) => {
    if (splashActive) {
      showWindow();
      return;
    }

    const next = dashboardUrlFromArgs(argv);
    if (window && !window.isDestroyed() && next !== currentUrl) {
      currentUrl = next;
      watchDaemon(next);
      window.loadURL(next);
    }

    showWindow();
  });

  // Closing the window leaves RigForge.Daemon.exe running in the tray.
  app.on('window-all-closed', () => app.quit());
  app.on('before-quit', () => {
    clearInterval(healthTimer);
    healthTimer = null;
  });

  app.whenReady().then(async () => {
    const dashboardUrl = dashboardUrlFromArgs(process.argv);
    currentUrl = dashboardUrl;
    const health = await bootstrap.probeHealth(endpoint(dashboardUrl));
    createWindow();
    if (health.status === 'ready') {
      await showDashboard(dashboardUrl);
      return;
    }

    await window.loadFile(path.join(__dirname, 'splash.html'));
    await bootstrapDashboard(dashboardUrl);
  }).catch((error) => {
    console.error(error);
    app.quit();
  });
}
