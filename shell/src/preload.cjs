'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('rigforgeSplash', {
  onState(callback) {
    if (typeof callback !== 'function') {
      return () => {};
    }

    const listener = (_event, state) => {
      try {
        callback(state);
      } catch {
        // The splash page handles its own rendering errors.
      }
    };

    ipcRenderer.on('splash:state', listener);
    return () => ipcRenderer.removeListener('splash:state', listener);
  },
  retry() {
    ipcRenderer.send('splash:retry');
  },
  quit() {
    ipcRenderer.send('splash:quit');
  },
});
