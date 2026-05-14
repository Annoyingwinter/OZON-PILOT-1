const pages = [
  { id: "prepare", step: "01", title: "准备", subtitle: "先配浏览器和 Ozon API" },
  { id: "operate", step: "02", title: "运营", subtitle: "从选品到上架集中处理" },
  { id: "assets", step: "03", title: "资产", subtitle: "类目、运费、素材统一管理" },
  { id: "labels", step: "04", title: "面单", subtitle: "下载结果和批次汇总" },
  { id: "settings", step: "05", title: "设置", subtitle: "定价规则和运行参数" },
];

let shellState = null;
let activePage = "prepare";
let drawerCollapsed = false;
let browserStatus = {
  state: "idle",
  text: "等待加载浏览器",
};

function getUiState() {
  return shellState?.uiState || {};
}

function getConfig() {
  return shellState?.config || {};
}

function getReadiness() {
  return shellState?.readiness || {};
}

function getSummaries() {
  return shellState?.summaries || {};
}

function splitKeywords(raw) {
  return String(raw || "")
    .split(/\r?\n/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function getKeywordPreview() {
  const keywords = splitKeywords(getUiState().keywords || "");
  return {
    count: keywords.length,
    text: keywords.length > 0 ? keywords.slice(0, 6).join("、") : "还没有读取到可用的关键词草稿。",
  };
}

function getMissingItems() {
  const readiness = getReadiness();
  const summaries = getSummaries();
  return [
    !readiness.has1688SessionHint ? "1688 登录" : null,
    !readiness.hasClientId || !readiness.hasApiKey ? "Ozon API" : null,
    !readiness.image2Configured ? "image2" : null,
    !summaries.categoryLoaded || !summaries.feeLoaded ? "规则文件" : null,
  ].filter(Boolean);
}

function pill(label, tone) {
  return `<span class="pill ${tone}">${label}</span>`;
}

function getHeroState() {
  const readiness = getReadiness();
  const summaries = getSummaries();

  if (!readiness.has1688SessionHint) {
    return {
      title: "先完成 1688 登录检测",
      text: "浏览器必须先稳定登录 1688。登录完成后，系统才能继续选品、抓取和自动循环。",
      statusText: "等待 1688 登录",
      statusGood: false,
      primaryLabel: "去登录 1688",
      primaryAction: "1688",
      secondaryLabel: "打开 Ozon",
      secondaryAction: "ozon",
    };
  }

  if (!readiness.hasClientId || !readiness.hasApiKey) {
    return {
      title: "下一步填写并验证 Ozon API",
      text: "先保存 Client ID 和 API Key。通过校验后，准备模块就不需要一直留在台面上了。",
      statusText: "等待 Ozon API",
      statusGood: false,
      primaryLabel: "打开 Ozon",
      primaryAction: "ozon",
      secondaryLabel: "回到 1688",
      secondaryAction: "1688",
    };
  }

  if (!summaries.categoryLoaded || !summaries.feeLoaded) {
    return {
      title: "还差类目和运费规则",
      text: "规则文件不到位时，不应该直接开始自动运营。先补齐规则，再进入正式流程。",
      statusText: "等待规则文件",
      statusGood: false,
      primaryLabel: "查看资产",
      primaryAction: "page-assets",
      secondaryLabel: "回到 1688",
      secondaryAction: "1688",
    };
  }

  return {
    title: "基础准备已齐全，可以进入正式运营",
    text: "现在主界面应该留给浏览器。需要操作时再展开控制台，结果统一去运营页和面单中心看。",
    statusText: "准备完成",
    statusGood: true,
    primaryLabel: "进入运营",
    primaryAction: "page-operate",
    secondaryLabel: "收起面板",
    secondaryAction: "toggle-drawer",
  };
}

function renderWorkflowNav() {
  const host = document.getElementById("workflow-nav");
  const hero = getHeroState();
  const locked = hero.primaryAction !== "page-operate" && hero.primaryAction !== "toggle-drawer";

  host.innerHTML = pages
    .map((page) => {
      const isActive = page.id === activePage;
      const isLocked = locked && page.id !== "prepare";
      return `
        <button class="workflow-btn ${isActive ? "active" : ""} ${isLocked ? "locked" : ""}" data-page="${page.id}" type="button" ${isLocked ? "disabled" : ""}>
          <span class="workflow-step">${page.step}</span>
          <span class="workflow-copy">
            <span class="workflow-title">${page.title}</span>
          </span>
        </button>
      `;
    })
    .join("");

  host.querySelectorAll(".workflow-btn").forEach((button) => {
    button.addEventListener("click", () => {
      activePage = button.dataset.page;
      render();
    });
  });
}

function renderHero() {
  const hero = getHeroState();
  const missingItems = getMissingItems();
  document.getElementById("hero-title").textContent = hero.title;
  document.getElementById("hero-text").textContent = hero.text;
  document.getElementById("status-text").textContent = browserStatus.state === "error" ? browserStatus.text : hero.statusText;
  document.getElementById("status-dot").className = `status-dot ${hero.statusGood && browserStatus.state !== "error" ? "good" : ""}`;
  document.getElementById("ready-summary").textContent = missingItems.length === 0 ? "已全部就绪" : `还差 ${missingItems.length} 项`;
  document.getElementById("primary-action").textContent = hero.primaryLabel;
  document.getElementById("primary-action").dataset.action = hero.primaryAction;
  document.getElementById("hero-primary-action").textContent = hero.primaryLabel;
  document.getElementById("hero-primary-action").dataset.action = hero.primaryAction;
  document.getElementById("hero-secondary-action").textContent = hero.secondaryLabel;
  document.getElementById("hero-secondary-action").dataset.action = hero.secondaryAction;
}

function updateBrowserStatus(state, text) {
  browserStatus = { state, text };
  const hero = getHeroState();
  const statusText = document.getElementById("status-text");
  const statusDot = document.getElementById("status-dot");
  const browserEmpty = document.getElementById("browser-empty");
  const emptyTitle = document.getElementById("empty-title");
  const emptyCopy = document.getElementById("empty-copy");

  if (statusText) {
    statusText.textContent = state === "error" ? text : hero.statusText;
  }
  if (statusDot) {
    statusDot.className = `status-dot ${state === "loaded" && hero.statusGood ? "good" : ""}`;
  }
  if (browserEmpty && state === "error") {
    browserEmpty.classList.add("visible");
    if (emptyTitle) emptyTitle.textContent = "浏览器加载失败";
    if (emptyCopy) emptyCopy.textContent = text;
  } else if (browserEmpty && state === "loading") {
    browserEmpty.classList.remove("visible");
  }
}

function bindBrowserEvents(browserView) {
  if (!browserView || browserView.dataset.bound === "yes") return;
  browserView.dataset.bound = "yes";

  browserView.addEventListener("did-start-loading", () => {
    updateBrowserStatus("loading", "浏览器正在加载");
  });

  browserView.addEventListener("did-stop-loading", () => {
    if (browserStatus.state !== "error") {
      updateBrowserStatus("loaded", "浏览器已加载");
    }
  });

  browserView.addEventListener("did-fail-load", (event) => {
    if (event.errorCode === -3) return;
    updateBrowserStatus("error", `页面加载失败：${event.errorDescription || "未知错误"}（${event.errorCode}）`);
  });
}

function setBrowserUrl(url) {
  const browserView = document.getElementById("browser-view");
  if (!browserView) return;
  updateBrowserStatus("loading", "浏览器正在加载");
  browserView.setAttribute("src", url);
}

function renderBrowserSurface() {
  const uiState = getUiState();
  const browserUrl = (uiState.browserUrl || "https://www.1688.com/").trim();
  const browserView = document.getElementById("browser-view");
  bindBrowserEvents(browserView);

  if (browserView && browserView.getAttribute("src") !== browserUrl) {
    setBrowserUrl(browserUrl);
  }
}

function renderReadiness() {
  const readiness = getReadiness();
  const summaries = getSummaries();
  const items = [
    {
      title: "1688 登录",
      tone: readiness.has1688SessionHint ? "good" : "warn",
      badge: readiness.has1688SessionHint ? "已识别" : "待完成",
      copy: readiness.has1688SessionHint
        ? "已经检测到 1688 工作页线索。浏览器可以继续作为主操作区。"
        : "先登录 1688，并通过一次人工验证码。通过后这里要能稳定变成已识别。",
    },
    {
      title: "Ozon API",
      tone: readiness.hasClientId && readiness.hasApiKey ? "good" : "warn",
      badge: readiness.hasClientId && readiness.hasApiKey ? "已保存" : "待补全",
      copy: readiness.hasClientId && readiness.hasApiKey
        ? "本地已检测到 Client ID 和 API Key。下一步补连通性校验。"
        : "先补齐 Client ID 和 API Key，并且做一次真正的可用性验证。",
    },
    {
      title: "image2",
      tone: readiness.image2Configured ? "good" : "warn",
      badge: readiness.image2Configured ? "已接入" : "待配置",
      copy: readiness.image2Configured
        ? "已检测到 image2 所需密钥。"
        : "目前还没有找到 image2 可用密钥，图片链路还不能算完成。",
    },
    {
      title: "规则文件",
      tone: summaries.categoryLoaded && summaries.feeLoaded ? "good" : "warn",
      badge: summaries.categoryLoaded && summaries.feeLoaded ? "已就绪" : "待检查",
      copy: summaries.categoryLoaded && summaries.feeLoaded
        ? "类目和运费规则都已加载。"
        : "类目文件或运费规则缺失时，不应该让自动流程直接开跑。",
    },
  ];

  document.getElementById("readiness-list").innerHTML = items
    .map(
      (item) => `
        <article class="readiness-item">
          <div class="readiness-top">
            <div class="readiness-title">${item.title}</div>
            ${pill(item.badge, item.tone)}
          </div>
          <div class="readiness-copy">${item.copy}</div>
        </article>
      `
    )
    .join("");
}

function renderPreparePage() {
  const uiState = getUiState();
  const keywordPreview = getKeywordPreview();
  const loop = uiState.loop || "1";

  return `
    <section class="drawer-panel page-drawer">
      <div class="panel-head">
        <h2>准备面板</h2>
        <span class="panel-note">完成后可收起</span>
      </div>

      <div class="task-stack">
        <article class="task-card">
          <div class="task-head">
            <div>
              <div class="card-kicker">当前操作顺序</div>
              <div class="task-title">先登录，再配 API，再检查规则</div>
            </div>
            ${pill("新手优先", "neutral")}
          </div>
          <div class="task-copy">
            这是第一次进入系统时最重要的三件事。完成后，再去运营页执行实际流程。
          </div>
          <div class="task-meta">
            <span class="meta-chip">浏览器地址：${uiState.browserUrl || "https://www.1688.com/"}</span>
            <span class="meta-chip">循环次数：${loop}</span>
          </div>
        </article>

        <article class="task-card">
          <div class="task-head">
            <div>
              <div class="card-kicker">本轮草稿</div>
              <div class="task-title">关键词预览</div>
            </div>
            ${pill(keywordPreview.count > 0 ? "已有内容" : "待输入", keywordPreview.count > 0 ? "good" : "warn")}
          </div>
          <div class="task-copy">${keywordPreview.text}</div>
        </article>
      </div>
    </section>
  `;
}

function renderOperatePage() {
  const uiState = getUiState();
  const labelPdfCount = getReadiness().labelPdfCount || 0;

  return `
    <section class="drawer-panel page-drawer">
      <div class="panel-head">
        <h2>运营主流程</h2>
        <span class="panel-note">结果在这里看</span>
      </div>

      <div class="task-stack">
        <article class="task-card">
          <div class="task-head">
            <div>
              <div class="card-kicker">执行链路</div>
              <div class="task-title">选类目、跑选品、生成图文、核价、上架</div>
            </div>
            ${pill("待接执行器", "warn")}
          </div>
          <div class="task-copy">
            这里以后只保留真实会点击的动作，不再堆无意义的工程状态块。
          </div>
          <div class="task-meta">
            <span class="meta-chip">当前类目：${uiState.categoryId || "未选择"}</span>
            <span class="meta-chip">循环次数：${uiState.loop || "1"}</span>
            <span class="meta-chip">已下载面单：${labelPdfCount}</span>
          </div>
        </article>
      </div>
    </section>
  `;
}

function renderAssetsPage() {
  const summaries = getSummaries();
  return `
    <section class="drawer-panel page-drawer">
      <div class="panel-head">
        <h2>资产</h2>
        <span class="panel-note">规则和素材集中查看</span>
      </div>

      <div class="asset-list">
        <article class="asset-item">
          <div class="asset-top">
            <div class="asset-title">类目字典</div>
            ${pill(summaries.categoryLoaded ? "已加载" : "缺失", summaries.categoryLoaded ? "good" : "warn")}
          </div>
          <div class="asset-copy">后面需要把“选择类目后自动灌入配置”的逻辑接回这个壳层。</div>
        </article>

        <article class="asset-item">
          <div class="asset-top">
            <div class="asset-title">运费规则</div>
            ${pill(summaries.feeLoaded ? "已加载" : "缺失", summaries.feeLoaded ? "good" : "warn")}
          </div>
          <div class="asset-copy">运费规则放在资产和设置里，不再作为首页垃圾信息重复展示。</div>
        </article>
      </div>
    </section>
  `;
}

function renderLabelsPage() {
  const config = getConfig();
  const labelPdfCount = getReadiness().labelPdfCount || 0;

  return `
    <section class="drawer-panel page-drawer">
      <div class="panel-head">
        <h2>面单中心</h2>
        <span class="panel-note">下载结果统一看这里</span>
      </div>

      <div class="stat-grid">
        <article class="stat-card">
          <div class="card-kicker">已下载 PDF</div>
          <div class="stat-value">${labelPdfCount}</div>
          <div class="stat-copy">下一步补上最近批次、合并下载和失败重试。</div>
        </article>
        <article class="stat-card">
          <div class="card-kicker">履约模式</div>
          <div class="stat-value">${config.IsFbo ? "FBO" : "FBS"}</div>
          <div class="stat-copy">面单下载、批次汇总、导出入口都放到这里。</div>
        </article>
      </div>
    </section>
  `;
}

function renderSettingsPage() {
  const config = getConfig();
  return `
    <section class="drawer-panel page-drawer">
      <div class="panel-head">
        <h2>设置</h2>
        <span class="panel-note">只保留关键参数</span>
      </div>

      <div class="settings-list">
        <article class="setting-item">
          <div class="setting-title">定价公式</div>
          <div class="setting-copy">售价 = (货物成本 + 国内物流 + 国际物流) / (1 - 平台佣金 - 推广费用 - 目标利润)</div>
        </article>
        <article class="setting-item">
          <div class="setting-title">当前关键参数</div>
          <div class="setting-copy">
            最低售价 ${config.MinPirce ?? "-"}，利润率 ${config.MinProfitPer ?? "-"}%，智能利润 ${config.ZNPer ?? "-"}%，国内物流 ${config.DeliveryFee ?? "-"}。
          </div>
        </article>
      </div>
    </section>
  `;
}

function renderPagePanel() {
  const host = document.getElementById("page-panel");
  const renderers = {
    prepare: renderPreparePage,
    operate: renderOperatePage,
    assets: renderAssetsPage,
    labels: renderLabelsPage,
    settings: renderSettingsPage,
  };
  host.outerHTML = renderers[activePage]();
}

function setDrawerState(collapsed) {
  drawerCollapsed = collapsed;
  document.getElementById("workspace")?.classList.toggle("drawer-collapsed", drawerCollapsed);
  document.getElementById("drawer-toggle").textContent = drawerCollapsed ? "展开面板" : "收起面板";
}

function executeAction(action) {
  if (!action) return;
  if (action === "1688") {
    setBrowserUrl("https://www.1688.com/");
  } else if (action === "ozon") {
    setBrowserUrl("https://seller.ozon.ru/");
  } else if (action === "page-assets") {
    activePage = "assets";
    setDrawerState(false);
    render();
    return;
  } else if (action === "page-operate") {
    activePage = "operate";
    setDrawerState(false);
    render();
    return;
  } else if (action === "toggle-drawer") {
    setDrawerState(!drawerCollapsed);
    return;
  }
  setDrawerState(false);
}

function wireButtons() {
  document.getElementById("drawer-toggle")?.addEventListener("click", () => {
    setDrawerState(!drawerCollapsed);
  });

  document.getElementById("browser-refresh")?.addEventListener("click", () => {
    const browserView = document.getElementById("browser-view");
    updateBrowserStatus("loading", "浏览器正在加载");
    browserView?.reload();
  });

  document.getElementById("browser-home")?.addEventListener("click", () => executeAction("1688"));
  document.getElementById("browser-ozon")?.addEventListener("click", () => executeAction("ozon"));

  [document.getElementById("primary-action"), document.getElementById("hero-primary-action"), document.getElementById("hero-secondary-action")].forEach((button) => {
    button?.addEventListener("click", () => executeAction(button.dataset.action));
  });
}

function render() {
  renderWorkflowNav();
  renderHero();
  renderReadiness();
  renderBrowserSurface();
  renderPagePanel();
  wireButtons();
}

async function boot() {
  if (window.ozonShell?.loadState) {
    shellState = await window.ozonShell.loadState();
  } else {
    shellState = { uiState: {}, config: {}, readiness: {}, summaries: {}, paths: {} };
  }
  render();
}

boot();
