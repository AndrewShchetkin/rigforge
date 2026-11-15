const http = require('node:http');
const path = require('node:path');
const { app, BrowserWindow, Menu } = require('electron');

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

function healthUrl(dashboardUrl) {
  const url = new URL(dashboardUrl);
  const port = url.port === '' ? '' : `:${url.port}`;
  return `http://${url.hostname}${port}/api/health`;
}

function daemonAnswers(url) {
  return new Promise((resolve) => {
    const request = http.get(url, { timeout: 1500 }, (response) => {
      response.resume();
      resolve(true);
    });
    request.on('error', () => resolve(false));
    request.on('timeout', () => {
      request.destroy();
      resolve(false);
    });
  });
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

function watchDaemon(dashboardUrl) {
  const probe = healthUrl(dashboardUrl);
  let misses = 0;
  clearInterval(healthTimer);
  healthTimer = setInterval(() => {
    daemonAnswers(probe).then((alive) => {
      if (alive) {
        misses = 0;
        return;
      }

      misses += 1;
      if (misses >= HealthMissLimit) {
        app.quit();
      }
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

function createWindow(dashboardUrl) {
  currentUrl = dashboardUrl;
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
    if (!sameOrigin(url, currentUrl)) {
      event.preventDefault();
    }
  });
  window.webContents.on('will-redirect', (event, url) => {
    if (!sameOrigin(url, currentUrl)) {
      event.preventDefault();
    }
  });
  window.on('closed', () => {
    window = null;
  });

  watchDaemon(dashboardUrl);
  window.loadURL(dashboardUrl);
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  Menu.setApplicationMenu(null);
  app.setAppUserModelId('com.rigforge.shell');

  app.on('second-instance', (_event, argv) => {
    const next = dashboardUrlFromArgs(argv);
    if (window && !window.isDestroyed() && next !== currentUrl) {
      currentUrl = next;
      watchDaemon(next);
      window.loadURL(next);
    }

    showWindow();
  });

  app.on('window-all-closed', () => app.quit());
  app.on('before-quit', () => clearInterval(healthTimer));
  app.whenReady().then(() => createWindow(dashboardUrlFromArgs(process.argv)));
}
