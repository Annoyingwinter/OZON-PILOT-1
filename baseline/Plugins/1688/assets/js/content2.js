(async () => {
    //商品详情页面
    if (window.location.href.includes("/product/")) {

        // 创建定时器（每隔1秒执行一次task）
        // let timer = setInterval(() => {
        //     // 执行方法
        //     var findObj = document.querySelector(".ap-sbi-aside-btn-minimize");
        //     if (findObj) {
        //         debugger;
        //         findObj.click();
        //         num++;
        //         if (num > 5) {
        //             clearInterval(timer);
        //         }
        //         console.log('找到啦');
        //     }
        // }, 1000);

        let timer = setInterval(() => {
            // 执行方法
            // var findObj = document.querySelector('.pdp_sa img[class*="pdp_v"]');
            var findObj = document.querySelector('img[data-lcp-name*="webGallery-"]')
            if (findObj) {
                const mouseOverEvent = new MouseEvent('mousemove', {
                    bubbles: true,       // 允许事件冒泡（可能影响按钮显示逻辑）
                    cancelable: true,
                    view: window,
                    // 鼠标位置设为图片左上角（更贴近真实滑过）
                    clientX: findObj.getBoundingClientRect().left + 10,
                    clientY: findObj.getBoundingClientRect().top + 10
                });
                findObj.dispatchEvent(mouseOverEvent);

                setTimeout(() => {
                    clearInterval(timer);
                    document.querySelector('.ap-sbi-btn-search').click();
                }, 1000);

            }
            else {
                console.log('未找到图片元素');
            }
        }, 1000);


        // setInterval(() => {
        //     if (!isSend) {
        //         queryGood();
        //     }
        // }, 1000);
    }
})();
let num = 0;
var isSend = false;
function queryGood() {
    var findObj = document.querySelector(".ap-sbi-aside-btn-minimize");
    if (findObj) {
        findObj.click();
        // var price = findObj.children[1].innerText;
        // console.log("找到价格了啊：" + price);


        // debugger;
        // if (window.chrome?.webview) {
        //     isSend = true;
        //     let pageUrl = window.location.href;
        //     let sku = extractSku(pageUrl);
        //     let pdata = {
        //         sku: sku,
        //         price: price
        //     }
        //     // 通过回调将结果传递给 C#
        //     window.chrome.webview.postMessage(pdata);
        // }

    }
}

//提取当前skuid
function extractSku(url) {
    url = url.includes("?") ? url.split("?")[0] : url;
    const matches = url.match(new RegExp("\\d{7,}", "g"));
    return matches && matches.length > 0 ? matches[matches.length - 1] : "";
}