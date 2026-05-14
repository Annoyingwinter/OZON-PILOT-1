const { app, BrowserWindow, ipcMain, screen, session } = require("electron");
const fs = require("fs");
const path = require("path");
const DESKTOP_UA =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";

// Electron in this workspace inherits local proxy env vars, which causes the
// embedded browser to stall on 1688/Ozon with SSL/proxy handshake failures.
[
  "HTTP_PROXY",
  "HTTPS_PROXY",
  "ALL_PROXY",
  "http_proxy",
  "https_proxy",
  "all_proxy",
  "NODE_USE_ENV_PROXY"
].forEach((name) => {
  delete process.env[name];
});

app.commandLine.appendSwitch("no-proxy-server");
app.commandLine.appendSwitch("disable-blink-features", "AutomationControlled");
app.commandLine.appendSwitch("disable-features", "IsolateOrigins,site-per-process");

async function configureNetwork() {
  try {
    await session.defaultSession.setProxy({ mode: "direct" });
    session.defaultSession.setUserAgent(DESKTOP_UA);
  } catch {
    // Keep startup resilient; the browser surface will still report load errors.
  }
}

function createWindow() {
  const workArea = screen.getPrimaryDisplay().workAreaSize;
  const width = Math.max(1240, Math.min(1480, workArea.width - 80));
  const height = Math.max(820, Math.min(960, workArea.height - 80));
  const win = new BrowserWindow({
    width,
    height,
    minWidth: 1180,
    minHeight: 820,
    backgroundColor: "#f4eee7",
    center: true,
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      webviewTag: true
    }
  });

  win.webContents.setUserAgent(DESKTOP_UA);

  win.webContents.on("will-attach-webview", (_event, webPreferences, params) => {
    params.useragent = DESKTOP_UA;
    webPreferences.contextIsolation = true;
    webPreferences.nodeIntegration = false;
    webPreferences.sandbox = false;
    webPreferences.allowRunningInsecureContent = false;
    webPreferences.spellcheck = false;
  });

  win.webContents.setWindowOpenHandler(({ url }) => {
    return { action: "allow" };
  });

  win.loadFile(path.join(__dirname, "index.html"));

  win.webContents.on("did-finish-load", () => {
    setTimeout(async () => {
      try {
        const image = await win.webContents.capturePage();
        const outputPath = path.join(__dirname, "electron-window-shot.png");
        fs.writeFileSync(outputPath, image.toPNG());
      } catch {
        // Ignore preview capture failures during local iteration.
      }
    }, 2200);
  });
}

function resolveWorkspaceRoot() {
  return path.resolve(__dirname, "..");
}

function discoverPaths() {
  const root = resolveWorkspaceRoot();
  return {
    root,
    uiState: path.join(root, "ozon-pilot-ui-state.json"),
    config: path.join(root, "baseline", "SettingConfig.txt"),
    category: path.join(root, "baseline", "category.txt"),
    fee: path.join(root, "baseline", "fee.txt"),
    labels: path.join(root, "ozon-labels"),
    browserProfile: path.join(root, "browser-profile"),
    image2Key: path.join(
      process.env.LOCALAPPDATA || "",
      "OzonPilot",
      "sub2api-local",
      "ozon-pilot-image2-key.txt"
    )
  };
}

function safeReadJson(filePath, fallback) {
  try {
    if (!fs.existsSync(filePath)) return fallback;
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return fallback;
  }
}

function countPdfFiles(dirPath) {
  if (!fs.existsSync(dirPath)) return 0;
  let count = 0;
  const stack = [dirPath];
  while (stack.length) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const next = path.join(current, entry.name);
      if (entry.isDirectory()) {
        stack.push(next);
      } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".pdf")) {
        count += 1;
      }
    }
  }
  return count;
}

function buildShellState() {
  const paths = discoverPaths();
  const uiState = safeReadJson(paths.uiState, {});
  const config = safeReadJson(paths.config, {});
  const labelPdfCount = countPdfFiles(paths.labels);
  const hasClientId = Boolean((uiState.clientId || "").trim());
  const hasApiKey = Boolean(
    (uiState.apiKey || "").trim() || (uiState.apiKeyProtected || "").trim()
  );
  const image2Configured =
    Boolean(process.env.SUB2API_API_KEY) || fs.existsSync(paths.image2Key);

  return {
    paths,
    uiState,
    config,
    readiness: {
      has1688SessionHint: Boolean((uiState.browserUrl || "").includes("1688")),
      hasClientId,
      hasApiKey,
      image2Configured,
      labelPdfCount
    },
    summaries: {
      categoryLoaded: fs.existsSync(paths.category),
      feeLoaded: fs.existsSync(paths.fee),
      configLoaded: fs.existsSync(paths.config),
      browserProfileReady: fs.existsSync(paths.browserProfile)
    }
  };
}

ipcMain.handle("shell:state", async () => buildShellState());

app.whenReady().then(() => {
  configureNetwork().finally(() => {
    createWindow();
    app.on("activate", () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
