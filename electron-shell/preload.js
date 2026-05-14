const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("ozonShell", {
  loadState: () => ipcRenderer.invoke("shell:state")
});
